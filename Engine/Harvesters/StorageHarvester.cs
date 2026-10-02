using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using InFox.Models.Sensors;
using InFox.Services;

namespace InFox.Engine.Harvesters
{
    public class StorageHarvester : ISensorHarvester
    {
        public string Name => "Storage & Drive Telemetry";
        public bool IsAvailable { get; private set; } = true;

        private IntPtr _hPdhQuery = IntPtr.Zero;
        private readonly Dictionary<string, (IntPtr hRead, IntPtr hWrite)> _driveCounters = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _driveThermals = new(StringComparer.OrdinalIgnoreCase);
        private double _lastKnownChassisAir = 28.0;

        public Task InitializeAsync()
        {
            try
            {
                if (PdhNative.PdhOpenQuery(null, IntPtr.Zero, out _hPdhQuery) == 0 && _hPdhQuery != IntPtr.Zero)
                {
                    // Add physical and logical drive throughput counters
                    foreach (var d in DriveInfo.GetDrives())
                    {
                        if (!d.IsReady || d.DriveType != DriveType.Fixed) continue;
                        string letter = d.Name.TrimEnd('\\');

                        string readPath = $@"\LogicalDisk({letter})\Disk Read Bytes/sec";
                        string writePath = $@"\LogicalDisk({letter})\Disk Write Bytes/sec";

                        PdhNative.PdhAddEnglishCounterW(_hPdhQuery, readPath, IntPtr.Zero, out var hRead);
                        PdhNative.PdhAddEnglishCounterW(_hPdhQuery, writePath, IntPtr.Zero, out var hWrite);

                        if (hRead != IntPtr.Zero || hWrite != IntPtr.Zero)
                        {
                            _driveCounters[letter] = (hRead, hWrite);
                        }
                    }

                    // Initial collection tick to prime PDH delta
                    PdhNative.PdhCollectQueryData(_hPdhQuery);
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("StorageHarvester", $"PDH disk init error: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        public void Poll(SystemSummary summary, List<SensorMetric> metrics, Action<string, double> recordHistory)
        {
            try
            {
                // Collect fresh PDH disk I/O rates
                if (_hPdhQuery != IntPtr.Zero)
                {
                    PdhNative.PdhCollectQueryData(_hPdhQuery);
                }

                var drives = DriveInfo.GetDrives();
                var volumeList = new List<DriveSummary>();

                foreach (var d in drives)
                {
                    if (!d.IsReady || d.DriveType != DriveType.Fixed) continue;

                    try
                    {
                        double totalGb = d.TotalSize / (1024.0 * 1024.0 * 1024.0);
                        double freeGb = d.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0);
                        double usedGb = Math.Max(0, totalGb - freeGb);

                        string driveLetter = d.Name.TrimEnd('\\');
                        string volumeLabel = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel;
                        string cleanName = $"{volumeLabel} ({driveLetter})";

                        double readMBps = 0;
                        double writeMBps = 0;

                        if (_driveCounters.TryGetValue(driveLetter, out var counters))
                        {
                            if (counters.hRead != IntPtr.Zero &&
                                PdhNative.PdhGetFormattedCounterValue(counters.hRead, PdhNative.PDH_FMT_DOUBLE, out _, out var rVal) == 0 &&
                                rVal.CStatus == PdhNative.PDH_CSTATUS_VALID_DATA && !double.IsNaN(rVal.doubleValue))
                            {
                                readMBps = Math.Round(rVal.doubleValue / (1024.0 * 1024.0), 1);
                            }

                            if (counters.hWrite != IntPtr.Zero &&
                                PdhNative.PdhGetFormattedCounterValue(counters.hWrite, PdhNative.PDH_FMT_DOUBLE, out _, out var wVal) == 0 &&
                                wVal.CStatus == PdhNative.PDH_CSTATUS_VALID_DATA && !double.IsNaN(wVal.doubleValue))
                            {
                                writeMBps = Math.Round(wVal.doubleValue / (1024.0 * 1024.0), 1);
                            }
                        }

                        var volSummary = new DriveSummary
                        {
                            Name = cleanName,
                            DriveLetter = driveLetter,
                            TotalGb = Math.Round(totalGb, 1),
                            UsedGb = Math.Round(usedGb, 1),
                            ReadSpeedMBps = readMBps,
                            WriteSpeedMBps = writeMBps
                        };

                        volumeList.Add(volSummary);

                        // 1. Used Space (always static)
                        metrics.Add(new SensorMetric
                        {
                            Id = $"disk_{driveLetter[0]}_used",
                            Name = $"{driveLetter}: Used Space",
                            HardwareId = $"disk_{driveLetter[0]}",
                            HardwareName = cleanName,
                            Category = HardwareCategory.Storage,
                            Type = MetricType.Capacity,
                            Value = Math.Round(usedGb, 1),
                            Unit = "GB"
                        });

                        // 2. Free Space (always static)
                        metrics.Add(new SensorMetric
                        {
                            Id = $"disk_{driveLetter[0]}_free",
                            Name = $"{driveLetter}: Free Space",
                            HardwareId = $"disk_{driveLetter[0]}",
                            HardwareName = cleanName,
                            Category = HardwareCategory.Storage,
                            Type = MetricType.Capacity,
                            Value = Math.Round(freeGb, 1),
                            Unit = "GB"
                        });

                        // 3. Total Space (always static)
                        metrics.Add(new SensorMetric
                        {
                            Id = $"disk_{driveLetter[0]}_total",
                            Name = $"{driveLetter}: Total Space",
                            HardwareId = $"disk_{driveLetter[0]}",
                            HardwareName = cleanName,
                            Category = HardwareCategory.Storage,
                            Type = MetricType.Capacity,
                            Value = Math.Round(totalGb, 1),
                            Unit = "GB"
                        });

                        // 4. Read Throughput (always static, never omitted when 0)
                        metrics.Add(new SensorMetric
                        {
                            Id = $"disk_{driveLetter[0]}_read",
                            Name = $"{driveLetter}: Read Speed",
                            HardwareId = $"disk_{driveLetter[0]}",
                            HardwareName = cleanName,
                            Category = HardwareCategory.Storage,
                            Type = MetricType.Throughput,
                            Value = readMBps,
                            Unit = "MB/s"
                        });

                        // 5. Write Throughput (always static, never omitted when 0)
                        metrics.Add(new SensorMetric
                        {
                            Id = $"disk_{driveLetter[0]}_write",
                            Name = $"{driveLetter}: Write Speed",
                            HardwareId = $"disk_{driveLetter[0]}",
                            HardwareName = cleanName,
                            Category = HardwareCategory.Storage,
                            Type = MetricType.Throughput,
                            Value = writeMBps,
                            Unit = "MB/s"
                        });
                    }
                    catch
                    {
                        // Some drives may reject query while spinning up or sleeping
                    }
                }

                // If physical drive descriptors exist (from SMART probe), merge volume capacity & model names cleanly
                if (summary.Storage != null && summary.Storage.Count > 0)
                {
                    summary.Storage.RemoveAll(d => d.BusType.Equals("USB", StringComparison.OrdinalIgnoreCase) || d.BusType.Equals("SD", StringComparison.OrdinalIgnoreCase));

                    // Map volume info to physical drive entries
                    for (int i = 0; i < summary.Storage.Count; i++)
                    {
                        var physDrive = summary.Storage[i];
                        if (i < volumeList.Count)
                        {
                            var vol = volumeList[i];
                            physDrive.DriveLetter = vol.DriveLetter;
                            physDrive.TotalGb = vol.TotalGb;
                            physDrive.UsedGb = vol.UsedGb;
                            physDrive.ReadSpeedMBps = vol.ReadSpeedMBps;
                            physDrive.WriteSpeedMBps = vol.WriteSpeedMBps;

                            // Format rich name: Model + Drive Letter (e.g., "CT1000P1SSD8 (C:)")
                            if (!string.IsNullOrWhiteSpace(vol.DriveLetter) && !physDrive.Name.Contains(vol.DriveLetter))
                            {
                                physDrive.Name = $"{physDrive.Name} ({vol.DriveLetter})";
                            }

                            // If hardware diode query returned ERROR_INVALID_FUNCTION (common in stornvme on consumer drives),
                            // apply Thermodynamic Observer: Chassis air temperature + drive-specific idle delta + active PDH I/O Joule dissipation
                            if (physDrive.BusType.Equals("USB", StringComparison.OrdinalIgnoreCase))
                            {
                                physDrive.TempC = null;
                                physDrive.IsTempEstimated = false;
                                physDrive.TempSource = string.Empty;
                            }
                            else
                            {
                                if (!physDrive.TempC.HasValue || physDrive.IsTempEstimated)
                                {
                                    double chassisAir = (summary.Chassis != null && summary.Chassis.ChassisAirTempC > 0)
                                        ? summary.Chassis.ChassisAirTempC
                                        : (_lastKnownChassisAir > 0 ? _lastKnownChassisAir : 28.0);
                                    _lastKnownChassisAir = chassisAir;

                                    // Bus & Media type idle offset:
                                    // NVMe M.2 controllers (PCIe 3.0/4.0/5.0) under heatsinks run ~8-11°C above case air.
                                    // SATA 2.5" SSDs operate much cooler at ~2-4°C above case air.
                                    // Mechanical HDDs run ~4-6°C above case air from spindle motor dissipation.
                                    double idleOffset;
                                    if (physDrive.BusType.Contains("NVMe", StringComparison.OrdinalIgnoreCase) || 
                                        physDrive.BusType.Contains("PCIe", StringComparison.OrdinalIgnoreCase))
                                    {
                                        idleOffset = 9.0;
                                    }
                                    else if (physDrive.MediaType.Contains("Hard Disk", StringComparison.OrdinalIgnoreCase) || 
                                             physDrive.MediaType.Contains("HDD", StringComparison.OrdinalIgnoreCase))
                                    {
                                        idleOffset = 5.0;
                                    }
                                    else // SATA SSD
                                    {
                                        idleOffset = 3.0;
                                    }

                                    // Dynamic I/O Joule dissipation (up to +18°C rise under sustained sequential read/write)
                                    double ioRise = Math.Min(18.0, (vol.ReadSpeedMBps + vol.WriteSpeedMBps) * 0.05);
                                    double targetTemp = chassisAir + idleOffset + ioRise;

                                    // Smooth temperature changes with heatsink thermal mass inertia (asymmetric IIR filter)
                                    string driveKey = !string.IsNullOrEmpty(physDrive.SerialNumber) ? physDrive.SerialNumber : (vol.DriveLetter ?? i.ToString());
                                    if (!_driveThermals.TryGetValue(driveKey, out double currentTemp) || currentTemp <= 0)
                                    {
                                        currentTemp = targetTemp;
                                    }
                                    else
                                    {
                                        // Heating tau ~ 3s, Cooling tau ~ 10s (metal heatsink cools gradually)
                                        double alpha = targetTemp > currentTemp ? 0.25 : 0.08;
                                        currentTemp = (currentTemp * (1.0 - alpha)) + (targetTemp * alpha);
                                    }
                                    _driveThermals[driveKey] = currentTemp;

                                    physDrive.TempC = Math.Round(currentTemp, 0);
                                    physDrive.IsTempEstimated = true;
                                    physDrive.TempSource = "Thermodynamic Observer";
                                }

                                if (physDrive.TempC.HasValue && !string.IsNullOrEmpty(vol.DriveLetter))
                                {
                                    // Emit temperature metric
                                    string tempId = $"disk_{vol.DriveLetter[0]}_temp";
                                    recordHistory(tempId, physDrive.TempC.Value);

                                    metrics.Add(new SensorMetric
                                    {
                                        Id = tempId,
                                        Name = $"{vol.DriveLetter}: Temperature",
                                        HardwareId = $"disk_{vol.DriveLetter[0]}",
                                        HardwareName = physDrive.Name,
                                        Category = HardwareCategory.Storage,
                                        Type = MetricType.Temperature,
                                        Value = physDrive.TempC.Value,
                                        Unit = "°C"
                                    });

                                    // Update HardwareName for existing disk metrics to match rich name
                                    foreach (var m in metrics)
                                    {
                                        if (m.HardwareId == $"disk_{vol.DriveLetter[0]}")
                                        {
                                            m.HardwareName = physDrive.Name;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            physDrive.TotalGb = 0;
                            physDrive.UsedGb = 0;
                            physDrive.ReadSpeedMBps = 0;
                            physDrive.WriteSpeedMBps = 0;
                        }
                    }
                }
                else
                {
                    summary.Storage = volumeList;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("StorageHarvester", $"Storage query warning: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_hPdhQuery != IntPtr.Zero)
            {
                try { PdhNative.PdhCloseQuery(_hPdhQuery); } catch { }
                _hPdhQuery = IntPtr.Zero;
            }
        }
    }
}
