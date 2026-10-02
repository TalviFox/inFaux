using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Windows.Threading;
using InFox.Engine;
using InFox.Models.Config;
using InFox.Models.Sensors;
using InFox.Services;

namespace InFox.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly Dispatcher _dispatcher;

        public event PropertyChangedEventHandler? PropertyChanged;

        private SystemSummary _summary = new();
        public SystemSummary Summary
        {
            get => _summary;
            set
            {
                _summary = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentGpu));
                OnPropertyChanged(nameof(HasMultipleGpus));
                OnPropertyChanged(nameof(MultiGpuVisibility));
                OnPropertyChanged(nameof(GpuList));
                OnPropertyChanged(nameof(DetailTitle));
                OnPropertyChanged(nameof(IsPhysicalMachine));
                OnPropertyChanged(nameof(ChassisProfileTooltip));
                OnPropertyChanged(nameof(ChassisProfileSubLabel));
                OnPropertyChanged(nameof(HasBattery));
                OnPropertyChanged(nameof(BatteryVisibility));
                OnPropertyChanged(nameof(BatteryColumnWidth));
                OnPropertyChanged(nameof(NetworkCardTitle));
                OnPropertyChanged(nameof(NetworkCardToolTip));
                OnPropertyChanged(nameof(ThrottlingDivergenceVisibility));
                OnPropertyChanged(nameof(TimAgeDisplay));
            }
        }

        public System.Windows.Visibility ThrottlingDivergenceVisibility => Summary.Cpu.IsThrottlingDivergence ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public bool HasBattery => Summary.Battery.HasBattery;
        public System.Windows.Visibility BatteryVisibility => Summary.Battery.HasBattery ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public System.Windows.GridLength BatteryColumnWidth => Summary.Battery.HasBattery ? new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) : new System.Windows.GridLength(0);
        public string NetworkCardTitle => Summary.Battery.HasBattery ? "NETWORK & POWER" : "NETWORK";
        public string NetworkCardToolTip => Summary.Battery.HasBattery ? "Click to view network and battery details" : "Click to view network details";

        public bool IsPhysicalMachine => !Summary.IsVirtualMachine;
        public string ChassisProfileTooltip => Summary.IsVirtualMachine
            ? "Disabled on Virtual Machines: Physical chassis modeling is bypassed."
            : "Calibrates thermal airflow baseline for desktop vs laptop enclosures.";
        public string ChassisProfileSubLabel => Summary.IsVirtualMachine
            ? "Bypassed on Virtual Machines."
            : "Calibrate internal case airflow and heat dissipation.";

        public ObservableCollection<SensorMetric> AllMetrics { get; } = new();
        public ObservableCollection<SensorMetric> DetailMetrics { get; } = new();

        // Multi-GPU Support
        private string? _selectedGpuId;
        public GpuSummary CurrentGpu
        {
            get
            {
                if (!string.IsNullOrEmpty(_selectedGpuId))
                {
                    var match = Summary.Gpus.Find(g => g.Id == _selectedGpuId);
                    if (match != null) return match;
                }
                return Summary.PrimaryGpu;
            }
            set
            {
                _selectedGpuId = value?.Id;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DetailTitle));
                UpdateDetailMetrics();
            }
        }

        public bool HasMultipleGpus => Summary.Gpus.Count > 1;
        public IReadOnlyList<GpuSummary> GpuList => Summary.Gpus;

        // Drilldown Detail State
        private string _detailSection = "None"; // "None", "Cpu", "Gpu", "Memory", "Storage", "Network"
        public string DetailSection
        {
            get => _detailSection;
            set
            {
                _detailSection = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDetailOpen));
                OnPropertyChanged(nameof(IsDashboardOpen));
                OnPropertyChanged(nameof(DashboardVisibility));
                OnPropertyChanged(nameof(DetailVisibility));
                OnPropertyChanged(nameof(DetailTitle));
                OnPropertyChanged(nameof(IsCpuDetail));
                OnPropertyChanged(nameof(IsGpuDetail));
                OnPropertyChanged(nameof(IsMemoryDetail));
                OnPropertyChanged(nameof(IsStorageDetail));
                OnPropertyChanged(nameof(IsNetworkDetail));
                OnPropertyChanged(nameof(CpuDetailVisibility));
                OnPropertyChanged(nameof(GpuDetailVisibility));
                OnPropertyChanged(nameof(StorageDetailVisibility));
                UpdateDetailMetrics();
            }
        }

        public bool IsDetailOpen => DetailSection != "None";
        public bool IsDashboardOpen => DetailSection == "None";

        public System.Windows.Visibility DashboardVisibility => IsDashboardOpen ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public System.Windows.Visibility DetailVisibility => IsDetailOpen ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public bool IsCpuDetail => DetailSection == "Cpu";
        public bool IsGpuDetail => DetailSection == "Gpu";
        public bool IsMemoryDetail => DetailSection == "Memory";
        public bool IsStorageDetail => DetailSection == "Storage";
        public bool IsNetworkDetail => DetailSection == "Network";

        public System.Windows.Visibility CpuDetailVisibility => IsCpuDetail ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public System.Windows.Visibility GpuDetailVisibility => IsGpuDetail ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public System.Windows.Visibility StorageDetailVisibility => IsStorageDetail ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public System.Windows.Visibility MultiGpuVisibility => HasMultipleGpus ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public string DetailTitle => DetailSection switch
        {
            "Cpu" => $"Processor (CPU) — {Summary.Cpu.DisplayName}",
            "Gpu" => $"Graphics (GPU) — {CurrentGpu.Name}",
            "Memory" => "System Memory (RAM)",
            "Storage" => "Storage Volumes & Physical Drives",
            "Network" => $"Network & Power — {Summary.Network.CleanAdapterName}",
            _ => "Component Details"
        };

        public void OpenDetail(string section)
        {
            DetailSection = section;
        }

        public void BackToDashboard()
        {
            DetailSection = "None";
        }

        public void SelectGpuById(string gpuId)
        {
            _selectedGpuId = gpuId;
            SyncGpuSelection();
            OnPropertyChanged(nameof(CurrentGpu));
            OnPropertyChanged(nameof(DetailTitle));
            UpdateDetailMetrics();
        }

        private void SyncGpuSelection()
        {
            var active = CurrentGpu;
            foreach (var g in Summary.Gpus)
            {
                g.IsSelected = (g.Id == active.Id);
            }
        }

        // History rolling arrays
        public double[] CpuTempHistory => TelemetryEngine.Instance.GetHistory("cpu_temp");
        public double[] CpuLoadHistory => TelemetryEngine.Instance.GetHistory("cpu_load");
        public double[] CpuPowerHistory => TelemetryEngine.Instance.GetHistory("cpu_power");
        public double[] GpuTempHistory => TelemetryEngine.Instance.GetHistory("gpu_temp");
        public double[] GpuLoadHistory => TelemetryEngine.Instance.GetHistory("gpu_load");
        public double[] GpuPowerHistory => TelemetryEngine.Instance.GetHistory("gpu_power");
        public double[] RamLoadHistory => TelemetryEngine.Instance.GetHistory("ram_load");
        public double[] NetDownloadHistory => TelemetryEngine.Instance.GetHistory("net_download_mbps");

        // Config properties for binding
        public AppConfig Config => ConfigManager.Instance.Config;

        public int ApiPort
        {
            get => Config.ApiPort;
            set
            {
                if (Config.ApiPort != value)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.ApiPort = value);
                    OnPropertyChanged();
                }
            }
        }

        public int PollingIntervalMs
        {
            get => Config.PollingIntervalMs;
            set
            {
                if (Config.PollingIntervalMs != value)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.PollingIntervalMs = value);
                    OnPropertyChanged();
                }
            }
        }

        public bool StartWithWindows
        {
            get => StartupService.IsStartupEnabled();
            set
            {
                StartupService.SetStartup(value, MinimizeOnStartup);
                OnPropertyChanged();
            }
        }

        public bool MinimizeOnStartup
        {
            get => Config.MinimizeOnStartup;
            set
            {
                if (Config.MinimizeOnStartup != value)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.MinimizeOnStartup = value);
                    OnPropertyChanged();
                    StartupService.UpdateStartupArguments(value);
                }
            }
        }

        public bool CheckForUpdates
        {
            get => Config.CheckForUpdates;
            set
            {
                if (Config.CheckForUpdates != value)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.CheckForUpdates = value);
                    OnPropertyChanged();
                }
            }
        }

        public string LocalExecutableHash => UpdateService.Instance.GetLocalExecutableHash();

        public IReadOnlyList<FoxThemeDefinition> AvailableThemes => ThemeService.AvailableThemes;

        public FoxThemeDefinition CurrentTheme => ThemeService.CurrentTheme;

        public string SelectedThemeId
        {
            get => Config.Theme;
            set
            {
                if (Config.Theme != value)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.Theme = value);
                    ThemeService.ApplyTheme(value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentTheme));
                    OnPropertyChanged(nameof(AvailableThemes));
                    OnPropertyChanged(nameof(IsArcticFoxSelected));
                    OnPropertyChanged(nameof(ArcticVariantVisibility));
                    OnPropertyChanged(nameof(IsSilverFoxSelected));
                    OnPropertyChanged(nameof(SilverVariantVisibility));
                }
            }
        }

        public bool IsArcticFoxSelected => string.Equals(SelectedThemeId, "ArcticFox", StringComparison.OrdinalIgnoreCase);
        public System.Windows.Visibility ArcticVariantVisibility => IsArcticFoxSelected ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public bool ArcticFoxLightMode
        {
            get => Config.ArcticFoxLightMode;
            set
            {
                if (Config.ArcticFoxLightMode != value)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.ArcticFoxLightMode = value);
                    ThemeService.ApplyTheme(SelectedThemeId, arcticLightMode: value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ArcticCoatModeName));
                    OnPropertyChanged(nameof(CurrentTheme));
                }
            }
        }

        public string ArcticCoatModeName => ArcticFoxLightMode ? "Blizzard Snow (Light)" : "Midnight Glacial (Dark)";

        public bool IsSilverFoxSelected => string.Equals(SelectedThemeId, "SilverFox", StringComparison.OrdinalIgnoreCase);
        public System.Windows.Visibility SilverVariantVisibility => IsSilverFoxSelected ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public bool SilverFoxOledMode
        {
            get => Config.SilverFoxOledMode;
            set
            {
                if (Config.SilverFoxOledMode != value)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.SilverFoxOledMode = value);
                    ThemeService.ApplyTheme(SelectedThemeId, silverOledMode: value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SilverCoatModeName));
                    OnPropertyChanged(nameof(CurrentTheme));
                }
            }
        }

        public string SilverCoatModeName => SilverFoxOledMode ? "OLED Midnight (True Black)" : "Stealth Slate (Standard)";

        public void SelectTheme(string themeId)
        {
            SelectedThemeId = themeId;
        }

        private string _updateStatusText = "Up to date";
        public string UpdateStatusText
        {
            get => _updateStatusText;
            set
            {
                _updateStatusText = value;
                OnPropertyChanged();
            }
        }

        public string TraySensorTarget
        {
            get => Config.TraySensorTarget;
            set
            {
                if (Config.TraySensorTarget != value)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.TraySensorTarget = value);
                    OnPropertyChanged();
                }
            }
        }

        public string PreferredNetworkAdapter
        {
            get => string.IsNullOrWhiteSpace(Config.PreferredNetworkAdapter) ? "Auto" : Config.PreferredNetworkAdapter;
            set
            {
                // Guard: Ignore null/empty values generated during WPF ComboBox layout/container recreation
                if (string.IsNullOrWhiteSpace(value)) return;

                if (Config.PreferredNetworkAdapter != value)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.PreferredNetworkAdapter = value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        public string ChassisProfile
        {
            get => Config.ChassisProfile;
            set
            {
                if (Config.ChassisProfile != value)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.ChassisProfile = value);
                    OnPropertyChanged();
                }
            }
        }

        public string CoolerProfile
        {
            get => Config.CoolerProfile;
            set
            {
                if (Config.CoolerProfile != value)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.CoolerProfile = value);
                    OnPropertyChanged();
                }
            }
        }

        public double CpuThermalOffset
        {
            get => Config.CpuThermalOffset;
            set
            {
                double rounded = Math.Round(value, 1);
                if (Math.Abs(Config.CpuThermalOffset - rounded) > 0.01)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.CpuThermalOffset = rounded);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CpuThermalOffsetDisplay));
                }
            }
        }

        public string CpuThermalOffsetDisplay => $"{Config.CpuThermalOffset:+0.0;-0.0;0.0} °C";

        public double RoomAmbientOffsetC
        {
            get => Config.RoomAmbientOffsetC;
            set
            {
                double rounded = Math.Round(value, 1);
                if (Math.Abs(Config.RoomAmbientOffsetC - rounded) > 0.01)
                {
                    double diff = rounded - Config.RoomAmbientOffsetC;
                    ConfigManager.Instance.UpdateConfig(c => c.RoomAmbientOffsetC = rounded);
                    if (Summary?.Chassis != null)
                    {
                        Summary.Chassis.EstimatedAmbientTempC = Math.Round(Summary.Chassis.EstimatedAmbientTempC + diff, 1);
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RoomAmbientOffsetDisplay));
                    OnPropertyChanged(nameof(Summary));
                }
            }
        }

        public string RoomAmbientOffsetDisplay => $"{Config.RoomAmbientOffsetC:+0.0;-0.0;0.0} °C";

        // Wireless Room Thermometer (BTHome / BLE) Settings
        public bool EnableBleAmbient
        {
            get => Config.EnableBleAmbient;
            set
            {
                if (Config.EnableBleAmbient != value)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.EnableBleAmbient = value);
                    if (value)
                    {
                        BTHomeBleService.Instance.Start();
                    }
                    else
                    {
                        BTHomeBleService.Instance.Stop();
                    }
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BleSensorSelectorVisibility));
                    OnPropertyChanged(nameof(BleAmbientStatusBadge));
                    OnPropertyChanged(nameof(BleAmbientStatusColor));
                    OnPropertyChanged(nameof(BleAmbientStatusText));
                }
            }
        }

        public System.Windows.Visibility BleSensorSelectorVisibility =>
            Config.EnableBleAmbient ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public string BleAmbientSensorMac
        {
            get => string.IsNullOrWhiteSpace(Config.BleAmbientSensorMac) ? "Auto" : Config.BleAmbientSensorMac;
            set
            {
                if (string.IsNullOrWhiteSpace(value)) return;

                string clean = value.Trim().ToUpperInvariant();
                if (Config.BleAmbientSensorMac != clean)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.BleAmbientSensorMac = clean);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BleAmbientStatusBadge));
                    OnPropertyChanged(nameof(BleAmbientStatusColor));
                    OnPropertyChanged(nameof(BleAmbientStatusText));
                }
            }
        }

        private string _manualBleMacInput = "";
        public string ManualBleMacInput
        {
            get => _manualBleMacInput;
            set
            {
                _manualBleMacInput = value;
                OnPropertyChanged();
            }
        }

        public void ApplyManualBleMac()
        {
            if (string.IsNullOrWhiteSpace(ManualBleMacInput)) return;
            string raw = ManualBleMacInput.Trim().ToUpperInvariant();

            if (raw.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
            {
                BleAmbientSensorMac = "Auto";
                ManualBleMacInput = "";
                return;
            }

            string mac = NormalizeMacAddress(raw);
            if (!string.IsNullOrEmpty(mac))
            {
                if (!BleSensorChoices.Any(c => c.Mac.Equals(mac, StringComparison.OrdinalIgnoreCase)))
                {
                    BleSensorChoices.Add(new BleSensorChoice { Mac = mac, DisplayName = $"MAC: {mac}" });
                }
                BleAmbientSensorMac = mac;
                ManualBleMacInput = "";
            }
        }

        private static string NormalizeMacAddress(string input)
        {
            string hex = input.Replace(":", "").Replace("-", "").Replace(" ", "").Trim();
            if (hex.Length == 12 && hex.All(Uri.IsHexDigit))
            {
                return $"{hex[0..2]}:{hex[2..4]}:{hex[4..6]}:{hex[6..8]}:{hex[8..10]}:{hex[10..12]}";
            }
            return input.ToUpperInvariant();
        }

        public ObservableCollection<BleSensorChoice> BleSensorChoices { get; } = new();
        public ObservableCollection<BleSensorChoice> BleChassisSensorChoices { get; } = new();
        public ObservableCollection<BleSensorChoice> BleRadiatorSensorChoices { get; } = new();

        public string BleAmbientStatusBadge
        {
            get
            {
                if (!Config.EnableBleAmbient) return "Disabled";
                if (!BTHomeBleService.Instance.IsSupported && !BTHomeBleService.Instance.IsRunning) return "Unavailable";
                var reading = BTHomeBleService.Instance.ActiveReading;
                if (reading != null && reading.LastSeenUtc >= DateTime.UtcNow.AddMinutes(-10))
                {
                    return $"{reading.TemperatureF:F1} °F ({reading.TemperatureC:F1} °C)";
                }
                return BTHomeBleService.Instance.IsRunning ? "Scanning..." : "Idle";
            }
        }

        public static string BleBlueColor => (ThemeService.CurrentTheme?.IsLightMode ?? false) ? "#1D4ED8" : "#38BDF8";

        public static string GetRssiColor(short rssi)
        {
            if (rssi <= -127) return "#94A3B8";
            if (rssi >= -60) return "#10B981"; // Vibrant Emerald (Excellent)
            if (rssi >= -72) return BleBlueColor; // Clean Blue (Good)
            if (rssi >= -84) return "#F59E0B"; // Amber (Fair)
            return "#EF4444";                  // Coral Red (Weak)
        }

        public string BleAmbientStatusColor
        {
            get
            {
                if (!Config.EnableBleAmbient) return "#94A3B8";
                if (!BTHomeBleService.Instance.IsSupported && !BTHomeBleService.Instance.IsRunning) return "#EF4444";
                var reading = BTHomeBleService.Instance.ActiveReading;
                if (reading != null && reading.LastSeenUtc >= DateTime.UtcNow.AddMinutes(-10))
                {
                    return BleBlueColor;
                }
                return BleBlueColor;
            }
        }

        public string BleAmbientStatusText
        {
            get
            {
                string main = BleAmbientMainText;
                string rssi = BleAmbientRssiText;
                return string.IsNullOrEmpty(rssi) ? main : $"{main}{rssi}";
            }
        }

        public string BleAmbientMainText
        {
            get
            {
                if (!Config.EnableBleAmbient)
                {
                    return "Disabled: Using chassis and hardware thermodynamic estimation.";
                }
                if (!BTHomeBleService.Instance.IsSupported && !BTHomeBleService.Instance.IsRunning)
                {
                    return "Bluetooth LE scanning is not supported or adapter is powered off.";
                }

                bool isAuto = string.IsNullOrWhiteSpace(Config.BleAmbientSensorMac) || 
                              Config.BleAmbientSensorMac.Equals("Auto", StringComparison.OrdinalIgnoreCase);

                var reading = BTHomeBleService.Instance.ActiveReading;
                if (reading != null && reading.LastSeenUtc >= DateTime.UtcNow.AddMinutes(-10))
                {
                    string humStr = reading.HumidityPercent.HasValue ? $" • {reading.HumidityPercent.Value:F0}% RH" : "";
                    string batStr = reading.BatteryPercent.HasValue ? $" • {reading.BatteryPercent.Value}% batt" : "";

                    if (isAuto)
                    {
                        return $"⚠️ Auto Nearest: Receiving {reading.MacAddress} ({reading.TemperatureF:F1}°F / {reading.TemperatureC:F1}°C{humStr}{batStr})";
                    }
                    return $"🔒 Locked to {reading.MacAddress}: {reading.TemperatureF:F1}°F / {reading.TemperatureC:F1}°C{humStr}{batStr}";
                }

                if (!isAuto)
                {
                    return $"🔒 Locked to {Config.BleAmbientSensorMac} (Waiting for beacon from this MAC... stranger beacons in public are ignored).";
                }

                if (BTHomeBleService.Instance.IsRunning)
                {
                    return "Scanning for nearby BLE beacons... Select your device MAC from the dropdown.";
                }
                return "Listener stopped.";
            }
            set { }
        }

        public string BleAmbientRssiText
        {
            get
            {
                if (!Config.EnableBleAmbient) return string.Empty;
                var reading = BTHomeBleService.Instance.ActiveReading;
                if (reading != null && reading.LastSeenUtc >= DateTime.UtcNow.AddMinutes(-10) && reading.Rssi > -127)
                {
                    string quality = reading.Rssi >= -60 ? "Excellent" : (reading.Rssi >= -72 ? "Good" : (reading.Rssi >= -84 ? "Fair" : "Weak"));
                    return $" • {reading.Rssi} dBm ({quality})";
                }
                return string.Empty;
            }
            set { }
        }

        public string BleAmbientRssiColor
        {
            get
            {
                var reading = BTHomeBleService.Instance.ActiveReading;
                return reading != null ? GetRssiColor(reading.Rssi) : "#94A3B8";
            }
            set { }
        }

        // ==========================================
        // In-Case Chassis Air BLE Sensor Settings
        // ==========================================
        public bool EnableBleChassis
        {
            get => Config.EnableBleChassis;
            set
            {
                if (Config.EnableBleChassis != value)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.EnableBleChassis = value);
                    UpdateBleWatcherState();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BleChassisSelectorVisibility));
                    OnPropertyChanged(nameof(BleChassisStatusBadge));
                    OnPropertyChanged(nameof(BleChassisStatusColor));
                    OnPropertyChanged(nameof(BleChassisStatusText));
                }
            }
        }

        public System.Windows.Visibility BleChassisSelectorVisibility =>
            Config.EnableBleChassis ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public string BleChassisSensorMac
        {
            get => Config.BleChassisSensorMac ?? "";
            set
            {
                string clean = (value ?? "").Trim().ToUpperInvariant();
                if (Config.BleChassisSensorMac != clean)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.BleChassisSensorMac = clean);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BleChassisStatusBadge));
                    OnPropertyChanged(nameof(BleChassisStatusColor));
                    OnPropertyChanged(nameof(BleChassisStatusText));
                }
            }
        }

        public string BleChassisStatusBadge
        {
            get
            {
                if (!Config.EnableBleChassis) return "Disabled";
                if (!BTHomeBleService.Instance.IsSupported && !BTHomeBleService.Instance.IsRunning) return "Unavailable";
                if (string.IsNullOrWhiteSpace(Config.BleChassisSensorMac) || Config.BleChassisSensorMac.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                {
                    return "Select Sensor";
                }
                var reading = BTHomeBleService.Instance.GetSpecificReading(Config.BleChassisSensorMac);
                if (reading != null && (DateTime.UtcNow - reading.LastSeenUtc).TotalMinutes < 10)
                {
                    return $"{reading.TemperatureF:F1} °F ({reading.TemperatureC:F1} °C)";
                }
                return BTHomeBleService.Instance.IsRunning ? "Waiting..." : "Idle";
            }
        }

        public string BleChassisStatusColor
        {
            get
            {
                if (!Config.EnableBleChassis) return "#94A3B8";
                if (!BTHomeBleService.Instance.IsSupported && !BTHomeBleService.Instance.IsRunning) return "#EF4444";
                if (string.IsNullOrWhiteSpace(Config.BleChassisSensorMac) || Config.BleChassisSensorMac.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                {
                    return "#FBBF24";
                }
                var reading = BTHomeBleService.Instance.GetSpecificReading(Config.BleChassisSensorMac);
                if (reading != null && (DateTime.UtcNow - reading.LastSeenUtc).TotalMinutes < 10)
                {
                    return BleBlueColor;
                }
                return BleBlueColor;
            }
        }

        public string BleChassisStatusText
        {
            get
            {
                string main = BleChassisMainText;
                string rssi = BleChassisRssiText;
                return string.IsNullOrEmpty(rssi) ? main : $"{main}{rssi}";
            }
        }

        public string BleChassisMainText
        {
            get
            {
                if (!Config.EnableBleChassis)
                {
                    return "Disabled: Using simulated internal case air temperature from GPU & component thermal dissipation.";
                }
                if (!BTHomeBleService.Instance.IsSupported && !BTHomeBleService.Instance.IsRunning)
                {
                    return "Bluetooth LE scanning is not supported.";
                }
                if (string.IsNullOrWhiteSpace(Config.BleChassisSensorMac) || Config.BleChassisSensorMac.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                {
                    return "Select your in-case thermometer from the dropdown list to link physical telemetry.";
                }

                var reading = BTHomeBleService.Instance.GetSpecificReading(Config.BleChassisSensorMac);
                if (reading != null && (DateTime.UtcNow - reading.LastSeenUtc).TotalMinutes < 10)
                {
                    string humStr = reading.HumidityPercent.HasValue ? $" • {reading.HumidityPercent.Value:F0}% RH" : "";
                    return $"🔒 Case Sensor {reading.MacAddress}: {reading.TemperatureF:F1}°F / {reading.TemperatureC:F1}°C{humStr}";
                }

                return $"🔒 Locked to {Config.BleChassisSensorMac} (Waiting for beacon packet from this sensor...).";
            }
            set { }
        }

        public string BleChassisRssiText
        {
            get
            {
                if (!Config.EnableBleChassis) return string.Empty;
                var reading = BTHomeBleService.Instance.GetSpecificReading(Config.BleChassisSensorMac);
                if (reading != null && (DateTime.UtcNow - reading.LastSeenUtc).TotalMinutes < 10 && reading.Rssi > -127)
                {
                    string quality = reading.Rssi >= -60 ? "Excellent" : (reading.Rssi >= -72 ? "Good" : (reading.Rssi >= -84 ? "Fair" : "Weak"));
                    return $" • {reading.Rssi} dBm ({quality})";
                }
                return string.Empty;
            }
            set { }
        }

        public string BleChassisRssiColor
        {
            get
            {
                var reading = BTHomeBleService.Instance.GetSpecificReading(Config.BleChassisSensorMac);
                return reading != null ? GetRssiColor(reading.Rssi) : "#94A3B8";
            }
            set { }
        }

        // ==========================================
        // Post-Radiator / Cooler Exhaust BLE Sensor Settings
        // ==========================================
        public bool EnableBleRadiator
        {
            get => Config.EnableBleRadiator;
            set
            {
                if (Config.EnableBleRadiator != value)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.EnableBleRadiator = value);
                    UpdateBleWatcherState();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BleRadiatorSelectorVisibility));
                    OnPropertyChanged(nameof(BleRadiatorStatusBadge));
                    OnPropertyChanged(nameof(BleRadiatorStatusColor));
                    OnPropertyChanged(nameof(BleRadiatorStatusText));
                }
            }
        }

        public System.Windows.Visibility BleRadiatorSelectorVisibility =>
            Config.EnableBleRadiator ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public string BleRadiatorSensorMac
        {
            get => Config.BleRadiatorSensorMac ?? "";
            set
            {
                string clean = (value ?? "").Trim().ToUpperInvariant();
                if (Config.BleRadiatorSensorMac != clean)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.BleRadiatorSensorMac = clean);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BleRadiatorStatusBadge));
                    OnPropertyChanged(nameof(BleRadiatorStatusColor));
                    OnPropertyChanged(nameof(BleRadiatorStatusText));
                }
            }
        }

        public string BleRadiatorStatusBadge
        {
            get
            {
                if (!Config.EnableBleRadiator) return "Disabled";
                if (!BTHomeBleService.Instance.IsSupported && !BTHomeBleService.Instance.IsRunning) return "Unavailable";
                if (string.IsNullOrWhiteSpace(Config.BleRadiatorSensorMac) || Config.BleRadiatorSensorMac.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                {
                    return "Select Sensor";
                }
                var reading = BTHomeBleService.Instance.GetSpecificReading(Config.BleRadiatorSensorMac);
                if (reading != null && (DateTime.UtcNow - reading.LastSeenUtc).TotalMinutes < 10)
                {
                    return $"{reading.TemperatureF:F1} °F ({reading.TemperatureC:F1} °C)";
                }
                return BTHomeBleService.Instance.IsRunning ? "Waiting..." : "Idle";
            }
        }

        public string BleRadiatorStatusColor
        {
            get
            {
                if (!Config.EnableBleRadiator) return "#94A3B8";
                if (!BTHomeBleService.Instance.IsSupported && !BTHomeBleService.Instance.IsRunning) return "#EF4444";
                if (string.IsNullOrWhiteSpace(Config.BleRadiatorSensorMac) || Config.BleRadiatorSensorMac.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                {
                    return "#FBBF24";
                }
                var reading = BTHomeBleService.Instance.GetSpecificReading(Config.BleRadiatorSensorMac);
                if (reading != null && (DateTime.UtcNow - reading.LastSeenUtc).TotalMinutes < 10)
                {
                    return "#34D399";
                }
                return "#38BDF8";
            }
        }

        public string BleRadiatorStatusText
        {
            get
            {
                if (!Config.EnableBleRadiator)
                {
                    return "Disabled: Post-radiator / cooler exhaust telemetry inactive.";
                }
                if (!BTHomeBleService.Instance.IsSupported && !BTHomeBleService.Instance.IsRunning)
                {
                    return "Bluetooth LE scanning is not supported.";
                }
                if (string.IsNullOrWhiteSpace(Config.BleRadiatorSensorMac) || Config.BleRadiatorSensorMac.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                {
                    return "Select your post-radiator thermometer from the dropdown list to calculate ΔT.";
                }

                var reading = BTHomeBleService.Instance.GetSpecificReading(Config.BleRadiatorSensorMac);
                if (reading != null && (DateTime.UtcNow - reading.LastSeenUtc).TotalMinutes < 10)
                {
                    string humStr = reading.HumidityPercent.HasValue ? $" • {reading.HumidityPercent.Value:F0}% RH" : "";
                    string rssiStr = reading.Rssi > -127 ? $" • {reading.Rssi} dBm" : "";
                    double deltaTF = Summary.Chassis?.RadiatorDeltaTF ?? 0;
                    return $"🔒 Radiator Sensor {reading.MacAddress}: {reading.TemperatureF:F1}°F / {reading.TemperatureC:F1}°C{humStr}{rssiStr} (Exhaust ΔT +{deltaTF:F1}°F).";
                }

                return $"🔒 Locked to {Config.BleRadiatorSensorMac} (Waiting for beacon packet from this sensor...).";
            }
        }

        private void UpdateBleWatcherState()
        {
            bool anyEnabled = Config.EnableBleAmbient || Config.EnableBleChassis || Config.EnableBleRadiator;
            if (anyEnabled)
            {
                BTHomeBleService.Instance.Start();
            }
            else
            {
                BTHomeBleService.Instance.Stop();
            }
        }

        private void PopulateInitialBleSensors()
        {
            BleSensorChoices.Clear();
            BleSensorChoices.Add(new BleSensorChoice { Mac = "Auto", DisplayName = "Auto (Nearest Beacon)" });

            BleChassisSensorChoices.Clear();
            BleChassisSensorChoices.Add(new BleSensorChoice { Mac = "", DisplayName = "-- Select In-Case Sensor --" });

            BleRadiatorSensorChoices.Clear();
            BleRadiatorSensorChoices.Add(new BleSensorChoice { Mac = "", DisplayName = "-- Select Radiator Sensor --" });

            void AddIfMissing(ObservableCollection<BleSensorChoice> list, string? mac)
            {
                if (!string.IsNullOrWhiteSpace(mac) && !mac.Equals("Auto", StringComparison.OrdinalIgnoreCase) && !list.Any(c => c.Mac.Equals(mac, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(new BleSensorChoice { Mac = mac, DisplayName = $"MAC: {mac}" });
                }
            }

            AddIfMissing(BleSensorChoices, Config.BleAmbientSensorMac);
            AddIfMissing(BleChassisSensorChoices, Config.BleChassisSensorMac);
            AddIfMissing(BleRadiatorSensorChoices, Config.BleRadiatorSensorMac);
        }

        private void SyncBleSensorChoices()
        {
            var discovered = BTHomeBleService.Instance.DiscoveredSensors;
            foreach (var s in discovered)
            {
                string namePart = (!string.IsNullOrWhiteSpace(s.Name) && s.Name != "BTHome Sensor" && s.Name != "BLE Thermometer" && s.Name != "Xiaomi Sensor")
                    ? $" [{s.Name}]"
                    : "";
                string humPart = s.HumidityPercent.HasValue ? $" • {s.HumidityPercent.Value:F0}%" : "";
                string rssiPart = s.Rssi > -127 ? $" • {s.Rssi} dBm" : "";
                string display = $"{s.MacAddress}{namePart}  ({s.TemperatureF:F1}°F / {s.TemperatureC:F1}°C{humPart}{rssiPart})";

                void UpdateList(ObservableCollection<BleSensorChoice> list)
                {
                    var existing = list.FirstOrDefault(c => c.Mac.Equals(s.MacAddress, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        list.Add(new BleSensorChoice { Mac = s.MacAddress, DisplayName = display });
                    }
                    else if (existing.DisplayName != display)
                    {
                        existing.DisplayName = display;
                    }
                }

                UpdateList(BleSensorChoices);
                UpdateList(BleChassisSensorChoices);
                UpdateList(BleRadiatorSensorChoices);
            }

            OnPropertyChanged(nameof(BleAmbientStatusBadge));
            OnPropertyChanged(nameof(BleAmbientStatusColor));
            OnPropertyChanged(nameof(BleAmbientStatusText));
            OnPropertyChanged(nameof(BleAmbientMainText));
            OnPropertyChanged(nameof(BleAmbientRssiText));
            OnPropertyChanged(nameof(BleAmbientRssiColor));

            OnPropertyChanged(nameof(BleChassisStatusBadge));
            OnPropertyChanged(nameof(BleChassisStatusColor));
            OnPropertyChanged(nameof(BleChassisStatusText));
            OnPropertyChanged(nameof(BleChassisMainText));
            OnPropertyChanged(nameof(BleChassisRssiText));
            OnPropertyChanged(nameof(BleChassisRssiColor));

            OnPropertyChanged(nameof(BleRadiatorStatusBadge));
            OnPropertyChanged(nameof(BleRadiatorStatusColor));
            OnPropertyChanged(nameof(BleRadiatorStatusText));
        }


        public string SelectedTimProfile
        {
            get => Config.TimProfile ?? "Auto";
            set
            {
                if (Config.TimProfile != value)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.TimProfile = value);
                    OnPropertyChanged();
                }
            }
        }

        public string TimAgeDisplay
        {
            get
            {
                if (!Summary.Cpu.TimAgeDays.HasValue) return "Unspecified";
                int days = Summary.Cpu.TimAgeDays.Value;
                if (days == 0) return "Applied Today";
                if (days < 30) return $"{days} {(days == 1 ? "day" : "days")}";
                int months = days / 30;
                if (months < 12) return $"{months} {(months == 1 ? "month" : "months")}";
                double years = Math.Round(days / 365.25, 1);
                return $"{years} {(years == 1.0 ? "year" : "years")}";
            }
        }

        public DateTime? TimAppliedDate
        {
            get => Config.TimAppliedDateUtc;
            set
            {
                if (Config.TimAppliedDateUtc != value)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.TimAppliedDateUtc = value.HasValue ? DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Utc) : null);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TimAppliedDateString));
                    OnPropertyChanged(nameof(HasTimAppliedDate));
                    OnPropertyChanged(nameof(TimAgeDisplay));
                }
            }
        }

        public string TimAppliedDateString
        {
            get => Config.TimAppliedDateUtc.HasValue ? Config.TimAppliedDateUtc.Value.ToLocalTime().ToString("yyyy-MM-dd") : "";
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    TimAppliedDate = null;
                }
                else if (DateTime.TryParse(value, out var dt))
                {
                    TimAppliedDate = dt;
                }
            }
        }

        public bool HasTimAppliedDate => Config.TimAppliedDateUtc.HasValue;

        public void MarkTimAppliedToday()
        {
            TimAppliedDate = DateTime.UtcNow.Date;
        }

        public void ClearTimAppliedDate()
        {
            TimAppliedDate = null;
        }

        public void CalibrateRoomAmbientToTarget(double targetDegC)
        {
            double currentEstimate = Summary.Chassis.EstimatedAmbientTempC;
            double delta = Math.Round(targetDegC - (currentEstimate - Config.RoomAmbientOffsetC), 1);
            RoomAmbientOffsetC = Math.Clamp(delta, -10.0, 10.0);
        }

        public int CpuTdpOverrideWatts
        {
            get => Config.CpuTdpOverrideWatts;
            set
            {
                int clamped = Math.Clamp(value, 0, 500);
                if (Config.CpuTdpOverrideWatts != clamped)
                {
                    ConfigManager.Instance.UpdateConfig(c => c.CpuTdpOverrideWatts = clamped);
                    OnPropertyChanged();
                }
            }
        }

        public IReadOnlyList<NetworkAdapterInfo> AvailableNetworkAdapters => Summary.Network.AvailableAdapters;

        public ObservableCollection<NetworkAdapterChoice> NetworkAdapterChoices { get; } = new();
        private string _lastAdapterFingerprint = "";

        private void PopulateInitialNetworkAdapters()
        {
            NetworkAdapterChoices.Add(new NetworkAdapterChoice { Id = "Auto", DisplayName = "Auto (Highest Traffic)" });

            try
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces();
                if (nics != null)
                {
                    foreach (var ni in nics)
                    {
                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || 
                            ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                            continue;

                        string id = ni.Name;
                        string desc = ni.Description;
                        string display = !string.IsNullOrWhiteSpace(desc) ? $"{id} ({desc})" : id;

                        if (!NetworkAdapterChoices.Any(c => c.Id == id))
                        {
                            NetworkAdapterChoices.Add(new NetworkAdapterChoice { Id = id, DisplayName = display });
                        }
                    }
                }
            }
            catch { }

            string configured = Config.PreferredNetworkAdapter;
            if (!string.IsNullOrWhiteSpace(configured) && configured != "Auto" && !NetworkAdapterChoices.Any(c => c.Id == configured))
            {
                NetworkAdapterChoices.Add(new NetworkAdapterChoice { Id = configured, DisplayName = configured });
            }
        }

        private void CheckAndRefreshNetworkAdapters()
        {
            var adapters = Summary.Network.AvailableAdapters;
            if (adapters == null || adapters.Count == 0) return;

            string fingerprint = string.Join("|", adapters.Select(a => $"{a.Name}_{a.Description}"));
            if (fingerprint != _lastAdapterFingerprint)
            {
                _lastAdapterFingerprint = fingerprint;

                var currentIds = new HashSet<string>(adapters.Select(a => a.Name));

                // Add or update physical adapters in place without destroying containers
                foreach (var a in adapters)
                {
                    string id = a.Name;
                    string desc = a.Description;
                    string display = !string.IsNullOrWhiteSpace(desc) ? $"{id} ({desc})" : id;

                    var existing = NetworkAdapterChoices.FirstOrDefault(c => c.Id == id);
                    if (existing == null)
                    {
                        NetworkAdapterChoices.Add(new NetworkAdapterChoice { Id = id, DisplayName = display });
                    }
                    else if (existing.DisplayName != display)
                    {
                        existing.DisplayName = display;
                    }
                }

                // Remove stale adapters (except "Auto" and currently selected adapter)
                string selected = Config.PreferredNetworkAdapter;
                for (int i = NetworkAdapterChoices.Count - 1; i >= 0; i--)
                {
                    var item = NetworkAdapterChoices[i];
                    if (item.Id == "Auto" || item.Id == selected) continue;
                    if (!currentIds.Contains(item.Id))
                    {
                        NetworkAdapterChoices.RemoveAt(i);
                    }
                }

                OnPropertyChanged(nameof(AvailableNetworkAdapters));
            }
        }

        public string ApiEndpointUrl => $"http://localhost:{Config.ApiPort}";
        public string VersionString => $"v{UpdateService.Instance.GetCurrentVersionString()}";

        // Admin Check & Non-Intrusive Notice
        public bool IsRunningAsAdmin { get; } = CheckIsRunningAsAdmin();

        private bool _isAdminNoticeDismissed;
        public bool IsAdminNoticeDismissed
        {
            get => _isAdminNoticeDismissed;
            set
            {
                _isAdminNoticeDismissed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowAdminNotice));
                OnPropertyChanged(nameof(AdminNoticeVisibility));
            }
        }

        public bool ShowAdminNotice => IsRunningAsAdmin && !IsAdminNoticeDismissed;
        public System.Windows.Visibility AdminNoticeVisibility => ShowAdminNotice ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public void DismissAdminNotice()
        {
            IsAdminNoticeDismissed = true;
        }

        private static bool CheckIsRunningAsAdmin()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public MainViewModel()
        {
            _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            _summary = TelemetryEngine.Instance.CurrentSummary;
            PopulateInitialNetworkAdapters();
            PopulateInitialBleSensors();
            TelemetryEngine.Instance.TelemetryUpdated += OnTelemetryUpdated;

            BTHomeBleService.Instance.ReadingUpdated += (reading) =>
            {
                _dispatcher.InvokeAsync(() =>
                {
                    SyncBleSensorChoices();
                });
            };

            UpdateService.Instance.UpdateCheckCompleted += (status) =>
            {
                _dispatcher.InvokeAsync(() =>
                {
                    UpdateStatusText = status;
                });
            };

            ThemeService.ThemeChanged += (theme) =>
            {
                _dispatcher.InvokeAsync(() =>
                {
                    OnPropertyChanged(nameof(CurrentTheme));
                    OnPropertyChanged(nameof(AvailableThemes));
                    OnPropertyChanged(nameof(SelectedThemeId));
                });
            };
        }

        private void OnTelemetryUpdated(SystemSummary summary, System.Collections.Generic.IReadOnlyList<SensorMetric> metrics)
        {
            _dispatcher.InvokeAsync(() =>
            {
                Summary = summary;
                SyncGpuSelection();

                // Non-destructive in-place collection sync (avoids DataGrid destruction and blinking!)
                SyncMetricCollection(AllMetrics, metrics);

                UpdateDetailMetrics();

                // Fire history graph updates
                OnPropertyChanged(nameof(CpuTempHistory));
                OnPropertyChanged(nameof(CpuLoadHistory));
                OnPropertyChanged(nameof(CpuPowerHistory));
                OnPropertyChanged(nameof(GpuTempHistory));
                OnPropertyChanged(nameof(GpuLoadHistory));
                OnPropertyChanged(nameof(GpuPowerHistory));
                OnPropertyChanged(nameof(RamLoadHistory));
                OnPropertyChanged(nameof(NetDownloadHistory));
                CheckAndRefreshNetworkAdapters();
                OnPropertyChanged(nameof(BleAmbientStatusBadge));
                OnPropertyChanged(nameof(BleAmbientStatusColor));
                OnPropertyChanged(nameof(BleAmbientStatusText));
                OnPropertyChanged(nameof(BleChassisStatusBadge));
                OnPropertyChanged(nameof(BleChassisStatusColor));
                OnPropertyChanged(nameof(BleChassisStatusText));
                OnPropertyChanged(nameof(BleRadiatorStatusBadge));
                OnPropertyChanged(nameof(BleRadiatorStatusColor));
                OnPropertyChanged(nameof(BleRadiatorStatusText));
            });
        }

        private void UpdateDetailMetrics()
        {
            if (DetailSection == "None") return;

            HardwareCategory? targetCategory = DetailSection switch
            {
                "Cpu" => HardwareCategory.Cpu,
                "Gpu" => HardwareCategory.Gpu,
                "Memory" => HardwareCategory.Memory,
                "Storage" => HardwareCategory.Storage,
                "Network" => HardwareCategory.Network,
                _ => null
            };

            var filtered = new List<SensorMetric>();

            if (targetCategory.HasValue)
            {
                foreach (var m in AllMetrics)
                {
                    if (m.Category == targetCategory.Value || (targetCategory == HardwareCategory.Cpu && m.Category == HardwareCategory.Cooler))
                    {
                        // If GPU detail, only show sensors for the currently selected GPU if available
                        if (targetCategory == HardwareCategory.Gpu && !string.IsNullOrEmpty(CurrentGpu.Id))
                        {
                            if (m.HardwareId.Contains(CurrentGpu.Id) || m.HardwareName.Contains(CurrentGpu.Name))
                            {
                                filtered.Add(m);
                            }
                        }
                        else
                        {
                            filtered.Add(m);
                        }
                    }
                }
            }

            // Non-destructive in-place sync for DetailMetrics DataGrid
            SyncMetricCollection(DetailMetrics, filtered);
        }

        /// <summary>
        /// Synchronizes an ObservableCollection in-place to prevent WPF DataGrid
        /// from destroying visual rows and flickering/blinking on every telemetry tick.
        /// </summary>
        private static void SyncMetricCollection(ObservableCollection<SensorMetric> target, IEnumerable<SensorMetric> source)
        {
            var incomingList = source as IList<SensorMetric> ?? source.ToList();
            var incomingIds = new HashSet<string>(incomingList.Select(m => m.Id));

            // 1. Remove items that no longer exist
            for (int i = target.Count - 1; i >= 0; i--)
            {
                if (!incomingIds.Contains(target[i].Id))
                {
                    target.RemoveAt(i);
                }
            }

            // 2. Update existing items in-place or insert new items in matching order
            for (int i = 0; i < incomingList.Count; i++)
            {
                var incoming = incomingList[i];
                var existing = target.FirstOrDefault(t => t.Id == incoming.Id);
                if (existing != null)
                {
                    existing.UpdateFrom(incoming);
                }
                else
                {
                    int insertIndex = Math.Min(i, target.Count);
                    target.Insert(insertIndex, incoming);
                }
            }
        }

        // Stream Deck Lifecycle Management (LCM)
        public StreamDeckPluginStatus StreamDeckStatus => StreamDeckService.GetStatus();
        public string? InstalledStreamDeckVersion => StreamDeckService.GetInstalledVersion();
        public string BundledStreamDeckVersion => StreamDeckService.BundledVersion;
        public bool IsStreamDeckDetected => StreamDeckService.IsStreamDeckInstalled();
        public bool IsStreamDeckPluginInstalled => StreamDeckStatus == StreamDeckPluginStatus.InstalledUpToDate || StreamDeckStatus == StreamDeckPluginStatus.UpdateAvailable;
        public bool HasStreamDeckUpdate => StreamDeckStatus == StreamDeckPluginStatus.UpdateAvailable;
        public System.Windows.Visibility StreamDeckUninstallVisibility => IsStreamDeckPluginInstalled ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public string StreamDeckStatusBadge => StreamDeckStatus switch
        {
            StreamDeckPluginStatus.InstalledUpToDate => $"Active (v{InstalledStreamDeckVersion}) • Up to Date",
            StreamDeckPluginStatus.UpdateAvailable => $"Update Available (v{BundledStreamDeckVersion})",
            StreamDeckPluginStatus.NotInstalled => "Not Installed • Ready to Deploy",
            StreamDeckPluginStatus.StreamDeckNotDetected => "Elgato Software Not Detected",
            _ => "Unknown"
        };

        public string StreamDeckStatusColor => StreamDeckStatus switch
        {
            StreamDeckPluginStatus.InstalledUpToDate => "#10B981", // Emerald
            StreamDeckPluginStatus.UpdateAvailable => "#00E5FF",   // Cyan
            StreamDeckPluginStatus.NotInstalled => "#F59E0B",       // Amber
            StreamDeckPluginStatus.StreamDeckNotDetected => "#94A3B8", // Slate
            _ => "#94A3B8"
        };

        public string StreamDeckActionButtonText => StreamDeckStatus switch
        {
            StreamDeckPluginStatus.UpdateAvailable => "Update Stream Deck Plugin",
            StreamDeckPluginStatus.InstalledUpToDate => "Reinstall / Repair Plugin",
            _ => "Install Stream Deck Plugin"
        };

        public void RefreshStreamDeckState()
        {
            OnPropertyChanged(nameof(StreamDeckStatus));
            OnPropertyChanged(nameof(InstalledStreamDeckVersion));
            OnPropertyChanged(nameof(BundledStreamDeckVersion));
            OnPropertyChanged(nameof(IsStreamDeckDetected));
            OnPropertyChanged(nameof(IsStreamDeckPluginInstalled));
            OnPropertyChanged(nameof(HasStreamDeckUpdate));
            OnPropertyChanged(nameof(StreamDeckUninstallVisibility));
            OnPropertyChanged(nameof(StreamDeckStatusBadge));
            OnPropertyChanged(nameof(StreamDeckStatusColor));
            OnPropertyChanged(nameof(StreamDeckActionButtonText));
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class NetworkAdapterChoice : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _id = "Auto";
        public string Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Id)));
                }
            }
        }

        private string _displayName = "Auto (Highest Traffic)";
        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
                }
            }
        }

        public override string ToString() => DisplayName;
    }

    public class BleSensorChoice : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _mac = "Auto";
        public string Mac
        {
            get => _mac;
            set
            {
                if (_mac != value)
                {
                    _mac = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mac)));
                }
            }
        }

        private string _displayName = "Auto (Nearest / Strongest Signal)";
        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
                }
            }
        }

        public override string ToString() => DisplayName;
    }
}
