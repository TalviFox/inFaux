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

        private IntPtr _hPdhQuery = IntPtr.Zero;
        private IntPtr _pdhTotalFreqCounter = IntPtr.Zero;
        private readonly List<IntPtr> _pdhCoreFreqCounters = new();
        private readonly List<IntPtr> _pdhCoreLoadCounters = new();
        private IntPtr _pdhThermalHighCounter = IntPtr.Zero;
        private IntPtr _pdhThermalCounter = IntPtr.Zero;

        private double _estimatedCpuTemp = 35.0;
        private readonly Dictionary<int, double> _estimatedCoreTemps = new();
        private DateTime _lastEstimateTime = DateTime.UtcNow;

        // Convective Chassis & Ambient Room Temperature Estimator
        private readonly List<(double Watts, double TempC)> _steadyStateSamples = new();
        private DateTime _lastSampleTime = DateTime.MinValue;
        private double _smoothedChassisAir = 33.0;
        private double _smoothedAmbient = 22.0;
        private double _smoothedThermalResistance = 0.33;

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

                // Total CPU Frequency
                PdhNative.PdhAddEnglishCounterW(_hPdhQuery, @"\Processor Information(0,_Total)\Processor Frequency", IntPtr.Zero, out _pdhTotalFreqCounter);

                // Per-Core Frequency and Load counters for all logical cores
                int coreCount = Environment.ProcessorCount;
                for (int i = 0; i < coreCount; i++)
                {
                    string pathFreq = $@"\Processor Information(0,{i})\Processor Frequency";
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
            PollCpuLoadFallback(summary, metrics, recordHistory);
            PollPdhCpuData(summary, metrics, recordHistory);
            PollBattery(summary, metrics, recordHistory);
            PollNetwork(summary, metrics, recordHistory);
            EstimateChassisAndAmbient(summary, recordHistory);
        }

        private void PollPdhCpuData(SystemSummary summary, List<SensorMetric> metrics, Action<string, double> recordHistory)
        {
            if (_hPdhQuery == IntPtr.Zero) return;

            try
            {
                PdhNative.PdhCollectQueryData(_hPdhQuery);

                // 1. Process CPU Total & Per-Core Frequencies (Zero Driver!)
                if (_pdhTotalFreqCounter != IntPtr.Zero)
                {
                    if (PdhNative.PdhGetFormattedCounterValue(_pdhTotalFreqCounter, PdhNative.PDH_FMT_DOUBLE, out _, out var val) == 0 && val.CStatus == PdhNative.PDH_CSTATUS_VALID_DATA)
                    {
                        if (!double.IsNaN(val.doubleValue) && val.doubleValue > 100)
                        {
                            summary.Cpu.ClockGhz = Math.Round(val.doubleValue / 1000.0, 2);
                        }
                    }
                }

                if (!summary.Cpu.ClockGhz.HasValue || double.IsNaN(summary.Cpu.ClockGhz.Value) || summary.Cpu.ClockGhz.Value <= 0)
                {
                    summary.Cpu.ClockGhz = 3.2;
                }

                // If cores do not exist yet, initialize them from PDH instance count
                if (summary.Cpu.Cores.Count == 0 && _pdhCoreFreqCounters.Count > 0)
                {
                    for (int i = 0; i < _pdhCoreFreqCounters.Count; i++)
                    {
                        summary.Cpu.Cores.Add(new CoreMetric
                        {
                            Name = $"Core #{i + 1}",
                            LoadPercent = summary.Cpu.LoadPercent
                        });
                    }
                }

                // If cores exist, map frequencies and real utilization directly to each core
                for (int i = 0; i < _pdhCoreFreqCounters.Count && i < summary.Cpu.Cores.Count; i++)
                {
                    var hCore = _pdhCoreFreqCounters[i];
                    if (PdhNative.PdhGetFormattedCounterValue(hCore, PdhNative.PDH_FMT_DOUBLE, out _, out var val) == 0 && val.CStatus == PdhNative.PDH_CSTATUS_VALID_DATA)
                    {
                        double coreMhz = Math.Round(val.doubleValue, 0);
                        if (!double.IsNaN(coreMhz) && coreMhz > 0)
                        {
                            summary.Cpu.Cores[i].ClockMhz = coreMhz;
                        }
                    }
                }

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

        private void PollCpuPowerFallback(SystemSummary summary, Action<string, double> recordHistory)
        {
            double cpuLoad = (!double.IsNaN(summary.Cpu.LoadPercent) && summary.Cpu.LoadPercent >= 0) ? summary.Cpu.LoadPercent : 0.0;
            double loadRatio = Math.Clamp(cpuLoad / 100.0, 0.0, 1.0);

            double clock = (summary.Cpu.ClockGhz.HasValue && !double.IsNaN(summary.Cpu.ClockGhz.Value) && summary.Cpu.ClockGhz.Value > 0)
                ? summary.Cpu.ClockGhz.Value
                : 3.2;
            double freqRatio = Math.Clamp(clock / 3.2, 0.8, 1.8);

            var cfg = ConfigManager.Instance.Config;
            double baseTdp = cfg.CpuTdpOverrideWatts > 0 ? cfg.CpuTdpOverrideWatts : 105.0;
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

            // Calculate live thermodynamic target
            double dynamicHeatRise = (loadRatio * freqRatio * 44.0 * coolerDissipationFactor) + (loadRatio * 6.0);
            double targetTemp = ambientAnchor + baseIdleOffset + dynamicHeatRise + 3.0 + cfg.CpuThermalOffset;
            if (double.IsNaN(targetTemp)) targetTemp = 38.0;

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

        private void EstimateChassisAndAmbient(SystemSummary summary, Action<string, double> recordHistory)
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
            if ((now - _lastSampleTime).TotalSeconds >= 3.0)
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
            // Laptop: ~0.95 to 1.20 °C/W; Desktop tower: ~0.40 to 0.60 °C/W
            double defaultR = isLaptop ? 1.05 : 0.48;
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
                        if (calculatedR >= 0.30 && calculatedR <= 1.50)
                        {
                            r = calculatedR;
                            confidence = Math.Min(95, 75 + (int)(spread * 2));
                        }
                    }
                }
            }

            // Low-pass filter the thermal resistance
            _smoothedThermalResistance = (_smoothedThermalResistance * 0.96) + (r * 0.04);

            // Instantaneous Zero-Watt Intercept (Chassis cavity floor)
            // At 0W workload, die equals internal chassis ambient air cavity
            double instantChassis = temp - (_smoothedThermalResistance * watts);
            instantChassis = Math.Clamp(instantChassis, 24.0, 39.0);

            // Convective air cavity-to-room temperature delta
            // Laptop internal cavity is ~8.5°C above room; Desktop case air is ~4.5°C above room
            double convectionOffset = isLaptop ? 8.5 : 4.5;
            double instantAmbient = instantChassis - convectionOffset;

            // Soft Bayesian normalization toward typical human indoor comfort (21.5°C / 71°F)
            // Prevents false room temp spikes during sudden thermal bursts
            instantAmbient = (instantAmbient * 0.65) + (21.5 * 0.35);
            instantAmbient = Math.Clamp(instantAmbient, 18.0, 26.5);

            // Smooth with gentle low-pass filter
            _smoothedChassisAir = (_smoothedChassisAir * 0.95) + (instantChassis * 0.05);
            _smoothedAmbient = (_smoothedAmbient * 0.95) + (instantAmbient * 0.05);

            summary.Chassis.ChassisAirTempC = Math.Round(_smoothedChassisAir, 1);
            summary.Chassis.EstimatedAmbientTempC = Math.Round(_smoothedAmbient, 1);
            summary.Chassis.ThermalResistanceCPerW = Math.Round(_smoothedThermalResistance, 3);
            summary.Chassis.ConfidencePercent = confidence;

            recordHistory("chassis_air_temp", summary.Chassis.ChassisAirTempC);
            recordHistory("ambient_temp", summary.Chassis.EstimatedAmbientTempC);
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
                    summary.Battery.IsCharging = (status.BatteryFlag & 8) != 0 || status.ACLineStatus == 1;

                    if (status.BatteryLifeTime > 0)
                    {
                        summary.Battery.EstimatedRuntimeMinutes = status.BatteryLifeTime / 60;
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
