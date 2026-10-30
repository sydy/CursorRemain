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

    public static string Format(DateTime value, bool includeTime) =>
        value.ToString(includeTime ? DateTimeFormat : DateFormat, CultureInfo.InvariantCulture);

    public static DateTime? Parse(string? raw)
    {
        var res = RustCore.Call("parse_settings_stamp", new { raw = raw ?? "" });
        if (res.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
        return new DateTime(
            res.GetProperty("year").GetInt32(),
            res.GetProperty("month").GetInt32(),
            res.GetProperty("day").GetInt32(),
            res.GetProperty("hour").GetInt32(),
            res.GetProperty("minute").GetInt32(),
            res.GetProperty("second").GetInt32()
        );
    }
}
