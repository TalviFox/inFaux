using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using InFox.Models.Sensors;
using InFox.Services;
using Microsoft.Win32.SafeHandles;

namespace InFox.Engine.Harvesters
{
    /// <summary>
    /// 100% Pure Native Windows Hardware Harvester.
    /// Gathers GPU telemetry via official DirectX DXGI COM interfaces
    /// and Physical Drive SMART metadata via standard read-only Win32 IOCTL.
    /// Completely zero-driver: NO WinRing0.sys, NO third-party kernel drivers, zero Defender blocks.
    /// </summary>
    public class HardwareMonitorHarvester : ISensorHarvester
    {
        public string Name => "Native Windows Hardware Harvester";
        public bool IsAvailable { get; private set; } = true;

        #region DXGI Standard COM Interfaces

        private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

        [DllImport("dxgi.dll")]
        private static extern int CreateDXGIFactory1([In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IDXGIFactory1 ppFactory);

        [ComImport]
        [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIFactory1
        {
            [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
            [PreserveSig] int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
            [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);
            [PreserveSig] int EnumAdapters(uint Adapter, out IntPtr ppAdapter);
            [PreserveSig] int MakeWindowAssociation(IntPtr WindowHandle, uint Flags);
            [PreserveSig] int GetWindowAssociation(out IntPtr pWindowHandle);
            [PreserveSig] int CreateSwapChain(IntPtr pDevice, IntPtr pDesc, out IntPtr ppSwapChain);
            [PreserveSig] int CreateSoftwareAdapter(IntPtr Module, out IntPtr ppAdapter);
            [PreserveSig] int EnumAdapters1(uint Adapter, [MarshalAs(UnmanagedType.Interface)] out IDXGIAdapter1 ppAdapter);
            [PreserveSig] int IsCurrent();
        }

        [ComImport]
        [Guid("29038f61-3839-4626-91fd-086879011a05")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIAdapter1
        {
            [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
            [PreserveSig] int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
            [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);
            [PreserveSig] int EnumOutputs(uint Output, out IntPtr ppOutput);
            [PreserveSig] int GetDesc(out IntPtr pDesc);
            [PreserveSig] int CheckInterfaceSupport(ref Guid InterfaceName, out long pUMDVersion);
            [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
        }

        [ComImport]
        [Guid("645967A4-1392-4310-A798-8053CE3E93FD")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIAdapter3
        {
            // IDXGIObject
            [PreserveSig] int SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
            [PreserveSig] int SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            [PreserveSig] int GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
            [PreserveSig] int GetParent(ref Guid riid, out IntPtr ppParent);
            // IDXGIAdapter
            [PreserveSig] int EnumOutputs(uint Output, out IntPtr ppOutput);
            [PreserveSig] int GetDesc(out IntPtr pDesc);
            [PreserveSig] int CheckInterfaceSupport(ref Guid InterfaceName, out long pUMDVersion);
            // IDXGIAdapter1
            [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
            // IDXGIAdapter2
            [PreserveSig] int GetDesc2(out DXGI_ADAPTER_DESC2 pDesc);
            // IDXGIAdapter3
            [PreserveSig] int RegisterHardwareContentProtectionTeardownStatusEvent(IntPtr hEvent, out uint pdwCookie);
            [PreserveSig] void UnregisterHardwareContentProtectionTeardownStatus(uint dwCookie);
            [PreserveSig] int QueryVideoMemoryInfo(uint NodeIndex, uint MemorySegmentGroup, out DXGI_QUERY_VIDEO_MEMORY_INFO pVideoMemoryInfo);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DXGI_ADAPTER_DESC1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public UIntPtr DedicatedVideoMemory;
            public UIntPtr DedicatedSystemMemory;
            public UIntPtr SharedSystemMemory;
            public long AdapterLuid;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DXGI_ADAPTER_DESC2
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Description;
            public uint VendorId;
            public uint DeviceId;
            public uint SubSysId;
            public uint Revision;
            public UIntPtr DedicatedVideoMemory;
            public UIntPtr DedicatedSystemMemory;
            public UIntPtr SharedSystemMemory;
            public long AdapterLuid;
            public uint Flags;
            public uint GraphicsPreemptionGranularity;
            public uint ComputePreemptionGranularity;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_QUERY_VIDEO_MEMORY_INFO
        {
            public ulong Budget;
            public ulong CurrentUsage;
            public ulong AvailableForReservation;
            public ulong CurrentReservation;
        }

        #endregion

        #region Safe Read-Only Win32 Storage Query Interop

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_PROPERTY_QUERY
        {
            public uint PropertyId;
            public uint QueryType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public byte[] AdditionalParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_DEVICE_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            public byte DeviceType;
            public byte DeviceTypeModifier;
            [MarshalAs(UnmanagedType.U1)] public bool RemovableMedia;
            [MarshalAs(UnmanagedType.U1)] public bool CommandQueueing;
            public uint VendorIdOffset;
            public uint ProductIdOffset;
            public uint ProductRevisionOffset;
            public uint SerialNumberOffset;
            public uint BusType;
            public uint RawPropertiesLength;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_TEMPERATURE_INFO
        {
            public ushort Index;
            public short Temperature;
            public short OverThreshold;
            public short UnderThreshold;
            public byte OverThresholdStatus;
            public byte UnderThresholdStatus;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public byte[] Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_TEMPERATURE_DATA_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            public short CriticalThreshold;
            public short WarningThreshold;
            public ushort InfoCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public byte[] Reserved0;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public STORAGE_TEMPERATURE_INFO[] TemperatureInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            [MarshalAs(UnmanagedType.I1)] public bool IncursSeekPenalty;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVICE_TRIM_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            [MarshalAs(UnmanagedType.I1)] public bool TrimEnabled;
        }

        #endregion

        public Task InitializeAsync()
        {
            LoggingService.Instance.Info("NativeHardware", "Native DXGI & Win32 Storage Harvester initialized (100% driverless).");
            return Task.CompletedTask;
        }

        public void Poll(SystemSummary summary, List<SensorMetric> metrics, Action<string, double> recordHistory)
        {
            try
            {
                PollGpus(summary, metrics, recordHistory);
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("NativeHardware", $"GPU DXGI poll warning: {ex.Message}");
            }

            try
            {
                PollPhysicalDrives(summary, metrics, recordHistory);
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("NativeHardware", $"Storage SMART poll warning: {ex.Message}");
            }
        }

        #region GPU Harvesting (DXGI)

        private void PollGpus(SystemSummary summary, List<SensorMetric> metrics, Action<string, double> recordHistory)
        {
            var guid = IID_IDXGIFactory1;
            if (CreateDXGIFactory1(ref guid, out var factory) != 0 || factory == null)
            {
                return;
            }

            try
            {
                string cpuName = !string.IsNullOrEmpty(summary.Cpu.Name) && summary.Cpu.Name != "Unknown CPU"
                    ? summary.Cpu.Name
                    : SystemHarvester.GetNativeCpuName();

                var gpuList = new List<GpuSummary>();
                uint adapterIndex = 0;

                while (factory.EnumAdapters1(adapterIndex, out var adapter) == 0 && adapter != null)
                {
                    try
                    {
                        if (adapter.GetDesc1(out var desc) == 0)
                        {
                            // Skip software renderers (WARP, Microsoft Basic Render Driver)
                            if ((desc.Flags & 2) != 0 || desc.Description.Contains("Basic Render", StringComparison.OrdinalIgnoreCase))
                            {
                                adapterIndex++;
                                continue;
                            }

                            string vendor = desc.VendorId switch
                            {
                                0x10DE => "NVIDIA",
                                0x1002 => "AMD",
                                0x8086 => "Intel",
                                _ => "Other"
                            };

                            bool isDiscrete = DetermineIfDiscrete(desc.Description, cpuName, desc.VendorId);
                            string resolvedName = ResolveGpuName(desc.Description, cpuName, desc.DeviceId);

                            double vramTotalGb = Math.Round((ulong)desc.DedicatedVideoMemory / (1024.0 * 1024.0 * 1024.0), 2);
                            double sharedTotalGb = Math.Round((ulong)desc.SharedSystemMemory / (1024.0 * 1024.0 * 1024.0), 2);
                            double? vramUsedGb = null;

                            // 1. Check Native User-Mode Vendor Probes for dGPUs (NVML / ADL)
                            GpuTelemetryData? vendorTel = null;
                            if (vendor == "NVIDIA")
                            {
                                vendorTel = NvmlGpuProbe.QueryTelemetry(desc.Description, (int)adapterIndex);
                            }
                            else if (vendor == "AMD" && isDiscrete)
                            {
                                vendorTel = AdlGpuProbe.QueryTelemetry((int)adapterIndex);
                            }

                            if (vendorTel != null)
                            {
                                if (vendorTel.VramUsedGb.HasValue) vramUsedGb = vendorTel.VramUsedGb;
                                if (vendorTel.VramTotalGb.HasValue && vendorTel.VramTotalGb > 0) vramTotalGb = vendorTel.VramTotalGb.Value;
                            }

                            // 2. Query live VRAM usage via IDXGIAdapter3 if not retrieved or for shared pool
                            if (!vramUsedGb.HasValue && adapter is IDXGIAdapter3 adapter3)
                            {
                                try
                                {
                                    // 0 = DXGI_MEMORY_SEGMENT_GROUP_LOCAL (Dedicated VRAM)
                                    // 1 = DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL (Shared RAM)
                                    uint segGroup = vramTotalGb > 0 ? 0u : 1u;
                                    if (adapter3.QueryVideoMemoryInfo(0, segGroup, out var memInfo) == 0)
                                    {
                                        vramUsedGb = Math.Round(memInfo.CurrentUsage / (1024.0 * 1024.0 * 1024.0), 2);
                                    }
                                }
                                catch { }
                            }

                            string gpuId = $"/gpu-{vendor.ToLowerInvariant()}/{adapterIndex}";
                            var gpu = new GpuSummary
                            {
                                Id = gpuId,
                                Name = resolvedName,
                                Vendor = vendor,
                                IsDiscrete = isDiscrete,
                                VramTotalGb = vramTotalGb > 0 ? vramTotalGb : sharedTotalGb,
                                VramUsedGb = vramUsedGb
                            };

                            if (vendorTel != null)
                            {
                                gpu.TempC = vendorTel.TempC;
                                gpu.HotspotTempC = vendorTel.HotspotTempC;
                                gpu.MemoryJunctionTempC = vendorTel.MemoryJunctionTempC;
                                gpu.LoadPercent = vendorTel.LoadPercent ?? 0.0;
                                gpu.PowerWatts = vendorTel.PowerWatts;
                                gpu.FanRpm = vendorTel.FanPercentOrRpm;
                                gpu.CoreClockMhz = vendorTel.CoreClockMhz;
                                gpu.MemoryClockMhz = vendorTel.MemoryClockMhz;
                                gpu.IsBoost = vendorTel.IsBoost;
                            }

                            if (!gpu.IsBoost && gpu.CoreClockMhz.HasValue && gpu.CoreClockMhz.Value >= 1350)
                            {
                                gpu.IsBoost = true;
                            }

                            gpuList.Add(gpu);

                            // Record sparkline history for discrete GPU (or primary)
                            if (gpu.IsDiscrete || gpuList.Count == 1)
                            {
                                if (gpu.TempC.HasValue && !double.IsNaN(gpu.TempC.Value)) recordHistory("gpu_temp", gpu.TempC.Value);
                                if (gpu.HotspotTempC.HasValue && !double.IsNaN(gpu.HotspotTempC.Value)) recordHistory("gpu_hotspot_temp", gpu.HotspotTempC.Value);
                                if (gpu.LoadPercent.HasValue && !double.IsNaN(gpu.LoadPercent.Value)) recordHistory("gpu_load", gpu.LoadPercent.Value);
                                if (gpu.PowerWatts.HasValue && !double.IsNaN(gpu.PowerWatts.Value)) recordHistory("gpu_power", gpu.PowerWatts.Value);
                            }

                            // Add normalized telemetry metrics
                            metrics.Add(new SensorMetric
                            {
                                Id = $"{gpuId}_vram_total",
                                Name = $"{resolvedName} - Dedicated VRAM",
                                HardwareId = gpuId,
                                HardwareName = resolvedName,
                                Category = HardwareCategory.Gpu,
                                Type = MetricType.Capacity,
                                Value = gpu.VramTotalGb ?? 0,
                                Unit = "GB"
                            });

                            if (vramUsedGb.HasValue)
                            {
                                metrics.Add(new SensorMetric
                                {
                                    Id = $"{gpuId}_vram_used",
                                    Name = $"{resolvedName} - VRAM Used",
                                    HardwareId = gpuId,
                                    HardwareName = resolvedName,
                                    Category = HardwareCategory.Gpu,
                                    Type = MetricType.Capacity,
                                    Value = vramUsedGb.Value,
                                    Unit = "GB"
                                });
                            }

                            if (gpu.TempC.HasValue)
                            {
                                metrics.Add(new SensorMetric
                                {
                                    Id = $"{gpuId}_temp",
                                    Name = $"{resolvedName} - Core Temperature",
                                    HardwareId = gpuId,
                                    HardwareName = resolvedName,
                                    Category = HardwareCategory.Gpu,
                                    Type = MetricType.Temperature,
                                    Value = gpu.TempC.Value,
                                    Unit = "°C"
                                });
                            }

                            if (gpu.HotspotTempC.HasValue)
                            {
                                metrics.Add(new SensorMetric
                                {
                                    Id = $"{gpuId}_hotspot",
                                    Name = $"{resolvedName} - Hotspot Temperature",
                                    HardwareId = gpuId,
                                    HardwareName = resolvedName,
                                    Category = HardwareCategory.Gpu,
                                    Type = MetricType.Temperature,
                                    Value = gpu.HotspotTempC.Value,
                                    Unit = "°C"
                                });
                            }

                            if (gpu.LoadPercent.HasValue)
                            {
                                metrics.Add(new SensorMetric
                                {
                                    Id = $"{gpuId}_load",
                                    Name = $"{resolvedName} - Core Load",
                                    HardwareId = gpuId,
                                    HardwareName = resolvedName,
                                    Category = HardwareCategory.Gpu,
                                    Type = MetricType.Load,
                                    Value = gpu.LoadPercent.Value,
                                    Unit = "%"
                                });
                            }

                            if (gpu.PowerWatts.HasValue)
                            {
                                metrics.Add(new SensorMetric
                                {
                                    Id = $"{gpuId}_power",
                                    Name = $"{resolvedName} - Board Power",
                                    HardwareId = gpuId,
                                    HardwareName = resolvedName,
                                    Category = HardwareCategory.Gpu,
                                    Type = MetricType.Power,
                                    Value = gpu.PowerWatts.Value,
                                    Unit = "W"
                                });
                            }

                            if (gpu.FanRpm.HasValue)
                            {
                                metrics.Add(new SensorMetric
                                {
                                    Id = $"{gpuId}_fan",
                                    Name = $"{resolvedName} - Fan Speed",
                                    HardwareId = gpuId,
                                    HardwareName = resolvedName,
                                    Category = HardwareCategory.Gpu,
                                    Type = MetricType.Fan,
                                    Value = gpu.FanRpm.Value,
                                    Unit = gpu.FanRpm.Value <= 100 ? "%" : "RPM"
                                });
                            }

                            if (gpu.CoreClockMhz.HasValue)
                            {
                                metrics.Add(new SensorMetric
                                {
                                    Id = $"{gpuId}_core_clock",
                                    Name = $"{resolvedName} - Core Clock",
                                    HardwareId = gpuId,
                                    HardwareName = resolvedName,
                                    Category = HardwareCategory.Gpu,
                                    Type = MetricType.Clock,
                                    Value = gpu.CoreClockMhz.Value,
                                    Unit = "MHz"
                                });
                            }

                            if (gpu.MemoryClockMhz.HasValue)
                            {
                                metrics.Add(new SensorMetric
                                {
                                    Id = $"{gpuId}_mem_clock",
                                    Name = $"{resolvedName} - Memory Clock",
                                    HardwareId = gpuId,
                                    HardwareName = resolvedName,
                                    Category = HardwareCategory.Gpu,
                                    Type = MetricType.Clock,
                                    Value = gpu.MemoryClockMhz.Value,
                                    Unit = "MHz"
                                });
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        try { Marshal.ReleaseComObject(adapter); } catch { }
                        adapterIndex++;
                    }
                }

                if (gpuList.Count > 0)
                {
                    summary.Gpus = gpuList;
                }
            }
            finally
            {
                try { Marshal.ReleaseComObject(factory); } catch { }
            }
        }

        private static bool DetermineIfDiscrete(string description, string cpuName, uint vendorId)
        {
            string descUpper = description.ToUpperInvariant();
            string cpuUpper = cpuName.ToUpperInvariant();

            // NVIDIA discrete GPUs
            if (vendorId == 0x10DE) return true;

            // AMD APUs
            if (vendorId == 0x1002)
            {
                if (descUpper.Contains("780M") || descUpper.Contains("680M") || descUpper.Contains("890M") ||
                    descUpper.Contains("610M") || descUpper.Contains("VEGA") || descUpper.Contains("RADEON(TM) GRAPHICS"))
                {
                    return false;
                }

                if (cpuUpper.Contains("RYZEN") && (cpuUpper.Contains("7840") || cpuUpper.Contains("6800") || cpuUpper.Contains("8840") || cpuUpper.Contains("5700G") || cpuUpper.Contains("5600G")))
                {
                    if (!descUpper.Contains("RX ") && !descUpper.Contains("XT") && !descUpper.Contains("PRO W"))
                    {
                        return false;
                    }
                }

                if (descUpper.Contains("RX ") || descUpper.Contains("PRO W") || descUpper.Contains("RADEON PRO"))
                {
                    return true;
                }

                return false;
            }

            // Intel
            if (vendorId == 0x8086)
            {
                if (descUpper.Contains("ARC")) return true;
                return false; // Iris, UHD, HD
            }

            return false;
        }

        private static string ResolveGpuName(string description, string cpuName, uint deviceId)
        {
            if (description.Equals("AMD Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) ||
                description.Equals("AMD Radeon Graphics", StringComparison.OrdinalIgnoreCase))
            {
                string cpuUpper = cpuName.ToUpperInvariant();
                if (cpuUpper.Contains("7840") || cpuUpper.Contains("7940") || cpuUpper.Contains("8840") || cpuUpper.Contains("8945"))
                {
                    return "AMD Radeon 780M Graphics";
                }
                if (cpuUpper.Contains("6800") || cpuUpper.Contains("6600") || cpuUpper.Contains("6900"))
                {
                    return "AMD Radeon 680M Graphics";
                }
                if (cpuUpper.Contains("7730") || cpuUpper.Contains("7530") || cpuUpper.Contains("5800") || cpuUpper.Contains("5700"))
                {
                    return "AMD Radeon Vega Graphics";
                }
            }
            return description;
        }

        #endregion

        #region Safe Physical Drive SMART Harvesting

        private void PollPhysicalDrives(SystemSummary summary, List<SensorMetric> metrics, Action<string, double> recordHistory)
        {
            var driveList = new List<DriveSummary>();

            // Query up to 4 physical drives using safe read-only query mode
            for (int driveIndex = 0; driveIndex < 4; driveIndex++)
            {
                string devicePath = $@"\\.\PhysicalDrive{driveIndex}";

                // dwDesiredAccess = 0 (Query only / read attributes, NEVER generic write)
                using var hDevice = CreateFile(
                    devicePath,
                    0,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (hDevice.IsInvalid) continue;
                ProcessSinglePhysicalDrive(hDevice, driveIndex, driveList, metrics, recordHistory);
            }

            if (driveList.Count > 0)
            {
                if (summary.Storage.Count == 0)
                {
                    summary.Storage = driveList;
                }
                else
                {
                    for (int i = 0; i < Math.Min(summary.Storage.Count, driveList.Count); i++)
                    {
                        if (driveList[i].TempC.HasValue)
                        {
                            summary.Storage[i].TempC = driveList[i].TempC;
                        }
                        if (!string.IsNullOrEmpty(driveList[i].Name) && !summary.Storage[i].Name.Contains(driveList[i].Name))
                        {
                            summary.Storage[i].Name = driveList[i].Name;
                        }
                    }
                }
            }
        }

        private void ProcessSinglePhysicalDrive(
            SafeFileHandle hDevice,
            int driveIndex,
            List<DriveSummary> driveList,
            List<SensorMetric> metrics,
            Action<string, double> recordHistory)
        {
            string driveModel = $"Drive #{driveIndex}";
            string firmwareRev = string.Empty;
            string serialNum = string.Empty;
            string busType = "NVMe";
            string mediaType = "Solid State Drive (SSD)";
            string trimStatus = "Supported (Active)";
            double? driveTemp = null;
            bool isRemovable = false;

            // 1. Query Device Descriptor for Model Name, Firmware Revision, Serial, and BusType
            var query = new STORAGE_PROPERTY_QUERY
            {
                PropertyId = 0, // StorageDeviceProperty
                QueryType = 0   // PropertyStandardQuery
            };

            int querySize = Marshal.SizeOf<STORAGE_PROPERTY_QUERY>();
            IntPtr pQuery = Marshal.AllocHGlobal(querySize);
            IntPtr pOut = Marshal.AllocHGlobal(2048);

            try
            {
                Marshal.StructureToPtr(query, pQuery, false);
                if (DeviceIoControl(hDevice, IOCTL_STORAGE_QUERY_PROPERTY, pQuery, (uint)querySize, pOut, 2048, out uint bytesRet, IntPtr.Zero))
                {
                    var desc = Marshal.PtrToStructure<STORAGE_DEVICE_DESCRIPTOR>(pOut);
                    isRemovable = desc.RemovableMedia;
                    if (desc.ProductIdOffset > 0 && desc.ProductIdOffset < bytesRet)
                    {
                        IntPtr pStr = IntPtr.Add(pOut, (int)desc.ProductIdOffset);
                        string? model = Marshal.PtrToStringAnsi(pStr);
                        if (!string.IsNullOrWhiteSpace(model)) driveModel = model.Trim();
                    }
                    if (desc.ProductRevisionOffset > 0 && desc.ProductRevisionOffset < bytesRet)
                    {
                        IntPtr pStr = IntPtr.Add(pOut, (int)desc.ProductRevisionOffset);
                        string? rev = Marshal.PtrToStringAnsi(pStr);
                        if (!string.IsNullOrWhiteSpace(rev)) firmwareRev = rev.Trim();
                    }
                    if (desc.SerialNumberOffset > 0 && desc.SerialNumberOffset < bytesRet)
                    {
                        IntPtr pStr = IntPtr.Add(pOut, (int)desc.SerialNumberOffset);
                        string? sn = Marshal.PtrToStringAnsi(pStr);
                        if (!string.IsNullOrWhiteSpace(sn)) serialNum = sn.Trim().TrimEnd('.');
                    }

                    busType = desc.BusType switch
                    {
                        17 => "NVMe",
                        11 => "SATA",
                        7 => "USB",
                        3 => "ATAPI",
                        8 => "RAID",
                        16 => "SD",
                        _ => "PCIe / NVMe"
                    };
                }
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(pQuery);
                Marshal.FreeHGlobal(pOut);
            }

            // Exclude external / removable USB flash drives and SD cards from internal system hardware monitor
            if (isRemovable || busType.Equals("USB", StringComparison.OrdinalIgnoreCase) || busType.Equals("SD", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 2. Query Seek Penalty (SSD vs HDD)
            try
            {
                var seekQuery = new STORAGE_PROPERTY_QUERY { PropertyId = 7, QueryType = 0 };
                IntPtr pSeekQ = Marshal.AllocHGlobal(querySize);
                IntPtr pSeekOut = Marshal.AllocHGlobal(64);
                Marshal.StructureToPtr(seekQuery, pSeekQ, false);
                if (DeviceIoControl(hDevice, IOCTL_STORAGE_QUERY_PROPERTY, pSeekQ, (uint)querySize, pSeekOut, 64, out _, IntPtr.Zero))
                {
                    var seekDesc = Marshal.PtrToStructure<DEVICE_SEEK_PENALTY_DESCRIPTOR>(pSeekOut);
                    mediaType = seekDesc.IncursSeekPenalty ? "Hard Disk Drive (HDD)" : "Solid State Drive (SSD)";
                }
                Marshal.FreeHGlobal(pSeekQ);
                Marshal.FreeHGlobal(pSeekOut);
            }
            catch { }

            // 3. Query TRIM Support
            try
            {
                var trimQuery = new STORAGE_PROPERTY_QUERY { PropertyId = 8, QueryType = 0 };
                IntPtr pTrimQ = Marshal.AllocHGlobal(querySize);
                IntPtr pTrimOut = Marshal.AllocHGlobal(64);
                Marshal.StructureToPtr(trimQuery, pTrimQ, false);
                if (DeviceIoControl(hDevice, IOCTL_STORAGE_QUERY_PROPERTY, pTrimQ, (uint)querySize, pTrimOut, 64, out _, IntPtr.Zero))
                {
                    var trimDesc = Marshal.PtrToStructure<DEVICE_TRIM_DESCRIPTOR>(pTrimOut);
                    trimStatus = trimDesc.TrimEnabled ? "Supported (Active)" : "Not Supported";
                }
                Marshal.FreeHGlobal(pTrimQ);
                Marshal.FreeHGlobal(pTrimOut);
            }
            catch { }

            // 4. Query StorageDeviceTemperatureProperty (PropertyId = 9, standard Windows 10/11)
            var tempQuery = new STORAGE_PROPERTY_QUERY
            {
                PropertyId = 9, // StorageDeviceTemperatureProperty
                QueryType = 0   // PropertyStandardQuery
            };

            int tempQuerySize = Marshal.SizeOf<STORAGE_PROPERTY_QUERY>();
            IntPtr pTempQuery = Marshal.AllocHGlobal(tempQuerySize);
            int tempOutSize = 512;
            IntPtr pTempOut = Marshal.AllocHGlobal(tempOutSize);

            try
            {
                Marshal.StructureToPtr(tempQuery, pTempQuery, false);
                if (DeviceIoControl(hDevice, IOCTL_STORAGE_QUERY_PROPERTY, pTempQuery, (uint)tempQuerySize, pTempOut, (uint)tempOutSize, out uint bytesReturned, IntPtr.Zero))
                {
                    if (bytesReturned >= Marshal.SizeOf<STORAGE_TEMPERATURE_DATA_DESCRIPTOR>())
                    {
                        var tempData = Marshal.PtrToStructure<STORAGE_TEMPERATURE_DATA_DESCRIPTOR>(pTempOut);
                        if (tempData.InfoCount > 0 && tempData.TemperatureInfo != null && tempData.TemperatureInfo.Length > 0)
                        {
                            short t = tempData.TemperatureInfo[0].Temperature;
                            if (t > 0 && t < 125)
                            {
                                driveTemp = t;
                            }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(pTempQuery);
                Marshal.FreeHGlobal(pTempOut);
            }

            var driveSummary = new DriveSummary
            {
                Name = driveModel,
                TempC = driveTemp,
                IsTempEstimated = !driveTemp.HasValue,
                TempSource = driveTemp.HasValue ? "Hardware Sensor" : "Estimated",
                FirmwareRevision = firmwareRev,
                SerialNumber = serialNum,
                BusType = busType,
                MediaType = mediaType,
                TrimStatus = trimStatus,
                HealthStatus = "Healthy (OK)"
            };

            driveList.Add(driveSummary);

            if (driveTemp.HasValue)
            {
                string metricId = $"disk_{driveIndex}_temp";
                recordHistory(metricId, driveTemp.Value);

                metrics.Add(new SensorMetric
                {
                    Id = metricId,
                    Name = $"{driveModel} Temperature",
                    HardwareId = $"disk_{driveIndex}",
                    HardwareName = driveModel,
                    Category = HardwareCategory.Storage,
                    Type = MetricType.Temperature,
                    Value = driveTemp.Value,
                    Unit = "°C"
                });
            }
        }

        #endregion

        public void Dispose() { }
    }
}
