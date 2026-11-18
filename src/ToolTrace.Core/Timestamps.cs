using System.Globalization;
using System.Text.RegularExpressions;

namespace ToolTrace.Core;

/// <summary>
/// Strict ISO 8601 timestamps. A timestamp without a UTC offset is rejected rather than
/// guessed: "06:00" from a tool in Dresden and "06:00" from a server in UTC are an hour apart,
/// and silently picking one corrupts every duration computed from it.
/// </summary>
public static partial class Timestamps
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd'T'HH:mmK",
    ];

    [GeneratedRegex(@"(Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetSuffix();

    public static bool TryParse(string? text, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text) || !OffsetSuffix().IsMatch(text))
        {
            return false;
        }

        return DateTimeOffset.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }
}
