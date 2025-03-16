using Chat.Infrastructure;
using Chat.Infrastructure.EventLog;
using Chat.Infrastructure.Hosting;
using Chat.Infrastructure.Observability;
using Chat.Infrastructure.ReadModel;

// Chat.Projections: reads every event stream from Kafka and keeps the read model that the
// API's queries use up to date. Only HTTP for health checks is served.
//
//   dotnet Chat.Projections.dll             project new events, resuming from the checkpoints
//   dotnet Chat.Projections.dll --rebuild   empty the read model, then replay the topics from
//                                           the first offset and keep running
var builder = WebApplication.CreateBuilder(args);

var kafka = builder.Configuration.GetSection(KafkaOptions.SectionName).Get<KafkaOptions>() ?? new KafkaOptions();
if (!kafka.IsConfigured)
{
    throw new InvalidOperationException(
        "Kafka:BootstrapServers is not configured. Without Kafka the API runs the projection itself.");
}

builder.Services.AddChatReadModel(builder.Configuration);
builder.Services.AddChatEventLog(builder.Configuration);
builder.Services.AddReadModelProjection();
builder.Services.AddChatTelemetry("chat-projections");
builder.Services.AddGracefulShutdown(builder.Configuration);
builder.Services.AddChatHealthChecks(builder.Configuration);

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

if (args.Contains("--rebuild"))
{
    await app.Services.ResetReadModelAsync(app.Lifetime.ApplicationStopping);
}

app.MapChatHealthEndpoints();
app.MapChatMetrics();

await app.RunAsync();
