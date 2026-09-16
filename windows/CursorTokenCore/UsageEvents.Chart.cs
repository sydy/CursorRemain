using System.Globalization;
using System.Text;

namespace CursorTokenCore;

public static partial class UsageEvents
{
    public static string ChartModelLabel(string? name)
    {
        var text = (name ?? "").Trim();
        return text.StartsWith("cursor-", StringComparison.Ordinal) ? text[7..] : text;
    }

    public static UsageChartSeries BuildChart(
        IEnumerable<UsageEvent> events,
        bool hourly,
        IEnumerable<string>? hiddenModels = null,
        int hourlyWindowHours = HourlyChartWindowHours)
    {
        var selected = events as IList<UsageEvent> ?? events.ToList();
        var models = ChartModels(selected);
        var hidden = hiddenModels is null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(hiddenModels, StringComparer.Ordinal);
        var visible = models.Where(n => !hidden.Contains(n)).ToList();
        var visibleSet = visible.ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0)
            return new UsageChartSeries { Hourly = hourly, Caption = ChartCaption(hourly, Array.Empty<string>()), Models = models, Buckets = [] };

        List<string> keys;
        Func<long, string> keyOf;
        if (hourly)
        {
            var lastMs = FloorHourMs(selected.Max(ev => ev.TimestampMs));
            var firstMs = FloorHourMs(selected.Min(ev => ev.TimestampMs));
            var window = Math.Max(1, hourlyWindowHours);
            var span = (lastMs - firstMs) / MsHour + 1;
            if (span > window) firstMs = lastMs - (window - 1) * MsHour;
            var count = (int)((lastMs - firstMs) / MsHour + 1);
            keys = Enumerable.Range(0, count).Select(i => EventHour(firstMs + i * MsHour)).ToList();
            keyOf = EventHour;
        }
        else
        {
            var lastMs = FloorDayMs(selected.Max(ev => ev.TimestampMs));
            var firstMs = FloorDayMs(selected.Min(ev => ev.TimestampMs));
            keys = [];
            for (var day = ToDisplay(firstMs).Date; day <= ToDisplay(lastMs).Date; day = day.AddDays(1))
                keys.Add(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            keyOf = EventDate;
        }

        var cells = new Dictionary<(string key, string model), (long tokens, double cents, int count)>();
        foreach (var ev in selected)
        {
            var key = keyOf(ev.TimestampMs);
            if (string.CompareOrdinal(key, keys[0]) < 0 || string.CompareOrdinal(key, keys[^1]) > 0) continue;
            var name = string.IsNullOrEmpty(ev.Model) ? "—" : ev.Model;
            if (!visibleSet.Contains(name)) continue;
            cells.TryGetValue((key, name), out var cell);
            cells[(key, name)] = (cell.tokens + ev.Tokens, cell.cents + CostCents(ev), cell.count + 1);
        }

        var multiDay = hourly && keys[0][..10] != keys[^1][..10];
        var buckets = new List<ChartBucket>(keys.Count);
        foreach (var key in keys)
        {
            var slices = new List<ChartSlice>();
            long tokens = 0;
            var cents = 0.0;
            var count = 0;
            foreach (var name in visible)
            {
                if (!cells.TryGetValue((key, name), out var cell)) continue;
                if (cell.tokens <= 0 && cell.cents <= 0 && cell.count <= 0) continue;
                slices.Add(new ChartSlice(name, cell.tokens, cell.cents, cell.count));
                tokens += cell.tokens;
                cents += cell.cents;
                count += cell.count;
            }
            buckets.Add(new ChartBucket(key, BucketLabel(key, hourly, multiDay), tokens, cents, count, slices));
        }
        return new UsageChartSeries
        {
            Hourly = hourly,
            Caption = ChartCaption(hourly, keys),
            Models = models,
            Buckets = buckets,
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
