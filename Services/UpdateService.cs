using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace InFox.Services
{
    public enum IntegrityStatus
    {
        Unknown,
        OfficialLatest,
        OfficialOlderUpdateAvailable,
        LocalDevelopmentBuild,
        MismatchWarning
    }

    public class IntegrityAuditResult
    {
        public IntegrityStatus Status { get; set; } = IntegrityStatus.Unknown;
        public string LocalHash { get; set; } = string.Empty;
        public string LocalVersion { get; set; } = string.Empty;
        public string? MatchedTag { get; set; }
        public string? LatestTag { get; set; }
        public string? ExpectedHash { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("published_at")]
        public DateTime? PublishedAt { get; set; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    public class UpdateService
    {
        private static readonly Lazy<UpdateService> _instance = new(() => new UpdateService());
        public static UpdateService Instance => _instance.Value;

        private const string RepoOwner = "TalviFox";
        private const string RepoName = "inFaux";
        private const string ReleasesApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases";

        private readonly HttpClient _httpClient;
        private string? _cachedLocalHash;

        public event Action<GitHubRelease>? UpdateAvailable;
        public event Action<string>? UpdateCheckCompleted;

        public GitHubRelease? LatestRelease { get; private set; }
        public bool IsUpdateAvailable { get; private set; }
        public bool IsChecking { get; private set; }

        public UpdateService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("inFaux", GetCurrentVersionString()));
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public string GetCurrentVersionString()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        }

        public string GetLocalExecutableHash()
        {
            if (_cachedLocalHash != null) return _cachedLocalHash;

            try
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    byte[] bytes = File.ReadAllBytes(exePath);
                    byte[] hash = SHA256.HashData(bytes);
                    _cachedLocalHash = Convert.ToHexString(hash).ToLowerInvariant();
                    return _cachedLocalHash;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("UpdateService", "Failed to compute local executable hash", ex);
            }

            return "unknown";
        }

        public async Task<GitHubRelease?> CheckForUpdatesAsync(bool isManual = false)
        {
            if (IsChecking) return LatestRelease;
            IsChecking = true;

            try
            {
                var config = ConfigManager.Instance.Config;
                if (!isManual && !config.CheckForUpdates)
                {
                    LoggingService.Instance.Info("UpdateService", "Automatic update checks disabled in settings.");
                    return null;
                }

                // Throttle background checks to at most once every 24 hours
                if (!isManual && config.LastUpdateCheckUtc.HasValue)
                {
                    var timeSinceLast = DateTime.UtcNow - config.LastUpdateCheckUtc.Value;
                    if (timeSinceLast < TimeSpan.FromHours(24))
                    {
                        LoggingService.Instance.Info("UpdateService", $"Skipping update check; last check was {timeSinceLast.TotalHours:F1} hours ago.");
                        return null;
                    }
                }

                LoggingService.Instance.Info("UpdateService", "Querying GitHub Releases API...");
                using var response = await _httpClient.GetAsync(ReleasesApiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    LoggingService.Instance.Warning("UpdateService", $"GitHub API returned {response.StatusCode}");
                    UpdateCheckCompleted?.Invoke($"GitHub API status: {response.StatusCode}");
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync();
                var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json);
                if (releases == null || releases.Count == 0)
                {
                    UpdateCheckCompleted?.Invoke("No releases found.");
                    return null;
                }

                ConfigManager.Instance.UpdateConfig(c => c.LastUpdateCheckUtc = DateTime.UtcNow);

                var latest = releases[0];
                LatestRelease = latest;

                string currentVersionStr = GetCurrentVersionString();
                string latestVersionStr = latest.TagName.TrimStart('v', 'V');

                if (IsNewerVersion(currentVersionStr, latestVersionStr))
                {
                    IsUpdateAvailable = true;
                    LoggingService.Instance.Info("UpdateService", $"Update available: {latest.TagName} (current: v{currentVersionStr})");

                    UpdateAvailable?.Invoke(latest);
                    UpdateCheckCompleted?.Invoke($"Update available: {latest.TagName}");
                    return latest;
                }
                else
                {
                    IsUpdateAvailable = false;
                    UpdateCheckCompleted?.Invoke("inFaux is up to date.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("UpdateService", "Error during update check", ex);
                UpdateCheckCompleted?.Invoke($"Check failed: {ex.Message}");
                return null;
            }
            finally
            {
                IsChecking = false;
            }
        }

        public async Task<IntegrityAuditResult> AuditAgainstGitHubAsync()
        {
            var result = new IntegrityAuditResult
            {
                LocalHash = GetLocalExecutableHash(),
                LocalVersion = GetCurrentVersionString()
            };

            try
            {
                using var response = await _httpClient.GetAsync(ReleasesApiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    result.Status = IntegrityStatus.Unknown;
                    result.Message = $"Could not contact GitHub API ({response.StatusCode}).";
                    return result;
                }

                string json = await response.Content.ReadAsStringAsync();
                var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json);
                if (releases == null || releases.Count == 0)
                {
                    result.Status = IntegrityStatus.Unknown;
                    result.Message = "No published releases found on GitHub repository.";
                    return result;
                }

                result.LatestTag = releases[0].TagName;

                foreach (var rel in releases)
                {
                    string? expectedHash = await ExtractExpectedHashForRelease(rel);
                    if (!string.IsNullOrEmpty(expectedHash) && string.Equals(result.LocalHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        result.MatchedTag = rel.TagName;
                        result.ExpectedHash = expectedHash;

                        if (rel.TagName == releases[0].TagName)
                        {
                            result.Status = IntegrityStatus.OfficialLatest;
                            result.Message = $"Binary matches official latest GitHub release ({rel.TagName}).";
                        }
                        else
                        {
                            result.Status = IntegrityStatus.OfficialOlderUpdateAvailable;
                            result.Message = $"Binary matches official release ({rel.TagName}). Newer version ({releases[0].TagName}) is available.";
                        }
                        return result;
                    }
                }

                foreach (var rel in releases)
                {
                    string relVer = rel.TagName.TrimStart('v', 'V');
                    if (string.Equals(result.LocalVersion, relVer, StringComparison.OrdinalIgnoreCase))
                    {
                        string? expectedHash = await ExtractExpectedHashForRelease(rel);
                        result.ExpectedHash = expectedHash;
                        result.MatchedTag = rel.TagName;
                        result.Status = IntegrityStatus.MismatchWarning;
                        result.Message = $"Version indicates {rel.TagName}, but local SHA-256 does NOT match the release checksum!";
                        return result;
                    }
                }

                result.Status = IntegrityStatus.LocalDevelopmentBuild;
                result.Message = "Local or self-compiled build. Hash does not match any official GitHub release.";
                return result;
            }
            catch (Exception ex)
            {
                result.Status = IntegrityStatus.Unknown;
                result.Message = $"Audit error: {ex.Message}";
                return result;
            }
        }

        private async Task<string?> ExtractExpectedHashForRelease(GitHubRelease release)
        {
            var sumsAsset = release.Assets.Find(a => a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase) ||
                                                    a.Name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase) ||
                                                    a.Name.Equals("inFaux.exe.sha256", StringComparison.OrdinalIgnoreCase));
            if (sumsAsset != null)
            {
                try
                {
                    string text = await _httpClient.GetStringAsync(sumsAsset.BrowserDownloadUrl);
                    string? hash = ParseHashFromChecksumFile(text);
                    if (!string.IsNullOrEmpty(hash)) return hash;
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(release.Body))
            {
                var match = Regex.Match(release.Body, @"\b[a-fA-F0-9]{64}\b", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Value.ToLowerInvariant();
                }
            }

            return null;
        }

        private static string? ParseHashFromChecksumFile(string content)
        {
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var match = Regex.Match(line, @"^([a-fA-F0-9]{64})\s+.*inFaux\.exe", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups[1].Value.ToLowerInvariant();
                }

                var anyHash = Regex.Match(line, @"^[a-fA-F0-9]{64}$");
                if (anyHash.Success)
                {
                    return anyHash.Value.ToLowerInvariant();
                }
            }
            return null;
        }

        public async Task<bool> ExecuteUpdateAsync(GitHubRelease release, IProgress<string>? progressReporter = null)
        {
            try
            {
                progressReporter?.Report("Locating inFaux.exe in release assets...");
                var exeAsset = release.Assets.Find(a => a.Name.Equals("inFaux.exe", StringComparison.OrdinalIgnoreCase));
                if (exeAsset == null)
                {
                    throw new InvalidOperationException($"Release {release.TagName} does not contain 'inFaux.exe' asset.");
                }

                string? expectedHash = await ExtractExpectedHashForRelease(release);
                if (string.IsNullOrEmpty(expectedHash))
                {
                    throw new InvalidOperationException($"Official SHA-256 checksum not found for {release.TagName}. Update aborted.");
                }

                string defaultInstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "inFaux");
                string currentExe = Environment.ProcessPath ?? Path.Combine(defaultInstallDir, "inFaux.exe");
                string installDir = Path.GetDirectoryName(currentExe) ?? defaultInstallDir;

                string updateDir = Path.Combine(installDir, "Updates");
                Directory.CreateDirectory(updateDir);
                string tempExe = Path.Combine(updateDir, "inFaux.exe.tmp");
                string updateScript = Path.Combine(updateDir, "apply_update.ps1");

                progressReporter?.Report($"Downloading {release.TagName} ({exeAsset.Size / 1024 / 1024:F1} MB)...");
                using (var response = await _httpClient.GetAsync(exeAsset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using var fs = new FileStream(tempExe, FileMode.Create, FileAccess.Write, FileShare.None);
                    await response.Content.CopyToAsync(fs);
                }

                progressReporter?.Report("Verifying SHA-256 integrity...");
                byte[] downloadedBytes = await File.ReadAllBytesAsync(tempExe);
                string actualHash = Convert.ToHexString(SHA256.HashData(downloadedBytes)).ToLowerInvariant();

                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tempExe);
                    throw new InvalidOperationException($"SHA-256 verification FAILED!\nExpected: {expectedHash}\nActual: {actualHash}");
                }

                string newVersionTag = release.TagName.TrimStart('v', 'V');

                string scriptContent = $@"
# inFaux Update Helper
Start-Sleep -Seconds 1
Get-Process -Name 'inFaux' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$maxRetries = 10
$retryCount = 0
$success = $false

while (-not $success -and $retryCount -lt $maxRetries) {{
    try {{
        Copy-Item -Path '{tempExe}' -Destination '{currentExe}' -Force -ErrorAction Stop
        Remove-Item -Path '{tempExe}' -Force -ErrorAction SilentlyContinue

        $regKeyUser = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\inFaux'
        if (Test-Path $regKeyUser) {{
            Set-ItemProperty -Path $regKeyUser -Name 'DisplayVersion' -Value '{newVersionTag}' -ErrorAction SilentlyContinue
        }}
        $regKeyMachine = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\inFaux'
        if (Test-Path $regKeyMachine) {{
            Set-ItemProperty -Path $regKeyMachine -Name 'DisplayVersion' -Value '{newVersionTag}' -ErrorAction SilentlyContinue
        }}

        Start-Process -FilePath '{currentExe}'
        $success = $true
    }} catch {{
        $retryCount++
        Start-Sleep -Milliseconds 500
    }}
}}
";
                await File.WriteAllTextAsync(updateScript, scriptContent);

                progressReporter?.Report("Applying update and restarting inFaux...");
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{updateScript}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(startInfo);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.Application.Current.Shutdown();
                });

                return true;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("UpdateService", "Update execution failed", ex);
                throw;
            }
        }

        private static bool IsNewerVersion(string currentVersion, string latestVersion)
        {
            if (Version.TryParse(currentVersion, out var cur) && Version.TryParse(latestVersion, out var lat))
            {
                return lat > cur;
            }
            return string.Compare(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase) > 0;
        }
    }
}
