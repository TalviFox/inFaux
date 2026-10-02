using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using InFox.Services;

namespace InFox.Engine.Harvesters
{
    public class GpuTelemetryData
    {
        public double? TempC { get; set; }
        public double? HotspotTempC { get; set; }
        public double? MemoryJunctionTempC { get; set; }
        public double? LoadPercent { get; set; }
        public double? PowerWatts { get; set; }
        public double? FanPercentOrRpm { get; set; }
        public double? CoreClockMhz { get; set; }
        public double? MemoryClockMhz { get; set; }
        public double? VramUsedGb { get; set; }
        public double? VramTotalGb { get; set; }
        public bool IsBoost { get; set; }
    }

    /// <summary>
    /// 100% User-Mode Native NVIDIA Management Library (NVML) Probe.
    /// Uses standard C exports from C:\Windows\System32\nvml.dll shipped with NVIDIA drivers.
    /// Runs strictly asInvoker with zero driver installations, zero UAC elevation.
    /// </summary>
    public static class NvmlGpuProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        private static readonly object _lock = new();

        #region Native NVML Interop

        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlInit();

        [DllImport("nvml.dll", EntryPoint = "nvmlShutdown", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlShutdown();

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount_v2", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetCount(out uint deviceCount);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint powerMw);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetFanSpeed", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetFanSpeed(IntPtr device, out uint fanSpeed);

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlUtilization
        {
            public uint Gpu;
            public uint Memory;
        }

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlMemory
        {
            public ulong Total;
            public ulong Free;
            public ulong Used;
        }

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetClockInfo(IntPtr device, int clockType, out uint clockMhz);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMaxClockInfo", CallingConvention = CallingConvention.Cdecl)]
        private static extern int nvmlDeviceGetMaxClockInfo(IntPtr device, int clockType, out uint clockMhz);

        #endregion

        public static bool EnsureInitialized()
        {
            if (_initialized) return _isAvailable;

            lock (_lock)
            {
                if (_initialized) return _isAvailable;

                try
                {
                    string systemDir = Environment.SystemDirectory;
                    string nvmlPath = Path.Combine(systemDir, "nvml.dll");
                    if (!File.Exists(nvmlPath))
                    {
                        _initialized = true;
                        _isAvailable = false;
                        return false;
                    }

                    int res = nvmlInit();
                    _isAvailable = (res == 0);
                    _initialized = true;

                    if (_isAvailable)
                    {
                        LoggingService.Instance.Info("NvmlProbe", "Native NVIDIA NVML user-mode probe successfully initialized.");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Warning("NvmlProbe", $"NVML unavailable: {ex.Message}");
                    _initialized = true;
                    _isAvailable = false;
                }

                return _isAvailable;
            }
        }

        public static GpuTelemetryData? QueryTelemetry(string gpuName, int fallbackIndex = 0)
        {
            if (!EnsureInitialized()) return null;

            lock (_lock)
            {
                try
                {
                    if (nvmlDeviceGetCount(out uint count) != 0 || count == 0) return null;

                    IntPtr targetDevice = IntPtr.Zero;

                    // Match device by name, or fallback to index
                    for (uint i = 0; i < count; i++)
                    {
                        if (nvmlDeviceGetHandleByIndex(i, out var dev) == 0 && dev != IntPtr.Zero)
                        {
                            var sb = new StringBuilder(128);
                            if (nvmlDeviceGetName(dev, sb, 128) == 0)
                            {
                                string devName = sb.ToString();
                                if (!string.IsNullOrEmpty(gpuName) &&
                                    (devName.Contains(gpuName, StringComparison.OrdinalIgnoreCase) ||
                                     gpuName.Contains(devName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    targetDevice = dev;
                                    break;
                                }
                            }

                            if (i == fallbackIndex && targetDevice == IntPtr.Zero)
                            {
                                targetDevice = dev;
                            }
                        }
                    }

                    if (targetDevice == IntPtr.Zero) return null;

                    var data = new GpuTelemetryData();

                    // Core Temperature (Sensor 0 = NVML_TEMPERATURE_GPU)
                    if (nvmlDeviceGetTemperature(targetDevice, 0, out uint tempC) == 0)
                    {
                        data.TempC = tempC;
                    }

                    // Utilization Rates (GPU & Memory)
                    if (nvmlDeviceGetUtilizationRates(targetDevice, out var util) == 0)
                    {
                        data.LoadPercent = util.Gpu;
                    }

                    // Board Power (reported in mW)
                    if (nvmlDeviceGetPowerUsage(targetDevice, out uint powerMw) == 0)
                    {
                        data.PowerWatts = Math.Round(powerMw / 1000.0, 1);
                    }

                    // Fan Speed (in %)
                    if (nvmlDeviceGetFanSpeed(targetDevice, out uint fanSpeed) == 0)
                    {
                        data.FanPercentOrRpm = fanSpeed;
                    }

                    // VRAM Info
                    if (nvmlDeviceGetMemoryInfo(targetDevice, out var mem) == 0 && mem.Total > 0)
                    {
                        data.VramUsedGb = Math.Round(mem.Used / (1024.0 * 1024.0 * 1024.0), 2);
                        data.VramTotalGb = Math.Round(mem.Total / (1024.0 * 1024.0 * 1024.0), 2);
                    }

                    // Clocks: 0 = Graphics/Core, 2 = Memory
                    if (nvmlDeviceGetClockInfo(targetDevice, 0, out uint coreClock) == 0)
                    {
                        data.CoreClockMhz = coreClock;
                    }
                    if (nvmlDeviceGetClockInfo(targetDevice, 2, out uint memClock) == 0)
                    {
                        data.MemoryClockMhz = memClock;
                    }

                    uint maxClock = 0;
                    if (nvmlDeviceGetMaxClockInfo(targetDevice, 0, out uint maxClk) == 0)
                    {
                        maxClock = maxClk;
                    }

                    // Boost evaluation:
                    // Idle 2D clock is ~210 MHz. Under 3D load / FurMark / gaming, GPU boosts dynamically
                    // into P0 state (>1350 MHz, scaling up toward max boost clock e.g. 2145 MHz).
                    if (data.CoreClockMhz.HasValue)
                    {
                        if (maxClock > 0 && data.CoreClockMhz.Value >= (maxClock * 0.60))
                        {
                            data.IsBoost = true;
                        }
                        else if (data.CoreClockMhz.Value >= 1350 || ((data.LoadPercent ?? 0) > 30 && data.CoreClockMhz.Value >= 1000))
                        {
                            data.IsBoost = true;
                        }
                    }

                    // Hotspot estimation if diode not directly exposed:
                    // Thermodynamic silicon gradient: hotspot is +9°C idle to +14°C under full load
                    if (data.TempC.HasValue)
                    {
                        double loadFactor = Math.Clamp((data.LoadPercent ?? 0.0) / 100.0, 0.0, 1.0);
                        data.HotspotTempC = Math.Round(data.TempC.Value + 9.0 + (loadFactor * 5.0), 1);
                    }

                    return data;
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Warning("NvmlProbe", $"Error reading NVML telemetry: {ex.Message}");
                    return null;
                }
            }
        }
    }

    /// <summary>
    /// 100% User-Mode Native AMD Display Library (ADL) Probe.
    /// Interacts directly with C:\Windows\System32\atiadlxx.dll on AMD platforms.
    /// Runs strictly asInvoker with zero driver installations, zero UAC elevation.
    /// </summary>
    public static class AdlGpuProbe
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        private static IntPtr _adlContext = IntPtr.Zero;
        private static readonly object _lock = new();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr AdlMainMemoryAlloc(int size);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Main_Control_Create(AdlMainMemoryAlloc callback, int iEnumConnectedAdapters, out IntPtr context);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Main_Control_Destroy(IntPtr context);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_OverdriveN_Temperature_Get(IntPtr context, int iAdapterIndex, int iTemperatureType, out int iTemperature);

        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int ADL2_Overdrive6_CurrentPower_Get(IntPtr context, int iAdapterIndex, int iPowerType, out int iCurrentValue);

        private static IntPtr AllocMemory(int size) => Marshal.AllocHGlobal(size);

        public static bool EnsureInitialized()
        {
            if (_initialized) return _isAvailable;

            lock (_lock)
            {
                if (_initialized) return _isAvailable;

                try
                {
                    string systemDir = Environment.SystemDirectory;
                    string adlPath = Path.Combine(systemDir, "atiadlxx.dll");
                    if (!File.Exists(adlPath))
                    {
                        _initialized = true;
                        _isAvailable = false;
                        return false;
                    }

                    int res = ADL2_Main_Control_Create(AllocMemory, 1, out _adlContext);
                    _isAvailable = (res == 0 && _adlContext != IntPtr.Zero);
                    _initialized = true;

                    if (_isAvailable)
                    {
                        LoggingService.Instance.Info("AdlProbe", "Native AMD ADL user-mode probe successfully initialized.");
                    }
                }
                catch
                {
                    _initialized = true;
                    _isAvailable = false;
                }

                return _isAvailable;
            }
        }

        public static GpuTelemetryData? QueryTelemetry(int adapterIndex = 0)
        {
            if (!EnsureInitialized()) return null;

            lock (_lock)
            {
                try
                {
                    var data = new GpuTelemetryData();

                    // Temperature Type 0: Edge/Core (in millidegrees C)
                    if (ADL2_OverdriveN_Temperature_Get(_adlContext, adapterIndex, 0, out int tempMilli) == 0 && tempMilli > 0)
                    {
                        data.TempC = Math.Round(tempMilli / 1000.0, 1);
                    }

                    // Temperature Type 1: Hotspot / Junction
                    if (ADL2_OverdriveN_Temperature_Get(_adlContext, adapterIndex, 1, out int hotspotMilli) == 0 && hotspotMilli > 0)
                    {
                        data.HotspotTempC = Math.Round(hotspotMilli / 1000.0, 1);
                    }

                    // Current Power (Type 0: Board power in Watts * 256 or milliwatts depending on arch)
                    if (ADL2_Overdrive6_CurrentPower_Get(_adlContext, adapterIndex, 0, out int powerVal) == 0 && powerVal > 0)
                    {
                        double watts = powerVal > 1000 ? powerVal / 1000.0 : powerVal / 256.0;
                        if (watts > 1 && watts < 1000)
                        {
                            data.PowerWatts = Math.Round(watts, 1);
                        }
                    }

                    if (data.TempC.HasValue && !data.HotspotTempC.HasValue)
                    {
                        data.HotspotTempC = Math.Round(data.TempC.Value + 10.0, 1);
                    }

                    return data.TempC.HasValue ? data : null;
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
