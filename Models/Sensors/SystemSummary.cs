using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace InFox.Models.Sensors
{
    public class SystemSummary
    {
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("cpu")]
        public CpuSummary Cpu { get; set; } = new();

        [JsonPropertyName("gpus")]
        public List<GpuSummary> Gpus { get; set; } = new();

        [JsonPropertyName("gpu")]
        public GpuSummary Gpu => PrimaryGpu;

        [JsonIgnore]
        public GpuSummary PrimaryGpu
        {
            get
            {
                if (Gpus.Count == 0) return new GpuSummary();
                return Gpus.Find(g => g.IsDiscrete) ?? Gpus[0];
            }
        }

        [JsonPropertyName("memory")]
        public MemorySummary Memory { get; set; } = new();

        [JsonPropertyName("storage")]
        public List<DriveSummary> Storage { get; set; } = new();

        [JsonPropertyName("battery")]
        public BatterySummary Battery { get; set; } = new();

        [JsonPropertyName("network")]
        public NetworkSummary Network { get; set; } = new();

        [JsonPropertyName("chassis")]
        public ChassisSummary Chassis { get; set; } = new();

        [JsonPropertyName("isVirtualMachine")]
        public bool IsVirtualMachine { get; set; }
    }

    public class CoreMetric
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("tempC")]
        public double? TempC { get; set; }

        [JsonPropertyName("clockMhz")]
        public double? ClockMhz { get; set; }

        [JsonPropertyName("loadPercent")]
        public double? LoadPercent { get; set; }
    }

    public class CpuSummary
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "Unknown CPU";

        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Name)) return "Processor";
                string clean = Name;
                // Strip redundant integrated graphics and core count suffixes from processor title
                clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+w/\s+.*Graphics.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+with\s+.*Graphics.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+(Six|Eight|Twelve|Sixteen|\d+)-Core Processor.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                return clean.Trim();
            }
        }

        [JsonPropertyName("tempC")]
        public double? TempC { get; set; }

        [JsonPropertyName("loadPercent")]
        public double LoadPercent { get; set; }

        [JsonPropertyName("powerWatts")]
        public double? PowerWatts { get; set; }

        [JsonPropertyName("clockGhz")]
        public double? ClockGhz { get; set; }

        [JsonPropertyName("coreCount")]
        public int CoreCount { get; set; } = Environment.ProcessorCount;

        [JsonPropertyName("cores")]
        public List<CoreMetric> Cores { get; set; } = new();
    }

    public class GpuSummary : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private bool _isSelected;
        [JsonIgnore]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        [JsonIgnore]
        public string ShortName
        {
            get
            {
                if (string.IsNullOrEmpty(Name)) return "GPU";
                return Name
                    .Replace("NVIDIA GeForce RTX ", "RTX ")
                    .Replace("NVIDIA GeForce GTX ", "GTX ")
                    .Replace("NVIDIA GeForce ", "")
                    .Replace("NVIDIA ", "")
                    .Replace("Intel(R) UHD Graphics ", "Intel UHD ")
                    .Replace("Intel(R) Iris(R) Xe Graphics ", "Iris Xe ")
                    .Replace("Intel(R) Arc(TM) Graphics", "Intel Arc")
                    .Replace("AMD Radeon RX ", "RX ")
                    .Replace("AMD Radeon(TM) Graphics", "Radeon Graphics")
                    .Replace("AMD Radeon Graphics", "Radeon Graphics")
                    .Replace("AMD Radeon ", "");
            }
        }

        [JsonIgnore]
        public string HotspotDisplayText
        {
            get
            {
                if (HotspotTempC.HasValue) return $"Hotspot: {HotspotTempC.Value:F0}°C";
                if (!IsDiscrete) return "SoC Shared Die";
                return "Hotspot: --";
            }
        }

        [JsonIgnore]
        public string FanDisplayText
        {
            get
            {
                if (FanRpm.HasValue)
                {
                    if (FanRpm.Value <= 100) return $"Fan: {FanRpm.Value:F0}%";
                    return $"Fan: {FanRpm.Value:F0} RPM";
                }
                if (!IsDiscrete) return "Integrated APU";
                return "Fan: N/A";
            }
        }

        [JsonIgnore]
        public string PowerLabel => IsDiscrete ? "BOARD POWER" : "SoC POWER";

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = "Unknown GPU";

        [JsonPropertyName("isDiscrete")]
        public bool IsDiscrete { get; set; } = true;

        [JsonPropertyName("vendor")]
        public string Vendor { get; set; } = "Unknown";

        [JsonPropertyName("tempC")]
        public double? TempC { get; set; }

        [JsonPropertyName("hotspotTempC")]
        public double? HotspotTempC { get; set; }

        [JsonPropertyName("memoryJunctionTempC")]
        public double? MemoryJunctionTempC { get; set; }

        [JsonPropertyName("loadPercent")]
        public double? LoadPercent { get; set; }

        [JsonPropertyName("powerWatts")]
        public double? PowerWatts { get; set; }

        [JsonPropertyName("vramUsedGb")]
        public double? VramUsedGb { get; set; }

        [JsonPropertyName("vramTotalGb")]
        public double? VramTotalGb { get; set; }

        [JsonPropertyName("fanRpm")]
        public double? FanRpm { get; set; }

        [JsonPropertyName("coreClockMhz")]
        public double? CoreClockMhz { get; set; }

        [JsonPropertyName("memoryClockMhz")]
        public double? MemoryClockMhz { get; set; }
    }

    public class MemorySummary
    {
        [JsonPropertyName("usedGb")]
        public double UsedGb { get; set; }

        [JsonPropertyName("totalGb")]
        public double TotalGb { get; set; }

        [JsonPropertyName("percent")]
        public double Percent { get; set; }

        [JsonPropertyName("availableGb")]
        public double AvailableGb { get; set; }

        [JsonPropertyName("speedMts")]
        public int? SpeedMts { get; set; }
    }

    public class DriveSummary
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("driveLetter")]
        public string DriveLetter { get; set; } = string.Empty;

        [JsonPropertyName("tempC")]
        public double? TempC { get; set; }

        [JsonPropertyName("isTempEstimated")]
        public bool IsTempEstimated { get; set; }

        [JsonPropertyName("tempSource")]
        public string TempSource { get; set; } = "Hardware Diode";

        [JsonPropertyName("healthPercent")]
        public double? HealthPercent { get; set; } = 100.0;

        [JsonPropertyName("healthStatus")]
        public string HealthStatus { get; set; } = "Healthy (OK)";

        [JsonPropertyName("firmwareRevision")]
        public string FirmwareRevision { get; set; } = string.Empty;

        [JsonPropertyName("serialNumber")]
        public string SerialNumber { get; set; } = string.Empty;

        [JsonPropertyName("busType")]
        public string BusType { get; set; } = "NVMe";

        [JsonPropertyName("mediaType")]
        public string MediaType { get; set; } = "Solid State Drive (SSD)";

        [JsonPropertyName("trimStatus")]
        public string TrimStatus { get; set; } = "Supported (Active)";

        [JsonPropertyName("usedGb")]
        public double UsedGb { get; set; }

        [JsonPropertyName("totalGb")]
        public double TotalGb { get; set; }

        [JsonPropertyName("readSpeedMBps")]
        public double ReadSpeedMBps { get; set; }

        [JsonPropertyName("writeSpeedMBps")]
        public double WriteSpeedMBps { get; set; }

        [JsonIgnore]
        public double FreeGb
        {
            get => Math.Max(0, TotalGb - UsedGb);
            set { }
        }

        [JsonIgnore]
        public double UsedPercent
        {
            get => TotalGb > 0 ? Math.Clamp(Math.Round((UsedGb / TotalGb) * 100.0, 1), 0.0, 100.0) : 0.0;
            set { }
        }

        [JsonIgnore]
        public string TempTooltip
        {
            get => IsTempEstimated
                ? "Modeled via Thermodynamic Observer (Chassis cavity baseline + active I/O Joule dissipation). Physical diode IOCTL returned ERROR_INVALID_FUNCTION from driver."
                : "Hardware Diode (Read via IOCTL_STORAGE_QUERY_PROPERTY)";
            set { }
        }
    }

    public class BatterySummary
    {
        [JsonPropertyName("hasBattery")]
        public bool HasBattery { get; set; }

        [JsonPropertyName("isCharging")]
        public bool IsCharging { get; set; }

        [JsonPropertyName("percent")]
        public double? Percent { get; set; }

        [JsonIgnore]
        public string DisplayPercent => Percent.HasValue ? $"{Percent.Value:F0}%" : (HasBattery ? "--%" : "N/A");

        [JsonPropertyName("dischargeWatts")]
        public double? DischargeWatts { get; set; }

        [JsonPropertyName("estimatedRuntimeMinutes")]
        public int? EstimatedRuntimeMinutes { get; set; }
    }

    public class NetworkSummary
    {
        [JsonPropertyName("adapterName")]
        public string AdapterName { get; set; } = "Primary";

        [JsonIgnore]
        public string CleanAdapterName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(AdapterName)) return "Network";
                string clean = AdapterName;
                clean = System.Text.RegularExpressions.Regex.Replace(clean, @"-WFP.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+MAC Layer.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+LightWeight Filter.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+Packet Scheduler.*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                clean = clean.Trim();
                return string.IsNullOrWhiteSpace(clean) ? "Network" : clean;
            }
        }

        [JsonIgnore]
        public string DisplayDeviceName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(AdapterDescription))
                {
                    string clean = AdapterDescription;
                    clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+#\d+$", "");
                    return clean.Trim();
                }
                return CleanAdapterName;
            }
        }

        [JsonPropertyName("adapterDescription")]
        public string AdapterDescription { get; set; } = string.Empty;

        [JsonPropertyName("downloadMbps")]
        public double DownloadMbps { get; set; }

        [JsonPropertyName("uploadMbps")]
        public double UploadMbps { get; set; }

        [JsonPropertyName("availableAdapters")]
        public List<NetworkAdapterInfo> AvailableAdapters { get; set; } = new();
    }

    public class NetworkAdapterInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("isUp")]
        public bool IsUp { get; set; }

        [JsonPropertyName("linkSpeedMbps")]
        public double LinkSpeedMbps { get; set; }

        [JsonPropertyName("downloadMbps")]
        public double DownloadMbps { get; set; }

        [JsonPropertyName("uploadMbps")]
        public double UploadMbps { get; set; }
    }

    public class ChassisSummary
    {
        [JsonPropertyName("chassisAirTempC")]
        public double ChassisAirTempC { get; set; }

        [JsonPropertyName("chassisAirTempF")]
        public double ChassisAirTempF => Math.Round(ChassisAirTempC * 9.0 / 5.0 + 32.0, 1);

        [JsonPropertyName("estimatedAmbientTempC")]
        public double EstimatedAmbientTempC { get; set; }

        [JsonPropertyName("estimatedAmbientTempF")]
        public double EstimatedAmbientTempF => Math.Round(EstimatedAmbientTempC * 9.0 / 5.0 + 32.0, 1);

        [JsonPropertyName("thermalResistanceCPerW")]
        public double ThermalResistanceCPerW { get; set; }

        [JsonPropertyName("confidencePercent")]
        public int ConfidencePercent { get; set; } = 85;

        [JsonPropertyName("isVirtualMachine")]
        public bool IsVirtualMachine { get; set; }

        [JsonPropertyName("additiveThermalDeltaC")]
        public double? AdditiveThermalDeltaC { get; set; }

        [JsonPropertyName("note")]
        public string Note { get; set; } = "Mathematically extrapolated via convective dissipation slope (P -> 0W).";

        [JsonIgnore]
        public string DisplayText => IsVirtualMachine
            ? $"VM Thermal Impact: +{(AdditiveThermalDeltaC ?? 0):F0}°C ΔT"
            : $"Chassis Air: {ChassisAirTempC:F0}°C ({ChassisAirTempF:F0}°F) • Est. Room: {EstimatedAmbientTempF:F0}°F ({EstimatedAmbientTempC:F0}°C)";
    }
}
