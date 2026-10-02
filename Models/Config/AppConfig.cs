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

        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "RedFox"; // "RedFox", "ArcticFox", "FennecFox", "SilverFox"

        [JsonPropertyName("arcticFoxLightMode")]
        public bool ArcticFoxLightMode { get; set; } = false;

        [JsonPropertyName("silverFoxOledMode")]
        public bool SilverFoxOledMode { get; set; } = false;

        private string _preferredNetworkAdapter = "Auto";
        [JsonPropertyName("preferredNetworkAdapter")]
        public string PreferredNetworkAdapter
        {
            get => string.IsNullOrWhiteSpace(_preferredNetworkAdapter) ? "Auto" : _preferredNetworkAdapter;
            set => _preferredNetworkAdapter = string.IsNullOrWhiteSpace(value) ? "Auto" : value;
        }

        [JsonPropertyName("chassisProfile")]
        public string ChassisProfile { get; set; } = "Auto"; // "Auto", "Laptop", "Desktop"

        [JsonPropertyName("coolerProfile")]
        public string CoolerProfile { get; set; } = "Auto"; // "Auto", "AIO", "TowerAir", "Compact"

        [JsonPropertyName("cpuThermalOffset")]
        public double CpuThermalOffset { get; set; } = 0.0; // Calibration offset in degrees C (-15 to +15)

        [JsonPropertyName("roomAmbientOffsetC")]
        public double RoomAmbientOffsetC { get; set; } = 0.0; // Manual room ambient calibration offset (-10 to +10 °C)

        [JsonPropertyName("enableBleAmbient")]
        public bool EnableBleAmbient { get; set; } = false;

        [JsonPropertyName("bleAmbientSensorMac")]
        public string BleAmbientSensorMac { get; set; } = "Auto"; // "Auto" (nearest/strongest) or specific MAC

        [JsonPropertyName("enableBleChassis")]
        public bool EnableBleChassis { get; set; } = false;

        [JsonPropertyName("bleChassisSensorMac")]
        public string BleChassisSensorMac { get; set; } = ""; // Specific MAC of thermometer placed inside PC case

        [JsonPropertyName("enableBleRadiator")]
        public bool EnableBleRadiator { get; set; } = false;

        [JsonPropertyName("bleRadiatorSensorMac")]
        public string BleRadiatorSensorMac { get; set; } = ""; // Specific MAC of thermometer placed post-radiator / exhaust

        [JsonPropertyName("cpuTdpOverrideWatts")]
        public int CpuTdpOverrideWatts { get; set; } = 0; // 0 = Auto detected / standard

        [JsonPropertyName("timProfile")]
        public string TimProfile { get; set; } = "Auto"; // "Auto", "Kryonaut", "Noctua_NTH2", "Arctic_MX6", "PTM7950", "LiquidMetal"

        [JsonPropertyName("timAppliedDateUtc")]
        public DateTime? TimAppliedDateUtc { get; set; }

        [JsonPropertyName("enableRepasteToasts")]
        public bool EnableRepasteToasts { get; set; } = false;

        [JsonPropertyName("trayDisplayBadge")]
        public bool TrayDisplayBadge { get; set; } = true;

        [JsonPropertyName("startWithWindows")]
        public bool StartWithWindows { get; set; } = true;

        [JsonPropertyName("minimizeOnStartup")]
        public bool MinimizeOnStartup { get; set; } = false;

        [JsonPropertyName("checkForUpdates")]
        public bool CheckForUpdates { get; set; } = true;

        [JsonPropertyName("lastUpdateCheckUtc")]
        public DateTime? LastUpdateCheckUtc { get; set; }

        [JsonPropertyName("ignoredUpdateVersion")]
        public string? IgnoredUpdateVersion { get; set; }
    }
}
