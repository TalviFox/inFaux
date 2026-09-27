using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
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

                int useDarkMode = 1;
                // Enable immersive dark mode (attribute 20 for Win11/Win10 20H1+, 19 for older Win10)
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
                }

                // Win11 (build 22000+) custom title bar color: #0C1017 (matching inFaux header)
                int captionColor = ToColorRef(0x0C, 0x10, 0x17);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

                // Win11 title text color: #ECEFF4
                int textColor = ToColorRef(0xEC, 0xEF, 0xF4);
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));

                // Win11 border color: #1E293B (matching border theme)
                int borderColor = ToColorRef(0x1E, 0x29, 0x3B);
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

        private async void OnCheckForUpdatesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var release = await UpdateService.Instance.CheckForUpdatesAsync(isManual: true);
                if (release != null)
                {
                    var result = System.Windows.MessageBox.Show(
                        $"A new release is available: {release.TagName}\n\nWould you like inFaux to verify the cryptographic SHA-256 hash and update automatically?",
                        "inFaux Update Available",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        await UpdateService.Instance.ExecuteUpdateAsync(release);
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show("inFaux is up to date!", "Update Check", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Update check failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnVerifyHashClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var audit = await UpdateService.Instance.AuditAgainstGitHubAsync();
                System.Windows.MessageBox.Show(
                    $"Status: {audit.Status}\n\nLocal Version: {audit.LocalVersion}\nLocal Hash:\n{audit.LocalHash}\n\nExpected Release Hash:\n{audit.ExpectedHash ?? "N/A"}\n\n{audit.Message}",
                    "inFaux Cryptographic Integrity Audit",
                    MessageBoxButton.OK,
                    audit.Status == IntegrityStatus.OfficialLatest ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Audit failed: {ex.Message}", "Audit Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
    }
}
