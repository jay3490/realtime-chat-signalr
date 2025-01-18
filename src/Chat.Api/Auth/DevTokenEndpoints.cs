using Chat.Application.Abstractions;
using Chat.Application.Users;
using Microsoft.Extensions.Options;

namespace Chat.Api.Auth;

internal static partial class DevTokenEndpoints
{
    public sealed record TokenRequest(string UserName);

    /// <summary>Maps the password-less token endpoint when it has been explicitly enabled.</summary>
    public static IEndpointRouteBuilder MapDevTokenEndpoint(this IEndpointRouteBuilder app)
    {
        var options = app.ServiceProvider.GetRequiredService<IOptions<DevTokenOptions>>().Value;
        if (!options.Enabled)
        {
            return app;
        }

        var logger = app.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DevTokenEndpoints));
        LogEnabled(logger);

        app.MapPost("/api/dev/token", async (TokenRequest request, IDispatcher dispatcher, DevelopmentTokenIssuer issuer, CancellationToken ct) =>
            {
                var userName = await dispatcher.SendAsync(new SignInCommand(request.UserName), ct);
                return Results.Ok(issuer.Issue(userName));
            })
            .AllowAnonymous()
            .WithTags("Development");

        return app;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Development token endpoint is enabled: anyone can obtain a token for any user name. Do not enable it on a public deployment.")]
    private static partial void LogEnabled(ILogger logger);
}
