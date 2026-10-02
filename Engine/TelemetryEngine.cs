using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InFox.Engine.Harvesters;
using InFox.Models.Config;
using InFox.Models.Sensors;
using InFox.Services;

namespace InFox.Engine
{
    public class TelemetryEngine : IDisposable
    {
        private static readonly Lazy<TelemetryEngine> _instance = new(() => new TelemetryEngine());
        public static TelemetryEngine Instance => _instance.Value;

        private readonly List<ISensorHarvester> _harvesters = new();
        private readonly ConcurrentDictionary<string, MetricHistory> _historyBuffers = new();
        private readonly ConcurrentDictionary<string, (double Min, double Max)> _minMaxBounds = new();
        private readonly object _stateLock = new();

        private SystemSummary _currentSummary = new();
        private List<SensorMetric> _currentMetrics = new();

        private CancellationTokenSource? _cts;
        private Task? _pollTask;
        private bool _isRunning = false;

        public event Action<SystemSummary, IReadOnlyList<SensorMetric>>? TelemetryUpdated;

        public SystemSummary CurrentSummary
        {
            get
            {
                lock (_stateLock)
                {
                    return _currentSummary;
                }
            }
        }

        public IReadOnlyList<SensorMetric> CurrentMetrics
        {
            get
            {
                lock (_stateLock)
                {
                    return _currentMetrics.ToArray();
                }
            }
        }

        private TelemetryEngine()
        {
            // Register harvesters in dependency order:
            // 1. HardwareMonitor probes physical hardware and builds core structures
            // 2. SystemHarvester provides native Win32/PDH telemetry and establishes chassis/ambient baseline
            // 3. StorageHarvester queries disk drive volumes and applies storage thermodynamic modeling
            _harvesters.Add(new HardwareMonitorHarvester());
            _harvesters.Add(new SystemHarvester());
            _harvesters.Add(new StorageHarvester());
        }

        public Task StartAsync()
        {
            if (_isRunning) return Task.CompletedTask;
            _isRunning = true;
            _cts = new CancellationTokenSource();

            LoggingService.Instance.Info("TelemetryEngine", "Starting telemetry polling loop...");
            _pollTask = Task.Run(() => PollingLoopAsync(_cts.Token));

            // Initialize harvesters in background so a blocked/quarantined driver never freezes the engine
            _ = Task.Run(async () =>
            {
                foreach (var harvester in _harvesters)
                {
                    try
                    {
                        var initTask = harvester.InitializeAsync();
                        if (await Task.WhenAny(initTask, Task.Delay(5000)) != initTask)
                        {
                            LoggingService.Instance.Warning("TelemetryEngine", $"Harvester '{harvester.Name}' timed out during initialization (driver may be blocked).");
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.Error("TelemetryEngine", $"Failed to initialize harvester '{harvester.Name}'", ex);
                    }
                }
            });

            return Task.CompletedTask;
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            _cts?.Cancel();

            try
            {
                _pollTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }

            LoggingService.Instance.Info("TelemetryEngine", "Telemetry engine stopped.");
        }

        private async Task PollingLoopAsync(CancellationToken ct)
        {
            // Initial burst tick to populate instantly
            Tick();

            while (!ct.IsCancellationRequested)
            {
                int delayMs = ConfigManager.Instance.Config.PollingIntervalMs;
                if (delayMs < 250) delayMs = 250;

                try
                {
                    await Task.Delay(delayMs, ct);
                    Tick();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Error("TelemetryEngine", "Unexpected error in poll tick", ex);
                }
            }
        }

        public void Tick()
        {
            var nextSummary = new SystemSummary();
            var nextMetrics = new List<SensorMetric>();

            // Enrich with active Fox Coat Theme metadata for Stream Deck and API consumers
            try
            {
                var curTheme = ThemeService.CurrentTheme;
                nextSummary.Theme.Id = curTheme.Id;
                nextSummary.Theme.Name = curTheme.Name;
                nextSummary.Theme.AccentHex = curTheme.AccentHex;
                nextSummary.Theme.CardBgHex = $"#{curTheme.CardBackground.R:X2}{curTheme.CardBackground.G:X2}{curTheme.CardBackground.B:X2}";
                nextSummary.Theme.BorderHex = $"#{curTheme.CardBorder.R:X2}{curTheme.CardBorder.G:X2}{curTheme.CardBorder.B:X2}";
                nextSummary.Theme.IsOled = (curTheme.Id == "SilverFox" && curTheme.WindowBackground.R == 0 && curTheme.WindowBackground.G == 0 && curTheme.WindowBackground.B == 0);
                nextSummary.Theme.IsLightMode = curTheme.IsLightMode;
            }
            catch { }

            foreach (var harvester in _harvesters)
            {
                try
                {
                    harvester.Poll(nextSummary, nextMetrics, RecordHistory);
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Error("TelemetryEngine", $"Harvester '{harvester.Name}' failed in poll", ex);
                }
            }

            // Track and update session Min/Max bounds for all sensor channels
            foreach (var metric in nextMetrics)
            {
                if (!string.IsNullOrEmpty(metric.Id) && !double.IsNaN(metric.Value) && !double.IsInfinity(metric.Value))
                {
                    var bounds = _minMaxBounds.AddOrUpdate(
                        metric.Id,
                        _ => (metric.Value, metric.Value),
                        (_, prev) => (Math.Min(prev.Min, metric.Value), Math.Max(prev.Max, metric.Value))
                    );
                    metric.Min = bounds.Min;
                    metric.Max = bounds.Max;
                }
            }

            lock (_stateLock)
            {
                _currentSummary = nextSummary;
                _currentMetrics = nextMetrics;
            }

            TelemetryUpdated?.Invoke(nextSummary, nextMetrics);
        }

        public void RecordHistory(string metricId, double value)
        {
            var buffer = _historyBuffers.GetOrAdd(metricId, _ => new MetricHistory(60));
            buffer.Add(value);
        }

        public void ResetMinMax()
        {
            _minMaxBounds.Clear();
        }

        public double[] GetHistory(string metricId)
        {
            if (_historyBuffers.TryGetValue(metricId, out var buffer))
            {
                return buffer.ToArray();
            }
            return Array.Empty<double>();
        }

        public void Dispose()
        {
            Stop();
            foreach (var harvester in _harvesters)
            {
                harvester.Dispose();
            }
            _harvesters.Clear();
        }
    }
}
