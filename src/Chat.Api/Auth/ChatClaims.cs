using System.Security.Claims;

namespace Chat.Api.Auth;

public static class ChatClaims
{
    /// <summary>The user name travels in the standard subject claim.</summary>
    public const string UserName = "sub";

    public static string GetUserName(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(UserName)
        ?? throw new InvalidOperationException("The authenticated principal has no user name claim.");
}
