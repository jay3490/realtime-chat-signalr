using System.Text.Json.Serialization;
using Chat.Api.Auth;
using Chat.Api.Endpoints;
using Chat.Api.Errors;
using Chat.Api.Realtime;
using Chat.Application;
using Chat.Application.Realtime;
using Chat.Infrastructure;
using Chat.Infrastructure.EventLog;
using Chat.Infrastructure.Notifications;
using Chat.Infrastructure.ReadModel;
using Chat.Infrastructure.Presence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddChatApplication(typeof(Program).Assembly);
builder.Services.AddChatPersistence(builder.Configuration);
builder.Services.AddChatReadModel(builder.Configuration);
builder.Services.AddChatEventLog(builder.Configuration);
builder.Services.AddOutboxRelay(builder.Configuration);

// With Kafka the Chat.Projections service maintains the read model. With the in-memory log
// only this process can read the events, so it runs the projection itself.
var kafkaConfigured = builder.Configuration.GetSection(KafkaOptions.SectionName).Get<KafkaOptions>()?.IsConfigured ?? false;
if (!kafkaConfigured)
{
    builder.Services.AddReadModelProjection();

    // Mention notifications are normally the Chat.Notifications service's job; a single
    // process can run them itself when asked to.
    if (builder.Configuration.GetValue<bool>($"{NotificationOptions.SectionName}:{nameof(NotificationOptions.RunInProcess)}"))
    {
        builder.Services.AddMentionNotifications(builder.Configuration);
    }
}
builder.Services.AddChatAuthentication(builder.Configuration);
builder.Services.AddChatPresence(builder.Configuration);
builder.Services.AddChatRealtime(builder.Configuration);

builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize =
    builder.Configuration.GetSection(RealtimeLimitsOptions.SectionName).Get<RealtimeLimitsOptions>()?.MaxRequestBodySizeBytes
    ?? new RealtimeLimitsOptions().MaxRequestBodySizeBytes);

builder.Services.ConfigureHttpJsonOptions(json =>
    json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ChatExceptionHandler>();
builder.Services.AddHealthChecks();

var app = builder.Build();

// `dotnet Chat.Api.dll --migrate` applies migrations, creates the Kafka topics and exits.
// docker-compose runs this once before starting the API replicas so they never race each
// other on the schema.
if (args.Contains("--migrate"))
{
    await app.Services.MigrateDatabaseAsync();
    await app.Services.EnsureEventTopicsAsync();
    return;
}

await app.Services.InitializeDatabaseAsync();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapDevTokenEndpoint();
app.MapChatEndpoints();
app.MapHub<ChatHub>(ChatHub.Path);

await app.RunAsync();

/// <summary>Exposed so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program;
