using System;
using System.IO;
using System.Text.Json;
using InFox.Models.Config;

namespace InFox.Services
{
    public class ConfigManager
    {
        private static readonly Lazy<ConfigManager> _instance = new(() => new ConfigManager());
        public static ConfigManager Instance => _instance.Value;

        private readonly object _lock = new();
        private readonly string _configFilePath;
        private AppConfig _config;

        public event Action<AppConfig>? ConfigChanged;

        public AppConfig Config
        {
            get
            {
                lock (_lock)
                {
                    return _config;
                }
            }
        }

        private ConfigManager()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(localAppData, "inFaux");
            Directory.CreateDirectory(appFolder);
            _configFilePath = Path.Combine(appFolder, "config.json");

            _config = LoadConfigInternal();
        }

        private AppConfig LoadConfigInternal()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var loaded = JsonSerializer.Deserialize<AppConfig>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("ConfigManager", "Failed to read config file, falling back to defaults", ex);
            }

            var defaultConfig = new AppConfig();
            SaveConfigInternal(defaultConfig);
            return defaultConfig;
        }

        public void SaveConfig()
        {
            lock (_lock)
            {
                SaveConfigInternal(_config);
            }
        }

        public void UpdateConfig(Action<AppConfig> updateAction)
        {
            lock (_lock)
            {
                updateAction(_config);
                SaveConfigInternal(_config);
            }
            ConfigChanged?.Invoke(_config);
        }

        private void SaveConfigInternal(AppConfig cfg)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(cfg, options);
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("ConfigManager", "Failed to save config file", ex);
            }
        }
    }
}
