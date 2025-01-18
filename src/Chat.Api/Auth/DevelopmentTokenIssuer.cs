using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Chat.Api.Auth;

public sealed record IssuedToken(string AccessToken, string UserName, DateTimeOffset ExpiresAt);

/// <summary>
/// Signs tokens for whatever user name it is given. There is no password check: this
/// stands in for a real identity provider while developing, and the endpoint that uses it
/// is only mapped when <see cref="DevTokenOptions.Enabled"/> is set.
/// </summary>
public sealed class DevelopmentTokenIssuer(IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JsonWebTokenHandler _handler = new();

    public IssuedToken Issue(string userName)
    {
        var jwt = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddHours(jwt.TokenLifetimeHours);

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity([new Claim(ChatClaims.UserName, userName)]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(jwt.CreateSecurityKey(), SecurityAlgorithms.HmacSha256),
        });

        return new IssuedToken(token, userName, expires);
    }
}
