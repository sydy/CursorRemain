using System.Globalization;
using System.Text;

namespace CursorTokenCore;

public static partial class UsageEvents
{
    public static string ChartModelLabel(string? name) =>
        RustCore.String("chart_model_label", new { input = name ?? "" });

    public static UsageChartSeries BuildChart(
        IEnumerable<UsageEvent> events,
        bool hourly,
        IEnumerable<string>? hiddenModels = null,
        int hourlyWindowHours = HourlyChartWindowHours)
    {
        var result = RustCore.Call("build_usage_chart", new
        {
            events = events.Select(EventJson).ToList(),
            hourly,
            hidden_models = (hiddenModels ?? []).ToList(),
        });
        return new UsageChartSeries
        {
            Hourly = result.GetProperty("hourly").GetBoolean(),
            Caption = result.GetProperty("caption").GetString() ?? "",
            Models = result.GetProperty("models").EnumerateArray().Select(x => x.GetString() ?? "").ToList(),
            Buckets = result.GetProperty("buckets").EnumerateArray().Select(row => new ChartBucket(
                row.GetProperty("key").GetString() ?? "",
                row.GetProperty("label").GetString() ?? "",
                row.GetProperty("tokens").GetInt64(),
                row.GetProperty("cents").GetDouble(),
                row.GetProperty("count").GetInt32(),
                row.GetProperty("slices").EnumerateArray().Select(slice => new ChartSlice(
                    slice.GetProperty("model").GetString() ?? "",
                    slice.GetProperty("tokens").GetInt64(),
                    slice.GetProperty("cents").GetDouble(),
                    slice.GetProperty("count").GetInt32())).ToList())).ToList(),
        };
    }

    static DateTimeOffset ToDisplay(long timestampMs)
    {
        var utc = DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, timestampMs));
        return TimeZoneInfo.ConvertTime(utc, DisplayTimeZone);
    }

    static long FloorHourMs(long timestampMs) => Math.Max(0, timestampMs) / MsHour * MsHour;
    static long FloorDayMs(long timestampMs)
    {
        var local = ToDisplay(timestampMs);
        var midnight = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, local.Offset);
        return Math.Max(0, midnight.ToUnixTimeMilliseconds());
    }

    public static List<string> ChartModels(IEnumerable<UsageEvent> events)
    {
        var totals = new Dictionary<string, (long tokens, double cents, int count)>(StringComparer.Ordinal);
        foreach (var ev in events)
        {
            var name = string.IsNullOrEmpty(ev.Model) ? "—" : ev.Model;
            totals.TryGetValue(name, out var row);
            totals[name] = (row.tokens + ev.Tokens, row.cents + CostCents(ev), row.count + 1);
        }
        return totals
            .OrderByDescending(kv => kv.Value.tokens)
            .ThenByDescending(kv => kv.Value.cents)
            .ThenByDescending(kv => kv.Value.count)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key)
            .ToList();
    }

    static string BucketLabel(string key, bool hourly, bool multiDay)
    {
        if (!hourly) return key.Length >= 10 ? key[5..] : key;
        var hour = key.Length >= 13 ? key[11..13] : key;
        return multiDay ? $"{key[5..10]} {hour}" : hour;
    }

    static string ChartCaption(bool hourly, IReadOnlyList<string> keys)
    {
        var kind = hourly ? "按小时 Token" : "按日 Token";
        if (keys.Count == 0) return $"{kind}（{TzLabel}）";
        var first = keys[0];
        var last = keys[^1];
        if (first == last) return $"{kind}（{TzLabel} · {first}）";
        if (hourly && first[..10] == last[..10])
            return $"{kind}（{TzLabel} · {first[..10]} {first[11..]}–{last[11..]}）";
        return $"{kind}（{TzLabel} · {first} 至 {last}）";
    }
}
