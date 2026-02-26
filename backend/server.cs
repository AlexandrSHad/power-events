#:sdk Microsoft.NET.Sdk.Web
#:property TreatWarningsAsErrors=true
#:package MQTTnet@5.0.1.1416
#:package System.Reactive@6.0.1
#:package System.Linq.Async@6.0.1
#:package Serilog.AspNetCore@9.0.0
#:package Serilog.Sinks.Seq@9.0.0
// #:property JsonSerializerIsReflectionEnabledByDefault=true
// #:property PublishTrimmed=false

using System.Net.ServerSentEvents;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MQTTnet;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
var seqEnabled = builder.Configuration.GetValue<bool>("Seq:Enabled", true);
var seqUrl = builder.Configuration.GetValue<string>("Seq:ServerUrl", "http://localhost:5341");

var loggerCfg = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Component", "Backend")
    .MinimumLevel.Information()
    .WriteTo.Console();

if (seqEnabled)
{
    loggerCfg.WriteTo.Seq(seqUrl, restrictedToMinimumLevel: LogEventLevel.Information);
}

Log.Logger = loggerCfg.CreateLogger();

builder.Host.UseSerilog(Log.Logger, dispose: true);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolver = SourceGenerationContext.Default;
});

// Service to broadcast power events to all SSE clients
builder.Services.AddSingleton<PowerEventService>();

// MQTT subscriber background service
builder.Services.AddHostedService<MqttSubscriberService>();

// CORS for Vue dev server
builder.Services.AddCors();

var app = builder.Build();

app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());

// TODO: Add health checks in addition to version endpoint
app.MapGet("/version", () => "v0.1.0");

app.MapPost(
    "/power-events",
    (ILogger<Program> logger, PowerEventData eventData) =>
    {
        logger.LogInformation(
            "[{ReceivedAt}] - Power event received: State={State}, TimeGenerated={TimeGenerated}",
            DateTime.Now,
            eventData.State,
            eventData.TimeGenerated
        );

        return Results.Accepted();
    }
);

// SSE endpoint for real-time power events
app.MapGet(
    "/events",
    (ILogger<Program> logger, PowerEventService powerEventService, CancellationToken ct) =>
    {
        async IAsyncEnumerable<SseItem<PowerEventData>> StreamEvents(
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            logger.LogInformation("[{ReceivedAt}] - Starting SSE stream for client", DateTime.Now);

            // Send initial event to establish connection
            yield return new SseItem<PowerEventData>(
                data: null!,
                eventType: "connection-established"
            );

            await foreach (
                var evt in powerEventService
                    .Subscribe()
                    .ToAsyncEnumerable()
                    .WithCancellation(cancellationToken)
            )
            {
                logger.LogInformation(
                    "[{ReceivedAt}] - Pushed message to SSE stream: State={State}, TimeGenerated={TimeGenerated}",
                    DateTime.Now,
                    evt.State,
                    evt.TimeGenerated
                );
                yield return new SseItem<PowerEventData>(data: evt, eventType: "power-event");
            }
        }

        return TypedResults.ServerSentEvents(StreamEvents(ct));
    }
);

// app.Urls.Clear();
// app.Urls.Add("http://localhost:5550");

await app.RunAsync();

class PowerEventData
{
    public required string State { get; set; }
    public required DateTime TimeGenerated { get; set; }
}

[JsonSerializable(typeof(PowerEventData))]
partial class SourceGenerationContext : JsonSerializerContext { }

class PowerEventService
{
    private readonly Subject<PowerEventData> _subject = new();

    public void Publish(PowerEventData data) => _subject.OnNext(data);

    public IObservable<PowerEventData> Subscribe() => _subject.AsObservable();
}

class MqttSubscriberService(
    PowerEventService powerEventService,
    ILogger<MqttSubscriberService> logger,
    IConfiguration configuration
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mqttHost = configuration["Mqtt:Host"] ?? "localhost";
        var mqttPort = int.Parse(configuration["Mqtt:Port"] ?? "1883");

        var mqttClient = new MqttClientFactory().CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(mqttHost, mqttPort)
            .WithClientId("server-sse-subscriber")
            .Build();

        mqttClient.ApplicationMessageReceivedAsync += e =>
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = e.ApplicationMessage.ConvertPayloadToString();

            // Handle power events
            if (topic == "power-events")
            {
                logger.LogInformation(
                    "{ReceivedAt} - MQTT message received: {Payload}",
                    DateTime.Now,
                    payload
                );

                var eventData = JsonSerializer.Deserialize(
                    payload,
                    SourceGenerationContext.Default.PowerEventData
                );
                if (eventData is not null)
                {
                    powerEventService.Publish(eventData);
                }
            }
            // Handle ESP32 logs
            else if (topic.StartsWith("logs/esp32/"))
            {
                HandleEsp32Log(topic, payload);
            }

            return Task.CompletedTask;
        };

        await mqttClient.ConnectAsync(options, stoppingToken);
        logger.LogInformation("Connected to MQTT broker at {Host}:{Port}", mqttHost, mqttPort);

        var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter("power-events")
            .WithTopicFilter("logs/esp32/#")
            .Build();

        await mqttClient.SubscribeAsync(subscribeOptions, stoppingToken);
        logger.LogInformation("Subscribed to 'power-events' and 'logs/esp32/#' topics");

        // Keep the service running
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private void HandleEsp32Log(string topic, string payload)
    {
        // Topic format: logs/esp32/<device-id>/<level>
        var parts = topic.Split('/');
        if (parts.Length < 5)
            return;

        var deviceId = parts[3];
        var levelStr = parts[4];

        // Map MQTT log level to Serilog level
        var logLevel = levelStr.ToLower() switch
        {
            "error" => LogEventLevel.Error,
            "warning" => LogEventLevel.Warning,
            "warn" => LogEventLevel.Warning,
            "info" => LogEventLevel.Information,
            "information" => LogEventLevel.Information,
            "debug" => LogEventLevel.Debug,
            _ => LogEventLevel.Information,
        };

        // Create context properties for ESP32 logs
        var esp32Logger = Log.ForContext("Component", "ESP32")
            .ForContext("DeviceId", deviceId)
            .ForContext("LogLevel", levelStr.ToUpper());

        // Write to Seq
        esp32Logger.Write(logLevel, "{Message}", payload);
    }
}
