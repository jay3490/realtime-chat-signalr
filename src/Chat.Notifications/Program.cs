using Chat.Infrastructure.EventLog;
using Chat.Infrastructure.Hosting;
using Chat.Infrastructure.Notifications;
using Chat.Infrastructure.Presence;

// Chat.Notifications: reads room events from Kafka and, for every mention of someone who has
// no open connection, sends a notification (a webhook, or the log when none is configured).
// Presence comes from the same Redis the API instances write to. Only HTTP for health
// checks is served.
var builder = WebApplication.CreateBuilder(args);

var kafka = builder.Configuration.GetSection(KafkaOptions.SectionName).Get<KafkaOptions>() ?? new KafkaOptions();
if (!kafka.IsConfigured || string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Redis")))
{
    throw new InvalidOperationException(
        "Chat.Notifications needs Kafka:BootstrapServers and ConnectionStrings:Redis: it reads the event log and the shared presence.");
}

builder.Services.AddChatEventLog(builder.Configuration);
builder.Services.AddChatPresence(builder.Configuration);
builder.Services.AddMentionNotifications(builder.Configuration);
builder.Services.AddGracefulShutdown(builder.Configuration);
builder.Services.AddChatHealthChecks(builder.Configuration);

var app = builder.Build();

app.MapChatHealthEndpoints();

await app.RunAsync();
