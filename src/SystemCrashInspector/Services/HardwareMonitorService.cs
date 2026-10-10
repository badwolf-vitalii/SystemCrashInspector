using LibreHardwareMonitor.Hardware;

namespace SystemCrashInspector.Services;

public sealed record HardwareReading(string Device, string Category, string Sensor, string Value, string Unit, float? NumericValue);

public sealed class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsStorageEnabled = true,
        IsMotherboardEnabled = true,
        IsControllerEnabled = true,
        IsNetworkEnabled = true
    };
    private readonly object _sync = new();
    private bool _opened;

    public IReadOnlyList<HardwareReading> Read()
    {
        lock (_sync)
        {
            if (!_opened)
            {
                _computer.Open();
                _opened = true;
            }

            var readings = new List<HardwareReading>();
            foreach (var hardware in _computer.Hardware)
                ReadHardware(hardware, readings);
            return readings;
        }
    }

    private static void ReadHardware(IHardware hardware, List<HardwareReading> readings)
    {
        hardware.Update();
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value is not float value ||
                sensor.SensorType is not (SensorType.Temperature or SensorType.Load or
                    SensorType.Clock or SensorType.Power or SensorType.Fan or
                    SensorType.Data or SensorType.Throughput or SensorType.Voltage))
                continue;

            var unit = sensor.SensorType switch
            {
                SensorType.Temperature => "°C",
                SensorType.Load => "%",
                SensorType.Clock => "MHz",
                SensorType.Power => "W",
                SensorType.Fan => "RPM",
                SensorType.Data => "GB",
                SensorType.Throughput => "B/s",
                SensorType.Voltage => "V",
                _ => ""
            };
            readings.Add(new HardwareReading(hardware.Name, hardware.HardwareType.ToString(),
                sensor.Name, value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                unit, value));
        }
        foreach (var subHardware in hardware.SubHardware)
            ReadHardware(subHardware, readings);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_opened)
            {
                _computer.Close();
                _opened = false;
            }
        }
    }
}
