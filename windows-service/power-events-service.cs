#:property TargetFramework=net10.0-windows
#:package System.Diagnostics.EventLog@10.0.1
#:package MQTTnet@5.0.1.1416
#:package Microsoft.Extensions.Hosting.WindowsServices@8.0.0

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    })
    .Build();

await host.RunAsync();

internal sealed class PowerEventsBackgroundService(MqttPublisher mqttPublisher) : BackgroundService
{
    private readonly MqttPublisher _mqttPublisher = mqttPublisher;
    private EventLog? _eventLog;
    private CancellationToken _stoppingToken;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        await _mqttPublisher.ConnectAsync(stoppingToken);
        await _mqttPublisher.PublishAsync(
            "power-events",
            new PowerEventData { State = "EMQX Working", TimeGenerated = DateTime.Now },
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

        await _mqttPublisher.PublishAsync("power-events", powerEventData, _stoppingToken);
    }
}

internal sealed class PowerEventData
{
    public required string State { get; set; }
    public required DateTime TimeGenerated { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PowerEventData))]
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

    public async Task PublishAsync(string topic, PowerEventData data, CancellationToken cancellationToken = default)
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
            var payload = JsonSerializer.Serialize(data, SourceGenerationContext.Default.PowerEventData);

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
