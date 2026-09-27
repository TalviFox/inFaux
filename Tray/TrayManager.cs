using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using InFox.Engine;
using InFox.Models.Sensors;
using InFox.Services;

namespace InFox.Tray
{
    public class TrayManager : IDisposable
    {
        private static readonly Lazy<TrayManager> _instance = new(() => new TrayManager());
        public static TrayManager Instance => _instance.Value;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private NotifyIcon? _notifyIcon;
        private IntPtr _lastHIcon = IntPtr.Zero;
        private Icon? _defaultIcon;
        private readonly object _renderLock = new();

        public event Action? OpenDashboardRequested;
        public event Action? ExitRequested;

        private TrayManager() { }

        public void Initialize()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                {
                    _defaultIcon = Icon.ExtractAssociatedIcon(exePath);
                }
            }
            catch { }

            _notifyIcon = new NotifyIcon
            {
                Text = "inFaux - Hardware Monitor",
                Icon = _defaultIcon ?? SystemIcons.Application,
                Visible = true
            };

            // Set default icon
            UpdateTrayBadge(null);

            // Context menu
            var menu = new ContextMenuStrip();

            var titleItem = new ToolStripMenuItem("inFaux (FoxDen Software)") { Enabled = false };
            titleItem.Font = new Font(titleItem.Font, FontStyle.Bold);
            menu.Items.Add(titleItem);
            menu.Items.Add(new ToolStripSeparator());

            var openItem = new ToolStripMenuItem("Open Dashboard", null, (s, e) => OpenDashboardRequested?.Invoke());
            openItem.Font = new Font(openItem.Font, FontStyle.Bold);
            menu.Items.Add(openItem);

            var apiItem = new ToolStripMenuItem("Open Local API Browser", null, (s, e) =>
            {
                int port = ConfigManager.Instance.Config.ApiPort;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = $"http://localhost:{port}/api/v1/summary",
                    UseShellExecute = true
                });
            });
            menu.Items.Add(apiItem);

            menu.Items.Add(new ToolStripSeparator());

            // Sensor target selection
            var targetMenu = new ToolStripMenuItem("Tray Sensor Target");
            targetMenu.DropDownItems.Add("CPU Package Temp", null, (s, e) => ConfigManager.Instance.UpdateConfig(c => c.TraySensorTarget = "cpu_temp"));
            targetMenu.DropDownItems.Add("GPU Core Temp", null, (s, e) => ConfigManager.Instance.UpdateConfig(c => c.TraySensorTarget = "gpu_temp"));
            targetMenu.DropDownItems.Add("Highest (CPU or GPU)", null, (s, e) => ConfigManager.Instance.UpdateConfig(c => c.TraySensorTarget = "auto_max_temp"));
            menu.Items.Add(targetMenu);

            menu.Items.Add(new ToolStripSeparator());

            // Update & Audit items
            var updateItem = new ToolStripMenuItem("Check for Updates...", null, async (s, e) =>
            {
                await UpdateService.Instance.CheckForUpdatesAsync(isManual: true);
            });
            menu.Items.Add(updateItem);

            var auditItem = new ToolStripMenuItem("Verify Binary Integrity...", null, async (s, e) =>
            {
                var audit = await UpdateService.Instance.AuditAgainstGitHubAsync();
                MessageBox.Show(
                    $"Status: {audit.Status}\n\nLocal Version: {audit.LocalVersion}\nLocal Hash: {audit.LocalHash}\nExpected Hash: {audit.ExpectedHash ?? "N/A"}\n\n{audit.Message}",
                    "inFaux Cryptographic Integrity Audit",
                    MessageBoxButtons.OK,
                    audit.Status == IntegrityStatus.OfficialLatest ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            });
            menu.Items.Add(auditItem);

            menu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Exit inFaux", null, (s, e) => ExitRequested?.Invoke());
            menu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (s, e) => OpenDashboardRequested?.Invoke();

            // Wire up telemetry ticks
            TelemetryEngine.Instance.TelemetryUpdated += OnTelemetryUpdated;
        }

        private void OnTelemetryUpdated(SystemSummary summary, System.Collections.Generic.IReadOnlyList<SensorMetric> metrics)
        {
            if (_notifyIcon == null) return;

            string target = ConfigManager.Instance.Config.TraySensorTarget;
            double? displayTemp = null;

            if (target == "gpu_temp")
            {
                displayTemp = summary.Gpu.TempC;
            }
            else if (target == "auto_max_temp")
            {
                double cpu = summary.Cpu.TempC ?? 0;
                double gpu = summary.Gpu.TempC ?? 0;
                displayTemp = Math.Max(cpu, gpu);
                if (displayTemp <= 0) displayTemp = null;
            }
            else
            {
                // Default cpu_temp
                displayTemp = summary.Cpu.TempC;
            }

            UpdateTrayBadge(displayTemp);

            // Update tooltip text
            string cpuText = summary.Cpu.TempC.HasValue ? $"{summary.Cpu.TempC:F0}°C ({summary.Cpu.LoadPercent:F0}%)" : $"{summary.Cpu.LoadPercent:F0}%";
            string gpuText = summary.Gpu.TempC.HasValue ? $"{summary.Gpu.TempC:F0}°C ({summary.Gpu.LoadPercent:F0}%)" : "Idle";
            string ramText = $"{summary.Memory.UsedGb:F1} / {summary.Memory.TotalGb:F0} GB ({summary.Memory.Percent:F0}%)";

            string tooltip = $"inFaux - FoxDen Software\nCPU: {cpuText}\nGPU: {gpuText}\nRAM: {ramText}";
            if (tooltip.Length >= 128) tooltip = tooltip.Substring(0, 127); // Windows Shell limit

            try
            {
                _notifyIcon.Text = tooltip;
            }
            catch { }
        }

        public void UpdateTrayBadge(double? tempC)
        {
            if (_notifyIcon == null) return;

            lock (_renderLock)
            {
                try
                {
                    using var bmp = new Bitmap(32, 32);
                    using var g = Graphics.FromImage(bmp);

                    g.SmoothingMode = SmoothingMode.HighQuality;
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.Transparent);

                    if (tempC.HasValue && tempC.Value > 0)
                    {
                        int tempInt = (int)Math.Round(tempC.Value);
                        string text = tempInt.ToString();

                        // Color coding
                        Color textColor;
                        if (tempInt >= 80)
                            textColor = Color.FromArgb(255, 82, 82);   // Hot Red
                        else if (tempInt >= 65)
                            textColor = Color.FromArgb(255, 145, 0);  // Fox Amber
                        else
                            textColor = Color.FromArgb(64, 196, 255);  // Fox Cyan

                        // Draw background pill badge
                        using (var bgBrush = new SolidBrush(Color.FromArgb(220, 12, 16, 24)))
                        {
                            g.FillEllipse(bgBrush, 0, 0, 31, 31);
                        }
                        using (var borderPen = new Pen(textColor, 1.5f))
                        {
                            g.DrawEllipse(borderPen, 0.75f, 0.75f, 30.5f, 30.5f);
                        }

                        // Draw text
                        float fontSize = text.Length > 2 ? 14f : 17f;
                        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                        using var brush = new SolidBrush(textColor);

                        var sf = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        };

                        g.DrawString(text, font, brush, new RectangleF(0, 1, 32, 31), sf);
                    }
                    else
                    {
                        if (_defaultIcon != null)
                        {
                            _notifyIcon.Icon = _defaultIcon;
                            return;
                        }
                    }

                    IntPtr hIcon = bmp.GetHicon();
                    var newIcon = Icon.FromHandle(hIcon);
                    _notifyIcon.Icon = newIcon;

                    if (_lastHIcon != IntPtr.Zero)
                    {
                        DestroyIcon(_lastHIcon);
                    }
                    _lastHIcon = hIcon;
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Warning("TrayManager", $"Failed to render dynamic badge: {ex.Message}");
                }
            }
        }

        public void Dispose()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            if (_lastHIcon != IntPtr.Zero)
            {
                DestroyIcon(_lastHIcon);
                _lastHIcon = IntPtr.Zero;
            }
        }
    }
}
