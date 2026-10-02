using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using InFox.Models.Sensors;
using InFox.Services;

namespace InFox.Engine.Harvesters
{
    // PDH Native Performance Counters (Zero-Driver, Native OS Interface)
    internal static class PdhNative
    {
        public const uint PDH_FMT_DOUBLE = 0x00000200;
        public const uint PDH_CSTATUS_VALID_DATA = 0x00000000;

        [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int PdhOpenQuery(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);

        [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int PdhAddEnglishCounterW(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

        [DllImport("pdh.dll", SetLastError = true)]
        public static extern int PdhCollectQueryData(IntPtr hQuery);

        [StructLayout(LayoutKind.Explicit)]
        public struct PDH_FMT_COUNTERVALUE
        {
            [FieldOffset(0)] public uint CStatus;
            [FieldOffset(8)] public double doubleValue;
            [FieldOffset(8)] public long longValue;
        }

        [DllImport("pdh.dll", SetLastError = true)]
        public static extern int PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, out uint lpdwType, out PDH_FMT_COUNTERVALUE pValue);

        [DllImport("pdh.dll", SetLastError = true)]
        public static extern int PdhCloseQuery(IntPtr hQuery);
    }

    public class SystemHarvester : ISensorHarvester
    {
        public string Name => "System & Native Telemetry";
        public bool IsAvailable { get; private set; } = true;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;

            public ulong ToUInt64() => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetLogicalProcessorInformationEx(int relationshipType, IntPtr buffer, ref int returnedLength);

        private IntPtr _hPdhQuery = IntPtr.Zero;
        private IntPtr _pdhTotalFreqCounter = IntPtr.Zero;
        private readonly List<IntPtr> _pdhCoreFreqCounters = new();
        private readonly List<IntPtr> _pdhCoreLoadCounters = new();
        private IntPtr _pdhThermalHighCounter = IntPtr.Zero;
        private IntPtr _pdhThermalCounter = IntPtr.Zero;

        private double _estimatedCpuTemp = 35.0;
        private readonly Dictionary<int, double> _estimatedCoreTemps = new();
        private readonly Dictionary<int, double> _coreBaseMhzCache = new();
        private readonly Dictionary<int, (string Name, string Type, double BaseMhz, string Description)> _coreTopology = new();
        private DateTime _lastEstimateTime = DateTime.UtcNow;

        // Load-vs-Frequency Divergence & PROCHOT / Mounting Failure Guard
        private int _throttlingDivergenceTicks = 0;
        private bool _isCurrentlyDivergent = false;

        // Convective Chassis & Ambient Room Temperature Estimator
        private readonly List<(double Watts, double TempC)> _steadyStateSamples = new();
        private DateTime _lastSampleTime = DateTime.MinValue;
        private double _smoothedChassisAir = 32.0;
        private double _smoothedAmbient = 22.5;
        private double _smoothedThermalResistance = 0.55;

        // Motherboard & VRM Power-Stage Observer
        private double _smoothedVrmTemp = 36.0;
        private DateTime _lastVrmEstimateTime = DateTime.UtcNow;
        private string? _mbManufacturer;
        private string? _mbModel;

        private ulong _prevIdleTime = 0;
        private ulong _prevKernelTime = 0;
        private ulong _prevUserTime = 0;

        private readonly Dictionary<string, (long Rx, long Tx)> _prevAdapterBytes = new();
        private DateTime _prevNetworkTime = DateTime.UtcNow;

        public Task InitializeAsync()
        {
            try
            {
                // Prime initial times
                if (GetSystemTimes(out var idle, out var kernel, out var user))
                {
                    _prevIdleTime = idle.ToUInt64();
                    _prevKernelTime = kernel.ToUInt64();
                    _prevUserTime = user.ToUInt64();
                }

                // Prime network counters
                try
                {
                    foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        var stats = nic.GetIPStatistics();
                        _prevAdapterBytes[nic.Id] = (stats.BytesReceived, stats.BytesSent);
                    }
                }
                catch { }
                _prevNetworkTime = DateTime.UtcNow;

                // Initialize PDH Query for zero-driver frequencies and thermals
                InitPdhCounters();

                // Query physical memory speed (MT/s) via SMBIOS / WMI
                _ = Task.Run(InitRamSpeed);
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("SystemHarvester", $"Initialization warning: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        private void InitPdhCounters()
        {
            try
            {
                int res = PdhNative.PdhOpenQuery(null, IntPtr.Zero, out _hPdhQuery);
                if (res != 0 || _hPdhQuery == IntPtr.Zero) return;

                // Total CPU Frequency (% Processor Performance gives dynamic live ratio against base)
                PdhNative.PdhAddEnglishCounterW(_hPdhQuery, @"\Processor Information(0,_Total)\% Processor Performance", IntPtr.Zero, out _pdhTotalFreqCounter);

                // Per-Core Frequency (% Processor Performance) and Load counters for all logical cores
                int coreCount = Environment.ProcessorCount;
                for (int i = 0; i < coreCount; i++)
                {
                    string pathFreq = $@"\Processor Information(0,{i})\% Processor Performance";
                    if (PdhNative.PdhAddEnglishCounterW(_hPdhQuery, pathFreq, IntPtr.Zero, out var hCoreCounter) == 0)
                    {
                        _pdhCoreFreqCounters.Add(hCoreCounter);
                    }

                    string pathLoad = $@"\Processor Information(0,{i})\% Processor Time";
                    if (PdhNative.PdhAddEnglishCounterW(_hPdhQuery, pathLoad, IntPtr.Zero, out var hCoreLoad) == 0)
                    {
                        _pdhCoreLoadCounters.Add(hCoreLoad);
                    }
                }

                // ACPI Thermal Zone Counters
                PdhNative.PdhAddEnglishCounterW(_hPdhQuery, @"\Thermal Zone Information(\_TZ.TZ00)\High Precision Temperature", IntPtr.Zero, out _pdhThermalHighCounter);
                PdhNative.PdhAddEnglishCounterW(_hPdhQuery, @"\Thermal Zone Information(\_TZ.TZ00)\Temperature", IntPtr.Zero, out _pdhThermalCounter);

                // Prime counters
                PdhNative.PdhCollectQueryData(_hPdhQuery);
                LoggingService.Instance.Info("SystemHarvester", $"Native PDH interface active with {_pdhCoreFreqCounters.Count} frequency and {_pdhCoreLoadCounters.Count} load probes.");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("SystemHarvester", $"PDH counter init failed: {ex.Message}");
            }
        }

        private int? _cachedRamSpeedMts;

        private void InitRamSpeed()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
                int maxSpeed = 0;
                foreach (var obj in searcher.Get())
                {
                    if (obj["ConfiguredClockSpeed"] != null && Convert.ToInt32(obj["ConfiguredClockSpeed"]) > 0)
                    {
                        int s = Convert.ToInt32(obj["ConfiguredClockSpeed"]);
                        if (s > maxSpeed) maxSpeed = s;
                    }
                    else if (obj["Speed"] != null && Convert.ToInt32(obj["Speed"]) > 0)
                    {
                        int s = Convert.ToInt32(obj["Speed"]);
                        if (s > maxSpeed) maxSpeed = s;
                    }
                }
                if (maxSpeed > 0)
                {
                    _cachedRamSpeedMts = maxSpeed;
                    LoggingService.Instance.Info("SystemHarvester", $"Detected physical RAM speed: {_cachedRamSpeedMts} MT/s");
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("SystemHarvester", $"Failed to query RAM speed via WMI: {ex.Message}");
            }
        }

        private static string? _nativeCpuName;
        public static string GetNativeCpuName()
        {
            if (_nativeCpuName != null) return _nativeCpuName;
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                var name = key?.GetValue("ProcessorNameString") as string;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _nativeCpuName = name.Trim();
                    return _nativeCpuName;
                }
            }
            catch { }
            _nativeCpuName = "Central Processor";
            return _nativeCpuName;
        }

        public void Poll(SystemSummary summary, List<SensorMetric> metrics, Action<string, double> recordHistory)
        {
            if (string.IsNullOrEmpty(summary.Cpu.Name) || summary.Cpu.Name == "Unknown CPU")
            {
                summary.Cpu.Name = GetNativeCpuName();
            }

            PollMemory(summary, metrics, recordHistory);
            PollBattery(summary, metrics, recordHistory);
            PollCpuLoadFallback(summary, metrics, recordHistory);
            PollPdhCpuData(summary, metrics, recordHistory);
            PollNetwork(summary, metrics, recordHistory);
            EstimateChassisAndAmbient(summary, metrics, recordHistory);
            EvaluateTimDiagnostics(summary);
            PollMotherboardAndVrm(summary, metrics, recordHistory);
            EmitCpuMetrics(summary, metrics, recordHistory);
        }

        private void PollPdhCpuData(SystemSummary summary, List<SensorMetric> metrics, Action<string, double> recordHistory)
        {
            if (_hPdhQuery == IntPtr.Zero) return;

            try
            {
                PdhNative.PdhCollectQueryData(_hPdhQuery);

                // 1. Process CPU Total & Per-Core Frequencies (Zero Driver!)
                double sysBaseMhz = GetCoreBaseMhz(0);
                if (_pdhTotalFreqCounter != IntPtr.Zero)
                {
                    if (PdhNative.PdhGetFormattedCounterValue(_pdhTotalFreqCounter, PdhNative.PDH_FMT_DOUBLE, out _, out var val) == 0 && val.CStatus == PdhNative.PDH_CSTATUS_VALID_DATA)
                    {
                        if (!double.IsNaN(val.doubleValue) && val.doubleValue > 0)
                        {
                            double liveMhz = (val.doubleValue <= 500.0)
                                ? sysBaseMhz * (val.doubleValue / 100.0)
                                : val.doubleValue;
                            summary.Cpu.ClockGhz = Math.Round(liveMhz / 1000.0, 2);
                        }
                    }
                }

                if (!summary.Cpu.ClockGhz.HasValue || double.IsNaN(summary.Cpu.ClockGhz.Value) || summary.Cpu.ClockGhz.Value <= 0)
                {
                    summary.Cpu.ClockGhz = Math.Round(sysBaseMhz / 1000.0, 2);
                }

                InitCoreTopology();

                // If cores do not exist yet, initialize them from PDH instance count
                if (summary.Cpu.Cores.Count == 0 && _pdhCoreFreqCounters.Count > 0)
                {
                    for (int i = 0; i < _pdhCoreFreqCounters.Count; i++)
                    {
                        string name = _coreTopology.TryGetValue(i, out var topN) && !string.IsNullOrEmpty(topN.Name) ? topN.Name : $"Core #{i + 1}";
                        string? coreType = _coreTopology.TryGetValue(i, out var top) && !string.IsNullOrEmpty(top.Type) ? top.Type : null;
                        string? desc = _coreTopology.TryGetValue(i, out var topD) && !string.IsNullOrEmpty(topD.Description) ? topD.Description : null;
                        summary.Cpu.Cores.Add(new CoreMetric
                        {
                            Name = name,
                            Type = coreType,
                            Description = desc,
                            LoadPercent = summary.Cpu.LoadPercent
                        });
                    }
                }

                // If cores exist, map frequencies and real utilization directly to each core
                bool anyCoreBoosting = false;
                for (int i = 0; i < _pdhCoreFreqCounters.Count && i < summary.Cpu.Cores.Count; i++)
                {
                    var hCore = _pdhCoreFreqCounters[i];
                    if (PdhNative.PdhGetFormattedCounterValue(hCore, PdhNative.PDH_FMT_DOUBLE, out _, out var val) == 0 && val.CStatus == PdhNative.PDH_CSTATUS_VALID_DATA)
                    {
                        if (!double.IsNaN(val.doubleValue) && val.doubleValue > 0)
                        {
                            if (_coreTopology.TryGetValue(i, out var top))
                            {
                                if (!string.IsNullOrEmpty(top.Name)) summary.Cpu.Cores[i].Name = top.Name;
                                if (!string.IsNullOrEmpty(top.Type)) summary.Cpu.Cores[i].Type = top.Type;
                                if (!string.IsNullOrEmpty(top.Description)) summary.Cpu.Cores[i].Description = top.Description;
                            }

                            // Dynamic live MHz from % Processor Performance (scaled against system nominal base)
                            double coreMhz = (val.doubleValue <= 500.0)
                                ? Math.Round(sysBaseMhz * (val.doubleValue / 100.0), 0)
                                : Math.Round(val.doubleValue, 0);

                            summary.Cpu.Cores[i].ClockMhz = coreMhz;

                            // Boost detection: E-cores use E-core base (e.g. 2400 MHz), P-cores use package base (e.g. 3200 MHz)
                            double effectiveBaseMhz = (_coreTopology.TryGetValue(i, out var t) && t.BaseMhz > 0) ? t.BaseMhz : sysBaseMhz;
                            bool isBoost = coreMhz > (effectiveBaseMhz + 50.0);
                            summary.Cpu.Cores[i].IsBoost = isBoost;
                            if (isBoost) anyCoreBoosting = true;
                        }
                    }
                }

                summary.Cpu.IsBoost = anyCoreBoosting || ((summary.Cpu.ClockGhz.HasValue ? summary.Cpu.ClockGhz.Value * 1000.0 : 0.0) > (sysBaseMhz + 50.0));

                for (int i = 0; i < _pdhCoreLoadCounters.Count && i < summary.Cpu.Cores.Count; i++)
                {
                    var hLoad = _pdhCoreLoadCounters[i];
                    if (PdhNative.PdhGetFormattedCounterValue(hLoad, PdhNative.PDH_FMT_DOUBLE, out _, out var val) == 0 && val.CStatus == PdhNative.PDH_CSTATUS_VALID_DATA)
                    {
                        if (!double.IsNaN(val.doubleValue) && val.doubleValue >= 0)
                        {
                            summary.Cpu.Cores[i].LoadPercent = Math.Round(Math.Clamp(val.doubleValue, 0.0, 100.0), 1);
                        }
                    }
                }

                // 2. Process CPU Temperature (Dynamic Newton Thermodynamic Model + ACPI Zone)
                PollCpuTemperature(summary, recordHistory);

                // 3. Process CPU Package Power (Dynamic CMOS Power Model)
                PollCpuPowerFallback(summary, recordHistory);

                // 4. Enrich Integrated GPUs (AMD APUs & Intel UHD / Iris / Arc)
                EnrichIntegratedGpus(summary, recordHistory);
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("SystemHarvester", $"Error reading PDH telemetry: {ex.Message}");
            }
        }

        private static (double BaseTdp, double SiliconOffset) ClassifySiliconTier(string? cpuName, int coreCount, bool isLaptop)
        {
            string name = (cpuName ?? "").ToLowerInvariant();

            if (isLaptop)
            {
                if (name.Contains("hx") || name.Contains("9945") || name.Contains("14900hx") || name.Contains("13900hx"))
                {
                    return (157.0, 4.0); // Desktop-replacement DTR mobile
                }
                if (name.Contains("h") || name.Contains("hs"))
                {
                    return (65.0, 2.0); // High performance mobile
                }
                return (28.0, 0.0); // Standard thin & light / U-series
            }

            // Flagship Desktop (i9-14900K/13900K, Ryzen 9 7950X/7900X, Core Ultra 9, Threadripper)
            if (name.Contains("i9") || name.Contains("ultra 9") || name.Contains("7950") || name.Contains("7900") || name.Contains("5950") || name.Contains("5900") || name.Contains("threadripper") || coreCount >= 24)
            {
                return (253.0, 7.5);
            }

            // Performance Desktop (i7-14700K/13700K, Ryzen 7 7800X3D/7700X, Core Ultra 7)
            if (name.Contains("i7") || name.Contains("ultra 7") || name.Contains("7800x3d") || name.Contains("7700") || name.Contains("5800x3d") || name.Contains("5800") || coreCount >= 16)
            {
                return (170.0, 3.5);
            }

            // Mainstream Desktop (i5-14600K/13600K, Ryzen 5 7600X, Core Ultra 5)
            if (name.Contains("i5") || name.Contains("ultra 5") || name.Contains("7600") || name.Contains("5600"))
            {
                return (125.0, 1.0);
            }

            // Standard Desktop Baseline
            return (105.0, 0.0);
        }

        private void PollCpuPowerFallback(SystemSummary summary, Action<string, double> recordHistory)
        {
            double cpuLoad = (!double.IsNaN(summary.Cpu.LoadPercent) && summary.Cpu.LoadPercent >= 0) ? summary.Cpu.LoadPercent : 0.0;
            double loadRatio = Math.Clamp(cpuLoad / 100.0, 0.0, 1.0);

            double clock = (summary.Cpu.ClockGhz.HasValue && !double.IsNaN(summary.Cpu.ClockGhz.Value) && summary.Cpu.ClockGhz.Value > 0)
                ? summary.Cpu.ClockGhz.Value
                : 3.2;
            double freqRatio = Math.Clamp(clock / 3.2, 0.8, 1.8);

            var cfg = ConfigManager.Instance.Config;
            bool isLaptop = summary.Battery.HasBattery || (cfg.ChassisProfile != null && cfg.ChassisProfile.Equals("Laptop", StringComparison.OrdinalIgnoreCase));
            var (autoTdp, _) = ClassifySiliconTier(summary.Cpu.Name, summary.Cpu.CoreCount, isLaptop);

            double baseTdp = cfg.CpuTdpOverrideWatts > 0 ? cfg.CpuTdpOverrideWatts : autoTdp;
            double powerEst = 12.0 + (loadRatio * Math.Pow(freqRatio, 1.8) * (baseTdp * 1.15));
            if (double.IsNaN(powerEst)) powerEst = 15.0;

            double finalWatts = Math.Round(powerEst, 1);
            summary.Cpu.PowerWatts = finalWatts;
            recordHistory("cpu_power", finalWatts);
        }

        private void EnrichIntegratedGpus(SystemSummary summary, Action<string, double> recordHistory)
        {
            foreach (var gpu in summary.Gpus)
            {
                if (!gpu.IsDiscrete)
                {
                    // Integrated GPU is part of the CPU die, so it shares the package temperature if no dedicated diode
                    if (!gpu.TempC.HasValue && summary.Cpu.TempC.HasValue && !double.IsNaN(summary.Cpu.TempC.Value))
                    {
                        gpu.TempC = summary.Cpu.TempC.Value;
                    }

                    // Default idle load if D3D engine not actively reported
                    if (!gpu.LoadPercent.HasValue || double.IsNaN(gpu.LoadPercent.Value))
                    {
                        gpu.LoadPercent = 0.0;
                    }

                    // Dynamic power slice of the CPU package based on iGPU load
                    if (!gpu.PowerWatts.HasValue && summary.Cpu.PowerWatts.HasValue && !double.IsNaN(summary.Cpu.PowerWatts.Value) && summary.Cpu.PowerWatts.Value > 0)
                    {
                        double loadFactor = Math.Clamp((gpu.LoadPercent ?? 0.0) / 100.0, 0.0, 1.0);
                        double sliceFraction = 0.10 + (loadFactor * 0.45);
                        double igpuPower = Math.Min(summary.Cpu.PowerWatts.Value, Math.Max(0.5, summary.Cpu.PowerWatts.Value * sliceFraction));
                        gpu.PowerWatts = Math.Round(igpuPower, 1);
                    }

                    // An iGPU has no dedicated fan or separate hotspot diode
                    gpu.FanRpm = null;
                    gpu.HotspotTempC = null;

                    // If this is the primary or sole GPU, feed the history buffers
                    if (gpu.Id == summary.PrimaryGpu.Id || summary.Gpus.Count == 1)
                    {
                        if (gpu.TempC.HasValue && !double.IsNaN(gpu.TempC.Value)) recordHistory("gpu_temp", gpu.TempC.Value);
                        if (gpu.PowerWatts.HasValue && !double.IsNaN(gpu.PowerWatts.Value)) recordHistory("gpu_power", gpu.PowerWatts.Value);
                        if (gpu.LoadPercent.HasValue && !double.IsNaN(gpu.LoadPercent.Value)) recordHistory("gpu_load", gpu.LoadPercent.Value);
                    }
                }
            }
        }

        private void PollCpuTemperature(SystemSummary summary, Action<string, double> recordHistory)
        {
            double? acpiTemp = null;

            // Try High Precision ACPI Thermal Zone (tenths of Kelvin, e.g. 3010 -> 27.85 C)
            if (_pdhThermalHighCounter != IntPtr.Zero)
            {
                if (PdhNative.PdhGetFormattedCounterValue(_pdhThermalHighCounter, PdhNative.PDH_FMT_DOUBLE, out _, out var val) == 0 && val.CStatus == PdhNative.PDH_CSTATUS_VALID_DATA)
                {
                    double degC = (val.doubleValue / 10.0) - 273.15;
                    if (!double.IsNaN(degC) && degC > 5 && degC < 115) acpiTemp = degC;
                }
            }

            // Fallback to standard Kelvin counter
            if (!acpiTemp.HasValue && _pdhThermalCounter != IntPtr.Zero)
            {
                if (PdhNative.PdhGetFormattedCounterValue(_pdhThermalCounter, PdhNative.PDH_FMT_DOUBLE, out _, out var val) == 0 && val.CStatus == PdhNative.PDH_CSTATUS_VALID_DATA)
                {
                    double degC = val.doubleValue - 273.15;
                    if (!double.IsNaN(degC) && degC > 5 && degC < 115) acpiTemp = degC;
                }
            }

            var cfg = ConfigManager.Instance.Config;

            // Derive realistic die temperature using dynamic thermodynamic Newton cooling model
            double ambientAnchor = 28.0;
            // Only anchor to discrete GPU diode if available, never circular to integrated APU
            var discreteGpu = summary.Gpus.Find(g => g.IsDiscrete && g.TempC.HasValue && !double.IsNaN(g.TempC.Value) && g.TempC.Value > 20);
            if (discreteGpu != null)
            {
                // Discrete GPU die in idle/desktop load runs ~8-10 C above ambient chassis air intake
                ambientAnchor = Math.Clamp(discreteGpu.TempC!.Value - 8.0, 24.0, 36.0);
            }
            else if (acpiTemp.HasValue && !double.IsNaN(acpiTemp.Value))
            {
                ambientAnchor = Math.Clamp(acpiTemp.Value, 24.0, 38.0);
            }

            // Cooler Profile Scaling
            double coolerDissipationFactor = 0.80; // Default balanced tower air
            double baseIdleOffset = 0.0;

            string profile = (cfg.CoolerProfile ?? "Auto").Trim();
            if (profile.Equals("AIO", StringComparison.OrdinalIgnoreCase))
            {
                coolerDissipationFactor = 0.65; // High dissipation AIO (240mm-360mm+)
                baseIdleOffset = -2.0;
            }
            else if (profile.Equals("TowerAir", StringComparison.OrdinalIgnoreCase))
            {
                coolerDissipationFactor = 0.80; // Standard / Dual-tower desktop air
                baseIdleOffset = 0.0;
            }
            else if (profile.Equals("Compact", StringComparison.OrdinalIgnoreCase))
            {
                coolerDissipationFactor = 1.05; // Low-profile / laptop / stock box cooler
                baseIdleOffset = 3.0;
            }
            else // Auto
            {
                if (summary.Battery.HasBattery || cfg.ChassisProfile.Equals("Laptop", StringComparison.OrdinalIgnoreCase))
                {
                    coolerDissipationFactor = 1.05;
                    baseIdleOffset = 2.0;
                }
                else
                {
                    coolerDissipationFactor = 0.80; // Modern desktop default
                    baseIdleOffset = 0.0;
                }
            }

            double cpuLoad = (!double.IsNaN(summary.Cpu.LoadPercent) && summary.Cpu.LoadPercent >= 0) ? summary.Cpu.LoadPercent : 0.0;
            double loadRatio = Math.Clamp(cpuLoad / 100.0, 0.0, 1.0);

            double clock = (summary.Cpu.ClockGhz.HasValue && !double.IsNaN(summary.Cpu.ClockGhz.Value) && summary.Cpu.ClockGhz.Value > 0)
                ? summary.Cpu.ClockGhz.Value
                : 3.2;
            double freqRatio = Math.Clamp(clock / 3.2, 0.8, 1.8);

            bool isLaptop = summary.Battery.HasBattery || (cfg.ChassisProfile != null && cfg.ChassisProfile.Equals("Laptop", StringComparison.OrdinalIgnoreCase));
            var (_, siliconOffset) = ClassifySiliconTier(summary.Cpu.Name, summary.Cpu.CoreCount, isLaptop);

            // Calculate live thermodynamic target
            double dynamicHeatRise = (loadRatio * freqRatio * 44.0 * coolerDissipationFactor) + (loadRatio * 6.0);
            double targetTemp = ambientAnchor + baseIdleOffset + dynamicHeatRise + 3.0 + siliconOffset + cfg.CpuThermalOffset;
            if (double.IsNaN(targetTemp)) targetTemp = 38.0;

            // Load-vs-Frequency Divergence Check (PROCHOT & Mounting Failure Guard)
            // High compute demand with cratered silicon clocks on wall power indicates physical cooling failure or thermal throttling.
            bool isWallPower = !summary.Battery.HasBattery || summary.Battery.IsPluggedIn;
            bool isThrottlingCondition = loadRatio >= 0.70 && clock <= 1.6 && isWallPower;

            if (isThrottlingCondition)
            {
                _throttlingDivergenceTicks = Math.Min(10, _throttlingDivergenceTicks + 1);
            }
            else
            {
                _throttlingDivergenceTicks = Math.Max(0, _throttlingDivergenceTicks - 1);
            }

            // 3-tick temporal hysteresis prevents false positives on transient profile switching
            _isCurrentlyDivergent = _throttlingDivergenceTicks >= 3;
            summary.Cpu.IsThrottlingDivergence = _isCurrentlyDivergent;

            if (_isCurrentlyDivergent)
            {
                // PROCHOT floor clamp overrides dynamic dissipation collapse before entering low-pass filter
                double throttleCeiling = 95.0 + cfg.CpuThermalOffset;
                targetTemp = Math.Max(targetTemp, throttleCeiling);
            }

            // Thermal inertia low-pass filter (Newton's law of cooling: tau ~ 1.5s heating, ~4s cooling)
            DateTime now = DateTime.UtcNow;
            double dt = Math.Clamp((now - _lastEstimateTime).TotalSeconds, 0.1, 5.0);
            _lastEstimateTime = now;

            if (double.IsNaN(_estimatedCpuTemp) || _estimatedCpuTemp <= 0) _estimatedCpuTemp = 35.0;

            double alpha = targetTemp > _estimatedCpuTemp ? (dt / 1.5) : (dt / 4.0);
            alpha = Math.Clamp(alpha, 0.05, 0.95);
            _estimatedCpuTemp = (_estimatedCpuTemp * (1.0 - alpha)) + (targetTemp * alpha);
            if (double.IsNaN(_estimatedCpuTemp)) _estimatedCpuTemp = 42.0;

            double finalTemp = Math.Round(_estimatedCpuTemp, 1);
            summary.Cpu.TempC = finalTemp;
            recordHistory("cpu_temp", finalTemp);

            // Localized Newtonian Thermal Flux Modeling across Silicon Cores
            if (summary.Cpu.Cores.Count > 0)
            {
                for (int i = 0; i < summary.Cpu.Cores.Count; i++)
                {
                    var core = summary.Cpu.Cores[i];
                    double coreLoad = (core.LoadPercent.HasValue && !double.IsNaN(core.LoadPercent.Value)) ? core.LoadPercent.Value : cpuLoad;
                    double coreClock = (core.ClockMhz.HasValue && !double.IsNaN(core.ClockMhz.Value) && core.ClockMhz.Value > 500)
                        ? core.ClockMhz.Value
                        : (clock * 1000.0);

                    // SMT Silicon Physical Pairing: Logical cores (2k, 2k+1) share the same physical Zen core
                    int siblingIdx = (i % 2 == 0) ? (i + 1) : (i - 1);
                    double siblingLoad = (siblingIdx >= 0 && siblingIdx < summary.Cpu.Cores.Count && summary.Cpu.Cores[siblingIdx].LoadPercent.HasValue)
                        ? summary.Cpu.Cores[siblingIdx].LoadPercent!.Value
                        : coreLoad;

                    // Physical silicon heat generation combines primary thread work and secondary SMT work
                    double effectivePhysicalLoad = Math.Clamp((coreLoad * 0.70) + (siblingLoad * 0.30), 0.0, 100.0);
                    double freqRatioCore = Math.Clamp(coreClock / 3200.0, 0.7, 1.8);

                    // Local thermal delta relative to package average (-3C at idle/parked up to +12C on high boost)
                    double loadDelta = (effectivePhysicalLoad - cpuLoad) / 100.0;
                    double targetCoreTemp = finalTemp + (loadDelta * freqRatioCore * 12.0);

                    if (!_estimatedCoreTemps.TryGetValue(i, out double coreEst) || double.IsNaN(coreEst) || coreEst <= 0)
                    {
                        coreEst = finalTemp;
                    }

                    // Thermal diffusion low-pass filter (heating tau ~ 1.8s, cooling tau ~ 3.5s)
                    double alphaCore = targetCoreTemp > coreEst ? (dt / 1.8) : (dt / 3.5);
                    alphaCore = Math.Clamp(alphaCore, 0.05, 0.95);
                    coreEst = (coreEst * (1.0 - alphaCore)) + (targetCoreTemp * alphaCore);
                    _estimatedCoreTemps[i] = coreEst;

                    core.TempC = Math.Round(coreEst, 0);
                }
            }
        }

        private static bool? _isVmCached;
        public static bool DetectVirtualMachine()
        {
            if (_isVmCached.HasValue) return _isVmCached.Value;

            try
            {
                // Query system BIOS & hardware manufacturer strings
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                if (key != null)
                {
                    string mfg = (key.GetValue("SystemManufacturer") as string ?? "").ToLowerInvariant();
                    string prod = (key.GetValue("SystemProductName") as string ?? "").ToLowerInvariant();
                    string bios = (key.GetValue("BIOSVersion") as string ?? "").ToLowerInvariant();

                    string combined = $"{mfg} {prod} {bios}";
                    if (combined.Contains("vmware") ||
                        combined.Contains("virtualbox") ||
                        combined.Contains("innotek") ||
                        combined.Contains("qemu") ||
                        combined.Contains("kvm") ||
                        combined.Contains("xen") ||
                        combined.Contains("bochs") ||
                        combined.Contains("parallels") ||
                        combined.Contains("amazon ec2") ||
                        (mfg.Contains("microsoft") && (prod.Contains("virtual") || bios.Contains("vmbus"))))
                    {
                        _isVmCached = true;
                        return true;
                    }
                }
            }
            catch { }

            _isVmCached = false;
            return false;
        }

        private void EstimateChassisAndAmbient(SystemSummary summary, List<SensorMetric> metrics, Action<string, double> recordHistory)
        {
            bool isVm = DetectVirtualMachine();
            summary.IsVirtualMachine = isVm;
            summary.Chassis.IsVirtualMachine = isVm;

            double watts = (summary.Cpu.PowerWatts.HasValue && !double.IsNaN(summary.Cpu.PowerWatts.Value) && summary.Cpu.PowerWatts.Value > 0)
                ? summary.Cpu.PowerWatts.Value
                : 18.0;

            double temp = (summary.Cpu.TempC.HasValue && !double.IsNaN(summary.Cpu.TempC.Value) && summary.Cpu.TempC.Value > 0)
                ? summary.Cpu.TempC.Value
                : 40.0;

            if (isVm)
            {
                // Compute Additive Thermal Contribution: Delta T = P_workload * R_thermal
                double deltaT = Math.Round(Math.Clamp(watts * 0.40, 1.0, 45.0), 1);
                summary.Chassis.AdditiveThermalDeltaC = deltaT;
                summary.Chassis.ConfidencePercent = 90;
                summary.Chassis.Note = "Running inside a Virtual Machine. Thermal readings reflect additive thermal impact (ΔT) contributed by this virtual workload to the host socket.";
                summary.Chassis.ChassisAirTempC = deltaT;
                summary.Chassis.EstimatedAmbientTempC = 0;
                recordHistory("vm_thermal_delta", deltaT);
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (!_isCurrentlyDivergent && (now - _lastSampleTime).TotalSeconds >= 3.0)
            {
                _lastSampleTime = now;
                _steadyStateSamples.Add((watts, temp));
                if (_steadyStateSamples.Count > 30)
                {
                    _steadyStateSamples.RemoveAt(0);
                }
            }

            string profile = ConfigManager.Instance.Config.ChassisProfile ?? "Auto";
            bool isLaptop = profile == "Laptop" || (profile == "Auto" && summary.Battery.HasBattery);

            // Realistic total thermal resistance (silicon junction + heatpipe + fin stack)
            // Laptop: ~0.55 to 0.75 °C/W; Desktop tower: ~0.35 to 0.50 °C/W
            double defaultR = isLaptop ? 0.65 : 0.42;
            double r = defaultR;
            int confidence = 75;

            if (_steadyStateSamples.Count >= 6)
            {
                double minW = double.MaxValue, maxW = double.MinValue;
                double sumW = 0, sumT = 0;
                foreach (var (w, t) in _steadyStateSamples)
                {
                    if (w < minW) minW = w;
                    if (w > maxW) maxW = w;
                    sumW += w;
                    sumT += t;
                }

                double spread = maxW - minW;
                if (spread >= 3.5)
                {
                    double avgW = sumW / _steadyStateSamples.Count;
                    double avgT = sumT / _steadyStateSamples.Count;

                    double num = 0, den = 0;
                    foreach (var (w, t) in _steadyStateSamples)
                    {
                        double dw = w - avgW;
                        double dtVal = t - avgT;
                        num += dw * dtVal;
                        den += dw * dw;
                    }

                    if (den > 0.001)
                    {
                        double calculatedR = num / den;
                        if (calculatedR >= 0.25 && calculatedR <= 1.20)
                        {
                            r = calculatedR;
                            confidence = Math.Min(95, 75 + (int)(spread * 2));
                        }
                    }
                }
            }

            // Low-pass filter the thermal resistance
            _smoothedThermalResistance = (_smoothedThermalResistance * 0.96) + (r * 0.04);

            // Anchor to physical passive sensors if available (Zero-privilege hardware truth)
            double? physicalChassisAnchor = null;

            // 1. Storage NVMe SSD Diode (Passive PCB inside chassis cavity)
            if (summary.Storage != null && summary.Storage.Count > 0)
            {
                var validDrives = summary.Storage.FindAll(d => d.TempC.HasValue && !d.IsTempEstimated && !double.IsNaN(d.TempC.Value) && d.TempC.Value > 15 && d.TempC.Value < 80);
                if (validDrives.Count > 0)
                {
                    // In a laptop, NVMe runs ~4-6°C above internal chassis air; in desktop ~6-9°C above case air
                    double ssdDelta = isLaptop ? 5.0 : 7.0;
                    double minSsdTemp = validDrives.Min(d => d.TempC!.Value);
                    physicalChassisAnchor = Math.Max(22.0, minSsdTemp - ssdDelta);
                    confidence = Math.Max(confidence, 88);
                }
            }

            // 2. Discrete GPU diode at idle/desktop load (< 55°C)
            var discreteGpu = summary.Gpus.Find(g => g.IsDiscrete && g.TempC.HasValue && !double.IsNaN(g.TempC.Value) && g.TempC.Value > 20 && g.TempC.Value < 55);
            if (discreteGpu != null)
            {
                double gpuAnchor = discreteGpu.TempC!.Value - (isLaptop ? 6.0 : 8.0);
                physicalChassisAnchor = physicalChassisAnchor.HasValue
                    ? (physicalChassisAnchor.Value * 0.5 + gpuAnchor * 0.5)
                    : gpuAnchor;
                confidence = Math.Max(confidence, 92);
            }

            // 3. Fallback: Dynamic Zero-Watt Intercept
            // Base idle wattage (~10W) maintains silicon at ~ambient + baseIdleOffset. Dynamic rise is only from watts above idle.
            double activeWatts = Math.Max(0.0, watts - 10.0);
            double dynamicModelChassis = temp - (_smoothedThermalResistance * activeWatts) - (isLaptop ? 3.0 : 1.0);
            double chassisMinFloor = isLaptop ? 27.0 : 23.0;
            double chassisMaxCeil = isLaptop ? 45.0 : 42.0;
            dynamicModelChassis = Math.Clamp(dynamicModelChassis, chassisMinFloor, chassisMaxCeil);

            double instantChassis = physicalChassisAnchor.HasValue
                ? (physicalChassisAnchor.Value * 0.70 + dynamicModelChassis * 0.30)
                : dynamicModelChassis;
            instantChassis = Math.Clamp(instantChassis, chassisMinFloor, chassisMaxCeil);

            // Convective air cavity-to-room temperature delta
            // Laptop internal cavity is ~8.5°C above room; Desktop case air is ~4.5°C above room
            double convectionOffset = isLaptop ? 8.5 : 4.5;
            double instantAmbientBase = instantChassis - convectionOffset;

            // Gentle Bayesian normalization toward typical human indoor comfort (22.0°C / 71.6°F)
            instantAmbientBase = (instantAmbientBase * 0.75) + (22.0 * 0.25);
            instantAmbientBase = Math.Clamp(instantAmbientBase, 15.0, 36.0);

            // Smooth the baseline physics with low-pass filter
            _smoothedChassisAir = (_smoothedChassisAir * 0.95) + (instantChassis * 0.05);
            _smoothedAmbient = (_smoothedAmbient * 0.95) + (instantAmbientBase * 0.05);

            // 1. Room Ambient BLE Sensor
            var bleReading = BTHomeBleService.Instance.GetSelectedOrNearestReading(ConfigManager.Instance.Config.BleAmbientSensorMac);
            bool useBle = ConfigManager.Instance.Config.EnableBleAmbient && bleReading != null && (DateTime.UtcNow - bleReading.LastSeenUtc).TotalMinutes < 10;

            double userOffset = ConfigManager.Instance.Config.RoomAmbientOffsetC;
            double finalAmbient;

            if (useBle && bleReading != null)
            {
                summary.Chassis.IsAmbientMeasured = true;
                summary.Chassis.AmbientSensorName = bleReading.DisplayName;
                summary.Chassis.AmbientHumidityPercent = bleReading.HumidityPercent;
                finalAmbient = Math.Clamp(bleReading.TemperatureC + userOffset, 5.0, 50.0);
            }
            else
            {
                summary.Chassis.IsAmbientMeasured = false;
                summary.Chassis.AmbientSensorName = null;
                summary.Chassis.AmbientHumidityPercent = null;
                finalAmbient = Math.Clamp(_smoothedAmbient + userOffset, 10.0, 45.0);
            }

            // 2. In-Case Air BLE Sensor (Requires explicit selected sensor, no Auto fallback)
            var bleChassisReading = BTHomeBleService.Instance.GetSpecificReading(ConfigManager.Instance.Config.BleChassisSensorMac);
            bool useBleChassis = ConfigManager.Instance.Config.EnableBleChassis && bleChassisReading != null && (DateTime.UtcNow - bleChassisReading.LastSeenUtc).TotalMinutes < 10;

            if (useBleChassis && bleChassisReading != null)
            {
                summary.Chassis.IsChassisAirMeasured = true;
                summary.Chassis.ChassisAirSensorName = bleChassisReading.DisplayName;
                summary.Chassis.ChassisAirHumidityPercent = bleChassisReading.HumidityPercent;
                summary.Chassis.ChassisAirTempC = Math.Round(bleChassisReading.TemperatureC, 1);
            }
            else
            {
                summary.Chassis.IsChassisAirMeasured = false;
                summary.Chassis.ChassisAirSensorName = null;
                summary.Chassis.ChassisAirHumidityPercent = null;
                summary.Chassis.ChassisAirTempC = Math.Round(_smoothedChassisAir, 1);
            }

            // 3. Post-Radiator Exhaust BLE Sensor (Requires explicit selected sensor, no Auto fallback)
            var bleRadReading = BTHomeBleService.Instance.GetSpecificReading(ConfigManager.Instance.Config.BleRadiatorSensorMac);
            bool useBleRad = ConfigManager.Instance.Config.EnableBleRadiator && bleRadReading != null && (DateTime.UtcNow - bleRadReading.LastSeenUtc).TotalMinutes < 10;

            if (useBleRad && bleRadReading != null)
            {
                summary.Chassis.IsRadiatorExhaustMeasured = true;
                summary.Chassis.RadiatorExhaustSensorName = bleRadReading.DisplayName;
                summary.Chassis.RadiatorExhaustHumidityPercent = bleRadReading.HumidityPercent;
                summary.Chassis.RadiatorExhaustTempC = Math.Round(bleRadReading.TemperatureC, 1);
                double deltaT = bleRadReading.TemperatureC - finalAmbient;
                summary.Chassis.RadiatorDeltaTC = Math.Round(deltaT, 1);

                recordHistory("radiator_exhaust_temp", summary.Chassis.RadiatorExhaustTempC.Value);
                metrics.Add(new SensorMetric
                {
                    Id = "radiator_exhaust_temp",
                    Name = "Radiator Exhaust Temperature",
                    HardwareId = "cooler_radiator",
                    HardwareName = $"BLE Sensor ({bleRadReading.DisplayName})",
                    Category = HardwareCategory.Cooler,
                    Type = MetricType.Temperature,
                    Value = summary.Chassis.RadiatorExhaustTempC.Value,
                    Unit = "°C"
                });
            }
            else
            {
                summary.Chassis.IsRadiatorExhaustMeasured = false;
                summary.Chassis.RadiatorExhaustSensorName = null;
                summary.Chassis.RadiatorExhaustHumidityPercent = null;
                summary.Chassis.RadiatorExhaustTempC = null;
                summary.Chassis.RadiatorDeltaTC = null;
            }

            // Populate locked-on BLE telemetry in summary.Bluetooth
            summary.Bluetooth.IsRunning = BTHomeBleService.Instance.IsRunning;
            summary.Bluetooth.IsSupported = BTHomeBleService.Instance.IsSupported;

            summary.Bluetooth.Ambient = (useBle && bleReading != null) ? new BleSensorSnapshot
            {
                Mac = bleReading.MacAddress,
                Name = bleReading.DisplayName,
                TempC = bleReading.TemperatureC,
                HumidityPercent = bleReading.HumidityPercent,
                BatteryPercent = bleReading.BatteryPercent,
                Rssi = bleReading.Rssi,
                LastSeenUtc = bleReading.LastSeenUtc
            } : null;

            summary.Bluetooth.Chassis = (useBleChassis && bleChassisReading != null) ? new BleSensorSnapshot
            {
                Mac = bleChassisReading.MacAddress,
                Name = bleChassisReading.DisplayName,
                TempC = bleChassisReading.TemperatureC,
                HumidityPercent = bleChassisReading.HumidityPercent,
                BatteryPercent = bleChassisReading.BatteryPercent,
                Rssi = bleChassisReading.Rssi,
                LastSeenUtc = bleChassisReading.LastSeenUtc
            } : null;

            summary.Bluetooth.Radiator = (useBleRad && bleRadReading != null) ? new BleSensorSnapshot
            {
                Mac = bleRadReading.MacAddress,
                Name = bleRadReading.DisplayName,
                TempC = bleRadReading.TemperatureC,
                HumidityPercent = bleRadReading.HumidityPercent,
                BatteryPercent = bleRadReading.BatteryPercent,
                Rssi = bleRadReading.Rssi,
                LastSeenUtc = bleRadReading.LastSeenUtc
            } : null;

            summary.Chassis.EstimatedAmbientTempC = Math.Round(finalAmbient, 1);
            summary.Chassis.ThermalResistanceCPerW = Math.Round(_smoothedThermalResistance, 3);
            summary.Chassis.ConfidencePercent = useBle ? 99 : confidence;

            // Construct rich composite note tooltip
            var notes = new List<string>();
            if (useBle && bleReading != null)
            {
                string rRssi = bleReading.Rssi > -127 ? $"{bleReading.Rssi} dBm" : "Active";
                string rHum = bleReading.HumidityPercent.HasValue ? $" • {bleReading.HumidityPercent.Value:F0}% RH" : "";
                notes.Add($"Room Ambient: {bleReading.TemperatureF:F1}°F ({bleReading.TemperatureC:F1}°C){rHum} ({rRssi}) [{bleReading.DisplayName}]");
            }
            if (useBleChassis && bleChassisReading != null)
            {
                string cRssi = bleChassisReading.Rssi > -127 ? $"{bleChassisReading.Rssi} dBm" : "Active";
                string cHum = bleChassisReading.HumidityPercent.HasValue ? $" • {bleChassisReading.HumidityPercent.Value:F0}% RH" : "";
                notes.Add($"In-Case Air: {bleChassisReading.TemperatureF:F1}°F ({bleChassisReading.TemperatureC:F1}°C){cHum} ({cRssi}) [{bleChassisReading.DisplayName}]");
            }
            if (useBleRad && bleRadReading != null && summary.Chassis.RadiatorExhaustTempF.HasValue)
            {
                string radRssi = bleRadReading.Rssi > -127 ? $"{bleRadReading.Rssi} dBm" : "Active";
                notes.Add($"Rad Exhaust: {summary.Chassis.RadiatorExhaustTempF.Value:F1}°F ({summary.Chassis.RadiatorExhaustTempC!.Value:F1}°C) (ΔT +{summary.Chassis.RadiatorDeltaTF:F1}°F) ({radRssi}) [{bleRadReading.DisplayName}]");
            }

            if (notes.Count > 0)
            {
                summary.Chassis.Note = "Wireless BLE Hardware Telemetry Active:\n" + string.Join("\n", notes);
            }
            else
            {
                summary.Chassis.Note = "Real-time chassis ambient floor, estimated from component diode baselines and passive cooling decay curves.";
            }

            recordHistory("chassis_air_temp", summary.Chassis.ChassisAirTempC);
            recordHistory("ambient_temp", summary.Chassis.EstimatedAmbientTempC);

            metrics.Add(new SensorMetric
            {
                Id = "ambient_temp",
                Name = useBle ? "Room Temperature (Measured)" : "Estimated Ambient Room",
                HardwareId = "chassis_ambient",
                HardwareName = useBle ? $"BLE Sensor ({bleReading?.DisplayName ?? "BTHome"})" : "Thermodynamic Observer",
                Category = HardwareCategory.Motherboard,
                Type = MetricType.Temperature,
                Value = summary.Chassis.EstimatedAmbientTempC,
                Unit = "°C"
            });

            metrics.Add(new SensorMetric
            {
                Id = "chassis_air_temp",
                Name = useBleChassis ? "In-Case Air Temperature (Measured)" : "Chassis Air Temperature (Modeled)",
                HardwareId = "chassis_air",
                HardwareName = useBleChassis ? $"BLE Sensor ({bleChassisReading?.DisplayName ?? "BTHome"})" : "Thermodynamic Observer",
                Category = HardwareCategory.Motherboard,
                Type = MetricType.Temperature,
                Value = summary.Chassis.ChassisAirTempC,
                Unit = "°C"
            });
        }

        private void PollMemory(SystemSummary summary, List<SensorMetric> metrics, Action<string, double> recordHistory)
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                double totalGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                double availGb = mem.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
                double usedGb = Math.Max(0, totalGb - availGb);
                double percent = mem.dwMemoryLoad;

                summary.Memory.TotalGb = Math.Round(totalGb, 2);
                summary.Memory.UsedGb = Math.Round(usedGb, 2);
                summary.Memory.AvailableGb = Math.Round(availGb, 2);
                summary.Memory.Percent = percent;
                if (_cachedRamSpeedMts.HasValue)
                {
                    summary.Memory.SpeedMts = _cachedRamSpeedMts.Value;
                }

                recordHistory("ram_load", percent);
                recordHistory("ram_used", usedGb);

                metrics.Add(new SensorMetric
                {
                    Id = "ram_load",
                    Name = "Memory Used %",
                    HardwareId = "system_ram",
                    HardwareName = "Physical Memory",
                    Category = HardwareCategory.Memory,
                    Type = MetricType.Load,
                    Value = percent,
                    Unit = "%"
                });

                metrics.Add(new SensorMetric
                {
                    Id = "ram_used",
                    Name = "Memory In-Use",
                    HardwareId = "system_ram",
                    HardwareName = "Physical Memory",
                    Category = HardwareCategory.Memory,
                    Type = MetricType.Capacity,
                    Value = usedGb,
                    Unit = "GB"
                });

                if (_cachedRamSpeedMts.HasValue)
                {
                    metrics.Add(new SensorMetric
                    {
                        Id = "ram_speed",
                        Name = "Memory Speed",
                        HardwareId = "system_ram",
                        HardwareName = "Physical Memory",
                        Category = HardwareCategory.Memory,
                        Type = MetricType.Clock,
                        Value = _cachedRamSpeedMts.Value,
                        Unit = "MT/s"
                    });
                }
            }
        }

        private void PollCpuLoadFallback(SystemSummary summary, List<SensorMetric> metrics, Action<string, double> recordHistory)
        {
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                ulong idleTime = idle.ToUInt64();
                ulong kernelTime = kernel.ToUInt64();
                ulong userTime = user.ToUInt64();

                ulong usrDiff = userTime - _prevUserTime;
                ulong kerDiff = kernelTime - _prevKernelTime;
                ulong idlDiff = idleTime - _prevIdleTime;

                ulong sysDiff = usrDiff + kerDiff;

                if (sysDiff > 0 && sysDiff >= idlDiff)
                {
                    double cpuLoad = (double)(sysDiff - idlDiff) * 100.0 / sysDiff;
                    cpuLoad = Math.Clamp(cpuLoad, 0.0, 100.0);

                    // Only set if not already populated by hardware monitor
                    if (summary.Cpu.LoadPercent <= 0)
                    {
                        summary.Cpu.LoadPercent = Math.Round(cpuLoad, 1);
                    }

                    recordHistory("cpu_load_native", cpuLoad);
                }

                _prevIdleTime = idleTime;
                _prevKernelTime = kernelTime;
                _prevUserTime = userTime;
            }
        }

        private void PollBattery(SystemSummary summary, List<SensorMetric> metrics, Action<string, double> recordHistory)
        {
            if (GetSystemPowerStatus(out var status))
            {
                bool hasBattery = (status.BatteryFlag & 128) == 0 && status.BatteryFlag != 255;
                summary.Battery.HasBattery = hasBattery;

                if (hasBattery)
                {
                    summary.Battery.Percent = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : null;
                    bool isPluggedIn = status.ACLineStatus == 1;
                    bool isCharging = (status.BatteryFlag & 8) != 0;

                    summary.Battery.IsPluggedIn = isPluggedIn;
                    summary.Battery.IsCharging = isCharging;

                    if (status.BatteryLifeTime > 0)
                    {
                        summary.Battery.EstimatedRuntimeMinutes = status.BatteryLifeTime / 60;
                    }
                    else
                    {
                        summary.Battery.EstimatedRuntimeMinutes = null;
                    }

                    if (summary.Battery.Percent.HasValue)
                    {
                        recordHistory("battery_percent", summary.Battery.Percent.Value);
                        metrics.Add(new SensorMetric
                        {
                            Id = "battery_percent",
                            Name = "Battery Charge",
                            HardwareId = "battery_0",
                            HardwareName = "System Battery",
                            Category = HardwareCategory.Battery,
                            Type = MetricType.Level,
                            Value = summary.Battery.Percent.Value,
                            Unit = "%"
                        });
                    }

                    metrics.Add(new SensorMetric
                    {
                        Id = "battery_power_source",
                        Name = "Power Source",
                        HardwareId = "battery_0",
                        HardwareName = "System Power",
                        Category = HardwareCategory.Battery,
                        Type = MetricType.Level,
                        Value = isPluggedIn ? 1.0 : 0.0,
                        Unit = isPluggedIn ? (isCharging ? "Charging" : "Plugged In") : "Battery"
                    });
                }
            }
        }

        private void PollNetwork(SystemSummary summary, List<SensorMetric> metrics, Action<string, double> recordHistory)
        {
            DateTime now = DateTime.UtcNow;
            double seconds = (now - _prevNetworkTime).TotalSeconds;
            if (seconds <= 0.2) return;

            try
            {
                var adapterList = new List<NetworkAdapterInfo>();
                string userPreferred = ConfigManager.Instance.Config.PreferredNetworkAdapter ?? "Auto";

                NetworkAdapterInfo? selectedAdapter = null;
                double maxActivity = -1;

                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in interfaces)
                {
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    string desc = nic.Description ?? "";
                    string name = nic.Name ?? "";

                    bool isFilter = desc.Contains("Filter", StringComparison.OrdinalIgnoreCase) ||
                                    desc.Contains("LightWeight", StringComparison.OrdinalIgnoreCase) ||
                                    desc.Contains("WFP", StringComparison.OrdinalIgnoreCase) ||
                                    desc.Contains("Pseudo", StringComparison.OrdinalIgnoreCase) ||
                                    name.Contains("-WFP", StringComparison.OrdinalIgnoreCase);

                    if (isFilter) continue;

                    var stats = nic.GetIPStatistics();
                    long rx = stats.BytesReceived;
                    long tx = stats.BytesSent;

                    double rxMbps = 0;
                    double txMbps = 0;

                    if (_prevAdapterBytes.TryGetValue(nic.Id, out var prevBytes))
                    {
                        if (rx >= prevBytes.Rx && tx >= prevBytes.Tx)
                        {
                            rxMbps = Math.Round(((rx - prevBytes.Rx) * 8.0 / seconds) / 1_000_000.0, 2);
                            txMbps = Math.Round(((tx - prevBytes.Tx) * 8.0 / seconds) / 1_000_000.0, 2);
                        }
                    }

                    _prevAdapterBytes[nic.Id] = (rx, tx);

                    var adapterInfo = new NetworkAdapterInfo
                    {
                        Id = nic.Id,
                        Name = name,
                        Description = desc,
                        Type = nic.NetworkInterfaceType.ToString(),
                        Status = nic.OperationalStatus.ToString(),
                        IsUp = nic.OperationalStatus == OperationalStatus.Up,
                        LinkSpeedMbps = nic.Speed > 0 ? nic.Speed / 1_000_000.0 : 0,
                        DownloadMbps = rxMbps,
                        UploadMbps = txMbps
                    };

                    adapterList.Add(adapterInfo);

                    // Check if this adapter matches user preferred setting
                    bool isUserMatch = !string.Equals(userPreferred, "Auto", StringComparison.OrdinalIgnoreCase) &&
                                       (string.Equals(userPreferred, name, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(userPreferred, desc, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(userPreferred, nic.Id, StringComparison.OrdinalIgnoreCase));

                    if (isUserMatch)
                    {
                        selectedAdapter = adapterInfo;
                    }
                    else if (selectedAdapter == null || (!isUserMatch && userPreferred.Equals("Auto", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Auto-select: pick the physical adapter with active link and highest instantaneous traffic, or Wi-Fi/Ethernet that is UP
                        if (adapterInfo.IsUp && (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet || nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                        {
                            double currentActivity = rxMbps + txMbps;
                            if (currentActivity > maxActivity || (selectedAdapter != null && !selectedAdapter.IsUp))
                            {
                                maxActivity = currentActivity;
                                selectedAdapter = adapterInfo;
                            }
                        }
                    }
                }

                summary.Network.AvailableAdapters = adapterList;

                // Fallback if none matched
                selectedAdapter ??= adapterList.Find(a => a.IsUp) ?? (adapterList.Count > 0 ? adapterList[0] : null);

                if (selectedAdapter != null)
                {
                    summary.Network.AdapterName = selectedAdapter.Name;
                    summary.Network.AdapterDescription = selectedAdapter.Description;
                    summary.Network.DownloadMbps = selectedAdapter.DownloadMbps;
                    summary.Network.UploadMbps = selectedAdapter.UploadMbps;

                    recordHistory("net_download_mbps", selectedAdapter.DownloadMbps);
                    recordHistory("net_upload_mbps", selectedAdapter.UploadMbps);

                    metrics.Add(new SensorMetric
                    {
                        Id = "net_rx",
                        Name = $"{selectedAdapter.Name} Download",
                        HardwareId = "net_primary",
                        HardwareName = selectedAdapter.Description,
                        Category = HardwareCategory.Network,
                        Type = MetricType.Throughput,
                        Value = selectedAdapter.DownloadMbps,
                        Unit = "Mbps"
                    });

                    metrics.Add(new SensorMetric
                    {
                        Id = "net_tx",
                        Name = $"{selectedAdapter.Name} Upload",
                        HardwareId = "net_primary",
                        HardwareName = selectedAdapter.Description,
                        Category = HardwareCategory.Network,
                        Type = MetricType.Throughput,
                        Value = selectedAdapter.UploadMbps,
                        Unit = "Mbps"
                    });
                }

                _prevNetworkTime = now;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("SystemHarvester", $"Network poll error: {ex.Message}");
            }
        }

        private void EmitCpuMetrics(SystemSummary summary, List<SensorMetric> metrics, Action<string, double> recordHistory)
        {
            string cpuName = string.IsNullOrEmpty(summary.Cpu.DisplayName) ? "Processor (CPU)" : summary.Cpu.DisplayName;

            // Package Temp
            if (summary.Cpu.TempC.HasValue && summary.Cpu.TempC.Value > 0)
            {
                metrics.Add(new SensorMetric
                {
                    Id = "cpu_package_temp",
                    Category = HardwareCategory.Cpu,
                    HardwareName = cpuName,
                    Name = "CPU Package",
                    Type = MetricType.Temperature,
                    Value = summary.Cpu.TempC.Value,
                    Unit = "°C"
                });
            }

            // Package Load
            metrics.Add(new SensorMetric
            {
                Id = "cpu_total_load",
                Category = HardwareCategory.Cpu,
                HardwareName = cpuName,
                Name = "CPU Total Load",
                Type = MetricType.Load,
                Value = summary.Cpu.LoadPercent,
                Unit = "%"
            });

            // Package Power
            if (summary.Cpu.PowerWatts.HasValue)
            {
                metrics.Add(new SensorMetric
                {
                    Id = "cpu_package_power",
                    Category = HardwareCategory.Cpu,
                    HardwareName = cpuName,
                    Name = "CPU Package Power",
                    Type = MetricType.Power,
                    Value = summary.Cpu.PowerWatts.Value,
                    Unit = "W"
                });
            }

            // Core Clock
            if (summary.Cpu.ClockGhz.HasValue && summary.Cpu.ClockGhz.Value > 0)
            {
                metrics.Add(new SensorMetric
                {
                    Id = "cpu_clock_ghz",
                    Category = HardwareCategory.Cpu,
                    HardwareName = cpuName,
                    Name = "CPU Core Frequency",
                    Type = MetricType.Clock,
                    Value = Math.Round(summary.Cpu.ClockGhz.Value * 1000.0, 0),
                    Unit = "MHz"
                });
            }

            // Per-core sensors
            for (int i = 0; i < summary.Cpu.Cores.Count; i++)
            {
                var core = summary.Cpu.Cores[i];
                string coreName = string.IsNullOrEmpty(core.Name) ? $"Core #{i + 1}" : core.Name;

                if (core.TempC.HasValue && core.TempC.Value > 0)
                {
                    metrics.Add(new SensorMetric
                    {
                        Id = $"cpu_core_{i + 1}_temp",
                        Category = HardwareCategory.Cpu,
                        HardwareName = cpuName,
                        Name = $"{coreName} Temperature",
                        Type = MetricType.Temperature,
                        Value = core.TempC.Value,
                        Unit = "°C"
                    });
                }

                if (core.ClockMhz.HasValue && core.ClockMhz.Value > 0)
                {
                    metrics.Add(new SensorMetric
                    {
                        Id = $"cpu_core_{i + 1}_clock",
                        Category = HardwareCategory.Cpu,
                        HardwareName = cpuName,
                        Name = $"{coreName} Clock",
                        Type = MetricType.Clock,
                        Value = core.ClockMhz.Value,
                        Unit = "MHz"
                    });
                }

                if (core.LoadPercent.HasValue)
                {
                    metrics.Add(new SensorMetric
                    {
                        Id = $"cpu_core_{i + 1}_load",
                        Category = HardwareCategory.Cpu,
                        HardwareName = cpuName,
                        Name = $"{coreName} Load",
                        Type = MetricType.Load,
                        Value = core.LoadPercent.Value,
                        Unit = "%"
                    });
                }
            }
        }

        private void EvaluateTimDiagnostics(SystemSummary summary)
        {
            try
            {
                var cfg = ConfigManager.Instance.Config;
                string profileKey = (cfg.TimProfile ?? "Auto").Trim();

                int serviceLifeDays = 730; // 2 years default (Standard OEM)
                string profileName = "Standard OEM Paste";

                switch (profileKey.ToLowerInvariant())
                {
                    case "kryonaut":
                        serviceLifeDays = 365; // 1 year (high pump-out under thermal cycles)
                        profileName = "Thermal Grizzly Kryonaut";
                        break;
                    case "kryonaut_extreme":
                        serviceLifeDays = 365;
                        profileName = "Thermal Grizzly Kryonaut Extreme";
                        break;
                    case "hydronaut":
                    case "aeronaut":
                        serviceLifeDays = 730;
                        profileName = "Thermal Grizzly Hydronaut / Aeronaut";
                        break;
                    case "noctua_nth1":
                    case "nth1":
                        serviceLifeDays = 1095; // 3 years
                        profileName = "Noctua NT-H1";
                        break;
                    case "noctua_nth2":
                    case "nth2":
                        serviceLifeDays = 1825; // 5 years
                        profileName = "Noctua NT-H2";
                        break;
                    case "arctic_mx4":
                    case "mx4":
                        serviceLifeDays = 2920; // 8 years
                        profileName = "Arctic MX-4";
                        break;
                    case "arctic_mx6":
                    case "mx6":
                        serviceLifeDays = 2920; // 8 years
                        profileName = "Arctic MX-6";
                        break;
                    case "thermalright_tf":
                    case "tf7":
                    case "tf8":
                    case "tf9":
                        serviceLifeDays = 1460; // 4 years (dense clay paste)
                        profileName = "Thermalright TF Series (TF7/TF8/TF9)";
                        break;
                    case "kingpin_kpx":
                    case "kpx":
                        serviceLifeDays = 365; // 1 year enthusiast
                        profileName = "Kingpin Cooling KPx";
                        break;
                    case "gelid_gc":
                    case "gc_extreme":
                        serviceLifeDays = 730; // 2 years
                        profileName = "Gelid GC-Extreme";
                        break;
                    case "corsair_tm":
                    case "xtm50":
                    case "xtm70":
                        serviceLifeDays = 1095; // 3 years
                        profileName = "Corsair TM30 / XTM50 / XTM70";
                        break;
                    case "prolimatech_pk3":
                    case "pk3":
                        serviceLifeDays = 1095;
                        profileName = "Prolimatech PK-3";
                        break;
                    case "ptm7950":
                    case "honeywell_ptm7950":
                        serviceLifeDays = 2920; // 8+ years (Phase Change)
                        profileName = "Honeywell PTM7950 (Phase Change)";
                        break;
                    case "phasesheet_ptm":
                        serviceLifeDays = 2920; // 8+ years (Phase Change)
                        profileName = "Thermal Grizzly PhaseSheet PTM";
                        break;
                    case "liquidmetal":
                    case "conductonaut":
                        serviceLifeDays = 540; // ~1.5 years (gallium absorption curve)
                        profileName = "Thermal Grizzly Conductonaut (Liquid Metal)";
                        break;
                    case "coollaboratory_liquid":
                        serviceLifeDays = 540;
                        profileName = "Coollaboratory Liquid Ultra / Pro";
                        break;
                    default:
                        serviceLifeDays = 730;
                        profileName = "Standard OEM Paste";
                        break;
                }

                summary.Cpu.TimProfile = profileName;
                summary.Cpu.TimServiceLifeDays = serviceLifeDays;

                if (cfg.TimAppliedDateUtc.HasValue)
                {
                    var age = (DateTime.UtcNow - cfg.TimAppliedDateUtc.Value).TotalDays;
                    int ageDays = Math.Max(0, (int)age);
                    summary.Cpu.TimAgeDays = ageDays;

                    double healthPercent = Math.Clamp(100.0 - ((double)ageDays / serviceLifeDays * 100.0), 0.0, 100.0);
                    summary.Cpu.TimHealthPercent = Math.Round(healthPercent, 1);

                    if (ageDays > serviceLifeDays)
                    {
                        summary.Cpu.TimStatus = "Expired (Repaste Recommended)";
                    }
                    else if (healthPercent <= 20.0)
                    {
                        summary.Cpu.TimStatus = "Degraded (Repaste Soon)";
                    }
                    else
                    {
                        summary.Cpu.TimStatus = "Optimal";
                    }
                }
                else
                {
                    summary.Cpu.TimAgeDays = null;
                    summary.Cpu.TimHealthPercent = 100.0;
                    summary.Cpu.TimStatus = "Optimal (No Date Recorded)";
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("SystemHarvester", $"TIM evaluation warning: {ex.Message}");
            }
        }

        private void PollMotherboardAndVrm(SystemSummary summary, List<SensorMetric> metrics, Action<string, double> recordHistory)
        {
            try
            {
                // 1. Motherboard Identification (Zero-Driver Native Registry Query)
                if (_mbManufacturer == null || _mbModel == null)
                {
                    try
                    {
                        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                        if (key != null)
                        {
                            var mfg = key.GetValue("BaseBoardManufacturer") as string;
                            if (string.IsNullOrWhiteSpace(mfg)) mfg = key.GetValue("SystemManufacturer") as string;
                            var model = key.GetValue("BaseBoardProduct") as string;
                            if (string.IsNullOrWhiteSpace(model)) model = key.GetValue("SystemProductName") as string;

                            _mbManufacturer = !string.IsNullOrWhiteSpace(mfg) ? CleanManufacturer(mfg) : "Generic Motherboard";
                            _mbModel = !string.IsNullOrWhiteSpace(model) ? model.Trim() : "Standard System Board";
                        }
                    }
                    catch
                    {
                        _mbManufacturer = "Generic Motherboard";
                        _mbModel = "Standard System Board";
                    }
                }

                summary.Motherboard.Manufacturer = _mbManufacturer ?? "Generic Motherboard";
                summary.Motherboard.Model = _mbModel ?? "Standard System Board";

                // 2. Dual-Mode VRM Telemetry (Direct ACPI Zone Probe vs. Thermodynamic Power-Stage Observer)
                DateTime now = DateTime.UtcNow;
                double dt = Math.Clamp((now - _lastVrmEstimateTime).TotalSeconds, 0.1, 5.0);
                _lastVrmEstimateTime = now;

                bool isAcpiZoneActive = false;
                double rawAcpiTemp = 0;

                // Attempt Native ACPI Thermal Zone probe (Secondary zone / TZ00)
                if (_pdhThermalHighCounter != IntPtr.Zero)
                {
                    if (PdhNative.PdhGetFormattedCounterValue(_pdhThermalHighCounter, PdhNative.PDH_FMT_DOUBLE, out _, out var val) == 0 && val.CStatus == PdhNative.PDH_CSTATUS_VALID_DATA)
                    {
                        if (!double.IsNaN(val.doubleValue) && val.doubleValue > 250 && val.doubleValue < 400) // Tenths of Kelvin
                        {
                            double c = (val.doubleValue / 10.0) - 273.15;
                            if (c >= 15.0 && c <= 115.0)
                            {
                                rawAcpiTemp = c;
                                isAcpiZoneActive = true;
                            }
                        }
                    }
                }

                if (isAcpiZoneActive)
                {
                    _smoothedVrmTemp = (_smoothedVrmTemp * 0.90) + (rawAcpiTemp * 0.10);
                    summary.Motherboard.VrmTempC = Math.Round(_smoothedVrmTemp, 1);
                    summary.Motherboard.IsVrmModeled = false;
                    summary.Cpu.VrmTempC = Math.Round(_smoothedVrmTemp, 1);
                    summary.Cpu.VrmSource = "ACPI Thermal Zone (Hardware)";
                }
                else
                {
                    // Physical Fallback: Thermodynamic Power-Stage Loss Observer
                    // Synchronous DC-DC buck converter DrMOS power conversion efficiency is ~91-94% at load
                    // P_VRM_loss = (P_CPU * 0.072) + 2.2W (quiescent gate drive & switching loss)
                    double cpuWatts = summary.Cpu.PowerWatts ?? 35.0;
                    double vrmLossWatts = (cpuWatts * 0.072) + 2.2;

                    // Heat sink dissipates into internal chassis air cavity
                    double chassisAir = summary.Chassis.ChassisAirTempC > 0 ? summary.Chassis.ChassisAirTempC : 30.0;

                    // Internal silicon-to-heatsink delta (silicone thermal pad junction resistance + PCB socket copper heat spreading)
                    double junctionDelta = 4.0;

                    // Typical VRM aluminum extrusion thermal resistance R_th = 0.65 °C/W
                    double targetVrmTemp = chassisAir + junctionDelta + (vrmLossWatts * 0.65);

                    // High thermal mass inertia (asymmetric IIR filter: tau_rise = 45s, tau_fall = 85s)
                    double tau = (targetVrmTemp > _smoothedVrmTemp) ? 45.0 : 85.0;
                    double alpha = 1.0 - Math.Exp(-dt / tau);

                    _smoothedVrmTemp += alpha * (targetVrmTemp - _smoothedVrmTemp);
                    _smoothedVrmTemp = Math.Clamp(_smoothedVrmTemp, chassisAir, 115.0);

                    summary.Motherboard.VrmTempC = Math.Round(_smoothedVrmTemp, 1);
                    summary.Motherboard.IsVrmModeled = true;
                    summary.Cpu.VrmTempC = Math.Round(_smoothedVrmTemp, 1);
                    summary.Cpu.VrmSource = "Estimated (Modeled)";
                }

                recordHistory("cpu_vrm_temp", _smoothedVrmTemp);
                recordHistory("mb_vrm_temp", _smoothedVrmTemp);

                // 3. Emit VRM Sensors into Metrics Pipeline
                metrics.Add(new SensorMetric
                {
                    Id = "cpu_vrm_temp",
                    Category = HardwareCategory.Cpu,
                    HardwareName = summary.Cpu.DisplayName,
                    Name = "CPU VRM / Power Stages",
                    Type = MetricType.Temperature,
                    Value = Math.Round(_smoothedVrmTemp, 1),
                    Unit = "°C"
                });

                metrics.Add(new SensorMetric
                {
                    Id = "mb_vrm_temp",
                    Category = HardwareCategory.Motherboard,
                    HardwareName = summary.Motherboard.Model ?? "Motherboard",
                    Name = "VRM MOSFET Temperature",
                    Type = MetricType.Temperature,
                    Value = Math.Round(_smoothedVrmTemp, 1),
                    Unit = "°C"
                });
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("SystemHarvester", $"Motherboard/VRM polling warning: {ex.Message}");
            }
        }

        private static string CleanManufacturer(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Generic Motherboard";
            string s = raw.Trim();
            if (s.Contains("ASUSTeK", StringComparison.OrdinalIgnoreCase)) return "ASUS";
            if (s.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) || s.Equals("MSI", StringComparison.OrdinalIgnoreCase)) return "MSI";
            if (s.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase)) return "GIGABYTE";
            if (s.Contains("ASRock", StringComparison.OrdinalIgnoreCase)) return "ASRock";
            if (s.Contains("EVGA", StringComparison.OrdinalIgnoreCase)) return "EVGA";
            if (s.Contains("NZXT", StringComparison.OrdinalIgnoreCase)) return "NZXT";
            if (s.Contains("Dell", StringComparison.OrdinalIgnoreCase)) return "Dell";
            if (s.Contains("HP", StringComparison.OrdinalIgnoreCase) || s.Contains("Hewlett-Packard", StringComparison.OrdinalIgnoreCase)) return "HP";
            if (s.Contains("Lenovo", StringComparison.OrdinalIgnoreCase)) return "Lenovo";
            return s;
        }

        private static double ParseBaseMhzFromName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return 3200.0;
            var match = System.Text.RegularExpressions.Regex.Match(name, @"@\s*([0-9\.]+)\s*GHz", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ghz) && ghz > 0.5)
            {
                return Math.Round(ghz * 1000.0, 0);
            }
            return 3200.0;
        }

        private double GetCoreBaseMhz(int coreIndex)
        {
            if (_coreBaseMhzCache.TryGetValue(coreIndex, out var cached))
                return cached;

            double mhz = 0;
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"HARDWARE\DESCRIPTION\System\CentralProcessor\{coreIndex}");
                if (key?.GetValue("~MHz") is int regMhz && regMhz > 0)
                {
                    mhz = regMhz;
                }
                else if (_coreBaseMhzCache.TryGetValue(0, out var fallback0))
                {
                    mhz = fallback0;
                }
            }
            catch { }

            if (mhz <= 0)
            {
                mhz = ParseBaseMhzFromName(_nativeCpuName);
            }

            _coreBaseMhzCache[coreIndex] = mhz;
            return mhz;
        }

        private void InitCoreTopology()
        {
            if (_coreTopology.Count > 0) return;

            try
            {
                int length = 0;
                GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref length); // 0 = RelationProcessorCore
                if (length > 0)
                {
                    IntPtr ptr = Marshal.AllocHGlobal(length);
                    try
                    {
                        if (GetLogicalProcessorInformationEx(0, ptr, ref length))
                        {
                            int offset = 0;
                            var efficiencyClasses = new List<(byte EffClass, ulong Mask)>();
                            while (offset < length)
                            {
                                IntPtr current = IntPtr.Add(ptr, offset);
                                int size = Marshal.ReadInt32(current, 4);
                                byte eff = Marshal.ReadByte(current, 9);
                                long mask = Marshal.ReadInt64(current, 32);
                                efficiencyClasses.Add((eff, (ulong)mask));
                                offset += size;
                            }

                            bool isHybrid = efficiencyClasses.Select(e => e.EffClass).Distinct().Count() > 1;
                            bool hasSmt = efficiencyClasses.Any(e => (e.Mask & (e.Mask - 1)) != 0);
                            double pkgBaseMhz = GetCoreBaseMhz(0);
                            double eCoreBaseMhz = Math.Round(pkgBaseMhz * 0.75, 0); // E-Core / C-Core nominal base (~75% of full-power base)
                            bool isAmd = _nativeCpuName != null && (_nativeCpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) || _nativeCpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase));
                            string denseLabel = isAmd ? "C" : "E";
                            string htLabel = isAmd ? "SMT" : "HT";

                            int physCoreCounter = 1;
                            int eCoreCounter = 1;

                            foreach (var (eff, mask) in efficiencyClasses)
                            {
                                var threadList = new List<int>();
                                for (int bit = 0; bit < 64; bit++)
                                {
                                    if ((mask & (1UL << bit)) != 0)
                                    {
                                        threadList.Add(bit);
                                    }
                                }

                                if (threadList.Count == 0) continue;

                                bool isEfficient = isHybrid && eff == 0;
                                double baseMhz = isEfficient ? eCoreBaseMhz : pkgBaseMhz;

                                if (isEfficient)
                                {
                                    int eNum = eCoreCounter++;
                                    for (int t = 0; t < threadList.Count; t++)
                                    {
                                        int bit = threadList[t];
                                        string type = (t == 0) ? denseLabel : htLabel;
                                        string name = (threadList.Count > 1)
                                            ? $"{denseLabel}-Core {eNum} T{t + 1}"
                                            : $"{denseLabel}-Core {eNum}";
                                        string desc = (threadList.Count > 1)
                                            ? $"Physical {denseLabel}-Core #{eNum} (Thread {t + 1}) — Shares Silicon with thread #{threadList[1 - t] + 1}"
                                            : $"Physical {denseLabel}-Core #{eNum} (Dedicated Silicon Core, No SMT)";
                                        _coreTopology[bit] = (name, type, baseMhz, desc);
                                    }
                                }
                                else
                                {
                                    int pNum = physCoreCounter++;
                                    for (int t = 0; t < threadList.Count; t++)
                                    {
                                        int bit = threadList[t];
                                        string type;
                                        if (t == 0)
                                        {
                                            type = isHybrid ? "P" : (hasSmt ? "Core" : string.Empty);
                                        }
                                        else
                                        {
                                            type = htLabel;
                                        }

                                        string name = (threadList.Count > 1)
                                            ? $"Core {pNum} T{t + 1}"
                                            : $"Core {pNum}";

                                        string coreName = isHybrid ? "P-Core" : "Core";
                                        string desc = (threadList.Count > 1)
                                            ? $"Physical {coreName} #{pNum} {(t == 0 ? "(Primary Thread)" : $"({htLabel} Sibling)")} — Shares Silicon Pipeline with Core {pNum} T{2 - t}"
                                            : $"Physical {coreName} #{pNum} (Dedicated Core)";
                                        _coreTopology[bit] = (name, type, baseMhz, desc);
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("SystemHarvester", $"Core topology probing warning: {ex.Message}");
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
