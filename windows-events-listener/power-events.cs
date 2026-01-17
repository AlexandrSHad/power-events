#:property TargetFramework=net10.0-windows
#:package System.Diagnostics.EventLog@10.0.1
#:package MQTTnet@5.0.1.1416

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MQTTnet;

// Initialize MQTT client
await MqttPublisher.ConnectAsync();

await MqttPublisher.PublishAsync("power-events", new PowerEventData
{
    State = "EMQX Working",
    TimeGenerated = DateTime.Now
});

Console.WriteLine("Listening for power events in Event Log...");

using (var eventLog = new EventLog("System"))
{
    eventLog.EntryWritten += OnEntryWritten;
    eventLog.EnableRaisingEvents = true;

    Console.WriteLine("Press Enter to exit.");
    Console.ReadLine();
}

static async void OnEntryWritten(object sender, EntryWrittenEventArgs e)
{
    // Filter for Kernel-Power events only
    if (e.Entry.Source is not "Microsoft-Windows-Kernel-Power")
    {
        return;
    }

    Console.WriteLine("");
    Console.WriteLine("----------------------------------------");
    Console.WriteLine($"New event log entry: {e.Entry.InstanceId}");
    Console.WriteLine($"Category: {e.Entry.Category}");
    Console.WriteLine($"Category number: {e.Entry.CategoryNumber}");
    Console.WriteLine($"Entry type: {e.Entry.EntryType}");
    Console.WriteLine($"Source: {e.Entry.Source}");
    Console.WriteLine($"Message: {e.Entry.Message}");
    Console.WriteLine($"Time Generated: {e.Entry.TimeGenerated}");
    Console.WriteLine($"Time Written: {e.Entry.TimeWritten}");
    Console.WriteLine($"Index: {e.Entry.Index}");
    Console.WriteLine($"Machine Name: {e.Entry.MachineName}");
    Console.WriteLine($"User Name: {e.Entry.UserName}");
    if (e.Entry.Data != null && e.Entry.Data.Length > 0)
    {
        Console.WriteLine($"Data: {BitConverter.ToString(e.Entry.Data)}");
    }
    if (e.Entry.ReplacementStrings != null && e.Entry.ReplacementStrings.Length > 0)
    {
        Console.WriteLine("Replacement Strings:");
        foreach (var str in e.Entry.ReplacementStrings)
        {
            Console.WriteLine($"  - {str}");
        }
    }

    // Publish to MQTT broker
    var state = e.Entry.InstanceId switch {
        42 => "Standby", // TODO: this is a Sleep, check it is the same as Modern Standby
        107 => "Awake",
        506 => "Standby",
        507 => "Awake",
        _ => "Unknown"
    };

    // Ignore other events for now
    if (state == "Unknown")
    {
        return;
    }

    var powerEventData = new PowerEventData
    {
        State = state,
        TimeGenerated = e.Entry.TimeGenerated
    };

    await MqttPublisher.PublishAsync("power-events", powerEventData);
}

class PowerEventData
{
    public required string State { get; set; }
    public required DateTime TimeGenerated { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PowerEventData))]
internal partial class SourceGenerationContext : JsonSerializerContext { }

static class MqttPublisher
{
    private static IMqttClient? _client;

    public static async Task ConnectAsync()
    {
        Console.WriteLine("Connecting to MQTT broker...");

        _client = new MqttClientFactory().CreateMqttClient();

        //var host = "localhost";
        var host = "rpi.local";
        var port = 1883;

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(host, port)
            .WithClientId("power-events-publisher")
            .WithKeepAlivePeriod(TimeSpan.FromDays(1))
            .Build();

        await _client.ConnectAsync(options);

        Console.WriteLine($"Connected to MQTT broker at {host}:{port}");
    }

    public static async Task PublishAsync(string topic, PowerEventData data)
    {
        if (_client is null)
        {
            Console.WriteLine("ERROR: MQTT client not initialized, connect to broker first");
            return;
        }

        // TODO: use timer reconnection + message queue for buffering messages while disconnected
        if (!_client.IsConnected)
        {
            Console.WriteLine("ERROR: MQTT client has been disconnected");
            return;
            // Console.WriteLine("Reconnecting to MQTT broker...");
            // await _client.ReconnectAsync();
            // Console.WriteLine("Reconnected to MQTT broker");
        }

        try
        {
            // TODO: use binary serialization for smaller payloads
            // TODO: message persistence
            var payload = JsonSerializer.Serialize(data, SourceGenerationContext.Default.PowerEventData);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag()
                .Build();

            await _client.PublishAsync(message);
            Console.WriteLine($"Published to MQTT topic '{topic}': {payload}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Failed publishing to MQTT. {ex.Message}");
        }
    }
}
