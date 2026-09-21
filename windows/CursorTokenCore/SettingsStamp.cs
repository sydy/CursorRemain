using System.Globalization;

namespace CursorTokenCore;

/// <summary>
/// Shared parse/format for the settings start-time field. Matches macOS
/// <c>SettingsDateTime</c>: typed text first, calendar popover second.
/// </summary>
public static class SettingsStamp
{
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm";
    public const string DateFormat = "yyyy-MM-dd";

    static readonly string[] Formats =
    [
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy/M/d H:m",
        "yyyy-MM-dd",
    ];

    public static string Format(DateTime value, bool includeTime) =>
        value.ToString(includeTime ? DateTimeFormat : DateFormat, CultureInfo.InvariantCulture);

    public static DateTime? Parse(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length == 0) return null;
        foreach (var format in Formats)
        {
            if (DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date;
        }
        return null;
    }
}
