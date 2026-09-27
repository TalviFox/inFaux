using System;
using System.Threading;
using System.Windows;
using InFox.Api;
using InFox.Engine;
using InFox.Services;
using InFox.Tray;
using InFox.Views;

namespace InFox
{
    public partial class App : System.Windows.Application
    {
        private static Mutex? _singleInstanceMutex;
        private MainWindow? _mainWindow;

        protected override async void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                string msg = $"CRASH (AppDomain): {ex?.ToString() ?? args.ExceptionObject?.ToString()}";
                LoggingService.Instance.Error("Crash", msg);
                try { System.IO.File.AppendAllText("crash.log", $"[{DateTime.UtcNow}] {msg}\n"); } catch { }
            };

            DispatcherUnhandledException += (s, args) =>
            {
                string msg = $"CRASH (Dispatcher): {args.Exception}";
                LoggingService.Instance.Error("Crash", msg);
                try { System.IO.File.AppendAllText("crash.log", $"[{DateTime.UtcNow}] {msg}\n"); } catch { }
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                string msg = $"CRASH (TaskScheduler): {args.Exception}";
                LoggingService.Instance.Error("Crash", msg);
                try { System.IO.File.AppendAllText("crash.log", $"[{DateTime.UtcNow}] {msg}\n"); } catch { }
            };

            base.OnStartup(e);

            // 1. Single-Instance Check
            const string mutexName = @"Local\inFaux_SingleInstance_Mutex";
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
            if (!createdNew)
            {
                System.Windows.MessageBox.Show("inFaux is already running in the background.", "inFaux", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            LoggingService.Instance.Info("App", $"=== Starting inFaux {UpdateService.Instance.GetCurrentVersionString()} (FoxDen Software) ===");

            // 1.5. Initialize Active Fox Theme
            try
            {
                ThemeService.Initialize(ConfigManager.Instance.Config.Theme);
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("Theme", $"Failed to initialize theme: {ex.Message}");
            }

            // 2. Initialize Telemetry Engine
            try
            {
                await TelemetryEngine.Instance.StartAsync();
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("App", "Failed to start TelemetryEngine", ex);
            }

            // 3. Start Embedded Open API Server
            try
            {
                await InFoxApiServer.Instance.StartAsync();
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("App", "Failed to start API Server", ex);
            }

            // 4. Initialize System Tray Manager
            try
            {
                TrayManager.Instance.Initialize();
                TrayManager.Instance.OpenDashboardRequested += ShowDashboard;
                TrayManager.Instance.ExitRequested += ExitApp;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("App", "Failed to initialize TrayManager", ex);
            }

            // 5. Create Main Dashboard Window
            _mainWindow = new MainWindow();

            // Check command line arguments or config: if launched with --minimized or MinimizeOnStartup is set, don't show window immediately
            bool startMinimized = ConfigManager.Instance.Config.MinimizeOnStartup;
            foreach (var arg in e.Args)
            {
                if (arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-m", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/min", StringComparison.OrdinalIgnoreCase))
                {
                    startMinimized = true;
                    break;
                }
            }

            if (!startMinimized)
            {
                _mainWindow.Show();
            }

            // 6. Background Update Check (Throttled to 24h)
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(5000); // Wait 5s after startup
                    await UpdateService.Instance.CheckForUpdatesAsync(isManual: false);
                }
                catch { }
            });
        }

        private void ShowDashboard()
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
            }

            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }
            _mainWindow.Activate();
        }

        private void ExitApp()
        {
            LoggingService.Instance.Info("App", "Shutting down inFaux...");
            try
            {
                TrayManager.Instance.Dispose();
                InFoxApiServer.Instance.Dispose();
                TelemetryEngine.Instance.Dispose();
                _singleInstanceMutex?.ReleaseMutex();
                _singleInstanceMutex?.Dispose();
            }
            catch { }

            _mainWindow?.ExitApplication();
            Environment.Exit(0);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                TrayManager.Instance.Dispose();
                InFoxApiServer.Instance.Dispose();
                TelemetryEngine.Instance.Dispose();
                _singleInstanceMutex?.ReleaseMutex();
                _singleInstanceMutex?.Dispose();
            }
            catch { }

            base.OnExit(e);
        }
    }
}
