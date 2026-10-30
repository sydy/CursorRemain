using System.Globalization;
using System.Linq;

namespace CursorTokenCore;

public static class StatusText
{
    public static string FormatSummary(UsageSnapshot? usage, string? error, string? updatedAt, string? accountLabel = null)
    {
        if (error is not null) return $"状态: {error} | 更新 {SummaryClock(updatedAt)}";
        if (usage is null) return "状态: 等待刷新…";
        var auto = usage.AutoPercentUsed is null ? "—" : usage.AutoPercentUsed.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        var api = usage.ApiPercentUsed is null ? "—" : usage.ApiPercentUsed.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        var est = FormatEstimatedDays(usage);
        var tokens = usage.TotalTokens is > 0 ? $"消耗 {UsageParser.FormatTokenCount(usage.TotalTokens)} Token | " : "";
        var spend = usage.ShowsAmount ? $"金额 {UsageParser.FormatSpendRange(usage.UsedCents, usage.LimitCents)} | " : "";
        var plan = FormatPlanCaption(usage.MembershipType, accountLabel);
        if (usage.IsUnlimited) plan += " · 不限量";
        if (usage.IsXSubscription)
        {
            var prepaid = usage.PrepaidRemainingCents is { } cents ? $" | 预付余额 {UsageParser.FormatUsdCents(cents)}" : "";
            var weekly = $"每周额度 {usage.UsedPercent.ToString("0.0", CultureInfo.InvariantCulture)}%";
            return $"剩余 {usage.RemainingPercent.ToString("0.0", CultureInfo.InvariantCulture)}% | {plan} | {weekly}{prepaid} | 预计可用 {est} | 更新 {SummaryClock(updatedAt)}";
        }
        var grok = usage.ShowsGrokBot && usage.GrokBotRemainingPercent is { } grokLeft
            ? $" | Grok Bot 剩余 {grokLeft.ToString("0.0", CultureInfo.InvariantCulture)}%"
            : "";
        return $"剩余 {usage.RemainingPercent.ToString("0.0", CultureInfo.InvariantCulture)}% | {plan} | {spend}{tokens}First-party {auto} | API {api}{grok} | 预计可用 {est} | 更新 {SummaryClock(updatedAt)}";
    }

    public static string FormatEstimatedDays(UsageSnapshot usage)
    {
        if (usage.EstimatedUsableDays is null)
        {
            if (usage.UsedPercent < 0.2) return "用量过低，暂无法估算";
            if (usage.DaysElapsed is < 0.04) return "周期刚开始，统计中";
            return "暂无法估算";
        }
        var est = usage.EstimatedUsableDays.Value;
        string text;
        if (est <= 0) text = "已耗尽";
        else if (est < 1) text = $"约 {Math.Max(1, (int)(est * 24))} 小时";
        else text = $"约 {est.ToString("0.0", CultureInfo.InvariantCulture)} 天".Replace(".0 天", " 天");
        if (usage.DaysRemaining is { } resetLeft && est > 0)
            text += est >= resetLeft ? "  ·  可撑过本周期" : "  ·  可能提前耗尽";
        return text;
    }

    public static string StatusPillText(double? remaining, bool error = false) =>
        RustCore.String("status_pill_text", new { remaining, error });

    public static string FormatPlanCaption(string? membership, string? accountLabel = null) =>
        RustCore.String("format_plan_caption", new { membership = membership ?? "", label = accountLabel ?? "" });

    public static string FormatEstimateCaption(UsageSnapshot usage)
    {
        var text = FormatEstimatedDays(usage);
        if (text.Contains("可撑过本周期")) return "预计能撑到重置";
        if (text.Contains("提前耗尽")) return "预计可能提前耗尽";
        if (text == "已耗尽") return "额度已耗尽";
        return text;
    }

    public static string FormatResetDate(string iso, bool includeTime = false)
    {
        var text = iso.Replace("Z", "+00:00");
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            return iso;
        var show = dt.ToLocalTime();
        var label = $"{show.Month}月{show.Day}日";
        if (includeTime && (show.Hour != 0 || show.Minute != 0))
            label += $" {show.Hour:00}:{show.Minute:00}";
        return label;
    }

    public static string FormatCycleRemaining(string? endIso, int? daysRemaining, DateTimeOffset? now = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow;
        DateTimeOffset end;
        var parsed = false;
        if (!string.IsNullOrEmpty(endIso))
        {
            var text = endIso.Replace("Z", "+00:00");
            parsed = DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out end);
        }
        else end = default;
        if (!parsed)
            return daysRemaining is { } d ? $"还剩 {d} 天" : "";
        var seconds = (end - clock).TotalSeconds;
        if (seconds <= 0) return "已到期";
        var hours = (int)(seconds / 3600);
        if (hours < 24)
        {
            if (hours < 1)
            {
                var minutes = Math.Max(1, (int)(seconds / 60));
                return $"还剩 {minutes} 分钟";
            }
            return $"还剩 {hours} 小时";
        }
        var days = daysRemaining ?? (int)(seconds / 86400);
        return $"还剩 {days} 天";
    }

    public static string CycleEndLabel(UsageSnapshot usage) =>
        usage.BillingCycleEndOverridden ? "到期" : "重置";

    public static List<(string, string)> BuildStatusLines(UsageSnapshot? usage, string? error, string? updatedAt = null, string? accountLabel = null)
    {
        var result = RustCore.Call("build_status_lines", new
        {
            usage = usage is null ? null : UsageParser.SnapshotJson(usage),
            error,
            updated_at = updatedAt,
            label = accountLabel,
        });
        return result.EnumerateArray().Select(row => (row.GetProperty("key").GetString() ?? "", row.GetProperty("value").GetString() ?? "")).ToList();
    }

    public static string ShortError(string? text, int maxLen = 40) =>
        RustCore.String("short_error", new { text = text ?? "", max_len = maxLen });

    public static string FormatCompareSync(int ok, IReadOnlyList<string> failures, string stamp) =>
        RustCore.String("format_compare_sync", new { ok, failures, stamp });

    public static string FormatReportPerMillionKpi(double totalCny, long totalTokens) =>
        RustCore.String("format_report_per_million_kpi", new { total_cny = totalCny, total_tokens = totalTokens });

    public static string FormatReportSpendKpi(
        double totalCny,
        double planCny,
        double onDemandCny,
        double usdCnyRate,
        bool usesActual,
        double windowPlanCny = 0) =>
        RustCore.String("format_report_spend_kpi", new { total_cny = totalCny, plan_cny = planCny, on_demand_cny = onDemandCny, usd_cny_rate = usdCnyRate, uses_actual = usesActual, window_plan_cny = windowPlanCny });

    public const string XReportNotice = "这个套餐只提供每周用量，没有逐条请求明细。";

    public static string FormatXReportBody(UsageSnapshot? usage, double? lastRemaining = null)
    {
        if (usage is null)
        {
            var pending = XReportNotice;
            if (lastRemaining is { } left)
                pending += Environment.NewLine + Environment.NewLine + "剩余  " + left.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            return pending;
        }
        var lines = new List<string> { FormatPlanCaption(usage.MembershipType), XReportNotice, "" };
        lines.Add("已用  " + usage.UsedPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%");
        lines.Add("剩余  " + usage.RemainingPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%");
        if (usage.UsageResetAt is { } reset)
        {
            var endText = FormatResetDate(reset, includeTime: true);
            var remaining = FormatCycleRemaining(reset, usage.DaysRemaining);
            lines.Add(string.IsNullOrEmpty(remaining) ? $"重置  {endText}" : $"重置  {endText}（{remaining}）");
        }
        if (usage.PrepaidRemainingCents is { } prepaid)
            lines.Add($"预付余额  {UsageParser.FormatUsdCents(prepaid)}");
        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatReportSyncResult(
        int count,
        int fetched,
        string stamp,
        bool truncated = false,
        int totalAvailable = 0,
        string note = "",
        bool hasToken = true) =>
        RustCore.String("format_report_sync_result", new { count, fetched, stamp, truncated, total_available = totalAvailable, note, has_token = hasToken });

    public static string FormatFlyoutError(string? error) =>
        RustCore.String("format_flyout_error", new { error = error ?? "" });

    public const string CompareHint =
        "账号一行，First-party / API / Grok Bot 各占一行。日均持有 = 折合月费÷30。填了实际成本时，实付按该成本在窗口内折算分摊，按需不再按官网标价另加。绿色数字只比较已填成本的账号，取最低 ¥/百万 Token。";

    public static string FormatCompareHint(bool mixedWindows) =>
        RustCore.String("format_compare_hint", new { mixed = mixedWindows });

    public static string FormatReportSyncProgress(int page) =>
        RustCore.String("format_report_sync_progress", new { page });

    public static string FormatReportFilterEmpty(int total) =>
        RustCore.String("format_report_filter_empty", new { total });

    public static string FormatCompareSyncProgress(int index, int total, string? name, int page = 0) =>
        RustCore.String("format_compare_sync_progress", new { index, total, name = name ?? "", page });

    public static string FormatCloudSyncNotify(bool ok, string? message) =>
        RustCore.String("format_cloud_sync_notify", new { ok, message = message ?? "" });

    public static string ExportFileSlug(string? label) =>
        RustCore.String("export_file_slug", new { label = label ?? "" });

    public static string FormatExportFilename(string prefix, string? label = null, string? day = null) =>
        RustCore.String("format_export_filename", new { prefix, label = label ?? "", day = string.IsNullOrWhiteSpace(day) ? DateTimeOffset.Now.ToString("yyyyMMdd") : day.Trim() });

    public static string FormatCompareAccountName(string? name, bool isActive) =>
        RustCore.String("format_compare_account_name", new { name = name ?? "", active = isActive });

    public static string FormatTokenSaveResult(int ok, int fail) =>
        RustCore.String("format_token_save_result", new { ok, fail });

    public static string FormatExportEmpty() => "当前没有可导出的明细";

    public static string FormatAccountMenuTitle(string? label, double? remaining) =>
        RustCore.String("format_account_menu_title", new { label = label ?? "", remaining });

    public static string FormatReportCacheStatus(int count, string? accountLabel = null) =>
        RustCore.String("format_report_cache_status", new { count, label = accountLabel ?? "" });

    public static string FormatReportSyncError(string? error) =>
        RustCore.String("format_report_sync_error", new { error = error ?? "" });

    public static string FormatCloudDecryptNote(bool decryptError, bool syncSecretFailed, bool cloudAccessFailed) =>
        RustCore.String("format_cloud_decrypt_note", new { decrypt_error = decryptError, sync_secret_failed = syncSecretFailed, cloud_access_failed = cloudAccessFailed });

    public static string FlyoutSettingsTitle(string? error) =>
        RustCore.String("flyout_settings_title", new { error = error ?? "" });

    public static string FormatAccountPlan(Account account) =>
        RustCore.String("format_account_plan", new { membership = account.MembershipType ?? "" });

    public static string FormatAccountCheckStatus(Account account) =>
        RustCore.String("format_account_check_status", new { decrypt_failed = account.TokenDecryptFailed, error = account.LastError ?? "", remaining = account.LastRemaining, has_token = !string.IsNullOrWhiteSpace(account.Token) });

    public static string FormatAccountCheckTime(Account account)
    {
        var checkedAt = (account.UpdatedAt ?? "").Trim();
        if (checkedAt.Length > 0)
        {
            if (IsFullStamp(checkedAt))
                return AccountSync.FormatLocal(checkedAt, "MM-dd HH:mm");
            return checkedAt;
        }
        var iso = (account.UsageUpdatedAt ?? "").Trim();
        if (IsFullStamp(iso))
            return AccountSync.FormatLocal(iso, "MM-dd HH:mm");
        return "—";
    }

    static bool IsFullStamp(string value) => value.Contains('T') && AccountSync.ParseIso(value) is not null;

    static string SummaryClock(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return "—";
        if (IsFullStamp(text)) return AccountSync.FormatLocal(text, "HH:mm:ss");
        return text;
    }

    public static string FormatUpdateClock(string? value)
    {
        var text = (value ?? "").Trim();
        if (IsFullStamp(text))
            return AccountSync.FormatLocal(text, "HH:mm:ss");
        if (text.Length > 0) return text;
        return DateTime.Now.ToString("HH:mm:ss");
    }

    static bool IsUnreachableStatus(string error)
    {
        var text = error.ToLowerInvariant();
        if (text.Contains("网络错误") || text.Contains("刷新失败") || text.Contains("无法连接")) return true;
        if (text.Contains("timed out") || text.Contains("timeout") || text.Contains("超时")) return true;
        if (text.Contains("offline") || text.Contains("not connected") || text.Contains("网络连接")) return true;
        if (text.StartsWith("http 429") || text.StartsWith("http 408") || text.StartsWith("http 5")) return true;
        return false;
    }

    public static string FormatAccountKind(Account account) =>
        RustCore.String("format_account_kind", new { kind = account.AccountKind ?? "" });

    public static string FormatSyncStatus(string lastAt, string lastError)
    {
        var error = (lastError ?? "").Trim();
        var at = (lastAt ?? "").Trim();
        if (error.Length > 0 && AccountSync.IsTrimNote(error) && at.Length > 0)
            return "上次同步 " + AccountSync.FormatLocal(at) + "；" + error;
        if (error.Length > 0) return error;
        if (at.Length > 0) return "上次同步 " + AccountSync.FormatLocal(at);
        return "";
    }
}
