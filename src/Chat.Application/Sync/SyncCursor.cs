using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Chat.Application.Sync;

/// <summary>
/// A device's position in the read model's change feed. Clients treat it as an opaque
/// string. It carries the read model's epoch so a cursor handed out before a rebuild, whose
/// positions no longer mean anything, is recognized and refused.
/// </summary>
public readonly record struct SyncCursor(Guid Epoch, long Position)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Epoch:N}.{Position}");

    public static bool TryParse(string? value, [NotNullWhen(true)] out SyncCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var dot = value.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0
            || !Guid.TryParseExact(value.AsSpan(0, dot), "N", out var epoch)
            || !long.TryParse(value.AsSpan(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var position))
        {
            return false;
        }

        cursor = new SyncCursor(epoch, position);
        return true;
    }
}
