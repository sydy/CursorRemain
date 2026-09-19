using System.Globalization;

namespace CursorTokenCore;

public static class StatusText
{
    public static string FormatSummary(UsageSnapshot? usage, string? error, string? updatedAt, string? accountLabel = null)
    {
        if (error is not null) return $"状态: {error} | 更新 {updatedAt ?? "—"}";
        if (usage is null) return "状态: 等待刷新…";
        var auto = usage.AutoPercentUsed is null ? "—" : usage.AutoPercentUsed.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        var api = usage.ApiPercentUsed is null ? "—" : usage.ApiPercentUsed.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        var est = FormatEstimatedDays(usage);
        var tokens = usage.TotalTokens is > 0 ? $"消耗 {UsageParser.FormatTokenCount(usage.TotalTokens)} Token | " : "";
        var spend = usage.ShowsAmount ? $"金额 {UsageParser.FormatSpendRange(usage.UsedCents, usage.LimitCents)} | " : "";
        var plan = FormatPlanCaption(usage.MembershipType, accountLabel);
        if (usage.IsUnlimited) plan += " · 不限量";
        var grok = usage.ShowsGrokBot && usage.GrokBotRemainingPercent is { } grokLeft
            ? $" | Grok Bot 剩余 {grokLeft.ToString("0.0", CultureInfo.InvariantCulture)}%"
            : "";
        return $"剩余 {usage.RemainingPercent.ToString("0.0", CultureInfo.InvariantCulture)}% | {plan} | {spend}{tokens}First-party {auto} | API {api}{grok} | 预计可用 {est} | 更新 {updatedAt ?? "—"}";
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

    public static string StatusPillText(double? remaining, bool error = false)
    {
        if (error) return "异常";
        if (remaining is null) return "等待刷新";
        if (remaining <= 0) return "已耗尽";
        if (remaining < 20) return "额度紧张";
        if (remaining < 50) return "略偏低";
        return "状态良好";
    }

    public static string FormatPlanCaption(string? membership, string? accountLabel = null)
    {
        var raw = (membership ?? "").Trim();
        string name;
        if (raw.Length == 0) name = "—";
        else
        {
            name = UsageParser.FormatMembershipType(raw);
            if (!name.Contains("套餐")) name += " 套餐";
        }
        var label = (accountLabel ?? "").Trim();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name, raw, UsageParser.FormatMembershipType(raw) };
        if (label.Length > 0 && !known.Contains(label))
            return name == "—" ? label : $"{label} · {name}";
        return name;
    }

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
        if (error is not null) return [("状态", error)];
        if (usage is null) return [("状态", "等待刷新…")];
        var rows = new List<(string, string)>();
        if (usage.IsUnlimited) rows.Add(("剩余", "不限量"));
        else if (usage.ShowsAmount)
            rows.Add(("剩余", $"{usage.RemainingPercent.ToString("0.0", CultureInfo.InvariantCulture)}%（{UsageParser.FormatSpendRange(usage.UsedCents, usage.LimitCents)}）"));
        else
            rows.Add(("剩余", $"{usage.RemainingPercent.ToString("0.0", CultureInfo.InvariantCulture)}%（已用 {usage.UsedPercent.ToString("0.0", CultureInfo.InvariantCulture)}%）"));
        var label = (accountLabel ?? "").Trim();
        var memb = string.IsNullOrEmpty(usage.MembershipType) ? "" : UsageParser.FormatMembershipType(usage.MembershipType);
        if (label.Length > 0 && !label.Equals(memb, StringComparison.OrdinalIgnoreCase)) rows.Add(("账号", label));
        var plan = string.IsNullOrEmpty(memb) ? "—" : memb;
        if (usage.IsUnlimited) plan += " · 不限量";
        rows.Add(("计划", plan));
        if (usage.ShowsAmount) rows.Add(("金额", UsageParser.FormatSpendRange(usage.UsedCents, usage.LimitCents)));
        if (usage.PooledUsedCents is { } pu && usage.PooledLimitCents is { } pl && pl > 0 && (usage.UsedCents != pu || usage.LimitCents != pl))
            rows.Add(("团队额度", UsageParser.FormatSpendRange(pu, pl)));
        if (usage.OnDemandUsedCents is { } ou && usage.OnDemandLimitCents is { } ol && ol > 0 && (usage.UsedCents != ou || usage.LimitCents != ol))
            rows.Add(("按需用量", UsageParser.FormatSpendRange(ou, ol)));
        if (usage.TotalTokens is > 0) rows.Add(("消耗 Token", UsageParser.FormatTokenCount(usage.TotalTokens)));
        if (usage.AutoPercentUsed is not null || usage.ApiPercentUsed is not null)
        {
            var auto = usage.AutoPercentUsed is null ? "—" : usage.AutoPercentUsed.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            var api = usage.ApiPercentUsed is null ? "—" : usage.ApiPercentUsed.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            rows.Add(("明细", $"First-party {auto} · API {api}"));
        }
        if (usage.ShowsGrokBot && usage.GrokBotPercentUsed is { } grokUsed)
        {
            var grok = $"剩余 {usage.GrokBotRemainingPercent?.ToString("0.0", CultureInfo.InvariantCulture)}%（本周已用 {grokUsed.ToString("0.0", CultureInfo.InvariantCulture)}%）";
            if (usage.GrokBotResetAt is { } grokReset)
            {
                var resetText = FormatResetDate(grokReset);
                var remaining = FormatCycleRemaining(grokReset, usage.GrokBotDaysRemaining);
                grok += string.IsNullOrEmpty(remaining) ? $" · {resetText} 重置" : $" · {resetText}（{remaining}）";
            }
            rows.Add(("Grok Bot", grok));
        }
        if (usage.BillingCycleEnd is { } end)
        {
            var endText = FormatResetDate(end, usage.BillingCycleEndOverridden);
            var remaining = FormatCycleRemaining(end, usage.DaysRemaining);
            var endLabel = CycleEndLabel(usage);
            rows.Add((endLabel, string.IsNullOrEmpty(remaining) ? endText : $"{endText}（{remaining}）"));
            rows.Add(("预计可用", FormatEstimatedDays(usage)));
        }
        else if (usage.EstimatedUsableDays is not null) rows.Add(("预计可用", FormatEstimatedDays(usage)));
        rows.Add(("更新", updatedAt ?? DateTime.Now.ToString("HH:mm:ss")));
        return rows;
    }

    public static string ShortError(string? text, int maxLen = 40)
    {
        var value = (text ?? "同步失败").Trim();
        if (value.Length == 0) value = "同步失败";
        return value.Length <= maxLen ? value : value[..(maxLen - 1)] + "…";
    }

    public static string FormatCompareSync(int ok, IReadOnlyList<string> failures, string stamp)
    {
        if (failures.Count == 0)
            return $"已同步 {ok} 个账号  ·  {stamp}";
        var shown = failures.Take(3).ToList();
        var detail = string.Join("；", shown);
        if (failures.Count > 3) detail += $" 等{failures.Count}个";
        return $"已同步 {ok} 个账号，{failures.Count} 个失败（{detail}）  ·  {stamp}";
    }

    public static string FormatReportSpendKpi(
        double totalCny,
        double planCny,
        double onDemandCny,
        double usdCnyRate,
        bool usesActual,
        double windowPlanCny = 0)
    {
        if (planCny <= 0 && onDemandCny <= 0 && totalCny <= 0) return "";
        var rate = usdCnyRate.ToString("0.00", CultureInfo.InvariantCulture);
        if (usesActual)
        {
            var text = $"    已分摊 {UsageEvents.FormatCny(totalCny)}（月成本 {UsageEvents.FormatCny(planCny)}";
            if (windowPlanCny > 0 && Math.Abs(windowPlanCny - planCny) > 0.005)
                text += $"，本窗口折算 {UsageEvents.FormatCny(windowPlanCny)}";
            return text + $"，按需已计入）· 汇率 {rate}";
        }
        return $"    已分摊 {UsageEvents.FormatCny(totalCny)}（月费 {UsageEvents.FormatCny(planCny)} + 按需 {UsageEvents.FormatCny(onDemandCny)}）· 汇率 {rate}";
    }

    public static string FormatReportSyncResult(
        int count,
        int fetched,
        string stamp,
        bool truncated = false,
        int totalAvailable = 0,
        string note = "",
        bool hasToken = true)
    {
        if (!hasToken) return "未配置 Token，请先在设置里导入账号";
        if (note == UsageEvents.NoteTeamPersonal)
            return "未能拉取个人明细（团队账号）。请先刷新用量，或把范围切到「全员」。";
        var extra = truncated ? $"（服务端约 {totalAvailable} 条，已截到最近 {count} 条）" : "";
        if (fetched > 0) return $"已同步 {count} 条（新增 {fetched}）{extra}  ·  {stamp}";
        if (count > 0) return $"已是最新  ·  {count} 条{extra}  ·  {stamp}";
        return $"还没有本周期明细。点「同步」拉取，或先去设置添加账号。  ·  {stamp}";
    }

    public static string FormatFlyoutError(string? error)
    {
        var text = (error ?? "").Trim();
        if (text.Length == 0) return "等待刷新…";
        if (!Token.IsAuthErrorMessage(text)) return text;
        return text.Contains("未配置") ? "未配置 Token，点下方「粘贴 Token」导入" : "登录已过期，点下方「粘贴 Token」更新";
    }

    public const string CompareHint =
        "账号一行，First-party / API / Grok Bot 各占一行。日均持有 = 折合月费÷30。填了实际成本时，实付按该成本在窗口内折算分摊，按需不再按官网标价另加。绿色数字只比较已填成本的账号，取最低 ¥/百万 Token。";

    public static string FormatCompareHint(bool mixedWindows)
    {
        if (!mixedWindows) return CompareHint;
        return CompareHint + " 当前表里计费窗口不一致，绿色仅供参考。";
    }

    public static string FormatReportSyncProgress(int page) =>
        page <= 1 ? "正在同步本周期明细…" : $"正在同步本周期明细…第 {page} 页";

    public static string FormatReportCacheStatus(int count, string? accountLabel = null)
    {
        var prefix = string.IsNullOrWhiteSpace(accountLabel) ? "" : "当前：" + accountLabel.Trim() + " · ";
        return count > 0
            ? prefix + $"本地 {count} 条，正在刷新…"
            : prefix + "本地还没有明细，正在同步…";
    }

    public static string FormatReportSyncError(string? error)
    {
        var text = (error ?? "").Trim();
        if (text.Length == 0) return "同步失败";
        if (Token.IsAuthErrorMessage(text))
            return text.Contains("未配置") ? "未配置 Token，请先在设置里导入账号" : "登录已过期，请到设置重新粘贴 Token";
        return "同步失败：" + ShortError(text, 80);
    }

    public static string FormatCloudDecryptNote(bool decryptError, bool syncSecretFailed, bool cloudAccessFailed)
    {
        if (syncSecretFailed || cloudAccessFailed)
            return "本机解不开云同步密钥。请退出后用当前密码重新登录，或导入备份。";
        if (decryptError)
            return "本机有账号 Token 解不开。请重新粘贴 Token。";
        return "";
    }

    public static string FlyoutSettingsTitle(string? error) =>
        Token.IsAuthErrorMessage(error) ? "粘贴 Token" : "设置";

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
