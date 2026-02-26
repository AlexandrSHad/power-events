using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MQTTnet;
using Serilog;
using Serilog.Events;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(o => o.ServiceName = "PowerEvents")
    .UseSerilog(
        (context, services, loggerConfiguration) =>
        {
            var seqEnabled = context.Configuration.GetValue<bool>("Seq:Enabled", true);
            var seqUrl = context.Configuration.GetValue<string>(
                "Seq:ServerUrl",
                "http://localhost:5341"
            );

            loggerConfiguration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Component", "PowerEvents")
                .Enrich.WithProperty("DeviceId", Environment.MachineName);

            if (seqEnabled)
            {
                loggerConfiguration.WriteTo.Seq(
                    seqUrl,
                    restrictedToMinimumLevel: LogEventLevel.Information
                );
            }

            loggerConfiguration.WriteTo.Console();
        }
    )
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
            stoppingToken
        );

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
            _ => "Unknown",
        };

        if (state == "Unknown")
        {
            return;
        }

        var powerEventData = new PowerEventData
        {
            State = state,
            TimeGenerated = e.Entry.TimeGenerated,
        };

        await mqttPublisher.PublishAsync(
            "power-events",
            powerEventData,
            SourceGenerationContext.Default.PowerEventData,
            _stoppingToken
        );
    }
}

internal sealed class SystemMetricsBackgroundService(
    MqttPublisher mqttPublisher,
    ILogger<SystemMetricsBackgroundService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hardwareInfo = new Hardware.Info.HardwareInfo();
        var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };

        AmdAdlTemperature? amdAdl = null;
        try
        {
            amdAdl = AmdAdlTemperature.TryCreate(logger);
        }
        catch
        {
            // AMD ADL not available (no AMD GPU driver installed)
        }

        try
        {
            computer.Open();
            var visitor = new UpdateVisitor();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    hardwareInfo.RefreshCPUList();
                    hardwareInfo.RefreshMemoryStatus();
                    hardwareInfo.RefreshBatteryList();

                    var cpuPercent = hardwareInfo.CpuList.Average(cpu =>
                        (double)cpu.PercentProcessorTime
                    );
                    var memoryPercent = Math.Round(
                        (
                            hardwareInfo.MemoryStatus.TotalPhysical
                            - hardwareInfo.MemoryStatus.AvailablePhysical
                        )
                            / (double)hardwareInfo.MemoryStatus.TotalPhysical
                            * 100
                    );

                    // Collect temperatures
                    computer.Accept(visitor);

                    double? cpuTemp = null;
                    double? gpuTemp = null;

                    foreach (var hardware in computer.Hardware)
                    {
                        if (hardware.HardwareType == HardwareType.Cpu)
                        {
                            cpuTemp = hardware.FindTemperature(
                                "Package",
                                "Tctl",
                                "Tdie",
                                "Core (Tctl/Tdie)"
                            );
                        }

                        if (
                            hardware.HardwareType
                            is HardwareType.GpuNvidia
                                or HardwareType.GpuAmd
                                or HardwareType.GpuIntel
                        )
                        {
                            gpuTemp = hardware.FindTemperature("Core");
                        }
                    }

                    // On AMD APUs, the SMU often fails but the AMD Display Library
                    // reports real-time CPU temperature via ADL_PMLOG_TEMPERATURE_CPU
                    cpuTemp ??= amdAdl?.GetCpuTemperature();

                    // Collect battery info
                    double? batteryPercent = null;
                    bool? batteryCharging = null;

                    if (hardwareInfo.BatteryList.Count > 0)
                    {
                        var battery = hardwareInfo.BatteryList[0];
                        batteryPercent = battery.EstimatedChargeRemaining;
                        batteryCharging = battery.BatteryStatusDescription?.Contains(
                            "Charging",
                            StringComparison.OrdinalIgnoreCase
                        );
                    }

                    var metricsData = new SystemMetricsData
                    {
                        CpuPercent = cpuPercent,
                        RamPercent = memoryPercent,
                        CpuTempCelsius = cpuTemp.HasValue ? Math.Round(cpuTemp.Value) : null,
                        GpuTempCelsius = gpuTemp.HasValue ? Math.Round(gpuTemp.Value) : null,
                        BatteryPercent = batteryPercent,
                        BatteryCharging = batteryCharging,
                        Timestamp = DateTime.Now,
                    };

                    logger.LogInformation(
                        "Collected system metrics: CPU {CpuPercent}%, RAM {RamPercent}%, CPU Temp {CpuTemp}°C, GPU Temp {GpuTemp}°C, Battery {BatteryPercent}% (Charging: {BatteryCharging})",
                        cpuPercent,
                        memoryPercent,
                        cpuTemp,
                        gpuTemp,
                        batteryPercent,
                        batteryCharging
                    );
                    await mqttPublisher.PublishAsync(
                        "system-metrics",
                        metricsData,
                        SourceGenerationContext.Default.SystemMetricsData,
                        stoppingToken
                    );
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
            amdAdl?.Dispose();
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

/// <summary>
/// Reads CPU temperature from AMD Display Library (ADL) PMLOG on AMD APU systems.
/// On Ryzen APUs, LibreHardwareMonitor's SMU access often fails, but the integrated
/// GPU driver exposes real-time CPU temperature via ADL_PMLOG_TEMPERATURE_CPU.
/// </summary>
internal sealed class AmdAdlTemperature : IDisposable
{
    private const int AdlOk = 0;
    private const int AdlPmlogTemperatureCpu = 32; // AI: 31
    private const int AdlPmlogMaxSensors = 256;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr AdlMainMemoryAllocDelegate(int size);

    // Must be stored to prevent GC from collecting it while ADL holds the pointer
    private static readonly AdlMainMemoryAllocDelegate MemoryAllocDelegate = size =>
        Marshal.AllocHGlobal(size);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Main_Control_Create(
        AdlMainMemoryAllocDelegate callback,
        int enumConnectedAdapters,
        out IntPtr context
    );

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Main_Control_Destroy(IntPtr context);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_Adapter_NumberOfAdapters_Get(
        IntPtr context,
        out int numAdapters
    );

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL2_New_QueryPMLogData_Get(
        IntPtr context,
        int adapterIndex,
        out AdlPmLogDataOutput data
    );

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlSingleSensorData
    {
        public int Supported;
        public int Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdlPmLogDataOutput
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = AdlPmlogMaxSensors)]
        public AdlSingleSensorData[] Sensors;
    }

    private readonly IntPtr _context;
    private readonly int _adapterIndex;

    private AmdAdlTemperature(IntPtr context, int adapterIndex)
    {
        _context = context;
        _adapterIndex = adapterIndex;
    }

    public static AmdAdlTemperature? TryCreate(ILogger logger)
    {
        if (ADL2_Main_Control_Create(MemoryAllocDelegate, 1, out var context) != AdlOk)
            return null;

        if (
            ADL2_Adapter_NumberOfAdapters_Get(context, out int numAdapters) != AdlOk
            || numAdapters == 0
        )
        {
            ADL2_Main_Control_Destroy(context);
            return null;
        }

        // Find the first adapter that supports ADL_PMLOG_TEMPERATURE_CPU
        for (int i = 0; i < numAdapters; i++)
        {
            if (
                ADL2_New_QueryPMLogData_Get(context, i, out var data) == AdlOk
                && data.Sensors[AdlPmlogTemperatureCpu].Supported != 0
            )
            {
                logger.LogInformation(
                    "AMD ADL: using adapter {Index} for CPU temperature (PMLOG)",
                    i
                );
                return new AmdAdlTemperature(context, i);
            }
        }

        ADL2_Main_Control_Destroy(context);
        return null;
    }

    public double? GetCpuTemperature()
    {
        if (ADL2_New_QueryPMLogData_Get(_context, _adapterIndex, out var data) != AdlOk)
            return null;

        var sensor = data.Sensors[AdlPmlogTemperatureCpu];
        if (sensor.Supported != 0 && sensor.Value is > 0 and < 150)
            return sensor.Value;

        return null;
    }

    public void Dispose()
    {
        if (_context != IntPtr.Zero)
            ADL2_Main_Control_Destroy(_context);
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
                if (
                    preferredNames.Any(name =>
                        sensor.Name.Contains(name, StringComparison.OrdinalIgnoreCase)
                    )
                )
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

    public async Task PublishAsync<T>(
        string topic,
        T data,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default
    )
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
