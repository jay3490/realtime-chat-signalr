using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chat.Api.Auth;
using Chat.Api.Realtime;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.IntegrationTests;

/// <summary>
/// Hosts the real API in memory against a throwaway SQLite file and the in-memory presence
/// tracker. Each test class instance gets its own factory, so state never leaks between tests.
/// </summary>
public sealed class ChatApiFactory : WebApplicationFactory<Program>
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"chat-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Chat", $"Data Source={_databasePath};Pooling=False");
        builder.UseSetting("ConnectionStrings:Redis", string.Empty);
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Auth:Jwt:SigningKey", "integration-test-signing-key-0123456789abcdef");
        builder.UseSetting("Auth:DevTokens:Enabled", "true");
        builder.UseSetting("Typing:ThrottleMilliseconds", "60000");
        builder.UseSetting("Typing:IndicatorMilliseconds", "5000");
    }

    /// <summary>Signs in through the dev token endpoint and returns an authenticated HTTP client.</summary>
    public async Task<TestUser> SignInAsync(string userName)
    {
        var anonymous = CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/dev/token", new { userName }, Json);
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<IssuedToken>(Json))!;

        var http = CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return new TestUser(token.UserName, token.AccessToken, http, this);
    }

    public HubConnection CreateHubConnection(string? accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, "hubs/chat"), options =>
            {
                // The test server has no sockets, so long polling over its in-memory handler is used.
                options.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult(accessToken);
            })
            .AddJsonProtocol(json => json.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        // Pooling is off, but clear anyway so no handle keeps the file open on Windows.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}

public sealed record TestUser(string Name, string AccessToken, HttpClient Http, ChatApiFactory Factory)
{
    public async Task<T> GetAsync<T>(string path) =>
        (await Http.GetFromJsonAsync<T>(path, ChatApiFactory.Json))!;

    public async Task<T> PostAsync<T>(string path, object body)
    {
        var response = await Http.PostAsJsonAsync(path, body, ChatApiFactory.Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(ChatApiFactory.Json))!;
    }

    public async Task<HubConnection> ConnectAsync()
    {
        var connection = Factory.CreateHubConnection(AccessToken);
        await connection.StartAsync();

        // StartAsync returns after the handshake, which the server sends before it runs
        // OnConnectedAsync. Invocations are only dispatched once OnConnectedAsync has
        // finished, so a round trip guarantees the connection has joined its room groups.
        await connection.InvokeAsync<ConnectionInfoDto>("GetConnectionInfo");
        return connection;
    }
}
