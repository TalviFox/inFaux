using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace InFox.Services
{
    public enum StreamDeckPluginStatus
    {
        StreamDeckNotDetected,
        NotInstalled,
        InstalledUpToDate,
        UpdateAvailable
    }

    public static class StreamDeckService
    {
        public const string BundledVersion = "1.1.0";
        public const string PluginUuid = "com.foxden.infaux.sdPlugin";

        private static string AppDataPath => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        public static string ElgatoDataDir => Path.Combine(AppDataPath, "Elgato", "StreamDeck");
        public static string PluginsDir => Path.Combine(ElgatoDataDir, "Plugins");
        public static string InFauxPluginDir => Path.Combine(PluginsDir, PluginUuid);
        public static string ManifestFilePath => Path.Combine(InFauxPluginDir, "manifest.json");

        public static bool IsStreamDeckInstalled()
        {
            if (Directory.Exists(ElgatoDataDir)) return true;

            var processes = Process.GetProcessesByName("StreamDeck");
            if (processes.Length > 0)
            {
                foreach (var p in processes) p.Dispose();
                return true;
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string exePath = Path.Combine(programFiles, "Elgato", "StreamDeck", "StreamDeck.exe");
            return File.Exists(exePath);
        }

        public static string? GetInstalledVersion()
        {
            try
            {
                if (!File.Exists(ManifestFilePath)) return null;

                string json = File.ReadAllText(ManifestFilePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Version", out var verProp))
                {
                    return verProp.GetString();
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("StreamDeck", $"Failed to parse installed manifest.json: {ex.Message}");
            }
            return null;
        }

        public static StreamDeckPluginStatus GetStatus()
        {
            string? installedVer = GetInstalledVersion();
            bool streamDeckDetected = IsStreamDeckInstalled();

            if (installedVer == null)
            {
                return streamDeckDetected ? StreamDeckPluginStatus.NotInstalled : StreamDeckPluginStatus.StreamDeckNotDetected;
            }

            if (Version.TryParse(installedVer, out var installed) && Version.TryParse(BundledVersion, out var bundled))
            {
                if (installed < bundled) return StreamDeckPluginStatus.UpdateAvailable;
                return StreamDeckPluginStatus.InstalledUpToDate;
            }

            return string.Equals(installedVer, BundledVersion, StringComparison.OrdinalIgnoreCase)
                ? StreamDeckPluginStatus.InstalledUpToDate
                : StreamDeckPluginStatus.UpdateAvailable;
        }

        public static bool InstallOrUpdatePlugin(out string? error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(PluginsDir);

                // 1. Try to load embedded .streamDeckPlugin archive
                var assembly = typeof(StreamDeckService).Assembly;
                using var stream = assembly.GetManifestResourceStream("com.foxden.infaux.streamDeckPlugin");

                if (stream != null)
                {
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                    ExtractArchiveToPluginsDir(archive);
                }
                else
                {
                    // Fallback to local dev path if running from source/debug without embedded resource
                    string localPluginFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "integrations", "streamdeck", "com.foxden.infaux.streamDeckPlugin");
                    string devPluginFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "integrations", "streamdeck", "com.foxden.infaux.streamDeckPlugin");

                    string? validZip = File.Exists(localPluginFile) ? localPluginFile : (File.Exists(devPluginFile) ? devPluginFile : null);

                    if (validZip != null)
                    {
                        using var fileStream = File.OpenRead(validZip);
                        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);
                        ExtractArchiveToPluginsDir(archive);
                    }
                    else
                    {
                        // Fallback: Copy raw directory if unzipped files exist in dev
                        string localRawDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "integrations", "streamdeck", PluginUuid);
                        string devRawDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "integrations", "streamdeck", PluginUuid);
                        string? rawDir = Directory.Exists(localRawDir) ? localRawDir : (Directory.Exists(devRawDir) ? devRawDir : null);

                        if (rawDir != null)
                        {
                            CopyDirectoryRecursively(rawDir, InFauxPluginDir);
                        }
                        else
                        {
                            error = "Embedded Stream Deck plugin package could not be located.";
                            return false;
                        }
                    }
                }

                LoggingService.Instance.Info("StreamDeck", $"Successfully installed/updated inFaux Stream Deck companion plugin (v{BundledVersion}).");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                LoggingService.Instance.Error("StreamDeck", $"Failed to install Stream Deck plugin: {ex.Message}", ex);
                return false;
            }
        }

        public static bool UninstallPlugin(out string? error)
        {
            error = null;
            try
            {
                if (Directory.Exists(InFauxPluginDir))
                {
                    Directory.Delete(InFauxPluginDir, recursive: true);
                    LoggingService.Instance.Info("StreamDeck", "Successfully uninstalled inFaux Stream Deck companion plugin.");
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                LoggingService.Instance.Error("StreamDeck", $"Failed to uninstall Stream Deck plugin: {ex.Message}", ex);
                return false;
            }
        }

        public static void OpenPluginFolder()
        {
            try
            {
                string target = Directory.Exists(InFauxPluginDir) ? InFauxPluginDir : PluginsDir;
                if (!Directory.Exists(target)) Directory.CreateDirectory(target);
                Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("StreamDeck", $"Could not open plugin folder: {ex.Message}");
            }
        }

        public static void LaunchOrRestartStreamDeck()
        {
            try
            {
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string exePath = Path.Combine(programFiles, "Elgato", "StreamDeck", "StreamDeck.exe");
                if (File.Exists(exePath))
                {
                    var existing = Process.GetProcessesByName("StreamDeck");
                    foreach (var p in existing)
                    {
                        try { p.CloseMainWindow(); } catch { }
                        p.Dispose();
                    }
                    Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("StreamDeck", $"Could not launch Stream Deck app: {ex.Message}");
            }
        }

        private static void ExtractArchiveToPluginsDir(ZipArchive archive)
        {
            // Determine if the archive already contains the root 'com.foxden.infaux.sdPlugin' directory
            bool hasRootFolder = archive.Entries.Any(e => e.FullName.StartsWith(PluginUuid + "/", StringComparison.OrdinalIgnoreCase) ||
                                                          e.FullName.StartsWith(PluginUuid + "\\", StringComparison.OrdinalIgnoreCase));

            string baseTargetDir = hasRootFolder ? PluginsDir : InFauxPluginDir;

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) && (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\")))
                {
                    // Directory entry
                    string dirPath = Path.Combine(baseTargetDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(dirPath);
                    continue;
                }

                string filePath = Path.Combine(baseTargetDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                string? parentDir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                entry.ExtractToFile(filePath, overwrite: true);
            }
        }

        private static void CopyDirectoryRecursively(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string dest = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, dest, overwrite: true);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                string dest = Path.Combine(targetDir, Path.GetFileName(subDir));
                CopyDirectoryRecursively(subDir, dest);
            }
        }
    }
}
