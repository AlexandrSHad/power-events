#:property TargetFramework=net10.0-windows
#:property BuiltInComInteropSupport=true
#:package System.Diagnostics.EventLog@10.0.1
#:package MQTTnet@5.0.1.1416
#:package Microsoft.Extensions.Hosting.WindowsServices@8.0.0
#:package Hardware.Info@101.1.0.1

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService()
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
            new PowerEventData { State = "EMQX Working", TimeGenerated = DateTime.Now },
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

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    hardwareInfo.RefreshCPUList();
                    hardwareInfo.RefreshMemoryStatus();

                    var cpuPercent = hardwareInfo.CpuList[0].PercentProcessorTime;
                    var memoryPercent = Math.Round((hardwareInfo.MemoryStatus.TotalPhysical - hardwareInfo.MemoryStatus.AvailablePhysical)
                        / (double)hardwareInfo.MemoryStatus.TotalPhysical * 100);

                    var metricsData = new SystemMetricsData
                    {
                        CpuPercent = cpuPercent,
                        RamPercent = memoryPercent,
                        Timestamp = DateTime.Now
                    };

                    logger.LogInformation("Collected system metrics: CPU {CpuPercent}%, RAM {RamPercent}%", cpuPercent, memoryPercent);
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
    public required DateTime Timestamp { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PowerEventData))]
[JsonSerializable(typeof(SystemMetricsData))]
internal partial class SourceGenerationContext : JsonSerializerContext { }

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
