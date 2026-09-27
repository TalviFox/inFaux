using System;
using System.Text.Json.Serialization;

namespace InFox.Models.Config
{
    public class AppConfig
    {
        [JsonPropertyName("apiPort")]
        public int ApiPort { get; set; } = 8765;

        [JsonPropertyName("enableApi")]
        public bool EnableApi { get; set; } = true;

        [JsonPropertyName("apiBindLocalhostOnly")]
        public bool ApiBindLocalhostOnly { get; set; } = true;

        [JsonPropertyName("pollingIntervalMs")]
        public int PollingIntervalMs { get; set; } = 1000;

        [JsonPropertyName("traySensorTarget")]
        public string TraySensorTarget { get; set; } = "cpu_temp"; // "cpu_temp", "gpu_temp", "auto_max_temp"

        [JsonPropertyName("preferredNetworkAdapter")]
        public string PreferredNetworkAdapter { get; set; } = "Auto"; // "Auto" or specific adapter name/description

        [JsonPropertyName("chassisProfile")]
        public string ChassisProfile { get; set; } = "Auto"; // "Auto", "Laptop", "Desktop"

        [JsonPropertyName("coolerProfile")]
        public string CoolerProfile { get; set; } = "Auto"; // "Auto", "AIO", "TowerAir", "Compact"

        [JsonPropertyName("cpuThermalOffset")]
        public double CpuThermalOffset { get; set; } = 0.0; // Calibration offset in degrees C (-15 to +15)

        [JsonPropertyName("cpuTdpOverrideWatts")]
        public int CpuTdpOverrideWatts { get; set; } = 0; // 0 = Auto detected / standard

        [JsonPropertyName("trayDisplayBadge")]
        public bool TrayDisplayBadge { get; set; } = true;

        [JsonPropertyName("startWithWindows")]
        public bool StartWithWindows { get; set; } = true;

        [JsonPropertyName("checkForUpdates")]
        public bool CheckForUpdates { get; set; } = true;

        [JsonPropertyName("lastUpdateCheckUtc")]
        public DateTime? LastUpdateCheckUtc { get; set; }

        [JsonPropertyName("ignoredUpdateVersion")]
        public string? IgnoredUpdateVersion { get; set; }
    }
}
