import Foundation

public struct UsageEvent: Equatable, Sendable {
    public var id: String
    public var timestampMs: Int64
    public var model: String
    public var kind: String
    public var userEmail: String
    public var owningUser: String
    public var tokens: Int
    public var inputTokens: Int
    public var outputTokens: Int
    public var cacheWriteTokens: Int
    public var cacheReadTokens: Int
    public var chargedCents: Double?
    public var totalCents: Double?
    public var isHeadless: Bool
    public var isChargeable: Bool
    public var allocatedCny: Double

    public init(
        id: String = "",
        timestampMs: Int64,
        model: String = "",
        kind: String = UsageEvents.kindOther,
        userEmail: String = "",
        owningUser: String = "",
        tokens: Int = 0,
        inputTokens: Int = 0,
        outputTokens: Int = 0,
        cacheWriteTokens: Int = 0,
        cacheReadTokens: Int = 0,
        chargedCents: Double? = nil,
        totalCents: Double? = nil,
        isHeadless: Bool = false,
        isChargeable: Bool = false,
        allocatedCny: Double = 0
    ) {
        self.id = id
        self.timestampMs = timestampMs
        self.model = model
        self.kind = kind
        self.userEmail = userEmail
        self.owningUser = owningUser
        self.tokens = tokens
        self.inputTokens = inputTokens
        self.outputTokens = outputTokens
        self.cacheWriteTokens = cacheWriteTokens
        self.cacheReadTokens = cacheReadTokens
        self.chargedCents = chargedCents
        self.totalCents = totalCents
        self.isHeadless = isHeadless
        self.isChargeable = isChargeable
        self.allocatedCny = allocatedCny
    }
}

public struct DailyUsageRow: Equatable, Sendable {
    public var date: String
    public var tokens: Int
    public var cents: Double
    public var count: Int
    public var cny: Double

    public init(date: String, tokens: Int, cents: Double, count: Int, cny: Double = 0) {
        self.date = date
        self.tokens = tokens
        self.cents = cents
        self.count = count
        self.cny = cny
    }
}

public struct ModelUsageRow: Equatable, Sendable {
    public var name: String
    public var tokens: Int
    public var cents: Double
    public var count: Int
    public var headlessCount: Int
    public var cny: Double

    public init(name: String, tokens: Int, cents: Double, count: Int, headlessCount: Int, cny: Double = 0) {
        self.name = name
        self.tokens = tokens
        self.cents = cents
        self.count = count
        self.headlessCount = headlessCount
        self.cny = cny
    }
}

public struct ChartSlice: Equatable, Sendable {
    public var model: String
    public var tokens: Int
    public var cents: Double
    public var count: Int
}

public struct ChartBucket: Equatable, Sendable, Identifiable {
    public var id: String { key }
    public var key: String
    public var label: String
    public var tokens: Int
    public var cents: Double
    public var count: Int
    public var slices: [ChartSlice]
}

public struct UsageChartSeries: Equatable, Sendable {
    public var hourly: Bool
    public var caption: String
    public var models: [String]
    public var buckets: [ChartBucket]
}

public struct UsageReportFilter: Equatable, Sendable {
    public var kind: String
    public var category: String
    public var model: String
    public var headless: Bool?
    public var owningUser: String
    public var startDate: String
    public var endDate: String

    public init(kind: String = "", category: String = "", model: String = "", headless: Bool? = nil, owningUser: String = "", startDate: String = "", endDate: String = "") {
        self.kind = kind
        self.category = category
        self.model = model
        self.headless = headless
        self.owningUser = owningUser
        self.startDate = startDate
        self.endDate = endDate
    }
}

public struct UsageReport: Equatable, Sendable {
    public var eventCount: Int
    public var totalTokens: Int
    public var totalCents: Double
    public var hasCost: Bool
    public var includedCount: Int
    public var freeCount: Int
    public var onDemandCount: Int
    public var otherCount: Int
    public var headlessCount: Int
    public var firstPartyCount: Int = 0
    public var apiCount: Int = 0
    public var grokBotCount: Int = 0
    public var daily: [DailyUsageRow]
    public var models: [ModelUsageRow]
    public var events: [UsageEvent]
    public var totalCny: Double = 0
    public var planCny: Double = 0
    public var onDemandCny: Double = 0
    public var usdCnyRate: Double = 0
    public var monthlyPlanUsd: Double = 0
    public var actualCny: Double = 0
    public var usesActualCny: Bool = false
    public var windowPlanCny: Double = 0
    public var cnyPerMillion: Double? { UsageEvents.unitCny(totalCny, totalTokens > 0 ? Double(totalTokens) / 1_000_000.0 : 0) }
}

public struct CnySpendSettings: Equatable, Sendable {
    public var monthlyPlanUsd: Double
    public var usdCnyRate: Double
    public var membershipType: String
    public var actualCny: Double

    public init(monthlyPlanUsd: Double = 0, usdCnyRate: Double = UsageEvents.defaultUsdCnyRate, membershipType: String = "", actualCny: Double = 0) {
        self.monthlyPlanUsd = monthlyPlanUsd
        self.usdCnyRate = usdCnyRate
        self.membershipType = membershipType
        self.actualCny = actualCny
    }
}

public struct ReportAllocationWindow: Equatable, Sendable {
    public var accountKind: String
    public var tempStartAt: String
    public var tempValidDays: Int
    public var tempValidHours: Int
    public var billingCycleStart: String
    public var billingCycleEnd: String
    public var nowMs: Int64?

    public init(
        accountKind: String = "",
        tempStartAt: String = "",
        tempValidDays: Int = 0,
        tempValidHours: Int = 0,
        billingCycleStart: String = "",
        billingCycleEnd: String = "",
        nowMs: Int64? = nil
    ) {
        self.accountKind = accountKind
        self.tempStartAt = tempStartAt
        self.tempValidDays = tempValidDays
        self.tempValidHours = tempValidHours
        self.billingCycleStart = billingCycleStart
        self.billingCycleEnd = billingCycleEnd
        self.nowMs = nowMs
    }

    public var hasWindow: Bool {
        if !billingCycleStart.trimmingCharacters(in: .whitespaces).isEmpty
            || !billingCycleEnd.trimmingCharacters(in: .whitespaces).isEmpty
        {
            return true
        }
        return AccountValidity.sanitizeKind(accountKind) == AccountValidity.temporary
            && (!tempStartAt.trimmingCharacters(in: .whitespaces).isEmpty || tempValidDays > 0 || tempValidHours > 0)
    }

    public static func from(account: Account?, usage: UsageSnapshot?, nowMs: Int64? = nil) -> ReportAllocationWindow {
        ReportAllocationWindow(
            accountKind: account?.accountKind ?? "",
            tempStartAt: account?.tempStartAt ?? "",
            tempValidDays: account?.tempValidDays ?? 0,
            tempValidHours: account?.tempValidHours ?? 0,
            billingCycleStart: firstNonEmpty(usage?.billingCycleStart, account?.billingCycleStart),
            billingCycleEnd: firstNonEmpty(usage?.billingCycleEnd, account?.billingCycleEnd),
            nowMs: nowMs
        )
    }

    static func firstNonEmpty(_ left: String?, _ right: String?) -> String {
        let a = (left ?? "").trimmingCharacters(in: .whitespaces)
        if !a.isEmpty { return a }
        return (right ?? "").trimmingCharacters(in: .whitespaces)
    }
}

public struct AccountCompareCategory: Equatable, Sendable {
    public var category: String
    public var count: Int
    public var tokens: Int
    public var cny: Double

    public init(category: String, count: Int = 0, tokens: Int = 0, cny: Double = 0) {
        self.category = category
        self.count = count
        self.tokens = tokens
        self.cny = cny
    }

    public var cnyPerMillion: Double? { UsageEvents.unitCny(cny, tokens > 0 ? Double(tokens) / 1_000_000.0 : 0) }
    public var cnyPerRequest: Double? { UsageEvents.unitCny(cny, Double(count)) }
}

public struct AccountCompareInput: Sendable {
    public var accountId: String
    public var label: String
    public var channel: String
    public var membershipType: String
    public var accountKind: String
    public var tempStartAt: String
    public var tempValidDays: Int
    public var tempValidHours: Int
    public var billingCycleStart: String
    public var billingCycleEnd: String
    public var lastRemaining: Double?
    public var events: [UsageEvent]
    public var spend: CnySpendSettings?

    public init(
        accountId: String,
        label: String = "",
        channel: String = "",
        membershipType: String = "",
        accountKind: String = "long_term",
        tempStartAt: String = "",
        tempValidDays: Int = 0,
        tempValidHours: Int = 0,
        billingCycleStart: String = "",
        billingCycleEnd: String = "",
        lastRemaining: Double? = nil,
        events: [UsageEvent] = [],
        spend: CnySpendSettings? = nil
    ) {
        self.accountId = accountId
        self.label = label
        self.channel = channel
        self.membershipType = membershipType
        self.accountKind = accountKind
        self.tempStartAt = tempStartAt
        self.tempValidDays = tempValidDays
        self.tempValidHours = tempValidHours
        self.billingCycleStart = billingCycleStart
        self.billingCycleEnd = billingCycleEnd
        self.lastRemaining = lastRemaining
        self.events = events
        self.spend = spend
    }
}

public struct AccountCompareRow: Equatable, Sendable {
    public var accountId: String
    public var label: String
    public var channel: String
    public var membershipType: String
    public var windowSource: String
    public var windowStartMs: Int64
    public var windowEndMs: Int64
    public var windowDays: Double
    public var planCny: Double
    public var dailyHoldingCny: Double
    public var windowPlanCny: Double
    public var onDemandCny: Double
    public var totalCny: Double
    public var eventCount: Int
    public var totalTokens: Int
    public var firstParty: AccountCompareCategory
    public var api: AccountCompareCategory
    public var grokBot: AccountCompareCategory
    public var lastRemaining: Double?
    public var usesActualCny: Bool

    public init(
        accountId: String = "",
        label: String = "",
        channel: String = "",
        membershipType: String = "",
        windowSource: String = "",
        windowStartMs: Int64 = 0,
        windowEndMs: Int64 = 0,
        windowDays: Double = 0,
        planCny: Double = 0,
        dailyHoldingCny: Double = 0,
        windowPlanCny: Double = 0,
        onDemandCny: Double = 0,
        totalCny: Double = 0,
        eventCount: Int = 0,
        totalTokens: Int = 0,
        firstParty: AccountCompareCategory = AccountCompareCategory(category: UsageEvents.categoryFirstParty),
        api: AccountCompareCategory = AccountCompareCategory(category: UsageEvents.categoryAPI),
        grokBot: AccountCompareCategory = AccountCompareCategory(category: UsageEvents.categoryGrokBot),
        lastRemaining: Double? = nil,
        usesActualCny: Bool = false
    ) {
        self.accountId = accountId
        self.label = label
        self.channel = channel
        self.membershipType = membershipType
        self.windowSource = windowSource
        self.windowStartMs = windowStartMs
        self.windowEndMs = windowEndMs
        self.windowDays = windowDays
        self.planCny = planCny
        self.dailyHoldingCny = dailyHoldingCny
        self.windowPlanCny = windowPlanCny
        self.onDemandCny = onDemandCny
        self.totalCny = totalCny
        self.eventCount = eventCount
        self.totalTokens = totalTokens
        self.firstParty = firstParty
        self.api = api
        self.grokBot = grokBot
        self.lastRemaining = lastRemaining
        self.usesActualCny = usesActualCny
    }

    public var channelLabel: String { UsageEvents.channelLabel(channel) }
    public var windowLabel: String { UsageEvents.windowLabel(windowSource) }
    public var cnyPerMillion: Double? { UsageEvents.unitCny(totalCny, totalTokens > 0 ? Double(totalTokens) / 1_000_000.0 : 0) }
    public var cnyPerRequest: Double? { UsageEvents.unitCny(totalCny, Double(eventCount)) }
}

public struct AccountCompareGroup: Equatable, Sendable {
    public var channel: String
    public var rows: [AccountCompareRow]
    public var dailyHoldingCny: Double
    public var totalCny: Double
    public var eventCount: Int
    public var totalTokens: Int
    public var firstParty: AccountCompareCategory
    public var api: AccountCompareCategory
    public var grokBot: AccountCompareCategory

    public var channelLabel: String { UsageEvents.channelLabel(channel) }
    public var cnyPerMillion: Double? { UsageEvents.unitCny(totalCny, totalTokens > 0 ? Double(totalTokens) / 1_000_000.0 : 0) }
    public var cnyPerRequest: Double? { UsageEvents.unitCny(totalCny, Double(eventCount)) }
}

public struct AccountCompareReport: Equatable, Sendable {
    public var rows: [AccountCompareRow]
    public var groups: [AccountCompareGroup]
    public var holdingDays: Double

    public init(rows: [AccountCompareRow] = [], groups: [AccountCompareGroup] = [], holdingDays: Double = UsageEvents.holdingDays) {
        self.rows = rows
        self.groups = groups
        self.holdingDays = holdingDays
    }
}

public struct UsageEventsWindow: Equatable, Sendable {
    public var startMs: Int64
    public var endMs: Int64
    public var stopAtMs: Int64?

    public init(startMs: Int64, endMs: Int64, stopAtMs: Int64? = nil) {
        self.startMs = startMs
        self.endMs = endMs
        self.stopAtMs = stopAtMs
    }
}

public struct UsageEventsSyncResult: Sendable {
    public var events: [UsageEvent]
    public var fetched: Int
    public var totalAvailable: Int
    public var truncated: Bool
    public var note: String

    public init(events: [UsageEvent], fetched: Int, totalAvailable: Int, truncated: Bool, note: String = "") {
        self.events = events
        self.fetched = fetched
        self.totalAvailable = totalAvailable
        self.truncated = truncated
        self.note = note
    }
}

public enum UsageEvents {
    public static let noteTeamPersonal = "team_personal"
    public static let kindIncluded = "included"
    public static let kindFree = "free"
    public static let kindOnDemand = "on_demand"
    public static let kindOther = "other"
    public static let categoryFirstParty = "first_party"
    public static let categoryAPI = "api"
    public static let categoryGrokBot = "grok_bot"
    public static let channelSelfPay = "self_pay"
    public static let channelThirdParty = "third_party"
    public static let holdingDays = 30.0
    public static let windowCycle = "cycle"
    public static let windowValidity = "validity"
    public static let windowFallback = "fallback"
    static let channelOrder = [channelSelfPay, channelThirdParty, ""]
    public static let tzLabel = "本地时间"
    public static let csvHeader = "日期(本地时间),用户,类型,模型,Token,费用,实付,折扣,云端Agent"
    public static let defaultUsdCnyRate = 7.50
    public static let hourlyChartWindowHours = 48
    static let msHour: Int64 = 3_600_000
    static let msDay: Int64 = 86_400_000
    public static var displayTimeZone = TimeZone.current

    public static func kindLabel(_ kind: String?) -> String {
        RustCore.string("kind_label", ["kind": kind ?? ""])
    }

    public static func classifyCategory(_ model: String?) -> String {
        RustCore.string("classify_category", ["model": model ?? ""])
    }

    public static func categoryLabel(_ category: String?) -> String {
        RustCore.string("category_label", ["category": category ?? ""])
    }

    public static func clampActualCny(_ amount: Double) -> Double {
        if amount.isNaN || amount.isInfinite || amount < 0 { return 0 }
        return min(1_000_000, amount)
    }

    public static func resolvePlanCny(_ spend: CnySpendSettings) -> (planCny: Double, monthly: Double, rate: Double, actual: Double, usesActual: Bool) {
        let rate = clampUsdCnyRate(spend.usdCnyRate)
        let monthly = resolveMonthlyPlanUsd(spend.monthlyPlanUsd, membership: spend.membershipType)
        let actual = clampActualCny(spend.actualCny)
        if actual > 0 { return (actual, monthly, rate, actual, true) }
        return (monthly * rate, monthly, rate, 0, false)
    }

    public static func classifyKind(_ kind: String?, usageBasedCosts: String? = nil, isChargeable: Bool = false) -> String {
        RustCore.string("classify_kind", ["kind": kind ?? "", "costs": usageBasedCosts ?? "", "chargeable": isChargeable])
    }

    public static func costCents(_ ev: UsageEvent) -> Double {
        if let charged = ev.chargedCents { return max(0, charged) }
        if let total = ev.totalCents { return max(0, total) }
        return 0
    }

    public static func formatCost(_ ev: UsageEvent) -> String {
        RustCore.string("format_event_cost", ["event": eventJSON(ev)])
    }

    public static func clampUsdCnyRate(_ rate: Double) -> Double {
        if rate.isNaN || rate.isInfinite { return defaultUsdCnyRate }
        return min(100, max(0.01, rate))
    }

    public static func clampMonthlyPlanUsd(_ usd: Double) -> Double {
        if usd.isNaN || usd.isInfinite || usd < 0 { return 0 }
        return min(10_000, usd)
    }

    public static func defaultMonthlyPlanUsd(_ membership: String?) -> Double {
        doubleValue(RustCore.call("default_monthly_plan_usd", ["membership": membership ?? ""]))
    }

    public static func resolveMonthlyPlanUsd(_ monthlyPlanUsd: Double, membership: String? = nil) -> Double {
        let stored = clampMonthlyPlanUsd(monthlyPlanUsd)
        return stored > 0 ? stored : defaultMonthlyPlanUsd(membership)
    }

    public static func isPlanCovered(_ kind: String?) -> Bool {
        let key = (kind ?? "").trimmingCharacters(in: .whitespaces).lowercased()
        return key != kindOnDemand && key != kindFree
    }

    public static func isCostPoolKind(_ kind: String?, usesActual: Bool) -> Bool {
        let key = (kind ?? "").trimmingCharacters(in: .whitespaces).lowercased()
        if key == kindFree { return false }
        return usesActual || isPlanCovered(key)
    }

    public static func sanitizeChannel(_ raw: String?) -> String {
        RustCore.string("sanitize_channel", ["input": raw ?? ""])
    }

    public static func channelLabel(_ channel: String?) -> String {
        switch sanitizeChannel(channel) {
        case channelSelfPay: return "自费"
        case channelThirdParty: return "第三方"
        default: return "未标"
        }
    }

    public static func windowLabel(_ source: String?) -> String {
        switch (source ?? "").trimmingCharacters(in: .whitespaces).lowercased() {
        case windowCycle: return "本周期"
        case windowValidity: return "有效期"
        default: return "近30天"
        }
    }

    public static func unitCny(_ amount: Double, _ denom: Double) -> Double? {
        if denom <= 1e-12 { return nil }
        return max(0, amount) / denom
    }

    public static func compareRowHasCost(_ row: AccountCompareRow) -> Bool {
        row.planCny > 0 || row.windowPlanCny > 0
    }

    public static func compareBestEligible(_ row: AccountCompareRow) -> Bool {
        row.cnyPerMillion != nil && compareRowHasCost(row)
    }

    public static func compareBestPerMillion(_ rows: [AccountCompareRow]) -> Double? {
        RustCore.doubleValue("compare_best_per_million", ["rows": rows.map(compareMetricJSON)])
    }

    static func compareMetricJSON(_ row: AccountCompareRow) -> [String: Any] {
        ["plan_cny": row.planCny, "window_plan_cny": row.windowPlanCny, "cny_per_million": row.cnyPerMillion as Any, "window_source": row.windowSource]
    }

    public static func compareWindowsMixed(_ rows: [AccountCompareRow]) -> Bool {
        RustCore.bool("compare_windows_mixed", ["rows": rows.map(compareMetricJSON)])
    }

    public static func compareRowNote(_ row: AccountCompareRow, hasToken: Bool = true, lastError: String = "") -> String {
        RustCore.string("compare_row_note", ["plan_cny": row.planCny, "window_plan_cny": row.windowPlanCny, "has_token": hasToken, "last_error": lastError])
    }
        if !hasToken { return "未配置 Token" }
        if !error.isEmpty && (error.contains("过期") || error.contains("无效") || error.contains("未配置")) {
            return error.contains("未配置") ? "未配置 Token" : "登录已过期"
        }
        if !compareRowHasCost(row) { return "未填成本" }
        return ""
    }

    public static func formatCnyUnit(_ amount: Double?, suffix: String) -> String {
        guard let amount else { return "—" }
        return formatCNY(amount) + suffix
    }

    public static func resolveCompareWindow(
        accountKind: String = "",
        tempStartAt: String = "",
        tempValidDays: Int = 0,
        tempValidHours: Int = 0,
        billingCycleStart: String = "",
        billingCycleEnd: String = "",
        nowMs: Int64? = nil
    ) -> (startMs: Int64, endMs: Int64, source: String) {
        let now = nowMs ?? Int64(Date().timeIntervalSince1970 * 1000)
        let kind = accountKind.trimmingCharacters(in: .whitespaces).lowercased().replacingOccurrences(of: "-", with: "_")
        if ["temporary", "temp", "short"].contains(kind) {
            if let start = UsageParser.isoToMs(tempStartAt).map({ Int64($0) }) {
                let end = tempEndMs(tempStartAt, days: tempValidDays, hours: tempValidHours) ?? now
                return (start, min(end, now), windowValidity)
            }
        }
        if let start = UsageParser.isoToMs(billingCycleStart).map({ Int64($0) }) {
            let end = UsageParser.isoToMs(billingCycleEnd).map({ Int64($0) }) ?? now
            return (start, min(end, now), windowCycle)
        }
        return (now - 30 * msDay, now, windowFallback)
    }

    static func tempEndMs(_ startAt: String, days: Int, hours: Int) -> Int64? {
        guard let iso = AccountValidity.computeEndIso(startAt: startAt, days: days, hours: hours) else { return nil }
        return UsageParser.isoToMs(iso).map { Int64($0) }
    }

    public static func compareWindowDays(_ startMs: Int64, _ endMs: Int64) -> Double {
        let span = max(0, endMs - startMs)
        return max(Double(span) / Double(msDay), 1.0 / 24.0)
    }

    public static func compareInput(from account: Account, events: [UsageEvent], monthlyPlanUsd: Double, usdCnyRate: Double) -> AccountCompareInput {
        AccountCompareInput(
            accountId: account.id,
            label: account.label,
            channel: account.channel,
            membershipType: account.membershipType,
            accountKind: account.accountKind,
            tempStartAt: account.tempStartAt,
            tempValidDays: account.tempValidDays,
            tempValidHours: account.tempValidHours,
            billingCycleStart: account.billingCycleStart,
            billingCycleEnd: account.billingCycleEnd,
            lastRemaining: account.lastRemaining,
            events: events,
            spend: CnySpendSettings(
                monthlyPlanUsd: monthlyPlanUsd,
                usdCnyRate: usdCnyRate,
                membershipType: account.membershipType,
                actualCny: account.actualCny
            )
        )
    }

    public static func buildAccountCompareRow(_ item: AccountCompareInput, nowMs: Int64? = nil) -> AccountCompareRow {
        let window = resolveCompareWindow(
            accountKind: item.accountKind,
            tempStartAt: item.tempStartAt,
            tempValidDays: item.tempValidDays,
            tempValidHours: item.tempValidHours,
            billingCycleStart: item.billingCycleStart,
            billingCycleEnd: item.billingCycleEnd,
            nowMs: nowMs
        )
        let windowDays = compareWindowDays(window.startMs, window.endMs)
        let windowEvents = item.events.filter { $0.timestampMs >= window.startMs && $0.timestampMs <= window.endMs }
        let spend = item.spend ?? CnySpendSettings()
        let resolved = resolvePlanCny(spend)
        let dailyHolding = resolved.planCny > 0 ? resolved.planCny / holdingDays : 0
        var windowPlan = dailyHolding * windowDays
        if resolved.usesActual && resolved.actual > 0 {
            windowPlan = min(windowPlan, resolved.actual)
        }
        let pool = windowEvents.filter { isCostPoolKind($0.kind, usesActual: resolved.usesActual) }
        let includedCostSum = pool.reduce(0.0) { $0 + costCents($1) }
        let includedCount = pool.count
        var cats: [String: (Int, Int, Double)] = [
            categoryFirstParty: (0, 0, 0),
            categoryAPI: (0, 0, 0),
            categoryGrokBot: (0, 0, 0),
        ]
        var onDemandCny = 0.0
        var totalCny = 0.0
        var totalTokens = 0
        for ev in windowEvents {
            let amount = allocateEventCny(ev, includedCostSum: includedCostSum, includedCount: includedCount, planCny: windowPlan, rate: resolved.rate, usesActual: resolved.usesActual)
            totalCny += amount
            totalTokens += ev.tokens
            if ev.kind == kindOnDemand { onDemandCny += amount }
            let bucket = classifyCategory(ev.model)
            var row = cats[bucket] ?? (0, 0, 0)
            row.0 += 1
            row.1 += ev.tokens
            row.2 += amount
            cats[bucket] = row
        }
        func cat(_ name: String) -> AccountCompareCategory {
            let row = cats[name] ?? (0, 0, 0)
            return AccountCompareCategory(category: name, count: row.0, tokens: row.1, cny: row.2)
        }
        return AccountCompareRow(
            accountId: item.accountId,
            label: item.label.isEmpty ? item.accountId : item.label,
            channel: sanitizeChannel(item.channel),
            membershipType: item.membershipType,
            windowSource: window.source,
            windowStartMs: window.startMs,
            windowEndMs: window.endMs,
            windowDays: windowDays,
            planCny: resolved.planCny,
            dailyHoldingCny: dailyHolding,
            windowPlanCny: windowPlan,
            onDemandCny: onDemandCny,
            totalCny: totalCny,
            eventCount: windowEvents.count,
            totalTokens: totalTokens,
            firstParty: cat(categoryFirstParty),
            api: cat(categoryAPI),
            grokBot: cat(categoryGrokBot),
            lastRemaining: item.lastRemaining,
            usesActualCny: resolved.usesActual
        )
    }

    public static func buildAccountCompareReport(_ items: [AccountCompareInput], nowMs: Int64? = nil) -> AccountCompareReport {
        let result = RustCore.object("build_account_compare", [
            "now_ms": nowMs as Any,
            "items": items.map { item in
                [
                    "account_id": item.accountId,
                    "label": item.label,
                    "channel": item.channel,
                    "membership_type": item.membershipType,
                    "account_kind": item.accountKind,
                    "temp_start_at": item.tempStartAt,
                    "temp_valid_days": item.tempValidDays,
                    "temp_valid_hours": item.tempValidHours,
                    "billing_cycle_start": item.billingCycleStart,
                    "billing_cycle_end": item.billingCycleEnd,
                    "spend": item.spend.map { ["monthly_plan_usd": $0.monthlyPlanUsd, "usd_cny_rate": $0.usdCnyRate, "membership_type": $0.membershipType, "actual_cny": $0.actualCny] } as Any,
                    "events": item.events.map(eventJSON),
                ] as [String: Any]
            },
        ])
        func cat(_ row: [String: Any], _ name: String) -> AccountCompareCategory {
            let part = row[name] as? [String: Any] ?? [:]
            return AccountCompareCategory(category: name, count: intValue(part["count"]), tokens: intValue(part["tokens"]), cny: doubleValue(part["cny"]))
        }
        let rows = (result["rows"] as? [[String: Any]] ?? []).map { row in
            AccountCompareRow(
                accountId: row["account_id"] as? String ?? "",
                label: row["label"] as? String ?? "",
                channel: row["channel"] as? String ?? "",
                membershipType: row["membership_type"] as? String ?? "",
                windowSource: row["window_source"] as? String ?? "",
                windowDays: doubleValue(row["window_days"]),
                planCny: doubleValue(row["plan_cny"]),
                dailyHoldingCny: doubleValue(row["daily_holding_cny"]),
                windowPlanCny: doubleValue(row["window_plan_cny"]),
                onDemandCny: doubleValue(row["on_demand_cny"]),
                totalCny: doubleValue(row["total_cny"]),
                eventCount: intValue(row["event_count"]),
                totalTokens: intValue(row["total_tokens"]),
                firstParty: cat(row, "first_party"),
                api: cat(row, "api"),
                grokBot: cat(row, "grok_bot")
            )
        }
        let groups = (result["groups"] as? [[String: Any]] ?? []).map { row in
            AccountCompareGroup(channel: row["channel"] as? String ?? "", rows: [], dailyHoldingCny: doubleValue(row["daily_holding_cny"]), totalCny: doubleValue(row["total_cny"]), eventCount: intValue(row["event_count"]), totalTokens: intValue(row["total_tokens"]), firstParty: cat(row, "first_party"), api: cat(row, "api"), grokBot: cat(row, "grok_bot"))
        }
        return AccountCompareReport(rows: rows, groups: groups, holdingDays: holdingDays)
    }

    public static func formatCNY(_ yuan: Double?) -> String {
        RustCore.string("format_cny", ["cny": yuan as Any])
    }

    public static func formatEventCny(_ ev: UsageEvent, amount: Double? = nil) -> String {
        if ev.kind == kindFree { return "—" }
        return formatCNY(amount ?? ev.allocatedCny)
    }

    public static func formatDiscount(cny: Double, cents: Double, rate: Double, kind: String = "") -> String {
        RustCore.string("format_discount", ["cny": cny, "cents": cents, "rate": rate, "kind": kind])
    }

    public static func formatEventDiscount(_ ev: UsageEvent, amount: Double? = nil, rate: Double) -> String {
        formatDiscount(cny: amount ?? ev.allocatedCny, cents: costCents(ev), rate: rate, kind: ev.kind)
    }

    public static func allocateEventCny(
        _ ev: UsageEvent,
        includedCostSum: Double,
        includedCount: Int,
        planCny: Double,
        rate: Double,
        usesActual: Bool = false
    ) -> Double {
        if ev.kind == kindFree { return 0 }
        if ev.kind == kindOnDemand && !usesActual { return costCents(ev) / 100.0 * rate }
        let cents = costCents(ev)
        if includedCostSum > 1e-9 { return planCny * (cents / includedCostSum) }
        if includedCount > 0 && planCny > 0 { return planCny / Double(includedCount) }
        return 0
    }

    static func cnyById(_ events: [UsageEvent], spend: CnySpendSettings?, allocation: ReportAllocationWindow? = nil) -> (byId: [String: Double], planCny: Double, onDemandCny: Double, monthly: Double, rate: Double, actual: Double, usesActual: Bool, windowPlan: Double) {
        guard let spend else { return ([:], 0, 0, 0, 0, 0, false, 0) }
        let resolved = resolvePlanCny(spend)
        var windowPlan = resolved.planCny
        var startMs: Int64?
        var endMs: Int64?
        if let allocation, allocation.hasWindow {
            let window = resolveCompareWindow(
                accountKind: allocation.accountKind,
                tempStartAt: allocation.tempStartAt,
                tempValidDays: allocation.tempValidDays,
                tempValidHours: allocation.tempValidHours,
                billingCycleStart: allocation.billingCycleStart,
                billingCycleEnd: allocation.billingCycleEnd,
                nowMs: allocation.nowMs
            )
            startMs = window.startMs
            endMs = window.endMs
            let daily = resolved.planCny > 0 ? resolved.planCny / holdingDays : 0
            windowPlan = daily * compareWindowDays(window.startMs, window.endMs)
            if resolved.usesActual && resolved.actual > 0 {
                windowPlan = min(windowPlan, resolved.actual)
            }
        }
        let allocEvents = events.filter { ev in
            guard let startMs, let endMs else { return true }
            return ev.timestampMs >= startMs && ev.timestampMs <= endMs
        }
        let pool = allocEvents.filter { isCostPoolKind($0.kind, usesActual: resolved.usesActual) }
        let includedCostSum = pool.reduce(0.0) { $0 + costCents($1) }
        let includedCount = pool.count
        var byId: [String: Double] = [:]
        var onDemandCny = 0.0
        for (i, ev) in events.enumerated() {
            let inWindow = startMs == nil || endMs == nil || (ev.timestampMs >= startMs! && ev.timestampMs <= endMs!)
            let amount = inWindow
                ? allocateEventCny(ev, includedCostSum: includedCostSum, includedCount: includedCount, planCny: windowPlan, rate: resolved.rate, usesActual: resolved.usesActual)
                : 0
            let key = ev.id.isEmpty ? "#\(i)" : ev.id
            byId[key] = amount
            if ev.kind == kindOnDemand { onDemandCny += amount }
        }
        return (byId, resolved.planCny, onDemandCny, resolved.monthly, resolved.rate, resolved.actual, resolved.usesActual, windowPlan)
    }

    public static func formatTime(_ timestampMs: Int64) -> String {
        RustCore.string("format_event_time", ["timestamp_ms": timestampMs])
    }

    public static func eventDate(_ timestampMs: Int64) -> String {
        RustCore.string("event_date", ["timestamp_ms": timestampMs])
    }

    public static func sanitizeReportDate(_ raw: Any?) -> String {
        let text = (raw as? String ?? "").trimmingCharacters(in: .whitespaces)
        guard !text.isEmpty else { return "" }
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = displayTimeZone
        f.dateFormat = "yyyy-MM-dd"
        f.isLenient = false
        return f.date(from: text) == nil ? "" : text
    }

    public static func normalizeReportRange(_ start: Any?, _ end: Any?) -> (start: String, end: String, swapped: Bool) {
        let result = RustCore.object("normalize_report_range", ["start": "\(start ?? "")", "end": "\(end ?? "")"])
        return (result["start"] as? String ?? "", result["end"] as? String ?? "", result["swapped"] as? Bool ?? false)
    }

    public static func reportDateStartMs(_ raw: Any?) -> Int64? {
        let date = sanitizeReportDate(raw)
        guard !date.isEmpty else { return nil }
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = displayTimeZone
        f.dateFormat = "yyyy-MM-dd"
        f.isLenient = false
        guard let parsed = f.date(from: date) else { return nil }
        return Int64((parsed.timeIntervalSince1970 * 1000.0).rounded())
    }

    public static func reportDateEndMs(_ raw: Any?) -> Int64? {
        guard let start = reportDateValue(raw) else { return nil }
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = displayTimeZone
        guard let end = cal.date(byAdding: .day, value: 1, to: start) else { return nil }
        return Int64((end.timeIntervalSince1970 * 1000.0).rounded())
    }

    public static func reportDateValue(_ raw: Any?) -> Date? {
        let date = sanitizeReportDate(raw)
        guard !date.isEmpty else { return nil }
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = displayTimeZone
        f.dateFormat = "yyyy-MM-dd"
        f.isLenient = false
        return f.date(from: date)
    }

    public static func reportDateString(from date: Date) -> String {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = displayTimeZone
        f.dateFormat = "yyyy-MM-dd"
        return f.string(from: date)
    }

    public static func eventHour(_ timestampMs: Int64) -> String {
        RustCore.string("event_hour", ["timestamp_ms": timestampMs])
    }

    public static func chartModelLabel(_ name: String?) -> String {
        RustCore.string("chart_model_label", ["input": name ?? ""])
    }

    public static func buildChart(
        _ events: [UsageEvent],
        hourly: Bool,
        hiddenModels: Set<String> = [],
        hourlyWindowHours: Int = hourlyChartWindowHours
    ) -> UsageChartSeries {
        let result = RustCore.object("build_usage_chart", [
            "events": events.map(eventJSON),
            "hourly": hourly,
            "hidden_models": Array(hiddenModels),
        ])
        let buckets = (result["buckets"] as? [[String: Any]] ?? []).map { row in
            let slices = (row["slices"] as? [[String: Any]] ?? []).map { slice in
                ChartSlice(model: slice["model"] as? String ?? "", tokens: intValue(slice["tokens"]), cents: doubleValue(slice["cents"]), count: intValue(slice["count"]))
            }
            return ChartBucket(key: row["key"] as? String ?? "", label: row["label"] as? String ?? "", tokens: intValue(row["tokens"]), cents: doubleValue(row["cents"]), count: intValue(row["count"]), slices: slices)
        }
        return UsageChartSeries(hourly: result["hourly"] as? Bool ?? hourly, caption: result["caption"] as? String ?? "", models: result["models"] as? [String] ?? [], buckets: buckets)
    }

    static func floorHourMs(_ timestampMs: Int64) -> Int64 {
        max(0, timestampMs) / msHour * msHour
    }

    static func floorDayMs(_ timestampMs: Int64) -> Int64 {
        let dt = Date(timeIntervalSince1970: Double(max(0, timestampMs)) / 1000.0)
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = displayTimeZone
        let start = cal.startOfDay(for: dt)
        return Int64((max(0, start.timeIntervalSince1970) * 1000.0).rounded())
    }

    static func chartModels(_ events: [UsageEvent]) -> [String] {
        var totals: [String: (Int, Double, Int)] = [:]
        for ev in events {
            let name = ev.model.isEmpty ? "—" : ev.model
            let row = totals[name] ?? (0, 0, 0)
            totals[name] = (row.0 + ev.tokens, row.1 + costCents(ev), row.2 + 1)
        }
        return totals.keys.sorted { lhs, rhs in
            let a = totals[lhs]!, b = totals[rhs]!
            if a.0 != b.0 { return a.0 > b.0 }
            if a.1 != b.1 { return a.1 > b.1 }
            if a.2 != b.2 { return a.2 > b.2 }
            return lhs < rhs
        }
    }

    static func bucketLabel(_ key: String, hourly: Bool, multiDay: Bool) -> String {
        if !hourly {
            return key.count >= 10 ? String(key.dropFirst(5)) : key
        }
        let hour: String
        if key.count >= 13 {
            let start = key.index(key.startIndex, offsetBy: 11)
            let end = key.index(start, offsetBy: 2)
            hour = String(key[start..<end])
        } else {
            hour = key
        }
        if multiDay {
            let mdStart = key.index(key.startIndex, offsetBy: 5)
            let mdEnd = key.index(mdStart, offsetBy: 5)
            return "\(key[mdStart..<mdEnd]) \(hour)"
        }
        return hour
    }

    static func chartCaption(hourly: Bool, keys: [String]) -> String {
        let kind = hourly ? "按小时 Token" : "按日 Token"
        if keys.isEmpty { return "\(kind)（\(tzLabel)）" }
        let first = keys[0], last = keys[keys.count - 1]
        if first == last { return "\(kind)（\(tzLabel) · \(first)）" }
        if hourly && first.prefix(10) == last.prefix(10) {
            return "\(kind)（\(tzLabel) · \(first.prefix(10)) \(first.dropFirst(11))–\(last.dropFirst(11))）"
        }
        return "\(kind)（\(tzLabel) · \(first) 至 \(last)）"
    }

    public static func parsePage(_ payload: JSONValue) -> (events: [UsageEvent], totalCount: Int) {
        let result = RustCore.object("parse_usage_page", ["payload": payload.raw])
        let events = (result["events"] as? [[String: Any]] ?? []).map(eventFromRust)
        return (events, result["total_count"] as? Int ?? events.count)
    }

    static func eventJSON(_ ev: UsageEvent) -> [String: Any] {
        [
            "id": ev.id,
            "timestamp_ms": ev.timestampMs,
            "model": ev.model,
            "kind": ev.kind,
            "user_email": ev.userEmail,
            "owning_user": ev.owningUser,
            "tokens": ev.tokens,
            "input_tokens": ev.inputTokens,
            "output_tokens": ev.outputTokens,
            "cache_write_tokens": ev.cacheWriteTokens,
            "cache_read_tokens": ev.cacheReadTokens,
            "charged_cents": ev.chargedCents as Any,
            "total_cents": ev.totalCents as Any,
            "is_headless": ev.isHeadless,
            "is_chargeable": ev.isChargeable,
        ]
    }

    static func eventFromRust(_ row: [String: Any]) -> UsageEvent {
        func whole(_ key: String) -> Int { row[key] as? Int ?? (row[key] as? NSNumber).map { Int($0.doubleValue) } ?? 0 }
        func money(_ key: String) -> Double? { row[key] as? Double ?? (row[key] as? NSNumber)?.doubleValue }
        return UsageEvent(
            id: row["id"] as? String ?? "",
            timestampMs: row["timestamp_ms"] as? Int64 ?? (row["timestamp_ms"] as? NSNumber)?.int64Value ?? 0,
            model: row["model"] as? String ?? "",
            kind: row["kind"] as? String ?? kindOther,
            userEmail: row["user_email"] as? String ?? "",
            owningUser: row["owning_user"] as? String ?? "",
            tokens: whole("tokens"),
            inputTokens: whole("input_tokens"),
            outputTokens: whole("output_tokens"),
            cacheWriteTokens: whole("cache_write_tokens"),
            cacheReadTokens: whole("cache_read_tokens"),
            chargedCents: money("charged_cents"),
            totalCents: money("total_cents"),
            isHeadless: row["is_headless"] as? Bool ?? false,
            isChargeable: row["is_chargeable"] as? Bool ?? false
        )
    }

        let kind = classifyKind(kindRaw, usageBasedCosts: costsRaw, isChargeable: isChargeable)
        let input = max(0, tokenUsage["inputTokens"].asInt() ?? item["inputTokens"].asInt() ?? 0)
        let output = max(0, tokenUsage["outputTokens"].asInt() ?? item["outputTokens"].asInt() ?? 0)
        let cacheWrite = max(0, tokenUsage["cacheWriteTokens"].asInt() ?? item["cacheWriteTokens"].asInt() ?? 0)
        let cacheRead = max(0, tokenUsage["cacheReadTokens"].asInt() ?? item["cacheReadTokens"].asInt() ?? 0)
        var tokens = sumTokens(tokenUsage)
        if tokens <= 0 { tokens = sumTokens(item) }
        if tokens <= 0 { tokens = input + output + cacheWrite + cacheRead }
        let charged = item["chargedCents"].asDouble() ?? parseMoneyCents(item["usageBasedCosts"])
        let totalCents = tokenUsage["totalCents"].asDouble() ?? item["totalCents"].asDouble()
        var email = (item["email"].asString() ?? item["userEmail"].asString() ?? item["user"].asString() ?? "").trimmingCharacters(in: .whitespaces)
        if email.isEmpty, item["user"].isObject {
            email = (item["user"]["email"].asString() ?? "").trimmingCharacters(in: .whitespaces)
        }
        let owning = (item["owningUser"].asString() ?? item["userId"].asString() ?? "").trimmingCharacters(in: .whitespaces)
        let givenId = (item["id"].asString() ?? item["eventId"].asString() ?? "").trimmingCharacters(in: .whitespaces)
        let id = givenId.isEmpty
            ? [String(ts), owning, model, String(input), String(output), String(cacheWrite), String(cacheRead), kindRaw].joined(separator: "|")
            : givenId
        return UsageEvent(
            id: id,
            timestampMs: ts,
            model: model,
            kind: kind,
            userEmail: email,
            owningUser: owning,
            tokens: max(0, tokens),
            inputTokens: input,
            outputTokens: output,
            cacheWriteTokens: cacheWrite,
            cacheReadTokens: cacheRead,
            chargedCents: charged,
            totalCents: totalCents,
            isHeadless: item["isHeadless"].asBool() || item["isCloudAgent"].asBool(),
            isChargeable: isChargeable
        )
    }

    public static func parseMoneyCents(_ value: JSONValue) -> Double? {
        if let n = value.asDouble(), value.asString() == nil { return n }
        let text = (value.asString() ?? "").trimmingCharacters(in: .whitespaces)
        if text.isEmpty { return nil }
        let lower = text.lowercased()
        if ["included", "free", "n/a", "—", "-", "none"].contains(lower) { return nil }
        if !lower.contains("us$") && !text.contains("$") { return nil }
        var cleaned = text.replacingOccurrences(of: "US$", with: "", options: .caseInsensitive)
        cleaned = cleaned.replacingOccurrences(of: "$", with: "")
        cleaned = cleaned.replacingOccurrences(of: ",", with: "")
        cleaned = cleaned.replacingOccurrences(of: "Included", with: "", options: .caseInsensitive)
        cleaned = cleaned.replacingOccurrences(of: "Free", with: "", options: .caseInsensitive)
        cleaned = cleaned.trimmingCharacters(in: .whitespaces)
        guard let dollars = Double(cleaned) else { return nil }
        return dollars * 100.0
    }

    public static func buildReport(_ events: [UsageEvent], filter: UsageReportFilter = UsageReportFilter(), spend: CnySpendSettings? = nil, allocation: ReportAllocationWindow? = nil) -> UsageReport {
        let result = RustCore.object("build_usage_report", [
                "events": events.map(eventJSON),
                "filter": [
                    "kind": filter.kind,
                    "category": filter.category,
                    "model": filter.model,
                    "headless": filter.headless as Any,
                    "owning_user": filter.owningUser,
                    "start_date": filter.startDate,
                    "end_date": filter.endDate,
                ],
                "spend": spend.map { ["monthly_plan_usd": $0.monthlyPlanUsd, "usd_cny_rate": $0.usdCnyRate, "membership_type": $0.membershipType, "actual_cny": $0.actualCny] } as Any,
                "allocation": allocation.map { ["account_kind": $0.accountKind, "temp_start_at": $0.tempStartAt, "temp_valid_days": $0.tempValidDays, "temp_valid_hours": $0.tempValidHours, "billing_cycle_start": $0.billingCycleStart, "billing_cycle_end": $0.billingCycleEnd, "now_ms": $0.nowMs as Any] } as Any,
            ])
        return reportFromRust(result)
    }

    static func reportFromRust(_ result: [String: Any]) -> UsageReport {
        let daily = (result["daily"] as? [[String: Any]] ?? []).map { row in
            DailyUsageRow(date: row["date"] as? String ?? "", tokens: intValue(row["tokens"]), cents: doubleValue(row["cents"]), count: intValue(row["count"]))
        }
        let models = (result["models"] as? [[String: Any]] ?? []).map { row in
            ModelUsageRow(name: row["name"] as? String ?? "", tokens: intValue(row["tokens"]), cents: doubleValue(row["cents"]), count: intValue(row["count"]), headlessCount: intValue(row["headless_count"]), cny: doubleValue(row["cny"]))
        }
        return UsageReport(
            eventCount: intValue(result["event_count"]),
            totalTokens: intValue(result["total_tokens"]),
            totalCents: doubleValue(result["total_cents"]),
            hasCost: result["has_cost"] as? Bool ?? false,
            includedCount: intValue(result["included_count"]),
            freeCount: intValue(result["free_count"]),
            onDemandCount: intValue(result["on_demand_count"]),
            otherCount: intValue(result["other_count"]),
            headlessCount: intValue(result["headless_count"]),
            firstPartyCount: intValue(result["first_party_count"]),
            apiCount: intValue(result["api_count"]),
            grokBotCount: intValue(result["grok_bot_count"]),
            daily: daily,
            models: models,
            events: (result["events"] as? [[String: Any]] ?? []).map { row in
                var ev = eventFromRust(row)
                ev.allocatedCny = doubleValue(row["allocated_cny"])
                return ev
            },
            totalCny: doubleValue(result["total_cny"]),
            planCny: doubleValue(result["plan_cny"]),
            onDemandCny: doubleValue(result["on_demand_cny"]),
            usdCnyRate: doubleValue(result["usd_cny_rate"]),
            monthlyPlanUsd: doubleValue(result["monthly_plan_usd"]),
            actualCny: doubleValue(result["actual_cny"]),
            usesActualCny: result["uses_actual_cny"] as? Bool ?? false,
            windowPlanCny: doubleValue(result["window_plan_cny"])
        )
    }

    static func intValue(_ value: Any?) -> Int { value as? Int ?? (value as? NSNumber).map { Int($0.doubleValue) } ?? 0 }
    static func int64Value(_ value: Any?) -> Int64 { value as? Int64 ?? (value as? NSNumber)?.int64Value ?? 0 }
    static func doubleValue(_ value: Any?) -> Double { value as? Double ?? (value as? NSNumber)?.doubleValue ?? 0 }

    public static func toCSV(_ events: [UsageEvent], spend: CnySpendSettings? = nil, allocationBase: [UsageEvent]? = nil, allocation: ReportAllocationWindow? = nil) -> String {
        let rows: [[String: Any]] = events.map { event in
            [
                "id": event.id,
                "timestamp_ms": event.timestampMs, "user_email": event.userEmail, "kind": event.kind,
                "model": event.model, "tokens": event.tokens, "charged_cents": event.chargedCents as Any,
                "total_cents": event.totalCents as Any, "is_headless": event.isHeadless, "allocated_cny": event.allocatedCny,
            ]
        }
        var payload: [String: Any] = [
            "lf": true,
            "events": rows,
        ]
        if let spend {
            payload["spend"] = [
                "monthly_plan_usd": spend.monthlyPlanUsd,
                "usd_cny_rate": spend.usdCnyRate,
                "membership_type": spend.membershipType,
                "actual_cny": spend.actualCny,
            ]
        }
        if let allocationBase {
            payload["allocation_base"] = allocationBase.map { event in
                [
                    "id": event.id,
                    "timestamp_ms": event.timestampMs, "user_email": event.userEmail, "kind": event.kind,
                    "model": event.model, "tokens": event.tokens, "charged_cents": event.chargedCents as Any,
                    "total_cents": event.totalCents as Any, "is_headless": event.isHeadless, "allocated_cny": event.allocatedCny,
                ]
            }
        }
        if let allocation {
            payload["allocation"] = [
                "account_kind": allocation.accountKind,
                "temp_start_at": allocation.tempStartAt,
                "temp_valid_days": allocation.tempValidDays,
                "temp_valid_hours": allocation.tempValidHours,
                "billing_cycle_start": allocation.billingCycleStart,
                "billing_cycle_end": allocation.billingCycleEnd,
                "now_ms": allocation.nowMs,
            ]
        }
        _ = RustCore.string("usage_events_csv", payload)
        return decodeRustText(RustCore.lastText)
    }

    public static func fromDict(_ raw: [String: Any]) -> UsageEvent? {
        let ts: Int64?
        if let n = number64(raw["timestamp_ms"]) ?? number64(raw["timestampMs"]) {
            ts = n
        } else if let iso = raw["timestamp"] as? String, !iso.isEmpty {
            ts = UsageParser.isoToMs(iso).map { Int64($0) }
        } else {
            ts = number64(raw["timestamp"])
        }
        guard let ts else { return nil }
        return UsageEvent(
            id: str(raw["id"]),
            timestampMs: ts,
            model: str(raw["model"]),
            kind: str(raw["kind"]).isEmpty ? kindOther : str(raw["kind"]),
            userEmail: str(raw["user_email"]),
            owningUser: str(raw["owning_user"]),
            tokens: max(0, intVal(raw["tokens"])),
            inputTokens: max(0, intVal(raw["input_tokens"])),
            outputTokens: max(0, intVal(raw["output_tokens"])),
            cacheWriteTokens: max(0, intVal(raw["cache_write_tokens"])),
            cacheReadTokens: max(0, intVal(raw["cache_read_tokens"])),
            chargedCents: number(raw["charged_cents"] ?? raw["chargedCents"]),
            totalCents: number(raw["total_cents"] ?? raw["totalCents"]),
            isHeadless: boolVal(raw["is_headless"]),
            isChargeable: boolVal(raw["is_chargeable"])
        )
    }

    public static func merge(_ existing: [UsageEvent], incoming: [UsageEvent]) -> [UsageEvent] {
        let result = RustCore.call("merge_usage_events", [
            "existing": existing.map(eventJSON),
            "incoming": incoming.map(eventJSON),
        ])
        return (result as? [[String: Any]] ?? []).map(eventFromRust)
    }

    public static func prune(_ events: [UsageEvent], minTimestampMs: Int64) -> [UsageEvent] {
        let result = RustCore.call("prune_usage_events", [
            "events": events.map(eventJSON),
            "min_timestamp_ms": minTimestampMs,
        ])
        return (result as? [[String: Any]] ?? []).map(eventFromRust)
    }

    public static func load(accountId: String, teamScope: Bool, directory: URL? = nil) -> [UsageEvent] {
        let path = AppPaths.usageEventsPath(accountId: accountId, teamScope: teamScope, in: directory)
        guard let text = try? String(contentsOf: path, encoding: .utf8) else { return [] }
        var events: [UsageEvent] = []
        for line in text.split(whereSeparator: \.isNewline) {
            let t = line.trimmingCharacters(in: .whitespaces)
            guard !t.isEmpty, let data = t.data(using: .utf8),
                  let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let ev = fromDict(obj)
            else { continue }
            events.append(ev)
        }
        return merge(events, incoming: [])
    }

    public static func save(_ events: [UsageEvent], accountId: String, teamScope: Bool, directory: URL? = nil) {
        let dir = directory ?? AppPaths.configDirectory()
        AppPaths.ensureDirectory(dir)
        let path = AppPaths.usageEventsPath(accountId: accountId, teamScope: teamScope, in: dir)
        let lines = events.compactMap { serialize($0) }
        let text = lines.isEmpty ? "" : lines.joined(separator: "\n") + "\n"
        try? text.write(to: path, atomically: true, encoding: .utf8)
    }

    public static func resolveSyncWindow(
        billingCycleStart: String?,
        billingCycleEnd: String?,
        endOverridden: Bool,
        apiBillingCycleEnd: String?,
        nowMs: Int64,
        watermarkMs: Int64
    ) -> UsageEventsWindow {
        let result = RustCore.object("resolve_sync_window", [
            "billing_cycle_start": billingCycleStart as Any,
            "billing_cycle_end": billingCycleEnd as Any,
            "end_overridden": endOverridden,
            "api_billing_cycle_end": apiBillingCycleEnd as Any,
            "now_ms": NSNumber(value: nowMs),
            "watermark_ms": NSNumber(value: watermarkMs),
        ])
        let stop = result["stop_at_ms"] as? Int64 ?? (result["stop_at_ms"] as? NSNumber)?.int64Value
        return UsageEventsWindow(startMs: (result["start_ms"] as? NSNumber)?.int64Value ?? 0, endMs: (result["end_ms"] as? NSNumber)?.int64Value ?? 0, stopAtMs: result["stop_at_ms"] is NSNull ? nil : stop)
    }

    public static func sync(
        client: CursorClient,
        token: String,
        accountId: String,
        usage: UsageSnapshot?,
        teamScope: Bool,
        directory: URL? = nil,
        onPage: ((Int) -> Void)? = nil
    ) async throws -> UsageEventsSyncResult {
        let existing = load(accountId: accountId, teamScope: teamScope, directory: directory)
        let nowMs = Int64(Date().timeIntervalSince1970 * 1000)
        let window = resolveSyncWindow(
            billingCycleStart: usage?.billingCycleStart,
            billingCycleEnd: usage?.billingCycleEnd,
            endOverridden: usage?.billingCycleEndOverridden == true,
            apiBillingCycleEnd: usage?.apiBillingCycleEnd,
            nowMs: nowMs,
            watermarkMs: existing.map(\.timestampMs).max() ?? 0
        )
        let cycleStart = Int64(UsageParser.isoToMs(usage?.billingCycleStart) ?? Int(nowMs - 30 * 86_400 * 1000))
        let startMs = window.startMs
        let cycleEnd = window.endMs
        let stopAt = window.stopAtMs
        let rawTeam = usage.map { UsageParser.teamId(from: $0.raw) } ?? -1
        let teamId: Int? = rawTeam > 0 ? rawTeam : nil
        var userId: Int?
        if !teamScope {
            var uid = usage.map { UsageParser.userId(from: $0.raw) } ?? -1
            if uid <= 0 {
                var counts: [Int: Int] = [:]
                for ev in existing {
                    if let n = Int(ev.owningUser), n > 0 { counts[n, default: 0] += 1 }
                }
                uid = counts.max(by: { $0.value < $1.value })?.key ?? 0
            }
            if uid > 0 { userId = uid }
            else if rawTeam > 0 {
                // Team feed without userId is the whole org; do not pollute the personal cache.
                return UsageEventsSyncResult(events: existing, fetched: 0, totalAvailable: existing.count, truncated: false, note: noteTeamPersonal)
            }
        }
        let fetched = try await client.fetchUsageEvents(
            sessionToken: token,
            startMs: startMs,
            endMs: cycleEnd,
            teamId: teamId,
            userId: teamScope ? nil : userId,
            stopAtMs: stopAt,
            onPage: onPage
        )
        var merged = merge(existing, incoming: fetched.events)
        if !teamScope, let userId {
            let uidText = String(userId)
            merged = merged.filter { $0.owningUser.isEmpty || $0.owningUser == uidText }
        }
        let minTs = min(cycleStart, nowMs - 120 * 86_400 * 1000)
        let pruned = prune(merged, minTimestampMs: minTs)
        save(pruned, accountId: accountId, teamScope: teamScope, directory: directory)
        return UsageEventsSyncResult(events: pruned, fetched: fetched.events.count, totalAvailable: fetched.totalCount, truncated: fetched.truncated)
    }

    static func sumTokens(_ item: JSONValue) -> Int {
        let keys = [
            "inputTokens", "outputTokens", "cacheWriteTokens", "cacheReadTokens",
            "totalInputTokens", "totalOutputTokens", "totalCacheWriteTokens", "totalCacheReadTokens",
        ]
        var total = 0
        var found = false
        for key in keys {
            if let n = item[key].asInt() {
                found = true
                total += max(0, n)
            }
        }
        if found { return total }
        if let n = item["totalTokens"].asInt() { return max(0, n) }
        return 0
    }

    static func displayModel(_ raw: String) -> String {
        let name = raw.trimmingCharacters(in: .whitespaces)
        if name.isEmpty { return "" }
        return name == "default" ? "auto" : name
    }

    static func escapeCSV(_ value: String) -> String {
        if value.contains(where: { $0 == "," || $0 == "\"" || $0 == "\n" || $0 == "\r" }) {
            return "\"" + value.replacingOccurrences(of: "\"", with: "\"\"") + "\""
        }
        return value
    }

    static func int64(_ value: JSONValue) -> Int64? {
        guard let n = value.asDouble() else { return nil }
        return Int64(n.rounded())
    }

    static func serialize(_ ev: UsageEvent) -> String? {
        var obj: [String: Any] = [
            "id": ev.id,
            "timestamp_ms": NSNumber(value: ev.timestampMs),
            "model": ev.model,
            "kind": ev.kind,
            "user_email": ev.userEmail,
            "owning_user": ev.owningUser,
            "tokens": ev.tokens,
            "input_tokens": ev.inputTokens,
            "output_tokens": ev.outputTokens,
            "cache_write_tokens": ev.cacheWriteTokens,
            "cache_read_tokens": ev.cacheReadTokens,
            "is_headless": ev.isHeadless,
            "is_chargeable": ev.isChargeable,
        ]
        if let v = ev.chargedCents { obj["charged_cents"] = v } else { obj["charged_cents"] = NSNull() }
        if let v = ev.totalCents { obj["total_cents"] = v } else { obj["total_cents"] = NSNull() }
        guard let data = try? JSONSerialization.data(withJSONObject: obj),
              let line = String(data: data, encoding: .utf8)
        else { return nil }
        return line
    }

    static func str(_ value: Any?) -> String {
        if value == nil || value is NSNull { return "" }
        return value as? String ?? ""
    }

    static func number(_ value: Any?) -> Double? {
        if value == nil || value is NSNull { return nil }
        if let n = value as? NSNumber { return n.doubleValue }
        if let s = value as? String { return Double(s) }
        return nil
    }

    static func number64(_ value: Any?) -> Int64? {
        guard let n = number(value) else { return nil }
        return Int64(n.rounded())
    }

    static func intVal(_ value: Any?) -> Int {
        number(value).map { Int($0.rounded()) } ?? 0
    }

    static func boolVal(_ value: Any?) -> Bool {
        if let b = value as? Bool { return b }
        if let n = value as? NSNumber { return n.boolValue }
        return false
    }
}
