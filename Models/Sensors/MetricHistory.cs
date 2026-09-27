using System;
using System.Collections.Generic;

namespace InFox.Models.Sensors
{
    /// <summary>
    /// Fixed-capacity circular ring buffer for zero-allocation telemetry history tracking.
    /// Ideal for sparklines and rolling trend charts.
    /// </summary>
    public class MetricHistory
    {
        private readonly double[] _buffer;
        private readonly object _syncLock = new();
        private int _head = 0;
        private int _count = 0;

        public int Capacity { get; }

        public MetricHistory(int capacity = 60)
        {
            Capacity = capacity > 0 ? capacity : 60;
            _buffer = new double[Capacity];
        }

        public void Add(double value)
        {
            lock (_syncLock)
            {
                _buffer[_head] = value;
                _head = (_head + 1) % Capacity;
                if (_count < Capacity)
                {
                    _count++;
                }
            }
        }

        public double[] ToArray()
        {
            lock (_syncLock)
            {
                if (_count == 0) return Array.Empty<double>();

                var result = new double[_count];
                int start = (_head - _count + Capacity) % Capacity;

                for (int i = 0; i < _count; i++)
                {
                    result[i] = _buffer[(start + i) % Capacity];
                }

                return result;
            }
        }

        public double? Latest
        {
            get
            {
                lock (_syncLock)
                {
                    if (_count == 0) return null;
                    int lastIndex = (_head - 1 + Capacity) % Capacity;
                    return _buffer[lastIndex];
                }
            }
        }
    }
}
