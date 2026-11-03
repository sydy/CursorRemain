using System.Collections.Concurrent;
using System.Text.Json;

namespace CursorTokenCore;

public sealed record HistoryPoint(double Ts, double Remaining, double? Auto, double? Api);

public static class UsageHistory
{
    public const int KeepDays = 90;
    public static readonly TimeSpan PruneInterval = TimeSpan.FromHours(6);
    public static readonly TimeSpan MinAppendInterval = TimeSpan.FromMinutes(15);
    static readonly ConcurrentDictionary<string, long> LastPruneMs = new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<string, (double Ts, double Remaining, double? Auto, double? Api)> LastWritten = new(StringComparer.OrdinalIgnoreCase);

    public static void AdoptLegacy(string accountId, string directory)
    {
        var dest = AppPaths.HistoryPath(accountId, directory);
        var legacy = Path.Combine(directory, "usage_history.jsonl");
        if (string.IsNullOrEmpty(accountId) || File.Exists(dest) || !File.Exists(legacy)) return;
        var others = Directory.GetFiles(directory, "usage_history.*.jsonl")
            .Where(p => !string.Equals(Path.GetFileName(p), "usage_history.jsonl", StringComparison.OrdinalIgnoreCase));
        if (others.Any(p => Path.GetFullPath(p) != Path.GetFullPath(dest))) return;
        File.Move(legacy, dest);
    }

    public static void Append(double remaining, double? auto = null, double? api = null, double? ts = null, string? accountId = null, string? directory = null)
    {
        var dir = AppPaths.ConfigDirectory(directory);
        Directory.CreateDirectory(dir);
        var aid = (accountId ?? "").Trim();
        if (aid.Length == 0) aid = ConfigStore.Load(dir).ActiveAccount?.Id ?? "";
        if (aid.Length > 0) AdoptLegacy(aid, dir);
        var path = AppPaths.HistoryPath(string.IsNullOrEmpty(aid) ? null : aid, dir);

        var rRem = Numbers.Round2(remaining);
        double? rAuto = auto is null ? null : Numbers.Round2(auto.Value);
        double? rApi = api is null ? null : Numbers.Round2(api.Value);
        var actualTs = ts ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        if (ts is null && LastWritten.TryGetValue(path, out var last))
        {
            var isSame = Math.Abs(last.Remaining - rRem) < 0.001
                && NullableEquals(last.Auto, rAuto)
                && NullableEquals(last.Api, rApi);
            if (isSame && (actualTs - last.Ts) < MinAppendInterval.TotalSeconds)
            {
                return;
            }
        }

        var obj = new Dictionary<string, object?>
        {
            ["ts"] = actualTs,
            ["remaining"] = rRem,
            ["auto"] = rAuto,
            ["api"] = rApi,
            ["account_id"] = aid,
        };
        var line = JsonSerializer.Serialize(obj) + "\n";
        SafeAppendText(path, line);
        LastWritten[path] = (actualTs, rRem, rAuto, rApi);
        if (ts is null) MaybePrune(path);
    }

    static bool NullableEquals(double? a, double? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return Math.Abs(a.Value - b.Value) < 0.001;
    }

    public static void SafeAppendText(string path, string text)
    {
        for (var i = 0; i < 5; i++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var sw = new StreamWriter(fs, System.Text.Encoding.UTF8);
                sw.Write(text);
                return;
            }
            catch (IOException) when (i < 4)
            {
                Thread.Sleep(20 * (i + 1));
            }
        }
    }

    public static IEnumerable<string> EnumerateFileLinesSafe(string path)
    {
        if (!File.Exists(path)) yield break;
        FileStream? fs = null;
        for (var i = 0; i < 5; i++)
        {
            try
            {
                fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                break;
            }
            catch (IOException) when (i < 4)
            {
                Thread.Sleep(20 * (i + 1));
            }
        }
        if (fs is null) yield break;

        using (fs)
        using (var sr = new StreamReader(fs, System.Text.Encoding.UTF8))
        {
            string? line;
            while ((line = sr.ReadLine()) is not null)
            {
                yield return line;
            }
        }
    }

    static void MaybePrune(string path)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var intervalMs = (long)PruneInterval.TotalMilliseconds;
        if (LastPruneMs.TryGetValue(path, out var prev) && now - prev < intervalMs) return;
        LastPruneMs[path] = now;
        Prune(path);
    }

    public static void PruneAll(string? directory = null)
    {
        var dir = AppPaths.ConfigDirectory(directory);
        if (!Directory.Exists(dir)) return;
        var files = Directory.GetFiles(dir, "usage_history.*.jsonl");
        foreach (var file in files)
        {
            try { Prune(file); }
            catch { }
        }
    }

    public static void Prune(string path, int keepDays = KeepDays)
    {
        if (!File.Exists(path)) return;
        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - Math.Max(1, keepDays) * 86400L;
        var kept = new List<HistoryPoint>();
        foreach (var line in EnumerateFileLinesSafe(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                if (!r.TryGetProperty("ts", out var tsEl) || !r.TryGetProperty("remaining", out var remEl)) continue;
                var ts = tsEl.GetDouble();
                if (ts >= cutoff)
                {
                    kept.Add(new HistoryPoint(ts, remEl.GetDouble(), Num(r, "auto"), Num(r, "api")));
                }
            }
            catch { }
        }
        var downsampled = Downsample(kept);
        var aid = Path.GetFileNameWithoutExtension(path);
        if (aid.StartsWith("usage_history.", StringComparison.OrdinalIgnoreCase))
            aid = aid["usage_history.".Length..];
        else aid = "";

        var lines = new List<string>(downsampled.Count);
        foreach (var p in downsampled)
        {
            var obj = new Dictionary<string, object?>
            {
                ["ts"] = p.Ts,
                ["remaining"] = Numbers.Round2(p.Remaining),
                ["auto"] = p.Auto is null ? null : Numbers.Round2(p.Auto.Value),
                ["api"] = p.Api is null ? null : Numbers.Round2(p.Api.Value),
                ["account_id"] = aid,
            };
            lines.Add(JsonSerializer.Serialize(obj));
        }

        var tmp = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tmp, lines.Count == 0 ? "" : string.Join("\n", lines) + "\n");
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(tmp)) try { File.Delete(tmp); } catch { }
        }
    }

    public static List<HistoryPoint> Downsample(IReadOnlyList<HistoryPoint> points, int maxPoints = 2000)
    {
        if (points.Count <= 2) return [.. points];
        var sorted = points.OrderBy(p => p.Ts).ToList();

        // Pass 1: Deduplicate consecutive identical points within 15 minutes
        var deduped = new List<HistoryPoint>(sorted.Count);
        deduped.Add(sorted[0]);
        for (var i = 1; i < sorted.Count; i++)
        {
            var curr = sorted[i];
            var prev = deduped[^1];
            var isSame = Math.Abs(curr.Remaining - prev.Remaining) < 0.001
                && NullableEquals(curr.Auto, prev.Auto)
                && NullableEquals(curr.Api, prev.Api);
            if (isSame && (curr.Ts - prev.Ts) < MinAppendInterval.TotalSeconds && i < sorted.Count - 1)
            {
                continue;
            }
            deduped.Add(curr);
        }

        if (deduped.Count <= maxPoints || maxPoints < 2) return deduped;

        // Pass 2: Evenly sample while always retaining the very first and very last points
        var result = new List<HistoryPoint>(maxPoints);
        result.Add(deduped[0]);
        var step = (double)(deduped.Count - 1) / (maxPoints - 1);
        for (var i = 1; i < maxPoints - 1; i++)
        {
            var idx = (int)Math.Round(i * step);
            idx = Math.Clamp(idx, 1, deduped.Count - 2);
            if (idx > result.Count - 1 && deduped[idx].Ts > result[^1].Ts)
            {
                result.Add(deduped[idx]);
            }
        }
        if (deduped[^1].Ts > result[^1].Ts)
        {
            result.Add(deduped[^1]);
        }
        return result;
    }

    public static void Replace(IEnumerable<HistoryPoint> points, string accountId, string? directory = null)
    {
        var aid = (accountId ?? "").Trim();
        if (aid.Length == 0) return;
        var dir = AppPaths.ConfigDirectory(directory);
        Directory.CreateDirectory(dir);
        var path = AppPaths.HistoryPath(aid, dir);
        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - KeepDays * 86400L;
        var filtered = points.Where(p => p.Ts >= cutoff).ToList();
        var downsampled = Downsample(filtered);
        var lines = new List<string>(downsampled.Count);
        foreach (var p in downsampled)
        {
            var obj = new Dictionary<string, object?>
            {
                ["ts"] = p.Ts,
                ["remaining"] = Numbers.Round2(p.Remaining),
                ["auto"] = p.Auto is null ? null : Numbers.Round2(p.Auto.Value),
                ["api"] = p.Api is null ? null : Numbers.Round2(p.Api.Value),
                ["account_id"] = aid,
            };
            lines.Add(JsonSerializer.Serialize(obj));
        }
        File.WriteAllText(path, lines.Count == 0 ? "" : string.Join("\n", lines) + "\n");
    }

    public static List<HistoryPoint> LoadRecent(int days = 7, string? accountId = null, string? directory = null, int maxPoints = 0)
    {
        var dir = AppPaths.ConfigDirectory(directory);
        var aid = (accountId ?? "").Trim();
        if (aid.Length == 0) aid = ConfigStore.Load(dir).ActiveAccount?.Id ?? "";
        if (aid.Length > 0) AdoptLegacy(aid, dir);
        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - Math.Max(1, days) * 86400L;
        var points = new List<HistoryPoint>();
        var path = AppPaths.HistoryPath(string.IsNullOrEmpty(aid) ? null : aid, dir);
        if (!File.Exists(path)) return points;
        foreach (var line in EnumerateFileLinesSafe(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                if (!r.TryGetProperty("ts", out var tsEl) || !r.TryGetProperty("remaining", out var remEl)) continue;
                var ts = tsEl.GetDouble();
                if (ts < cutoff) continue;
                points.Add(new HistoryPoint(ts, remEl.GetDouble(), Num(r, "auto"), Num(r, "api")));
            }
            catch { }
        }
        var sorted = points.OrderBy(p => p.Ts).ToList();
        return maxPoints > 0 ? Downsample(sorted, maxPoints) : sorted;
    }

    public static double? DailyAvgBurn(int days = 7, string? accountId = null, string? directory = null) =>
        DailyAvgBurn(LoadRecent(days, accountId, directory));

    public static double? DailyAvgBurn(IReadOnlyList<HistoryPoint> points)
    {
        var result = RustCore.Call("daily_avg_burn", new { points = points.Select(p => new { ts = p.Ts, remaining = p.Remaining }) });
        return result.ValueKind == JsonValueKind.Number ? result.GetDouble() : null;
    }

    static double? Num(JsonElement r, string key) =>
        r.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}

public static class AlertLogic
{
    public readonly record struct Notice(string Title, string Body);

    public static bool IsExhaustionRisk(UsageSnapshot snap) =>
        RustCore.Bool("is_exhaustion_risk", new { estimated_usable_days = snap.EstimatedUsableDays, days_remaining = snap.DaysRemaining });

    public static List<Notice> Evaluate(AppConfig config, Account account, UsageSnapshot snapshot)
    {
        if (!config.NotifyEnabled) return [];
        var notices = new List<Notice>();
        var remaining = snapshot.RemainingPercent;
        var thresholds = config.AlertThresholds.Where(x => x is >= 1 and <= 100).Distinct().OrderByDescending(x => x).ToList();
        var notified = account.AlertNotifiedLevels.ToHashSet();
        var changed = false;
        var name = account.DisplayLabel;
        var who = string.IsNullOrEmpty(name) ? "套餐" : $"账号「{name}」";
        var still = notified.Where(lvl => remaining < lvl).ToHashSet();
        if (!still.SetEquals(notified)) { notified = still; changed = true; }
        var newly = thresholds.Where(lvl => remaining < lvl && !notified.Contains(lvl)).ToList();
        if (newly.Count > 0)
        {
            var hit = newly.Min();
            notices.Add(new Notice("额度告警", $"{who}剩余 {remaining:0.0}%，已低于 {hit}% 档。"));
            foreach (var lvl in newly) notified.Add(lvl);
            changed = true;
        }
        if (config.NotifyExhaustionRisk)
        {
            var atRisk = IsExhaustionRisk(snapshot);
            if (atRisk && !account.ExhaustionNotified)
            {
                notices.Add(new Notice("耗尽风险", $"{who}按当前速度可能提前耗尽（剩余 {remaining:0.0}%）。"));
                account.ExhaustionNotified = true;
                changed = true;
            }
            else if (!atRisk && account.ExhaustionNotified)
            {
                account.ExhaustionNotified = false;
                changed = true;
            }
        }
        if (changed)
        {
            account.AlertNotifiedLevels = notified.OrderBy(x => x).ToList();
            var minThr = thresholds.Count > 0 ? thresholds.Min() : 20;
            account.LowQuotaNotified = remaining < minThr;
        }
        return notices;
    }
}
