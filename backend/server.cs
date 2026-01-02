#:sdk Microsoft.NET.Sdk.Web
#:property TreatWarningsAsErrors=true
#:package MQTTnet@5.0.1.1416
// #:property JsonSerializerIsReflectionEnabledByDefault=true
// #:property PublishTrimmed=false

using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using MQTTnet;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging(builder => builder.AddConsole());

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolver = SourceGenerationContext.Default;
});

// Channel to bridge MQTT messages to SSE clients
var powerEventChannel = Channel.CreateUnbounded<PowerEventData>();
builder.Services.AddSingleton(powerEventChannel);

// MQTT subscriber background service
builder.Services.AddHostedService<MqttSubscriberService>();

// CORS for Vue dev server
builder.Services.AddCors();

var app = builder.Build();

app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod());

app.MapGet("/version", () => "v0.1.0");

app.MapPost("/power-events", (ILogger<Program> logger, PowerEventData eventData) =>
{
    logger.LogInformation("[{ReceivedAt}] - Power event received: State={State}, TimeGenerated={TimeGenerated}",
        DateTime.Now, eventData.State, eventData.TimeGenerated);

    return Results.Accepted();
});

// SSE endpoint for real-time power events
app.MapGet("/events", (ILogger<Program> logger, Channel<PowerEventData> channel, CancellationToken ct) =>
{
    async IAsyncEnumerable<SseItem<PowerEventData>> StreamEvents(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        logger.LogInformation("[{ReceivedAt}] - Starting SSE stream for client", DateTime.Now);

        // Send initial event to establish connection
        yield return new SseItem<PowerEventData>
        (
            data: null!,
            eventType: "connection-established"
        );

        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
        {
            // Send regular updates
            logger.LogInformation("[{ReceivedAt}] - Pushed message to SSE stream: State={State}, TimeGenerated={TimeGenerated}", DateTime.Now, evt.State, evt.TimeGenerated);
            yield return new SseItem<PowerEventData>
            (
                data: evt,
                eventType: "power-event"
            );
        }
    }

    // Uncomment for testing without MQTT
    // async IAsyncEnumerable<PowerEventData> StreamEvents(
    //     [EnumeratorCancellation] CancellationToken cancellationToken)
    // {
    //     while (true)
    //     {
    //         yield return new PowerEventData
    //         {
    //             State = "Awake",
    //             TimeGenerated = DateTime.Now
    //         };
    //         Task.Delay(1000, cancellationToken).Wait(cancellationToken);
    //     }
    // }

    return TypedResults.ServerSentEvents(StreamEvents(ct));
});

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

class MqttSubscriberService(
    Channel<PowerEventData> channel,
    ILogger<MqttSubscriberService> logger,
    IConfiguration configuration
) : BackgroundService
{
    private readonly Channel<PowerEventData> _channel = channel;
    private readonly ILogger<MqttSubscriberService> _logger = logger;
    private readonly IConfiguration _configuration = configuration;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mqttHost = _configuration["Mqtt:Host"] ?? "localhost";
        var mqttPort = int.Parse(_configuration["Mqtt:Port"] ?? "1883");

        var mqttClient = new MqttClientFactory().CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(mqttHost, mqttPort)
            .WithClientId("server-sse-subscriber")
            .Build();

        mqttClient.ApplicationMessageReceivedAsync += async e =>
        {
            var payload = e.ApplicationMessage.ConvertPayloadToString();
            _logger.LogInformation("{ReceivedAt} - MQTT message received: {Payload}", DateTime.Now, payload);

            var eventData = JsonSerializer.Deserialize(payload, SourceGenerationContext.Default.PowerEventData);
            if (eventData is not null)
            {
                await _channel.Writer.WriteAsync(eventData, stoppingToken);
            }
        };

        await mqttClient.ConnectAsync(options, stoppingToken);
        _logger.LogInformation("Connected to MQTT broker at {Host}:{Port}", mqttHost, mqttPort);

        var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter("power-events")
            .Build();

        await mqttClient.SubscribeAsync(subscribeOptions, stoppingToken);
        _logger.LogInformation("Subscribed to 'power-events' topic");

        // Keep the service running
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}
