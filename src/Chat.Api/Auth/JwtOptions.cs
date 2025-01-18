using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Chat.Api.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Auth:Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 key. At least 32 bytes; supply it from a secret store outside development.</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 24 * 30)]
    public int TokenLifetimeHours { get; set; } = 12;

    public SymmetricSecurityKey CreateSecurityKey() => new(Encoding.UTF8.GetBytes(SigningKey));
}

public sealed class DevTokenOptions
{
    public const string SectionName = "Auth:DevTokens";

    /// <summary>
    /// Exposes <c>POST /api/dev/token</c>, which signs a token for any user name without a
    /// password. Meant for local development and demos only.
    /// </summary>
    public bool Enabled { get; set; }
}
