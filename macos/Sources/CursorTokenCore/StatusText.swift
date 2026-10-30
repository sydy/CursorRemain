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
        var plan = formatPlanCaptionCore(usage.membershipType, accountLabel: accountLabel)
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

    static func statusPillTextCore(_ remaining: Double?, error: Bool = false) -> String {
        if error { return "异常" }
        guard let remaining else { return "等待刷新" }
        if remaining <= 0 { return "已耗尽" }
        if remaining < 20 { return "额度紧张" }
        if remaining < 50 { return "略偏低" }
        return "状态良好"
    }

    public static func formatPlanCaption(_ membership: String?, accountLabel: String? = nil) -> String {
        RustCore.string("format_plan_caption", ["membership": membership ?? "", "label": accountLabel ?? ""])
    }

    static func formatPlanCaptionCore(_ membership: String?, accountLabel: String? = nil) -> String {
        let raw = (membership ?? "").trimmingCharacters(in: .whitespaces)
        var name: String
        if raw.isEmpty {
            name = "—"
        } else {
            name = UsageParser.formatMembershipType(raw)
            if !name.contains("套餐") { name = "\(name) 套餐" }
        }
        let label = (accountLabel ?? "").trimmingCharacters(in: .whitespaces)
        let known: Set<String> = [
            name.lowercased(),
            raw.lowercased(),
            UsageParser.formatMembershipType(raw).lowercased(),
        ]
        if !label.isEmpty, !known.contains(label.lowercased()) {
            if name == "—" { return label }
            return "\(label) · \(name)"
        }
        return name
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

    static func buildStatusLinesCore(
        _ usage: UsageSnapshot?,
        errorMessage: String?,
        updatedAt: String? = nil,
        accountLabel: String? = nil
    ) -> [(String, String)] {
        if let errorMessage { return [("状态", errorMessage)] }
        guard let usage else { return [("状态", "等待刷新…")] }

        var rows: [(String, String)] = []
        if usage.isUnlimited {
            rows.append(("剩余", "不限量"))
        } else if usage.showsAmount {
            rows.append((
                "剩余",
                String(format: "%.1f%%（%@）", usage.remainingPercent, UsageParser.formatSpendRange(used: usage.usedCents, limit: usage.limitCents))
            ))
        } else {
            rows.append(("剩余", String(format: "%.1f%%（已用 %.1f%%）", usage.remainingPercent, usage.usedPercent)))
        }
        let label = (accountLabel ?? "").trimmingCharacters(in: .whitespaces)
        let memb = usage.membershipType.isEmpty ? "" : UsageParser.formatMembershipType(usage.membershipType)
        if !label.isEmpty, label.lowercased() != memb.lowercased() {
            rows.append(("账号", label))
        }
        var plan = memb.isEmpty ? "—" : memb
        if usage.isUnlimited { plan += " · 不限量" }
        rows.append(("计划", plan))
        if usage.isXSubscription, let prepaid = usage.prepaidRemainingCents {
            rows.append(("预付余额", UsageParser.formatUSDCents(prepaid)))
        }
        if usage.isXSubscription {
            if let reset = usage.usageResetAt {
                let resetText = formatResetDate(reset)
                let left = formatCycleRemaining(reset, daysRemaining: usage.daysRemaining)
                rows.append(("重置", left.isEmpty ? resetText : "\(resetText)（\(left)）"))
                rows.append(("预计可用", formatEstimatedDays(usage)))
            } else if usage.estimatedUsableDays != nil {
                rows.append(("预计可用", formatEstimatedDays(usage)))
            }
            rows.append(("更新", formatUpdateClock(updatedAt)))
            return rows
        }
        if usage.showsAmount {
            rows.append(("金额", UsageParser.formatSpendRange(used: usage.usedCents, limit: usage.limitCents)))
        }
        if let pu = usage.pooledUsedCents, let pl = usage.pooledLimitCents, pl > 0,
           usage.usedCents != pu || usage.limitCents != pl
        {
            rows.append(("团队额度", UsageParser.formatSpendRange(used: pu, limit: pl)))
        }
        if let ou = usage.onDemandUsedCents, let ol = usage.onDemandLimitCents, ol > 0,
           usage.usedCents != ou || usage.limitCents != ol
        {
            rows.append(("按需用量", UsageParser.formatSpendRange(used: ou, limit: ol)))
        }
        if let tokens = usage.totalTokens, tokens > 0 {
            rows.append(("消耗 Token", UsageParser.formatTokenCount(Double(tokens))))
        }
        if usage.autoPercentUsed != nil || usage.apiPercentUsed != nil {
            let auto = usage.autoPercentUsed.map { String(format: "%.1f%%", $0) } ?? "—"
            let api = usage.apiPercentUsed.map { String(format: "%.1f%%", $0) } ?? "—"
            rows.append(("明细", "First-party \(auto) · API \(api)"))
        }
        if usage.showsGrokBot, let grokUsed = usage.grokBotPercentUsed {
            var grok = String(format: "剩余 %.1f%%（本周已用 %.1f%%）", usage.grokBotRemainingPercent ?? 0, grokUsed)
            if let reset = usage.grokBotResetAt {
                let resetText = formatResetDate(reset)
                let remaining = formatCycleRemaining(reset, daysRemaining: usage.grokBotDaysRemaining)
                grok += remaining.isEmpty ? " · \(resetText) 重置" : " · \(resetText)（\(remaining)）"
            }
            rows.append(("Grok Bot", grok))
        }
        if let end = usage.billingCycleEnd {
            let endText = formatResetDate(end, includeTime: usage.billingCycleEndOverridden)
            let remaining = formatCycleRemaining(end, daysRemaining: usage.daysRemaining)
            let label = cycleEndLabel(usage)
            if remaining.isEmpty {
                rows.append((label, endText))
            } else {
                rows.append((label, "\(endText)（\(remaining)）"))
            }
            rows.append(("预计可用", formatEstimatedDays(usage)))
        } else if usage.estimatedUsableDays != nil {
            rows.append(("预计可用", formatEstimatedDays(usage)))
        }
        let stamp = formatUpdateClock(updatedAt)
        rows.append(("更新", stamp))
        return rows
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

    static func shortErrorCore(_ text: String?, maxLen: Int = 40) -> String {
        var value = (text ?? "同步失败").trimmingCharacters(in: .whitespaces)
        if value.isEmpty { value = "同步失败" }
        if value.count <= maxLen { return value }
        return String(value.prefix(maxLen - 1)) + "…"
    }

    public static func formatCompareSync(ok: Int, failures: [String], stamp: String) -> String {
        RustCore.string("format_compare_sync", ["ok": ok, "failures": failures, "stamp": stamp])
    }

    static func formatCompareSyncCore(ok: Int, failures: [String], stamp: String) -> String {
        if failures.isEmpty {
            return "已同步 \(ok) 个账号  ·  \(stamp)"
        }
        var detail = failures.prefix(3).joined(separator: "；")
        if failures.count > 3 { detail += " 等\(failures.count)个" }
        return "已同步 \(ok) 个账号，\(failures.count) 个失败（\(detail)）  ·  \(stamp)"
    }

    public static func formatReportPerMillionKpi(totalCny: Double, totalTokens: Int) -> String {
        RustCore.string("format_report_per_million_kpi", ["total_cny": totalCny, "total_tokens": totalTokens])
    }

    static func formatReportPerMillionKpiCore(totalCny: Double, totalTokens: Int) -> String {
        guard let per = UsageEvents.unitCny(totalCny, totalTokens > 0 ? Double(totalTokens) / 1_000_000.0 : 0) else { return "" }
        return "≈" + UsageEvents.formatCNY(per)
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

    static func formatReportSpendKpiCore(
        totalCny: Double,
        planCny: Double,
        onDemandCny: Double,
        usdCnyRate: Double,
        usesActual: Bool,
        windowPlanCny: Double = 0
    ) -> String {
        if planCny <= 0 && onDemandCny <= 0 && totalCny <= 0 { return "" }
        let rate = String(format: "%.2f", usdCnyRate)
        if usesActual {
            var text = "    已分摊 \(UsageEvents.formatCNY(totalCny))（月成本 \(UsageEvents.formatCNY(planCny))"
            if windowPlanCny > 0 && abs(windowPlanCny - planCny) > 0.005 {
                text += "，本窗口折算 \(UsageEvents.formatCNY(windowPlanCny))"
            }
            return text + "，按需已计入）· 汇率 \(rate)"
        }
        return "    已分摊 \(UsageEvents.formatCNY(totalCny))（月费 \(UsageEvents.formatCNY(planCny)) + 按需 \(UsageEvents.formatCNY(onDemandCny))）· 汇率 \(rate)"
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
        var lines = [formatPlanCaptionCore(usage.membershipType), formatXReportNotice(), ""]
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

    static func formatReportSyncResultCore(
        count: Int,
        fetched: Int,
        stamp: String,
        truncated: Bool = false,
        totalAvailable: Int = 0,
        note: String = "",
        hasToken: Bool = true
    ) -> String {
        if !hasToken { return "未配置 Token，请先在设置里导入账号" }
        if note == UsageEvents.noteTeamPersonal {
            return "未能拉取个人明细（团队账号）。请先刷新用量，或把范围切到「全员」。"
        }
        let extra = truncated ? "（服务端约 \(totalAvailable) 条，已截到最近 \(count) 条）" : ""
        if fetched > 0 { return "已同步 \(count) 条（新增 \(fetched)）\(extra)  ·  \(stamp)" }
        if count > 0 { return "已是最新  ·  \(count) 条\(extra)  ·  \(stamp)" }
        return "还没有本周期明细。点「同步」拉取，或先去设置添加账号。  ·  \(stamp)"
    }

    public static func formatFlyoutError(_ errorMessage: String?) -> String {
        RustCore.string("format_flyout_error", ["error": errorMessage ?? ""])
    }

    static func formatFlyoutErrorCore(_ errorMessage: String?) -> String {
        let text = (errorMessage ?? "").trimmingCharacters(in: .whitespaces)
        if text.isEmpty { return "等待刷新…" }
        guard Token.isAuthErrorMessage(text) else { return text }
        return text.contains("未配置") ? "未配置 Token，点下方「粘贴 Token」导入" : "登录已过期，点下方「粘贴 Token」更新"
    }

    public static let compareHint =
        "账号一行，First-party / API / Grok Bot 各占一行。日均持有 = 折合月费÷30。填了实际成本时，实付按该成本在窗口内折算分摊，按需不再按官网标价另加。绿色数字只比较已填成本的账号，取最低 ¥/百万 Token。"

    public static func formatCompareHint(mixedWindows: Bool) -> String {
        RustCore.string("format_compare_hint", ["mixed": mixedWindows])
    }

    static func formatCompareHintCore(mixedWindows: Bool) -> String {
        mixedWindows ? compareHint + " 当前表里计费窗口不一致，绿色仅供参考。" : compareHint
    }

    public static func formatReportSyncProgress(_ page: Int) -> String {
        RustCore.string("format_report_sync_progress", ["page": page])
    }

    static func formatReportSyncProgressCore(_ page: Int) -> String {
        page <= 1 ? "正在同步本周期明细…" : "正在同步本周期明细…第 \(page) 页"
    }

    public static func formatReportFilterEmpty(_ total: Int) -> String {
        RustCore.string("format_report_filter_empty", ["total": total])
    }

    static func formatReportFilterEmptyCore(_ total: Int) -> String {
        total <= 0 ? "" : "当前筛选无结果（本地共 \(total) 条，可清空筛选）"
    }

    public static func formatCompareSyncProgress(index: Int, total: Int, name: String, page: Int = 0) -> String {
        RustCore.string("format_compare_sync_progress", ["index": index, "total": total, "name": name, "page": page])
    }

    static func formatCompareSyncProgressCore(index: Int, total: Int, name: String, page: Int = 0) -> String {
        let label = name.trimmingCharacters(in: .whitespaces)
        let text = "正在同步 \(label.isEmpty ? "账号" : label)（\(max(1, index))/\(max(1, total))）…"
        return page > 1 ? text + "第 \(page) 页" : text
    }

    public static func formatCloudSyncNotify(ok: Bool, message: String) -> String {
        RustCore.string("format_cloud_sync_notify", ["ok": ok, "message": message])
    }

    static func formatCloudSyncNotifyCore(ok: Bool, message: String) -> String {
        if ok { return "" }
        let text = message.trimmingCharacters(in: .whitespaces)
        if text.isEmpty || AccountSync.isTrimNote(text) { return "" }
        return text
    }

    public static func exportFileSlug(_ label: String?) -> String {
        RustCore.string("export_file_slug", ["label": label ?? ""])
    }

    static func exportFileSlugCore(_ label: String?) -> String {
        let text = (label ?? "").trimmingCharacters(in: .whitespaces)
        if text.isEmpty { return "" }
        let cleaned = String(text.map { ch in
            ch.isLetter || ch.isNumber || (ch >= "\u{4e00}" && ch <= "\u{9fff}") || ch == "-" || ch == "_"
                ? ch
                : "-"
        }).trimmingCharacters(in: CharacterSet(charactersIn: "-"))
        return cleaned.count > 24 ? String(cleaned.prefix(24)) : cleaned
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

    static func formatExportFilenameCore(_ prefix: String, label: String? = nil, day: String? = nil) -> String {
        let stamp: String = {
            if let day, !day.trimmingCharacters(in: .whitespaces).isEmpty {
                return day.trimmingCharacters(in: .whitespaces)
            }
            let f = DateFormatter()
            f.locale = Locale(identifier: "en_US_POSIX")
            f.timeZone = .current
            f.dateFormat = "yyyyMMdd"
            return f.string(from: Date())
        }()
        let slug = exportFileSlug(label)
        return slug.isEmpty ? "\(prefix)-\(stamp).csv" : "\(prefix)-\(slug)-\(stamp).csv"
    }

    public static func formatCompareAccountName(_ name: String, isActive: Bool) -> String {
        RustCore.string("format_compare_account_name", ["name": name, "active": isActive])
    }

    static func formatCompareAccountNameCore(_ name: String, isActive: Bool) -> String {
        let text = name.trimmingCharacters(in: .whitespaces)
        let label = text.isEmpty ? "未命名账号" : text
        return isActive ? "\(label)  · 当前" : label
    }

    public static func formatTokenSaveResult(ok: Int, fail: Int) -> String {
        RustCore.string("format_token_save_result", ["ok": ok, "fail": fail])
    }

    static func formatTokenSaveResultCore(ok: Int, fail: Int) -> String {
        if ok <= 0 && fail <= 0 { return "" }
        if fail <= 0 { return ok > 1 ? "已保存 \(ok) 个账号" : "" }
        return "成功 \(ok) / 失败 \(fail)"
    }

    public static func formatExportEmpty() -> String { "当前没有可导出的明细" }

    public static func formatAccountMenuTitle(_ label: String?, remaining: Double?) -> String {
        RustCore.string("format_account_menu_title", ["label": label ?? "", "remaining": remaining as Any])
    }

    static func formatAccountMenuTitleCore(_ label: String?, remaining: Double?) -> String {
        let name = (label ?? "").trimmingCharacters(in: .whitespaces)
        let title = name.isEmpty ? "未命名账号" : name
        guard let remaining else { return title }
        return title + String(format: "  %.0f%%", remaining)
    }

    public static func formatReportCacheStatus(count: Int, accountLabel: String? = nil) -> String {
        RustCore.string("format_report_cache_status", ["count": count, "label": accountLabel ?? ""])
    }

    static func formatReportCacheStatusCore(count: Int, accountLabel: String? = nil) -> String {
        let trimmed = (accountLabel ?? "").trimmingCharacters(in: .whitespaces)
        let prefix = trimmed.isEmpty ? "" : "当前：\(trimmed) · "
        return count > 0 ? "\(prefix)本地 \(count) 条，正在刷新…" : "\(prefix)本地还没有明细，正在同步…"
    }

    public static func formatReportSyncError(_ error: String?) -> String {
        RustCore.string("format_report_sync_error", ["error": error ?? ""])
    }

    static func formatReportSyncErrorCore(_ error: String?) -> String {
        let text = (error ?? "").trimmingCharacters(in: .whitespaces)
        if text.isEmpty { return "同步失败" }
        if Token.isAuthErrorMessage(text) {
            return text.contains("未配置") ? "未配置 Token，请先在设置里导入账号" : "登录已过期，请到设置重新粘贴 Token"
        }
        return "同步失败：" + shortError(text, maxLen: 80)
    }

    public static func formatCloudDecryptNote(decryptError: Bool, syncSecretFailed: Bool, cloudAccessFailed: Bool) -> String {
        RustCore.string("format_cloud_decrypt_note", ["decrypt_error": decryptError, "sync_secret_failed": syncSecretFailed, "cloud_access_failed": cloudAccessFailed])
    }

    static func formatCloudDecryptNoteCore(decryptError: Bool, syncSecretFailed: Bool, cloudAccessFailed: Bool) -> String {
        if syncSecretFailed || cloudAccessFailed {
            return "本机解不开云同步密钥。请退出后用当前密码重新登录，或导入备份。"
        }
        if decryptError {
            return "本机有账号 Token 解不开。请重新粘贴 Token。"
        }
        return ""
    }

    public static func flyoutSettingsTitle(_ errorMessage: String?) -> String {
        Token.isAuthErrorMessage(errorMessage) ? "粘贴 Token" : "设置"
    }

    public static func formatAccountPlan(_ account: Account) -> String {
        RustCore.string("format_account_plan", ["membership": account.membershipType])
    }

    static func formatAccountPlanCore(_ account: Account) -> String {
        let plan = account.membershipType.trimmingCharacters(in: .whitespaces)
        return plan.isEmpty ? "—" : plan
    }

    public static func formatAccountCheckStatus(_ account: Account) -> String {
        RustCore.string("format_account_check_status", ["decrypt_failed": account.tokenDecryptFailed, "error": account.lastError, "remaining": account.lastRemaining as Any, "has_token": !account.token.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty])
    }

    static func formatAccountCheckStatusCore(_ account: Account) -> String {
        if account.tokenDecryptFailed { return "无法解密" }
        let err = account.lastError.trimmingCharacters(in: .whitespaces)
        if !err.isEmpty {
            if Token.isAuthErrorMessage(err) { return "已失效" }
            if isUnreachableStatus(err) { return "无法连接" }
            if err.count > 16 { return String(err.prefix(16)) + "…" }
            return err
        }
        if let remaining = account.lastRemaining {
            return String(format: "剩余 %.0f%%", remaining)
        }
        return account.token.trimmingCharacters(in: .whitespaces).isEmpty ? "未配置" : "未检查"
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

    static func formatAccountKindCore(_ account: Account) -> String {
        AccountValidity.isTemporary(account) ? "临时" : "长期"
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
