namespace InFox.Models.Sensors
{
    public enum HardwareCategory
    {
        Cpu,
        Gpu,
        Memory,
        Storage,
        Network,
        Battery,
        Motherboard
    }

    public enum MetricType
    {
        Temperature, // °C
        Load,        // %
        Power,       // W
        Clock,       // MHz or GHz
        Voltage,     // V
        Fan,         // RPM or %
        Throughput,  // MB/s or Mbps
        Capacity,    // GB
        Level        // %
    }
}
