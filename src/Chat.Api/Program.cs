using System.Text.Json.Serialization;
using Chat.Api.Auth;
using Chat.Api.Endpoints;
using Chat.Api.Errors;
using Chat.Application;
using Chat.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddChatApplication(typeof(Program).Assembly);
builder.Services.AddChatPersistence(builder.Configuration);
builder.Services.AddChatAuthentication(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(json =>
    json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ChatExceptionHandler>();
builder.Services.AddHealthChecks();

var app = builder.Build();

// `dotnet Chat.Api.dll --migrate` applies migrations and exits. docker-compose runs this
// once before starting the API replicas so they never race each other on the schema.
if (args.Contains("--migrate"))
{
    await app.Services.MigrateDatabaseAsync();
    return;
}

await app.Services.InitializeDatabaseAsync();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapDevTokenEndpoint();
app.MapChatEndpoints();

await app.RunAsync();

/// <summary>Exposed so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program;
