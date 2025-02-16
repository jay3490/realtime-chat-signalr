using System.Text.RegularExpressions;
using Chat.Domain.Users;

namespace Chat.Domain.Messages;

/// <summary>
/// Finds <c>@name</c> mentions in a message body. The client highlights mentions with the
/// same pattern, so both sides agree on what counts as one.
/// </summary>
public static partial class Mentions
{
    /// <summary>
    /// Distinct, lower-cased user names written as <c>@name</c>. A trailing full stop is not
    /// part of the name, so "thanks @bob." mentions <c>bob</c>. Whether the names belong to
    /// anyone is for the caller to decide.
    /// </summary>
    public static IReadOnlyList<string> Find(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return [];
        }

        return Pattern().Matches(body)
            .Select(match => match.Groups["name"].Value.TrimEnd('.'))
            .Where(UserName.IsValid)
            .Select(name => name.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    // Not preceded by a name character, so e-mail addresses are not mentions.
    [GeneratedRegex(@"(?<![A-Za-z0-9._@-])@(?<name>[A-Za-z0-9][A-Za-z0-9._-]{1,31})")]
    private static partial Regex Pattern();
}
