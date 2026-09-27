using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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
            }
        }

        public bool HasBattery => Summary.Battery.HasBattery;
        public System.Windows.Visibility BatteryVisibility => Summary.Battery.HasBattery ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        public System.Windows.GridLength BatteryColumnWidth => Summary.Battery.HasBattery ? new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) : new System.Windows.GridLength(0);
        public string NetworkCardTitle => Summary.Battery.HasBattery ? "NETWORK & POWER" : "NETWORK";
        public string NetworkCardToolTip => Summary.Battery.HasBattery ? "Click to view network and battery details" : "Click to view network details";

        public bool IsPhysicalMachine => !Summary.IsVirtualMachine;
        public string ChassisProfileTooltip => Summary.IsVirtualMachine
            ? "Disabled on Virtual Machines: Physical chassis enclosure modeling is bypassed (Additive Thermal Delta ΔT is active)."
            : "Calibrates the zero-driver thermodynamic air cavity solver.";
        public string ChassisProfileSubLabel => Summary.IsVirtualMachine
            ? "Bypassed on Virtual Machines (Operating in Additive Thermal ΔT mode)."
            : "Calibrates the zero-driver thermodynamic air cavity solver.";

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
            "Storage" => "Storage Volumes & SMART Drives",
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
                StartupService.SetStartup(value);
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
            get => Config.PreferredNetworkAdapter;
            set
            {
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
            TelemetryEngine.Instance.TelemetryUpdated += OnTelemetryUpdated;
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
                OnPropertyChanged(nameof(AvailableNetworkAdapters));
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
                    if (m.Category == targetCategory.Value)
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

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
