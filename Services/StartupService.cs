using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using Microsoft.Win32.TaskScheduler;

namespace InFox.Services
{
    public static class StartupService
    {
        public const string TaskName = "inFaux";
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunRegistryValueName = "inFaux";

        public static bool IsStartupEnabled()
        {
            try
            {
                using var ts = new TaskService();
                var task = ts.FindTask(TaskName);
                if (task != null)
                {
                    return task.Enabled;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("Startup", $"Failed to query scheduled task status: {ex.Message}");
            }

            return false;
        }

        public static bool IsRunningFromProgramFiles
        {
            get
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = Process.GetCurrentProcess().MainModule?.FileName;
                }
                if (string.IsNullOrEmpty(exePath)) return false;

                string localAppPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string pfX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

                return exePath.StartsWith(localAppPrograms, StringComparison.OrdinalIgnoreCase) ||
                       exePath.StartsWith(pf, StringComparison.OrdinalIgnoreCase) ||
                       exePath.StartsWith(pfX86, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static bool SetStartup(bool enable, bool? minimize = null)
        {
            RemoveLegacyRegistry();

            try
            {
                using var ts = new TaskService();

                if (enable)
                {
                    string? exePath = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exePath))
                    {
                        exePath = Process.GetCurrentProcess().MainModule?.FileName;
                    }

                    if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                    {
                        LoggingService.Instance.Error("Startup", "Cannot configure startup: executable path not found.");
                        return false;
                    }

                    string workingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty;

                    var td = ts.NewTask();
                    td.RegistrationInfo.Description = "inFaux - Hardware Monitor & Telemetry Server (FoxDen Software)";
                    td.RegistrationInfo.Author = "FoxDen Software";

                    var currentIdentity = WindowsIdentity.GetCurrent().Name;
                    var logonTrigger = new LogonTrigger
                    {
                        UserId = currentIdentity,
                        Delay = TimeSpan.FromSeconds(2) // Brief buffer for desktop shell to stabilize
                    };
                    td.Triggers.Add(logonTrigger);

                    td.Principal.RunLevel = TaskRunLevel.LUA;
                    td.Principal.LogonType = TaskLogonType.InteractiveToken;

                    td.Settings.DisallowStartIfOnBatteries = false;
                    td.Settings.StopIfGoingOnBatteries = false;
                    td.Settings.ExecutionTimeLimit = TimeSpan.Zero;
                    td.Settings.AllowDemandStart = true;
                    td.Settings.StartWhenAvailable = true;
                    td.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;

                    bool startMin = minimize ?? ConfigManager.Instance.Config.MinimizeOnStartup;
                    string? arguments = startMin ? "--minimized" : null;

                    td.Actions.Add(new ExecAction(exePath, arguments, workingDirectory));

                    ts.RootFolder.RegisterTaskDefinition(
                        TaskName,
                        td,
                        TaskCreation.CreateOrUpdate,
                        null,
                        null,
                        TaskLogonType.InteractiveToken);

                    LoggingService.Instance.Info("Startup", $"Successfully registered user-level scheduled task for inFaux (minimized: {startMin}).");
                    return true;
                }
                else
                {
                    var task = ts.FindTask(TaskName);
                    if (task != null)
                    {
                        ts.RootFolder.DeleteTask(TaskName, false);
                        LoggingService.Instance.Info("Startup", "Removed inFaux scheduled task.");
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Startup", $"Failed to update scheduled task: {ex.Message}", ex);
                return false;
            }
        }

        public static void UpdateStartupArguments(bool minimize)
        {
            try
            {
                if (!IsStartupEnabled()) return;
                SetStartup(true, minimize);
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("Startup", $"Failed to update startup arguments: {ex.Message}");
            }
        }

        private static void RemoveLegacyRegistry()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (key?.GetValue(RunRegistryValueName) != null)
                {
                    key.DeleteValue(RunRegistryValueName, false);
                }
            }
            catch { }
        }
    }
}
