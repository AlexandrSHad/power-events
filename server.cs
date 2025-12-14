#:sdk Microsoft.NET.Sdk.Web
#:property TreatWarningsAsErrors=true
// #:property JsonSerializerIsReflectionEnabledByDefault=true
// #:property PublishTrimmed=false

using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging(builder => builder.AddConsole());

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolver = SourceGenerationContext.Default;
});

var app = builder.Build();

app.MapGet("/version", () => "v0.1.0");

app.MapPost("/power-events", (ILogger<Program> logger, PowerEventData eventData) =>
{
    logger.LogInformation("[{ReceivedAt}] - Power event received: State={State}, TimeGenerated={TimeGenerated}",
        DateTime.Now, eventData.State, eventData.TimeGenerated);

    return Results.Accepted();
});

await app.RunAsync();

class PowerEventData
{
    public required string State { get; set; }
    public required DateTime TimeGenerated { get; set; }
}

[JsonSerializable(typeof(PowerEventData))]
partial class SourceGenerationContext : JsonSerializerContext { }
