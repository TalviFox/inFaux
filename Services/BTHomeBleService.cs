using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace InFox.Services
{
    public class BleThermometerReading
    {
        public string MacAddress { get; set; } = string.Empty;
        public string Name { get; set; } = "BTHome Sensor";
        public double TemperatureC { get; set; }
        public double TemperatureF => Math.Round(TemperatureC * 9.0 / 5.0 + 32.0, 1);
        public double? HumidityPercent { get; set; }
        public int? BatteryPercent { get; set; }
        public short Rssi { get; set; }
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        public string DisplayName => !string.IsNullOrWhiteSpace(Name) && Name != "BTHome Sensor" && Name != "BLE Thermometer" && Name != "Xiaomi Sensor"
            ? $"{Name} ({MacAddress})"
            : $"Sensor {MacAddress}";

        public string SummaryText => $"{TemperatureF:F1}°F ({TemperatureC:F1}°C)" +
            (HumidityPercent.HasValue ? $" • {HumidityPercent.Value:F0}% RH" : "") +
            (Rssi > -127 ? $" • {Rssi} dBm" : "");
    }

    public class BTHomeBleService
    {
        private static readonly Lazy<BTHomeBleService> _instance = new(() => new BTHomeBleService());
        public static BTHomeBleService Instance => _instance.Value;

        private BluetoothLEAdvertisementWatcher? _watcher;
        private readonly object _lock = new();
        private readonly ConcurrentDictionary<string, BleThermometerReading> _sensors = new();

        public ObservableCollection<BleThermometerReading> DiscoveredSensors { get; } = new();
        private BleThermometerReading? _lastAutoReading;
        public BleThermometerReading? ActiveReading => GetSelectedOrNearestReading(ConfigManager.Instance.Config.BleAmbientSensorMac);

        public bool IsRunning { get; private set; }
        public bool IsSupported { get; private set; } = true;

        public event Action<BleThermometerReading>? ReadingUpdated;

        private BTHomeBleService()
        {
        }

        public void Initialize()
        {
            var config = ConfigManager.Instance.Config;
            if (config.EnableBleAmbient || config.EnableBleChassis || config.EnableBleRadiator)
            {
                Start();
            }
        }

        public void Start()
        {
            lock (_lock)
            {
                if (IsRunning)
                {
                    IsSupported = true;
                    return;
                }

                try
                {
                    _watcher = new BluetoothLEAdvertisementWatcher
                    {
                        ScanningMode = BluetoothLEScanningMode.Active
                    };

                    _watcher.Received += OnAdvertisementReceived;
                    _watcher.Stopped += OnWatcherStopped;

                    _watcher.Start();
                    IsRunning = true;
                    IsSupported = true;
                    LoggingService.Instance.Info("BTHomeBle", "Passive BLE thermometer watcher started.");
                }
                catch (Exception ex)
                {
                    IsSupported = false;
                    IsRunning = false;
                    LoggingService.Instance.Warning("BTHomeBle", $"Bluetooth LE scanning not available on this machine: {ex.Message}");
                }
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!IsRunning || _watcher == null) return;

                try
                {
                    _watcher.Stop();
                    _watcher.Received -= OnAdvertisementReceived;
                    _watcher.Stopped -= OnWatcherStopped;
                }
                catch { }

                _watcher = null;
                IsRunning = false;
                LoggingService.Instance.Info("BTHomeBle", "Passive BLE thermometer watcher stopped.");
            }
        }

        public BleThermometerReading? GetSpecificReading(string? mac)
        {
            if (string.IsNullOrWhiteSpace(mac) || mac.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            DateTime cutoff = DateTime.UtcNow.AddMinutes(-10);
            string norm = mac.Trim().ToUpperInvariant();
            if (_sensors.TryGetValue(norm, out var match) && match.LastSeenUtc >= cutoff)
            {
                return match;
            }
            return null;
        }

        public BleThermometerReading? GetSelectedOrNearestReading(string? preferredMac)
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-10);

            // If specific MAC preferred: STRICT matching! Never fall back to strangers' beacons!
            if (!string.IsNullOrWhiteSpace(preferredMac) && !preferredMac.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                string norm = preferredMac.Trim().ToUpperInvariant();
                var match = _sensors.Values.FirstOrDefault(s => s.MacAddress.Equals(norm, StringComparison.OrdinalIgnoreCase));
                if (match != null && match.LastSeenUtc >= cutoff)
                {
                    return match;
                }
                return null;
            }

            // Fallback for explicit Auto mode:
            // Filter active sensors seen within the cutoff window
            var activeCandidates = _sensors.Values
                .Where(s => s.LastSeenUtc >= cutoff)
                .ToList();

            if (activeCandidates.Count == 0) return null;

            // Prioritize sensors reporting a valid RSSI (> -127 dBm)
            var withRssi = activeCandidates
                .Where(s => s.Rssi > -127)
                .OrderByDescending(s => s.Rssi)
                .ToList();

            var bestCandidate = withRssi.FirstOrDefault() ?? activeCandidates.OrderByDescending(s => s.LastSeenUtc).First();

            // Anti-flapping hysteresis: If we already have an active reading that is still warm (seen < 45s ago),
            // don't bounce between rooms unless the new candidate is significantly stronger (> 6 dBm higher)
            if (_lastAutoReading != null &&
                (DateTime.UtcNow - _lastAutoReading.LastSeenUtc).TotalSeconds < 45 &&
                _sensors.TryGetValue(_lastAutoReading.MacAddress, out var currentActive) &&
                currentActive.LastSeenUtc >= cutoff)
            {
                if (currentActive.Rssi > -127 && bestCandidate.Rssi > -127)
                {
                    if (bestCandidate.Rssi - currentActive.Rssi <= 6)
                    {
                        return currentActive;
                    }
                }
                else if (currentActive.Rssi > -127)
                {
                    return currentActive;
                }
            }

            _lastAutoReading = bestCandidate;
            return bestCandidate;
        }

        private void OnWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
        {
            IsRunning = false;
            if (args.Error != Windows.Devices.Bluetooth.BluetoothError.Success)
            {
                LoggingService.Instance.Warning("BTHomeBle", $"Bluetooth LE watcher stopped: {args.Error}");
                if (args.Error == Windows.Devices.Bluetooth.BluetoothError.RadioNotAvailable ||
                    args.Error == Windows.Devices.Bluetooth.BluetoothError.NotSupported ||
                    args.Error == Windows.Devices.Bluetooth.BluetoothError.DisabledByUser ||
                    args.Error == Windows.Devices.Bluetooth.BluetoothError.DisabledByPolicy)
                {
                    IsSupported = false;
                }
            }
        }

        private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            try
            {
                string mac = FormatMacAddress(args.BluetoothAddress);
                short rssi = args.RawSignalStrengthInDBm;
                string localName = args.Advertisement.LocalName;

                // Windows BLE API returns -127 dBm when signal strength is not measured in a packet.
                // Preserve the previous valid signal strength if known and recent.
                if (rssi <= -127 && _sensors.TryGetValue(mac, out var existingSensor) && existingSensor.Rssi > -127)
                {
                    rssi = existingSensor.Rssi;
                }

                if (mac.StartsWith("F8:44:77", StringComparison.OrdinalIgnoreCase))
                {
                    LoggingService.Instance.Info("BTHomeBle", $"Observed beacon {mac} (RSSI: {rssi} dBm, Name: '{localName}', Sections: {args.Advertisement.DataSections.Count})");
                }

                foreach (var section in args.Advertisement.DataSections)
                {
                    // Look for Service Data 16-bit UUID (0x16)
                    if (section.DataType == 0x16)
                    {
                        byte[] data = ReadBuffer(section.Data);
                        if (data.Length < 3) continue;

                        ushort uuid = (ushort)(data[0] | (data[1] << 8));

                        // 0xFCD2: BTHome V2 / Shelly BLU / Home Assistant BLE standard
                        if (uuid == 0xFCD2)
                        {
                            if (TryParseBTHomeV2(data, mac, localName, rssi, out var reading))
                            {
                                UpdateReading(reading);
                                return;
                            }
                        }
                        // 0x181A: Environmental Sensing standard (ATC / PVVX custom firmware thermometers)
                        else if (uuid == 0x181A)
                        {
                            if (TryParseAtcEnvironmental(data, mac, localName, rssi, out var reading))
                            {
                                UpdateReading(reading);
                                return;
                            }
                        }
                        // 0xFE95: Xiaomi MiBeacon
                        else if (uuid == 0xFE95)
                        {
                            if (TryParseXiaomiMiBeacon(data, mac, localName, rssi, out var reading))
                            {
                                UpdateReading(reading);
                                return;
                            }
                        }
                    }
                }

                // Also check Manufacturer Data (some sensors broadcast BTHome or ATC under 0xFF)
                foreach (var mfg in args.Advertisement.ManufacturerData)
                {
                    byte[] data = ReadBuffer(mfg.Data);
                    if (mfg.CompanyId == 0xFCD2 || mfg.CompanyId == 0x181A)
                    {
                        if (TryParseBTHomeV2(data, mac, localName, rssi, out var reading))
                        {
                            UpdateReading(reading);
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        private static bool TryParseBTHomeV2(byte[] data, string mac, string localName, short rssi, out BleThermometerReading reading)
        {
            reading = new BleThermometerReading
            {
                MacAddress = mac,
                Name = !string.IsNullOrWhiteSpace(localName) ? localName : "BTHome Sensor",
                Rssi = rssi,
                LastSeenUtc = DateTime.UtcNow
            };

            bool foundTemp = false;

            // Byte 0-1: UUID (0xD2, 0xFC)
            // Byte 2: Device Info Header
            int i = 3;
            while (i < data.Length)
            {
                byte objectId = data[i++];
                switch (objectId)
                {
                    case 0x00: // Packet ID
                        if (i < data.Length) i += 1;
                        break;

                    case 0x01: // Battery % (1 byte)
                        if (i < data.Length) reading.BatteryPercent = data[i++];
                        break;

                    case 0x02: // Temperature: 2 bytes signed little-endian, factor 0.01 °C
                        if (i + 1 < data.Length)
                        {
                            short raw = (short)(data[i] | (data[i + 1] << 8));
                            reading.TemperatureC = Math.Round(raw * 0.01, 1);
                            foundTemp = true;
                            i += 2;
                        }
                        break;

                    case 0x45: // Temperature: 2 bytes signed little-endian, factor 0.1 °C
                        if (i + 1 < data.Length)
                        {
                            short raw = (short)(data[i] | (data[i + 1] << 8));
                            reading.TemperatureC = Math.Round(raw * 0.1, 1);
                            foundTemp = true;
                            i += 2;
                        }
                        break;

                    case 0x03: // Humidity: 2 bytes unsigned little-endian, factor 0.01 %
                        if (i + 1 < data.Length)
                        {
                            ushort raw = (ushort)(data[i] | (data[i + 1] << 8));
                            reading.HumidityPercent = Math.Round(raw * 0.01, 1);
                            i += 2;
                        }
                        break;

                    case 0x2E: // Humidity: 1 byte unsigned, factor 1 %
                        if (i < data.Length)
                        {
                            reading.HumidityPercent = data[i++];
                        }
                        break;

                    case 0x04: // Pressure: 3 bytes uint24, factor 0.01 hPa
                        if (i + 2 < data.Length) i += 3;
                        break;

                    case 0x05: // Illuminance: 3 bytes
                        if (i + 2 < data.Length) i += 3;
                        break;

                    case 0x0C: // Voltage: 2 bytes uint16, factor 0.001 V
                        if (i + 1 < data.Length) i += 2;
                        break;

                    // Shelly BLU H&T Display & BTHome v2 Binary Sensors / Events (1 byte each)
                    // Note: Shelly BLU H&T Display emits 0x1E (Light binary sensor) right before temperature!
                    case >= 0x0F and <= 0x13:
                    case >= 0x15 and <= 0x2D:
                    case 0x2F: // Moisture (1 byte)
                    case 0x3A: // Button event (1 byte)
                    case 0x3C: // Dimmer (1 byte)
                        if (i < data.Length) i += 1;
                        break;

                    // 2-byte BTHome measurements
                    case 0x0A: // Energy (2 bytes)
                    case 0x0B: // Volume (2 bytes)
                    case 0x0D: // Voltage 0.1V (2 bytes)
                    case 0x0E: // Current (2 bytes)
                    case 0x14: // Moisture (2 bytes)
                    case 0x3F: // Rotation (2 bytes)
                        if (i + 1 < data.Length) i += 2;
                        break;

                    // 3-byte and 4-byte measurements
                    case 0x06: // Mass (3 bytes)
                        if (i + 2 < data.Length) i += 3;
                        break;
                    case 0x07: // Mass (4 bytes)
                    case 0x08: // Volume (4 bytes)
                    case 0x09: // Volume (4 bytes)
                    case 0x40: // Distance (4 bytes)
                        if (i + 3 < data.Length) i += 4;
                        break;

                    // Variable length string/raw
                    case 0x53:
                    case 0x54:
                        if (i < data.Length)
                        {
                            byte varLen = data[i++];
                            i += varLen;
                        }
                        break;

                    default:
                        // Unrecognized object ID: advance by 1 byte rather than discarding remaining payload
                        if (i < data.Length) i += 1;
                        break;
                }
            }

            return foundTemp;
        }

        private static bool TryParseAtcEnvironmental(byte[] data, string mac, string localName, short rssi, out BleThermometerReading reading)
        {
            reading = new BleThermometerReading
            {
                MacAddress = mac,
                Name = !string.IsNullOrWhiteSpace(localName) ? localName : "BLE Thermometer",
                Rssi = rssi,
                LastSeenUtc = DateTime.UtcNow
            };

            // Standard ATC custom format & PVVX Custom format:
            // Byte 0-1: UUID (0x1A, 0x18)
            // Byte 2-7: MAC (6 bytes)
            if (data.Length >= 12)
            {
                // 1. PVVX "Custom" format (15-19 bytes):
                // Byte 8-9: signed int16, factor 0.01°C (little-endian)
                // Byte 10-11: unsigned uint16, factor 0.01% (little-endian)
                // Byte 14: battery % (uint8)
                if (data.Length >= 15)
                {
                    short pvvxTempRaw = (short)(data[8] | (data[9] << 8));
                    double pvvxTemp = pvvxTempRaw * 0.01;
                    if (pvvxTemp >= -30.0 && pvvxTemp <= 70.0)
                    {
                        reading.TemperatureC = Math.Round(pvvxTemp, 1);
                        ushort pvvxHumRaw = (ushort)(data[10] | (data[11] << 8));
                        double pvvxHum = pvvxHumRaw * 0.01;
                        if (pvvxHum >= 0.0 && pvvxHum <= 100.0)
                        {
                            reading.HumidityPercent = Math.Round(pvvxHum, 1);
                        }
                        if (data[14] <= 100)
                        {
                            reading.BatteryPercent = data[14];
                        }
                        return true;
                    }
                }

                // 2. Standard ATC1441 format (12-15 bytes):
                // Byte 8-9: Temperature (int16, 0.1°C) big-endian or little-endian
                // Byte 10: Humidity (uint8, 1%)
                // Byte 11: Battery % (uint8)
                short tempRaw = (short)(data[8] << 8 | data[9]); // big-endian in ATC format
                double t1 = tempRaw * 0.1;
                short tempRawLe = (short)(data[9] << 8 | data[8]);
                double t2 = tempRawLe * 0.1;

                if (t1 >= -30.0 && t1 <= 70.0)
                {
                    reading.TemperatureC = Math.Round(t1, 1);
                    reading.HumidityPercent = data[10];
                    reading.BatteryPercent = data[11];
                    return true;
                }
                else if (t2 >= -30.0 && t2 <= 70.0)
                {
                    reading.TemperatureC = Math.Round(t2, 1);
                    reading.HumidityPercent = data[10];
                    reading.BatteryPercent = data[11];
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseXiaomiMiBeacon(byte[] data, string mac, string localName, short rssi, out BleThermometerReading reading)
        {
            reading = new BleThermometerReading
            {
                MacAddress = mac,
                Name = !string.IsNullOrWhiteSpace(localName) ? localName : "Xiaomi Sensor",
                Rssi = rssi,
                LastSeenUtc = DateTime.UtcNow
            };

            if (data.Length < 7) return false;

            ushort frameControl = (ushort)(data[2] | (data[3] << 8));
            bool hasMac = (frameControl & 0x0010) != 0;
            bool hasCapability = (frameControl & 0x0020) != 0;
            bool hasEvent = (frameControl & 0x0040) != 0;

            if (!hasEvent) return false;

            int offset = 7;
            if (hasMac) offset += 6;
            if (hasCapability) offset += 1;

            bool found = false;
            while (offset + 3 <= data.Length)
            {
                ushort eventId = (ushort)(data[offset] | (data[offset + 1] << 8));
                byte eventLen = data[offset + 2];
                offset += 3;

                if (offset + eventLen > data.Length) break;

                if (eventId == 0x1004 && eventLen >= 2)
                {
                    short raw = (short)(data[offset] | (data[offset + 1] << 8));
                    reading.TemperatureC = Math.Round(raw * 0.1, 1);
                    found = true;
                }
                else if (eventId == 0x1006 && eventLen >= 2)
                {
                    ushort raw = (ushort)(data[offset] | (data[offset + 1] << 8));
                    reading.HumidityPercent = Math.Round(raw * 0.1, 1);
                }
                else if (eventId == 0x100D && eventLen >= 4)
                {
                    short rawTemp = (short)(data[offset] | (data[offset + 1] << 8));
                    ushort rawHum = (ushort)(data[offset + 2] | (data[offset + 3] << 8));
                    reading.TemperatureC = Math.Round(rawTemp * 0.1, 1);
                    reading.HumidityPercent = Math.Round(rawHum * 0.1, 1);
                    found = true;
                }
                else if (eventId == 0x100A && eventLen >= 1)
                {
                    reading.BatteryPercent = data[offset];
                }

                offset += eventLen;
            }

            return found;
        }

        private void UpdateReading(BleThermometerReading reading)
        {
            _sensors[reading.MacAddress] = reading;

            ReadingUpdated?.Invoke(reading);

            // Sync to DiscoveredSensors list on Dispatcher if present
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.InvokeAsync(() => SyncDiscoveredSensor(reading));
            }
            else
            {
                SyncDiscoveredSensor(reading);
            }
        }

        private void SyncDiscoveredSensor(BleThermometerReading reading)
        {
            var existing = DiscoveredSensors.FirstOrDefault(s => s.MacAddress == reading.MacAddress);
            if (existing == null)
            {
                DiscoveredSensors.Add(reading);
            }
            else
            {
                existing.TemperatureC = reading.TemperatureC;
                existing.HumidityPercent = reading.HumidityPercent;
                existing.BatteryPercent = reading.BatteryPercent;
                if (reading.Rssi > -127 || existing.Rssi <= -127)
                {
                    existing.Rssi = reading.Rssi;
                }
                existing.LastSeenUtc = reading.LastSeenUtc;
                if (!string.IsNullOrWhiteSpace(reading.Name) && existing.Name != reading.Name)
                {
                    existing.Name = reading.Name;
                }
            }
        }

        private static byte[] ReadBuffer(IBuffer buffer)
        {
            var reader = DataReader.FromBuffer(buffer);
            byte[] bytes = new byte[buffer.Length];
            reader.ReadBytes(bytes);
            return bytes;
        }

        private static string FormatMacAddress(ulong address)
        {
            byte[] bytes = BitConverter.GetBytes(address);
            return $"{bytes[5]:X2}:{bytes[4]:X2}:{bytes[3]:X2}:{bytes[2]:X2}:{bytes[1]:X2}:{bytes[0]:X2}";
        }
    }
}
