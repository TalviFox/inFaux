using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using InFox.Engine;
using InFox.Services;
using InFox.ViewModels;

namespace InFox.Views
{
    public partial class MainWindow : Window
    {
        private bool _isExplicitExit = false;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "SetClassLongPtr", SetLastError = true)]
        private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetClassLong", SetLastError = true)]
        private static extern IntPtr SetClassLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetClassLongPtr64(hWnd, nIndex, dwNewLong) : SetClassLong32(hWnd, nIndex, dwNewLong);
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_DLGMODALFRAME = 0x0001;
        private const uint WM_GETICON = 0x007F;
        private const uint WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;
        private const int GCLP_HICON = -14;
        private const int GCLP_HICONSM = -34;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();

            ThemeService.ThemeChanged += (theme) =>
            {
                Dispatcher.InvokeAsync(() => ApplyDarkThemeWindowBar());
            };

            Loaded += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    StripTitleBarIcon(hwnd);
                }
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                var source = HwndSource.FromHwnd(hwnd);
                source?.AddHook(WndProc);

                StripTitleBarIcon(hwnd);
            }
            ApplyDarkThemeWindowBar();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Intercept icon queries from DWM / Windows Shell to keep title bar clean text-only
            if (msg == (int)WM_GETICON)
            {
                handled = true;
                return IntPtr.Zero;
            }
            if (msg == (int)WM_SETICON && lParam != IntPtr.Zero)
            {
                // Prevent WPF or external calls from re-attaching an icon to this window
                handled = true;
                return IntPtr.Zero;
            }
            return IntPtr.Zero;
        }

        private static void StripTitleBarIcon(IntPtr hwnd)
        {
            try
            {
                // 1. Remove extended frame styles that display an icon
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_DLGMODALFRAME);

                // 2. Clear class icons for this window
                SetClassLongPtr(hwnd, GCLP_HICONSM, IntPtr.Zero);
                SetClassLongPtr(hwnd, GCLP_HICON, IntPtr.Zero);

                // 3. Clear window-level small and large icons
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, IntPtr.Zero);
                SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, IntPtr.Zero);

                // 4. Force non-client area frame recalculation
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
            }
            catch
            {
                // Non-critical
            }
        }

        private static int ToColorRef(byte r, byte g, byte b)
        {
            return (b << 16) | (g << 8) | r;
        }

        private void ApplyDarkThemeWindowBar()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                var theme = ThemeService.CurrentTheme;
                int useDarkMode = theme.IsLightMode ? 0 : 1;
                // Enable immersive dark mode (attribute 20 for Win11/Win10 20H1+, 19 for older Win10)
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
                }

                // Win11 (build 22000+) custom title bar color (matching active theme header)
                int captionColor = ToColorRef(theme.DwmCaptionR, theme.DwmCaptionG, theme.DwmCaptionB);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

                // Win11 title text color
                int textColor = ToColorRef(theme.DwmTextR, theme.DwmTextG, theme.DwmTextB);
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));

                // Win11 border color (matching active theme card border)
                int borderColor = ToColorRef(theme.DwmBorderR, theme.DwmBorderG, theme.DwmBorderB);
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
            }
            catch
            {
                // Graceful fallback on older OS builds
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isExplicitExit)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                base.OnClosing(e);
            }
        }

        public void ExitApplication()
        {
            _isExplicitExit = true;
            Close();
        }

        private void OnMinimizeToTrayClick(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void OnOpenApiInBrowserClick(object sender, RoutedEventArgs e)
        {
            int port = ConfigManager.Instance.Config.ApiPort;
            Process.Start(new ProcessStartInfo
            {
                FileName = $"http://localhost:{port}/api/v1/summary",
                UseShellExecute = true
            });
        }

        private Action? _onModalPrimaryAction;
        private Action? _onModalSecondaryAction;

        public void ShowInAppModal(
            string title, 
            string message, 
            string icon = "ℹ️", 
            string? details = null, 
            string primaryButtonText = "OK", 
            Action? onPrimary = null, 
            string? secondaryButtonText = null, 
            Action? onSecondary = null)
        {
            _onModalPrimaryAction = onPrimary;
            _onModalSecondaryAction = onSecondary;

            ModalTitleText.Text = title;
            ModalMessageText.Text = message;
            ModalIconText.Text = icon;

            if (!string.IsNullOrWhiteSpace(details))
            {
                ModalDetailsBox.Text = details;
                ModalDetailsCard.Visibility = Visibility.Visible;
            }
            else
            {
                ModalDetailsBox.Text = string.Empty;
                ModalDetailsCard.Visibility = Visibility.Collapsed;
            }

            ModalPrimaryButton.Content = primaryButtonText;

            if (!string.IsNullOrWhiteSpace(secondaryButtonText))
            {
                ModalSecondaryButton.Content = secondaryButtonText;
                ModalSecondaryButton.Visibility = Visibility.Visible;
            }
            else
            {
                ModalSecondaryButton.Visibility = Visibility.Collapsed;
            }

            InAppModalOverlay.Visibility = Visibility.Visible;
        }

        public void HideInAppModal()
        {
            InAppModalOverlay.Visibility = Visibility.Collapsed;
            _onModalPrimaryAction = null;
            _onModalSecondaryAction = null;
        }

        private void OnModalBackdropMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            HideInAppModal();
        }

        private void OnModalCloseClick(object sender, RoutedEventArgs e)
        {
            HideInAppModal();
        }

        private void OnModalPrimaryClick(object sender, RoutedEventArgs e)
        {
            var action = _onModalPrimaryAction;
            HideInAppModal();
            action?.Invoke();
        }

        private void OnModalSecondaryClick(object sender, RoutedEventArgs e)
        {
            var action = _onModalSecondaryAction;
            HideInAppModal();
            action?.Invoke();
        }

        private void OnModalCopyClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(ModalDetailsBox.Text))
                {
                    System.Windows.Clipboard.SetText(ModalDetailsBox.Text);
                    ModalCopyButton.Content = "✓ Copied!";
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    timer.Tick += (s, args) =>
                    {
                        ModalCopyButton.Content = "Copy";
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
            catch { }
        }

        private async void OnCheckForUpdatesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var release = await UpdateService.Instance.CheckForUpdatesAsync(isManual: true);
                if (release != null)
                {
                    ShowInAppModal(
                        title: "inFaux Update Available",
                        message: $"A new official release is available: {release.TagName}\n\nWould you like inFaux to verify the cryptographic SHA-256 hash and update automatically?",
                        icon: "🚀",
                        details: $"Target Tag: {release.TagName}\nPublished: {release.PublishedAt?.ToString("g") ?? "N/A"}",
                        primaryButtonText: "Update Now",
                        onPrimary: async () =>
                        {
                            await UpdateService.Instance.ExecuteUpdateAsync(release);
                        },
                        secondaryButtonText: "Later");
                }
                else
                {
                    ShowInAppModal(
                        title: "Update Check",
                        message: "inFaux is up to date! You are running the latest official build.",
                        icon: "✓",
                        details: $"Current Version: v{UpdateService.Instance.GetCurrentVersionString()}\nSHA-256: {UpdateService.Instance.GetLocalExecutableHash()}",
                        primaryButtonText: "OK");
                }
            }
            catch (Exception ex)
            {
                ShowInAppModal(
                    title: "Update Check Failed",
                    message: $"Unable to check for updates:\n{ex.Message}",
                    icon: "⚠️",
                    primaryButtonText: "Close");
            }
        }

        private async void OnVerifyHashClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var audit = await UpdateService.Instance.AuditAgainstGitHubAsync();
                string icon = audit.Status == IntegrityStatus.OfficialLatest ? "🛡️" : "⚠️";
                string details = $"Local Version: {audit.LocalVersion}\nLocal Hash:\n{audit.LocalHash}\n\nExpected Release Hash:\n{audit.ExpectedHash ?? "N/A"}";

                ShowInAppModal(
                    title: "Cryptographic Integrity Audit",
                    message: $"Status: {audit.Status}\n\n{audit.Message}",
                    icon: icon,
                    details: details,
                    primaryButtonText: "OK");
            }
            catch (Exception ex)
            {
                ShowInAppModal(
                    title: "Audit Error",
                    message: $"Audit failed:\n{ex.Message}",
                    icon: "⚠️",
                    primaryButtonText: "Close");
            }
        }

        private void OnCopyHashClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string hash = UpdateService.Instance.GetLocalExecutableHash();
                System.Windows.Clipboard.SetText(hash);
                if (sender is System.Windows.Controls.Button btn)
                {
                    string oldText = btn.Content?.ToString() ?? "Copy Hash";
                    btn.Content = "✓ Copied!";
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    timer.Tick += (s, args) =>
                    {
                        btn.Content = oldText;
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("UI", $"Failed to copy hash: {ex.Message}");
            }
        }

        private void OnUninstallClick(object sender, RoutedEventArgs e)
        {
            ShowInAppModal(
                title: "Clean System Removal",
                message: "Are you sure you want to completely uninstall inFaux?\n\nThis will remove scheduled autostart tasks, Start Menu shortcuts, and application files.",
                icon: "⚠️",
                primaryButtonText: "Uninstall inFaux",
                onPrimary: () =>
                {
                    try
                    {
                        string? exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                        string appDir = !string.IsNullOrEmpty(exePath) ? (Path.GetDirectoryName(exePath) ?? "") : AppDomain.CurrentDomain.BaseDirectory;
                        string scriptPath = Path.Combine(appDir, "uninstall.ps1");

                        if (!File.Exists(scriptPath))
                        {
                            string localPath = Path.Combine(Environment.CurrentDirectory, "uninstall.ps1");
                            if (File.Exists(localPath)) scriptPath = localPath;
                        }

                        if (File.Exists(scriptPath))
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "powershell.exe",
                                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                                UseShellExecute = true
                            });
                            ExitApplication();
                        }
                        else
                        {
                            // Portable cleanup fallback
                            StartupService.SetStartup(false);
                            ShowInAppModal("Clean Removal", "inFaux scheduled startup tasks have been cleanly removed.", "✓");
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowInAppModal("Uninstall Error", $"Uninstallation error: {ex.Message}", "⚠️");
                    }
                },
                secondaryButtonText: "Cancel");
        }

        private void OnWebsiteClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/TalviFox/inFaux",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void OnThemeCardClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string themeId)
            {
                (DataContext as MainViewModel)?.SelectTheme(themeId);
            }
        }
        private void OnCpuCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            (DataContext as MainViewModel)?.OpenDetail("Cpu");
        }

        private void OnGpuCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            (DataContext as MainViewModel)?.OpenDetail("Gpu");
        }

        private void OnMemoryCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            (DataContext as MainViewModel)?.OpenDetail("Memory");
        }

        private void OnNetworkCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            (DataContext as MainViewModel)?.OpenDetail("Network");
        }

        private void OnStorageCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            (DataContext as MainViewModel)?.OpenDetail("Storage");
        }

        private void OnBackToDashboardClick(object sender, RoutedEventArgs e)
        {
            (DataContext as MainViewModel)?.BackToDashboard();
        }

        private void OnDismissAdminNoticeClick(object sender, RoutedEventArgs e)
        {
            (DataContext as MainViewModel)?.DismissAdminNotice();
        }

        private void OnGpuPillClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is FrameworkElement element && element.Tag is string gpuId)
            {
                (DataContext as MainViewModel)?.SelectGpuById(gpuId);
            }
        }

        private void OnInstallStreamDeckPluginClick(object sender, RoutedEventArgs e)
        {
            if (StreamDeckService.InstallOrUpdatePlugin(out var error))
            {
                (DataContext as MainViewModel)?.RefreshStreamDeckState();
                System.Windows.MessageBox.Show(
                    $"inFaux Stream Deck Companion Plugin (v{StreamDeckService.BundledVersion}) has been successfully deployed to your Elgato Stream Deck plugins folder!\n\nIf Stream Deck is running, restart it to load or refresh your keys.",
                    "Stream Deck Companion Installed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"Failed to deploy Stream Deck plugin: {error}",
                    "Installation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnUninstallStreamDeckPluginClick(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "Are you sure you want to remove the inFaux Companion Plugin from your Elgato Stream Deck?",
                "Confirm Plugin Removal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                if (StreamDeckService.UninstallPlugin(out var error))
                {
                    (DataContext as MainViewModel)?.RefreshStreamDeckState();
                    System.Windows.MessageBox.Show(
                        "inFaux Stream Deck plugin has been removed cleanly.",
                        "Plugin Removed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $"Failed to remove plugin: {error}",
                        "Removal Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void OnOpenStreamDeckFolderClick(object sender, RoutedEventArgs e)
        {
            StreamDeckService.OpenPluginFolder();
        }

        private void OnMarkTimAppliedTodayClick(object sender, RoutedEventArgs e)
        {
            (DataContext as MainViewModel)?.MarkTimAppliedToday();
        }

        private void OnClearTimAppliedDateClick(object sender, RoutedEventArgs e)
        {
            (DataContext as MainViewModel)?.ClearTimAppliedDate();
        }

        private void OnComboBoxLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.ComboBox cb)
            {
                cb.ApplyTemplate();
                AdjustComboBoxPopupPlacement(cb);
            }
        }

        private void OnComboBoxPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.ComboBox cb)
            {
                AdjustComboBoxPopupPlacement(cb);
            }
        }

        private void OnComboBoxPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.F4 || e.Key == System.Windows.Input.Key.Down || e.Key == System.Windows.Input.Key.Space || e.Key == System.Windows.Input.Key.Enter)
            {
                AdjustComboBoxPopupPlacement(sender as System.Windows.Controls.ComboBox);
            }
        }

        private void OnComboBoxDropDownOpened(object sender, EventArgs e)
        {
            AdjustComboBoxPopupPlacement(sender as System.Windows.Controls.ComboBox);
        }

        private void AdjustComboBoxPopupPlacement(System.Windows.Controls.ComboBox? cb)
        {
            if (cb == null) return;
            if (cb.Template?.FindName("Popup", cb) is System.Windows.Controls.Primitives.Popup popup)
            {
                try
                {
                    var pointInWindow = cb.TranslatePoint(new System.Windows.Point(0, 0), this);
                    double spaceBelow = ActualHeight - (pointInWindow.Y + cb.ActualHeight);
                    double spaceAbove = pointInWindow.Y;

                    // If space below is constrained and more space exists above, open upwards cleanly inside window
                    if (spaceBelow < 220 && spaceAbove > spaceBelow)
                    {
                        popup.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                        popup.VerticalOffset = -4;
                    }
                    else
                    {
                        popup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                        popup.VerticalOffset = 0;
                    }
                }
                catch
                {
                    popup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                }
            }
        }

        private void OnResetMinMaxClick(object sender, RoutedEventArgs e)
        {
            TelemetryEngine.Instance.ResetMinMax();
        }
    }
}
