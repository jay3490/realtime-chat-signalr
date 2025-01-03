using System.Text.RegularExpressions;
using Chat.Domain.Common;

namespace Chat.Domain.Users;

/// <summary>
/// Rules for user names. Names are case-insensitive, so they are stored lower-cased.
/// </summary>
public static partial class UserName
{
    public const int MinLength = 2;
    public const int MaxLength = 32;

    public static bool IsValid(string? value) =>
        value is not null && Pattern().IsMatch(value.Trim());

    public static string Normalize(string? value)
    {
        if (value is null || !IsValid(value))
        {
            throw new DomainRuleException(
                $"User names must be {MinLength}-{MaxLength} characters of letters, digits, '.', '_' or '-'.");
        }

        return value.Trim().ToLowerInvariant();
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{1,31}$")]
    private static partial Regex Pattern();
}
