using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace InFox.Models.Sensors
{
    public class SensorMetric : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private double _value;
        private double _min;
        private double _max;
        private DateTime _updatedAt = DateTime.UtcNow;

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("hardwareId")]
        public string HardwareId { get; set; } = string.Empty;

        [JsonPropertyName("hardwareName")]
        public string HardwareName { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public HardwareCategory Category { get; set; }

        [JsonPropertyName("type")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MetricType Type { get; set; }

        [JsonPropertyName("value")]
        public double Value
        {
            get => _value;
            set
            {
                if (Math.Abs(_value - value) > 0.0001)
                {
                    _value = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Formatted));
                }
            }
        }

        [JsonPropertyName("min")]
        public double Min
        {
            get => _min;
            set
            {
                if (Math.Abs(_min - value) > 0.0001)
                {
                    _min = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FormattedMin));
                }
            }
        }

        [JsonPropertyName("max")]
        public double Max
        {
            get => _max;
            set
            {
                if (Math.Abs(_max - value) > 0.0001)
                {
                    _max = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FormattedMax));
                }
            }
        }

        [JsonPropertyName("unit")]
        public string Unit { get; set; } = string.Empty;

        [JsonPropertyName("formatted")]
        public string Formatted => FormatValue(Value, Type, Unit);

        [JsonPropertyName("formattedMin")]
        public string FormattedMin => FormatValue(Min, Type, Unit);

        [JsonPropertyName("formattedMax")]
        public string FormattedMax => FormatValue(Max, Type, Unit);

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set
            {
                _updatedAt = value;
                OnPropertyChanged();
            }
        }

        public void UpdateFrom(SensorMetric other)
        {
            Value = other.Value;
            Min = other.Min;
            Max = other.Max;
            UpdatedAt = other.UpdatedAt;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public static string FormatValue(double val, MetricType type, string unit)
        {
            return type switch
            {
                MetricType.Temperature => $"{val:F1} {unit}",
                MetricType.Load => $"{val:F1} %",
                MetricType.Power => $"{val:F1} W",
                MetricType.Clock => val >= 1000 ? $"{val / 1000.0:F2} GHz" : $"{val:F0} MHz",
                MetricType.Voltage => $"{val:F3} V",
                MetricType.Fan => $"{val:F0} RPM",
                MetricType.Throughput => $"{val:F2} {unit}",
                MetricType.Capacity => $"{val:F1} GB",
                MetricType.Level => $"{val:F0} %",
                _ => $"{val:F1} {unit}"
            };
        }
    }
}
