using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using InFox.Models.Sensors;

namespace InFox.Engine
{
    public interface ISensorHarvester : IDisposable
    {
        string Name { get; }
        bool IsAvailable { get; }

        Task InitializeAsync();
        void Poll(SystemSummary summary, List<SensorMetric> metrics, Action<string, double> recordHistory);
    }
}
