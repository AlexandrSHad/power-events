#:property TargetFramework=net10.0-windows
#:property BuiltInComInteropSupport=true
#:package System.Diagnostics.EventLog@10.0.1
#:package MQTTnet@5.0.1.1416
#:package Microsoft.Extensions.Hosting.WindowsServices@8.0.0
#:package Hardware.Info@101.1.0.1
#:package LibreHardwareMonitorLib@0.9.6-pre625
#:package System.Management@10.0.2

using System.Diagnostics;
using System.Management;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using LibreHardwareMonitor.Hardware;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(o => o.ServiceName = "PowerEvents")
    .ConfigureServices(services =>
    {
        services.AddSingleton<MqttPublisher>();
        services.AddHostedService<PowerEventsBackgroundService>();
        services.AddHostedService<SystemMetricsBackgroundService>();
    })
    .Build();

await host.RunAsync();

internal sealed class PowerEventsBackgroundService(MqttPublisher mqttPublisher) : BackgroundService
{
    private EventLog? _eventLog;
    private CancellationToken _stoppingToken;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        await mqttPublisher.ConnectAsync(stoppingToken);
        await mqttPublisher.PublishAsync(
            "power-events",
            new PowerEventData { State = "Awake", TimeGenerated = DateTime.Now },
            SourceGenerationContext.Default.PowerEventData,
            stoppingToken);

        _eventLog = new EventLog("System");
        _eventLog.EntryWritten += OnEntryWritten;
        _eventLog.EnableRaisingEvents = true;

        // Keep the service alive until the host requests shutdown; ignore the expected cancellation.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected on shutdown
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        if (_eventLog != null)
        {
            _eventLog.EnableRaisingEvents = false;
            _eventLog.EntryWritten -= OnEntryWritten;
            _eventLog.Dispose();
            _eventLog = null;
        }

        return base.StopAsync(cancellationToken);
    }

    private void OnEntryWritten(object sender, EntryWrittenEventArgs e)
    {
        _ = Task.Run(() => ProcessEntryAsync(e), _stoppingToken);
    }

    private async Task ProcessEntryAsync(EntryWrittenEventArgs e)
    {
        if (_stoppingToken.IsCancellationRequested)
        {
            return;
        }

        if (e.Entry.Source is not "Microsoft-Windows-Kernel-Power")
        {
            return;
        }

        var state = e.Entry.InstanceId switch
        {
            42 => "Standby",
            107 => "Awake",
            506 => "Standby",
            507 => "Awake",
            _ => "Unknown"
        };

        if (state == "Unknown")
        {
            return;
        }

        var powerEventData = new PowerEventData
        {
            State = state,
            TimeGenerated = e.Entry.TimeGenerated
        };

        await mqttPublisher.PublishAsync("power-events", powerEventData, SourceGenerationContext.Default.PowerEventData, _stoppingToken);
    }
}

internal sealed class SystemMetricsBackgroundService(MqttPublisher mqttPublisher, ILogger<SystemMetricsBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hardwareInfo = new Hardware.Info.HardwareInfo();
        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true
        };

        try
        {
            computer.Open();
            var visitor = new UpdateVisitor();

            // One-time diagnostic: log all detected hardware and temperature sensors
            computer.Accept(visitor);
            foreach (var hw in computer.Hardware)
            {
                logger.LogInformation("Detected hardware: {Type} - {Name}", hw.HardwareType, hw.Name);
                foreach (var s in hw.Sensors.Where(s => s.SensorType == SensorType.Temperature))
                    logger.LogInformation("  Sensor: {Name} = {Value}°C", s.Name, s.Value);
                foreach (var sub in hw.SubHardware)
                {
                    logger.LogInformation("  Sub-hardware: {Type} - {Name}", sub.HardwareType, sub.Name);
                    foreach (var s in sub.Sensors.Where(s => s.SensorType == SensorType.Temperature))
                        logger.LogInformation("    Sensor: {Name} = {Value}°C", s.Name, s.Value);
                }
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    hardwareInfo.RefreshCPUList();
                    hardwareInfo.RefreshMemoryStatus();
                    hardwareInfo.RefreshBatteryList();

                    var cpuPercent = hardwareInfo.CpuList.Average(cpu => (double)cpu.PercentProcessorTime);
                    var memoryPercent = Math.Round((hardwareInfo.MemoryStatus.TotalPhysical - hardwareInfo.MemoryStatus.AvailablePhysical)
                        / (double)hardwareInfo.MemoryStatus.TotalPhysical * 100);

                    // Collect temperatures
                    computer.Accept(visitor);

                    double? cpuTemp = null;
                    double? gpuTemp = null;

                    foreach (var hardware in computer.Hardware)
                    {
                        if (hardware.HardwareType == HardwareType.Cpu)
                        {
                            cpuTemp = hardware.FindTemperature("Package", "Tctl", "Tdie", "Core (Tctl/Tdie)");
                        }

                        if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                        {
                            gpuTemp = hardware.FindTemperature("Core");
                        }

                        // Motherboard Super I/O chips often report CPU temperature on AMD systems
                        // where direct CPU SMU access fails
                        if (cpuTemp is null && hardware.HardwareType == HardwareType.Motherboard)
                        {
                            cpuTemp = hardware.FindTemperature("CPU");
                        }
                    }

                    // WMI fallback (returns stale ACPI value on some systems, used as last resort)
                    cpuTemp ??= WmiTemperatureReader.GetCpuTemperature();

                    // Collect battery info
                    double? batteryPercent = null;
                    bool? batteryCharging = null;

                    if (hardwareInfo.BatteryList.Count > 0)
                    {
                        var battery = hardwareInfo.BatteryList[0];
                        batteryPercent = battery.EstimatedChargeRemaining;
                        batteryCharging = battery.BatteryStatusDescription?.Contains("Charging", StringComparison.OrdinalIgnoreCase);
                    }

                    var metricsData = new SystemMetricsData
                    {
                        CpuPercent = cpuPercent,
                        RamPercent = memoryPercent,
                        CpuTempCelsius = cpuTemp.HasValue ? Math.Round(cpuTemp.Value) : null,
                        GpuTempCelsius = gpuTemp.HasValue ? Math.Round(gpuTemp.Value) : null,
                        BatteryPercent = batteryPercent,
                        BatteryCharging = batteryCharging,
                        Timestamp = DateTime.Now
                    };

                    logger.LogInformation(
                        "Collected system metrics: CPU {CpuPercent}%, RAM {RamPercent}%, CPU Temp {CpuTemp}°C, GPU Temp {GpuTemp}°C, Battery {BatteryPercent}% (Charging: {BatteryCharging})",
                        cpuPercent, memoryPercent, cpuTemp, gpuTemp, batteryPercent, batteryCharging);
                    await mqttPublisher.PublishAsync("system-metrics", metricsData, SourceGenerationContext.Default.SystemMetricsData, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error collecting system metrics");
                }

                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // expected on shutdown
        }
        finally
        {
            computer.Close();
        }
    }
}

internal sealed class PowerEventData
{
    public required string State { get; set; }
    public required DateTime TimeGenerated { get; set; }
}

internal sealed class SystemMetricsData
{
    public required double CpuPercent { get; set; }
    public required double RamPercent { get; set; }
    public double? CpuTempCelsius { get; set; }
    public double? GpuTempCelsius { get; set; }
    public double? BatteryPercent { get; set; }
    public bool? BatteryCharging { get; set; }
    public required DateTime Timestamp { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PowerEventData))]
[JsonSerializable(typeof(SystemMetricsData))]
internal partial class SourceGenerationContext : JsonSerializerContext { }

internal static class WmiTemperatureReader
{
    /// <summary>
    /// Reads CPU temperature via WMI MSAcpi_ThermalZoneTemperature.
    /// Returns temperature in Celsius, or null if unavailable.
    /// </summary>
    public static double? GetCpuTemperature()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            foreach (ManagementObject obj in searcher.Get())
            {
                var tempCelsius = (Convert.ToDouble(obj["CurrentTemperature"]) / 10.0) - 273.15;
                if (tempCelsius is > 0 and < 150)
                    return Math.Round(tempCelsius);
            }
        }
        catch
        {
            // WMI thermal zone not available on this system
        }

        return null;
    }
}

internal static class HardwareSensorExtensions
{
    /// <summary>
    /// Searches hardware and all sub-hardware for a valid temperature sensor.
    /// Prefers sensors matching any of <paramref name="preferredNames"/>, falls back to first valid reading.
    /// Ignores readings of 0°C as they indicate unreadable sensors.
    /// </summary>
    public static double? FindTemperature(this IHardware hardware, params string[] preferredNames)
    {
        double? fallback = null;

        foreach (var sensor in AllTemperatureSensors(hardware))
        {
            if (sensor.Value is > 0)
            {
                if (preferredNames.Any(name => sensor.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
                    return sensor.Value;
                fallback ??= sensor.Value;
            }
        }

        return fallback;
    }

    private static IEnumerable<ISensor> AllTemperatureSensors(IHardware hardware)
    {
        foreach (var sensor in hardware.Sensors)
            if (sensor.SensorType == SensorType.Temperature)
                yield return sensor;

        foreach (var sub in hardware.SubHardware)
            foreach (var sensor in sub.Sensors)
                if (sensor.SensorType == SensorType.Temperature)
                    yield return sensor;
    }
}

internal sealed class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer)
    {
        computer.Traverse(this);
    }

    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (var subHardware in hardware.SubHardware)
        {
            subHardware.Accept(this);
        }
    }

    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}

internal sealed class MqttPublisher(ILogger<MqttPublisher> logger)
{
    private IMqttClient? _client;
    
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Connecting to MQTT broker...");

        _client = new MqttClientFactory().CreateMqttClient();

        var host = "rpi.local";
        var port = 1883;

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(host, port)
            .WithClientId("power-events-publisher")
            .WithKeepAlivePeriod(TimeSpan.FromDays(1))
            .Build();

        await _client.ConnectAsync(options, cancellationToken);

        logger.LogInformation("Connected to MQTT broker at {Host}:{Port}", host, port);
    }

    public async Task PublishAsync<T>(string topic, T data, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            logger.LogWarning("MQTT client not initialized, connect to broker first");
            return;
        }

        if (!_client.IsConnected)
        {
            logger.LogWarning("MQTT client has been disconnected");
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(data, jsonTypeInfo);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag()
                .Build();

            await _client.PublishAsync(message, cancellationToken);
            logger.LogInformation("Published to MQTT topic '{Topic}': {Payload}", topic, payload);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed publishing to MQTT");
        }
    }
}
