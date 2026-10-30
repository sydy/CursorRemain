import Foundation

public enum StatusText {
    public static func formatSummary(
        _ usage: UsageSnapshot?,
        errorMessage: String?,
        updatedAt: String?,
        accountLabel: String? = nil
    ) -> String {
        let clock = formatStoredClock(updatedAt)
        if let errorMessage {
            return "状态: \(errorMessage) | 更新 \(clock)"
        }
        guard let usage else { return "状态: 等待刷新…" }
        let auto = usage.autoPercentUsed.map { String(format: "%.1f%%", $0) } ?? "—"
        let api = usage.apiPercentUsed.map { String(format: "%.1f%%", $0) } ?? "—"
        let est = formatEstimatedDays(usage)
        var tokens = ""
        if let total = usage.totalTokens, total > 0 {
            tokens = "消耗 \(UsageParser.formatTokenCount(Double(total))) Token | "
        }
        var spend = ""
        if usage.showsAmount {
            spend = "金额 \(UsageParser.formatSpendRange(used: usage.usedCents, limit: usage.limitCents)) | "
        }
        var plan = formatPlanCaption(usage.membershipType, accountLabel: accountLabel)
        if usage.isUnlimited { plan += " · 不限量" }
        if usage.isXSubscription {
            let prepaid = usage.prepaidRemainingCents.map { " | 预付余额 \(UsageParser.formatUSDCents($0))" } ?? ""
            let weekly = String(format: "每周额度 %.1f%%", usage.usedPercent)
            return "剩余 \(String(format: "%.1f", usage.remainingPercent))% | \(plan) | \(weekly)\(prepaid) | 预计可用 \(est) | 更新 \(clock)"
        }
        var grok = ""
        if usage.showsGrokBot, let left = usage.grokBotRemainingPercent {
            grok = String(format: " | Grok Bot 剩余 %.1f%%", left)
        }
        return "剩余 \(String(format: "%.1f", usage.remainingPercent))% | \(plan) | \(spend)\(tokens)First-party \(auto) | API \(api)\(grok) | 预计可用 \(est) | 更新 \(clock)"
    }

    public static func formatEstimatedDays(_ usage: UsageSnapshot) -> String {
        guard let est = usage.estimatedUsableDays else {
            if usage.usedPercent < 0.2 { return "用量过低，暂无法估算" }
            if let elapsed = usage.daysElapsed, elapsed < 0.04 { return "周期刚开始，统计中" }
            return "暂无法估算"
        }
        var text: String
        if est <= 0 {
            text = "已耗尽"
        } else if est < 1 {
            text = "约 \(max(1, Int(est * 24))) 小时"
        } else {
            text = String(format: "约 %.1f 天", est).replacingOccurrences(of: ".0 天", with: " 天")
        }
        if let resetLeft = usage.daysRemaining, est > 0 {
            if est >= Double(resetLeft) {
                text += "  ·  可撑过本周期"
            } else {
                text += "  ·  可能提前耗尽"
            }
        }
        return text
    }

    public static func statusPillText(_ remaining: Double?, error: Bool = false) -> String {
        RustCore.string("status_pill_text", ["remaining": remaining as Any, "error": error])
    }

    public static func formatPlanCaption(_ membership: String?, accountLabel: String? = nil) -> String {
        RustCore.string("format_plan_caption", ["membership": membership ?? "", "label": accountLabel ?? ""])
    }

    public static func formatEstimateCaption(_ usage: UsageSnapshot) -> String {
        let text = formatEstimatedDays(usage)
        if text.contains("可撑过本周期") { return "预计能撑到重置" }
        if text.contains("提前耗尽") { return "预计可能提前耗尽" }
        if text == "已耗尽" { return "额度已耗尽" }
        return text
    }

    public static func formatResetDate(_ isoValue: String, includeTime: Bool = false) -> String {
        let text = isoValue.replacingOccurrences(of: "Z", with: "+00:00")
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        var dt = f.date(from: text)
        if dt == nil {
            let f2 = ISO8601DateFormatter()
            f2.formatOptions = [.withInternetDateTime]
            dt = f2.date(from: text) ?? f2.date(from: isoValue)
        }
        guard let dt else { return isoValue }
        let cal = Calendar.current
        let m = cal.component(.month, from: dt)
        let d = cal.component(.day, from: dt)
        var label = "\(m)月\(d)日"
        if includeTime {
            let hour = cal.component(.hour, from: dt)
            let minute = cal.component(.minute, from: dt)
            if hour != 0 || minute != 0 {
                label += String(format: " %02d:%02d", hour, minute)
            }
        }
        return label
    }

    public static func formatCycleRemaining(_ endIso: String?, daysRemaining: Int?, now: Date = Date()) -> String {
        guard let endIso, let end = AccountSync.parseIso(endIso) else {
            if let daysRemaining { return "还剩 \(daysRemaining) 天" }
            return ""
        }
        let seconds = end.timeIntervalSince(now)
        if seconds <= 0 { return "已到期" }
        let hours = Int(seconds / 3600)
        if hours < 24 {
            if hours < 1 {
                let minutes = max(1, Int(seconds / 60))
                return "还剩 \(minutes) 分钟"
            }
            return "还剩 \(hours) 小时"
        }
        let days = daysRemaining ?? Int(seconds / 86_400)
        return "还剩 \(days) 天"
    }

    public static func cycleEndLabel(_ usage: UsageSnapshot) -> String {
        usage.billingCycleEndOverridden ? "到期" : "重置"
    }

    public static func buildStatusLines(
        _ usage: UsageSnapshot?,
        errorMessage: String?,
        updatedAt: String? = nil,
        accountLabel: String? = nil
    ) -> [(String, String)] {
        let result = RustCore.call("build_status_lines", [
            "usage": usage.map(snapshotPayload) as Any,
            "error": errorMessage as Any,
            "updated_at": updatedAt as Any,
            "label": accountLabel as Any,
        ])
        return (result as? [[String: Any]] ?? []).map { ($0["key"] as? String ?? "", $0["value"] as? String ?? "") }
    }

    static func snapshotPayload(_ usage: UsageSnapshot) -> [String: Any] {
        [
            "used_percent": usage.usedPercent,
            "remaining_percent": usage.remainingPercent,
            "shows_amount": usage.showsAmount,
            "is_unlimited": usage.isUnlimited,
            "is_team_account": usage.isTeamAccount,
            "is_x_subscription": usage.isXSubscription,
            "membership_type": usage.membershipType,
            "billing_mode": usage.billingMode,
            "used_cents": usage.usedCents as Any,
            "limit_cents": usage.limitCents as Any,
            "pooled_used_cents": usage.pooledUsedCents as Any,
            "pooled_limit_cents": usage.pooledLimitCents as Any,
            "on_demand_used_cents": usage.onDemandUsedCents as Any,
            "on_demand_limit_cents": usage.onDemandLimitCents as Any,
            "usage_reset_at": usage.usageResetAt as Any,
            "days_remaining": usage.daysRemaining as Any,
            "estimated_usable_days": usage.estimatedUsableDays as Any,
            "billing_cycle_end": usage.billingCycleEnd as Any,
            "billing_cycle_end_overridden": usage.billingCycleEndOverridden,
            "total_tokens": usage.totalTokens as Any,
            "auto_percent_used": usage.autoPercentUsed as Any,
            "api_percent_used": usage.apiPercentUsed as Any,
            "prepaid_remaining_cents": usage.prepaidRemainingCents as Any,
            "grok_bot_percent_used": usage.grokBotPercentUsed as Any,
        ]
    }

    /// Menu-bar template: unconfigured is idle (not error); other failures show "!".
    public static func trayTemplateState(errorMessage: String?, remainingPercent: Double?) -> (remaining: Double?, error: Bool) {
        if let err = errorMessage, err.hasPrefix("未配置") {
            return (nil, false)
        }
        if errorMessage != nil {
            return (nil, true)
        }
        return (remainingPercent, false)
    }

    public static func trayPercentLabel(_ remaining: Double?, error: Bool) -> String {
        if error { return "!" }
        guard let remaining else { return "–" }
        let pct = min(100, max(0, remaining))
        if pct >= 99.5 { return "100" }
        return String(Int(pct.rounded()))
    }

    public static func shortError(_ text: String?, maxLen: Int = 40) -> String {
        RustCore.string("short_error", ["text": text ?? "", "max_len": maxLen])
    }

    public static func formatCompareSync(ok: Int, failures: [String], stamp: String) -> String {
        RustCore.string("format_compare_sync", ["ok": ok, "failures": failures, "stamp": stamp])
    }

    public static func formatReportPerMillionKpi(totalCny: Double, totalTokens: Int) -> String {
        RustCore.string("format_report_per_million_kpi", ["total_cny": totalCny, "total_tokens": totalTokens])
    }

    public static func formatReportSpendKpi(
        totalCny: Double,
        planCny: Double,
        onDemandCny: Double,
        usdCnyRate: Double,
        usesActual: Bool,
        windowPlanCny: Double = 0
    ) -> String {
        RustCore.string("format_report_spend_kpi", ["total_cny": totalCny, "plan_cny": planCny, "on_demand_cny": onDemandCny, "usd_cny_rate": usdCnyRate, "uses_actual": usesActual, "window_plan_cny": windowPlanCny])
    }

    static func oneDecimal(_ value: Double) -> String {
        String(format: "%.1f", locale: Locale(identifier: "en_US_POSIX"), value)
    }

    public static func formatXReportNotice() -> String {
        "这个套餐只提供每周用量，没有逐条请求明细。"
    }

    public static func formatXReportBody(_ usage: UsageSnapshot?, lastRemaining: Double? = nil) -> String {
        guard let usage else {
            var text = formatXReportNotice()
            if let lastRemaining {
                text += "\n\n剩余  \(Self.oneDecimal(lastRemaining))%"
            }
            return text
        }
        var lines = [formatPlanCaption(usage.membershipType), formatXReportNotice(), ""]
        lines.append("已用  \(Self.oneDecimal(usage.usedPercent))%")
        lines.append("剩余  \(Self.oneDecimal(usage.remainingPercent))%")
        if let reset = usage.usageResetAt {
            let end = formatResetDate(reset, includeTime: true)
            let remaining = formatCycleRemaining(reset, daysRemaining: usage.daysRemaining)
            lines.append(remaining.isEmpty ? "重置  \(end)" : "重置  \(end)（\(remaining)）")
        }
        if let prepaid = usage.prepaidRemainingCents {
            lines.append("预付余额  \(UsageParser.formatUSDCents(prepaid))")
        }
        return lines.joined(separator: "\n")
    }

    public static func formatReportSyncResult(
        count: Int,
        fetched: Int,
        stamp: String,
        truncated: Bool = false,
        totalAvailable: Int = 0,
        note: String = "",
        hasToken: Bool = true
    ) -> String {
        RustCore.string("format_report_sync_result", ["count": count, "fetched": fetched, "stamp": stamp, "truncated": truncated, "total_available": totalAvailable, "note": note, "has_token": hasToken])
    }

    public static func formatFlyoutError(_ errorMessage: String?) -> String {
        RustCore.string("format_flyout_error", ["error": errorMessage ?? ""])
    }

    public static let compareHint =
        "账号一行，First-party / API / Grok Bot 各占一行。日均持有 = 折合月费÷30。填了实际成本时，实付按该成本在窗口内折算分摊，按需不再按官网标价另加。绿色数字只比较已填成本的账号，取最低 ¥/百万 Token。"

    public static func formatCompareHint(mixedWindows: Bool) -> String {
        RustCore.string("format_compare_hint", ["mixed": mixedWindows])
    }

    public static func formatReportSyncProgress(_ page: Int) -> String {
        RustCore.string("format_report_sync_progress", ["page": page])
    }

    public static func formatReportFilterEmpty(_ total: Int) -> String {
        RustCore.string("format_report_filter_empty", ["total": total])
    }

    public static func formatCompareSyncProgress(index: Int, total: Int, name: String, page: Int = 0) -> String {
        RustCore.string("format_compare_sync_progress", ["index": index, "total": total, "name": name, "page": page])
    }

    public static func formatCloudSyncNotify(ok: Bool, message: String) -> String {
        RustCore.string("format_cloud_sync_notify", ["ok": ok, "message": message])
    }

    public static func exportFileSlug(_ label: String?) -> String {
        RustCore.string("export_file_slug", ["label": label ?? ""])
    }

    public static func formatExportFilename(_ prefix: String, label: String? = nil, day: String? = nil) -> String {
        let stamp = day?.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty == false ? day!.trimmingCharacters(in: .whitespacesAndNewlines) : exportDay()
        return RustCore.string("format_export_filename", ["prefix": prefix, "label": label ?? "", "day": stamp])
    }

    static func exportDay() -> String {
        let formatter = DateFormatter()
        formatter.dateFormat = "yyyyMMdd"
        return formatter.string(from: Date())
    }

    public static func formatCompareAccountName(_ name: String, isActive: Bool) -> String {
        RustCore.string("format_compare_account_name", ["name": name, "active": isActive])
    }

    public static func formatTokenSaveResult(ok: Int, fail: Int) -> String {
        RustCore.string("format_token_save_result", ["ok": ok, "fail": fail])
    }

    public static func formatExportEmpty() -> String { "当前没有可导出的明细" }

    public static func formatAccountMenuTitle(_ label: String?, remaining: Double?) -> String {
        RustCore.string("format_account_menu_title", ["label": label ?? "", "remaining": remaining as Any])
    }

    public static func formatReportCacheStatus(count: Int, accountLabel: String? = nil) -> String {
        RustCore.string("format_report_cache_status", ["count": count, "label": accountLabel ?? ""])
    }

    public static func formatReportSyncError(_ error: String?) -> String {
        RustCore.string("format_report_sync_error", ["error": error ?? ""])
    }

    public static func formatCloudDecryptNote(decryptError: Bool, syncSecretFailed: Bool, cloudAccessFailed: Bool) -> String {
        RustCore.string("format_cloud_decrypt_note", ["decrypt_error": decryptError, "sync_secret_failed": syncSecretFailed, "cloud_access_failed": cloudAccessFailed])
    }

    public static func flyoutSettingsTitle(_ errorMessage: String?) -> String {
        RustCore.string("flyout_settings_title", ["error": errorMessage ?? ""])
    }

    public static func formatAccountPlan(_ account: Account) -> String {
        RustCore.string("format_account_plan", ["membership": account.membershipType])
    }

    public static func formatAccountCheckStatus(_ account: Account) -> String {
        RustCore.string("format_account_check_status", ["decrypt_failed": account.tokenDecryptFailed, "error": account.lastError, "remaining": account.lastRemaining as Any, "has_token": !account.token.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty])
    }

    public static func formatAccountCheckTime(_ account: Account) -> String {
        let checked = account.updatedAt.trimmingCharacters(in: .whitespaces)
        if !checked.isEmpty {
            if AccountSync.parseIso(checked) != nil {
                return AccountSync.formatLocal(checked, format: "MM-dd HH:mm")
            }
            return checked
        }
        let iso = account.usageUpdatedAt.trimmingCharacters(in: .whitespaces)
        if AccountSync.parseIso(iso) != nil {
            return AccountSync.formatLocal(iso, format: "MM-dd HH:mm")
        }
        return "—"
    }

    static func formatStoredClock(_ value: String?) -> String {
        let text = (value ?? "").trimmingCharacters(in: .whitespaces)
        if text.isEmpty { return "—" }
        if AccountSync.parseIso(text) != nil { return AccountSync.formatLocal(text, format: "HH:mm:ss") }
        return text
    }

    public static func formatUpdateClock(_ value: String?) -> String {
        let text = (value ?? "").trimmingCharacters(in: .whitespaces)
        if AccountSync.parseIso(text) != nil {
            return AccountSync.formatLocal(text, format: "HH:mm:ss")
        }
        if !text.isEmpty { return text }
        let f = DateFormatter()
        f.dateFormat = "HH:mm:ss"
        return f.string(from: Date())
    }

    static func isUnreachableStatus(_ error: String) -> Bool {
        let text = error.lowercased()
        if text.contains("网络错误") || text.contains("刷新失败") || text.contains("无法连接") { return true }
        if text.contains("timed out") || text.contains("timeout") || text.contains("超时") { return true }
        if text.contains("offline") || text.contains("not connected") || text.contains("网络连接") { return true }
        if text.hasPrefix("http 429") || text.hasPrefix("http 408") { return true }
        if text.hasPrefix("http 5") { return true }
        return false
    }

    public static func formatAccountKind(_ account: Account) -> String {
        RustCore.string("format_account_kind", ["kind": account.accountKind])
    }

    public static func formatSyncStatus(lastAt: String, lastError: String) -> String {
        let error = lastError.trimmingCharacters(in: .whitespaces)
        let at = lastAt.trimmingCharacters(in: .whitespaces)
        if !error.isEmpty && AccountSync.isTrimNote(error) && !at.isEmpty {
            return "上次同步 " + AccountSync.formatLocal(at) + "；" + error
        }
        if !error.isEmpty { return error }
        if !at.isEmpty { return "上次同步 " + AccountSync.formatLocal(at) }
        return ""
    }
}
