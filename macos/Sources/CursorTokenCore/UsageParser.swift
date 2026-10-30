import Foundation

public let cursorBaseURL = "https://cursor.com"
public let usageEndpoints = ["/api/usage-summary", "/api/dashboard/usage-summary"]
public let aggregatedUsageEndpoint = "/api/dashboard/get-aggregated-usage-events"
public let filteredUsageEndpoint = "/api/dashboard/get-filtered-usage-events"
public let sandUsageEndpoint = "/api/dashboard/get-sand-usage-status"
public let sandUsageTimeout: TimeInterval = 8
public let usageEventsPageSize = 100
public let usageEventsMaxPages = 50
public let usageURL = "https://cursor.com/dashboard/usage"
public let spendingURL = "https://cursor.com/dashboard/spending"
public let billingURL = "https://cursor.com/dashboard/billing"
public let grokDashboardURL = "https://grok.com/dashboard"

public let cursorModelTier = 2

private let teamMemberships: Set<String> = [
    "enterprise", "enterprise_trial", "team", "teams", "business",
]
private let membershipLabels: [String: String] = [
    "pro": "Pro",
    "pro_plus": "Pro+",
    "pro+": "Pro+",
    "ultra": "Ultra",
    "free": "Free",
    "hobby": "Hobby",
    "enterprise": "Enterprise",
    "enterprise_trial": "Enterprise",
    "team": "Team",
    "teams": "Team",
    "business": "Business",
    "unpaid": "Free",
    "plus": "Plus",
    "premium": "Premium",
    "super": "Super",
    "x_plus": "Plus",
    "x_premium": "Premium",
    "x_super": "Super",
    "x_ultra": "Ultra",
    "x-ultra": "Ultra",
]
private let xMemberships: Set<String> = [
    "plus", "premium", "super", "x_plus", "x_premium", "x_super", "x_ultra",
]
private let modelNameAliases: [String: String] = ["default": "auto"]

public struct ModelTokenUsage: Equatable, Sendable {
    public var name: String
    public var tokens: Int
    public var cents: Double
    public var tier: Int
    public var usagePercent: Double?

    public init(name: String, tokens: Int, cents: Double, tier: Int, usagePercent: Double? = nil) {
        self.name = name
        self.tokens = tokens
        self.cents = cents
        self.tier = tier
        self.usagePercent = usagePercent
    }

    public var isCursorModel: Bool { tier == cursorModelTier }
    public var isGrokBot: Bool { UsageParser.isGrokBotModel(name) }
}

public struct UsageSnapshot: Equatable {
    public var usedPercent: Double
    public var remainingPercent: Double
    public var autoPercentUsed: Double?
    public var apiPercentUsed: Double?
    public var totalPercentUsed: Double?
    public var membershipType: String
    public var billingCycleStart: String?
    public var billingCycleEnd: String?
    public var daysRemaining: Int?
    public var daysElapsed: Double?
    public var estimatedUsableDays: Double?
    public var raw: JSONValue
    public var totalTokens: Int?
    public var modelUsages: [ModelTokenUsage]
    public var billingMode: String
    public var usedCents: Double?
    public var limitCents: Double?
    public var remainingCents: Double?
    public var onDemandUsedCents: Double?
    public var onDemandLimitCents: Double?
    public var pooledUsedCents: Double?
    public var pooledLimitCents: Double?
    public var limitType: String
    public var isUnlimited: Bool
    public var billingCycleEndOverridden: Bool
    public var apiBillingCycleEnd: String?
    public var grokBotPercentUsed: Double?
    public var grokBotRemainingPercent: Double?
    public var grokBotPeriodStart: String?
    public var grokBotResetAt: String?
    public var grokBotDaysRemaining: Int?
    public var isXSubscription: Bool
    public var usageResetAt: String?
    public var prepaidRemainingCents: Double?

    public init(
        usedPercent: Double,
        remainingPercent: Double,
        autoPercentUsed: Double? = nil,
        apiPercentUsed: Double? = nil,
        totalPercentUsed: Double? = nil,
        membershipType: String = "",
        billingCycleStart: String? = nil,
        billingCycleEnd: String? = nil,
        daysRemaining: Int? = nil,
        daysElapsed: Double? = nil,
        estimatedUsableDays: Double? = nil,
        raw: JSONValue = .null,
        totalTokens: Int? = nil,
        modelUsages: [ModelTokenUsage] = [],
        billingMode: String = "percent",
        usedCents: Double? = nil,
        limitCents: Double? = nil,
        remainingCents: Double? = nil,
        onDemandUsedCents: Double? = nil,
        onDemandLimitCents: Double? = nil,
        pooledUsedCents: Double? = nil,
        pooledLimitCents: Double? = nil,
        limitType: String = "",
        isUnlimited: Bool = false,
        billingCycleEndOverridden: Bool = false,
        apiBillingCycleEnd: String? = nil,
        grokBotPercentUsed: Double? = nil,
        grokBotRemainingPercent: Double? = nil,
        grokBotPeriodStart: String? = nil,
        grokBotResetAt: String? = nil,
        grokBotDaysRemaining: Int? = nil,
        isXSubscription: Bool = false,
        usageResetAt: String? = nil,
        prepaidRemainingCents: Double? = nil
    ) {
        self.usedPercent = usedPercent
        self.remainingPercent = remainingPercent
        self.autoPercentUsed = autoPercentUsed
        self.apiPercentUsed = apiPercentUsed
        self.totalPercentUsed = totalPercentUsed
        self.membershipType = membershipType
        self.billingCycleStart = billingCycleStart
        self.billingCycleEnd = billingCycleEnd
        self.daysRemaining = daysRemaining
        self.daysElapsed = daysElapsed
        self.estimatedUsableDays = estimatedUsableDays
        self.raw = raw
        self.totalTokens = totalTokens
        self.modelUsages = modelUsages
        self.billingMode = billingMode
        self.usedCents = usedCents
        self.limitCents = limitCents
        self.remainingCents = remainingCents
        self.onDemandUsedCents = onDemandUsedCents
        self.onDemandLimitCents = onDemandLimitCents
        self.pooledUsedCents = pooledUsedCents
        self.pooledLimitCents = pooledLimitCents
        self.limitType = limitType
        self.isUnlimited = isUnlimited
        self.billingCycleEndOverridden = billingCycleEndOverridden
        self.apiBillingCycleEnd = apiBillingCycleEnd
        self.grokBotPercentUsed = grokBotPercentUsed
        self.grokBotRemainingPercent = grokBotRemainingPercent
        self.grokBotPeriodStart = grokBotPeriodStart
        self.grokBotResetAt = grokBotResetAt
        self.grokBotDaysRemaining = grokBotDaysRemaining
        self.isXSubscription = isXSubscription
        self.usageResetAt = usageResetAt
        self.prepaidRemainingCents = prepaidRemainingCents
    }

    public var isTeamAccount: Bool {
        if isXSubscription || UsageParser.isXMembership(membershipType) { return false }
        return UsageParser.isTeamMembership(membershipType, limitType: limitType)
    }

    public var showsAmount: Bool {
        guard usedCents != nil, let limit = limitCents, limit > 0 else { return false }
        return billingMode == "amount" || isTeamAccount
    }

    public var showsGrokBot: Bool { grokBotPercentUsed != nil }

    public var dashboardURL: String {
        UsageParser.dashboardURL(for: self)
    }
}

public struct GrokBotUsage: Equatable, Sendable {
    public var percentUsed: Double
    public var remainingPercent: Double
    public var periodStart: String?
    public var resetAt: String?
    public var daysRemaining: Int?

    public init(
        percentUsed: Double,
        remainingPercent: Double,
        periodStart: String? = nil,
        resetAt: String? = nil,
        daysRemaining: Int? = nil
    ) {
        self.percentUsed = percentUsed
        self.remainingPercent = remainingPercent
        self.periodStart = periodStart
        self.resetAt = resetAt
        self.daysRemaining = daysRemaining
    }
}

extension UsageSnapshot {
    public static func == (lhs: UsageSnapshot, rhs: UsageSnapshot) -> Bool {
        lhs.usedPercent == rhs.usedPercent
            && lhs.remainingPercent == rhs.remainingPercent
            && lhs.autoPercentUsed == rhs.autoPercentUsed
            && lhs.apiPercentUsed == rhs.apiPercentUsed
            && lhs.membershipType == rhs.membershipType
            && lhs.billingMode == rhs.billingMode
            && lhs.usedCents == rhs.usedCents
            && lhs.limitCents == rhs.limitCents
            && lhs.isUnlimited == rhs.isUnlimited
    }
}

public enum UsageParser {
    public static func formatMembershipType(_ raw: String?) -> String {
        RustCore.string("format_membership", ["input": raw ?? ""])
    }

    public static func isTeamMembership(_ membership: String?, limitType: String? = nil) -> Bool {
        RustCore.bool("is_team_membership", ["membership": membership ?? "", "limit_type": limitType ?? ""])
    }

    public static func dashboardURL(for usage: UsageSnapshot? = nil, membership: String = "", limitType: String = "") -> String {
        var memb = membership
        var limit = limitType
        if let usage {
            memb = usage.membershipType
            limit = usage.limitType
        }
        return RustCore.string("dashboard_url", ["membership": memb, "limit_type": limit, "x": preferGrokDashboard(usage, membership: memb)])
    }

    public static func dashboardMenuLabel(_ usage: UsageSnapshot? = nil, membership: String = "", limitType: String = "") -> String {
        var memb = membership
        var limit = limitType
        if let usage {
            memb = usage.membershipType
            limit = usage.limitType
        }
        return RustCore.string("dashboard_menu_label", ["membership": memb, "limit_type": limit, "x": preferGrokDashboard(usage, membership: memb)])
    }

    public static func dashboardButtonLabel(_ usage: UsageSnapshot? = nil, membership: String = "", limitType: String = "") -> String {
        var memb = membership
        var limit = limitType
        if let usage {
            memb = usage.membershipType
            limit = usage.limitType
        }
        return RustCore.string("dashboard_button_label", ["membership": memb, "limit_type": limit, "x": preferGrokDashboard(usage, membership: memb)])
    }

    public static func dashboardLinkLabel(_ usage: UsageSnapshot?) -> String {
        RustCore.string("dashboard_link_label", [
            "membership": usage?.membershipType ?? "",
            "x": usage?.isXSubscription == true || isXMembership(usage?.membershipType),
            "team": usage?.isTeamAccount == true,
        ])
    }

    public static func formatUSDCents(_ cents: Double?) -> String {
        RustCore.string("format_usd_cents", ["cents": cents as Any])
    }

    public static func formatSpendRange(used: Double?, limit: Double?) -> String {
        RustCore.string("format_spend_range", ["used": used as Any, "limit": limit as Any])
    }

    public static func isGrokBotModel(_ name: String?) -> Bool {
        RustCore.bool("is_grok_bot_model", ["input": name ?? ""])
    }

    public static func isFirstPartyModel(_ name: String?) -> Bool {
        RustCore.bool("is_first_party_model", ["input": name ?? ""])
    }

    public static func usageCategory(_ name: String?) -> String {
        RustCore.string("usage_category", ["input": name ?? ""])
    }

    public static func applySandUsage(_ snapshot: inout UsageSnapshot, _ payload: JSONValue, now: Date = Date()) {
        let result = RustCore.call("apply_sand_usage", [
            "snapshot": snapshotJSON(snapshot),
            "payload": payload.raw,
            "now": isoNow(now),
        ])
        let parsed = snapshotFromRust(result)
        let raw = snapshot.raw
        let models = snapshot.modelUsages
        let tokens = snapshot.totalTokens
        copySnapshot(parsed, &snapshot)
        snapshot.raw = raw
        snapshot.modelUsages = models
        snapshot.totalTokens = tokens
    }

    public static func parseSandUsageStatus(_ payload: JSONValue, now: Date = Date()) -> GrokBotUsage? {
        let result = RustCore.call("parse_sand_usage", ["payload": payload.raw, "now": isoNow(now)])
        guard let dict = result as? [String: Any] else { return nil }
        return GrokBotUsage(
            percentUsed: dict["percent_used"] as? Double ?? 0,
            remainingPercent: dict["remaining_percent"] as? Double ?? 0,
            periodStart: dict["period_start"] as? String,
            resetAt: dict["reset_at"] as? String,
            daysRemaining: dict["days_remaining"] as? Int
        )
    }

    public static func formatTokenCount(_ count: Double?) -> String {
        RustCore.string("format_token_count", ["n": count as Any])
    }

    public static func parseUsageSummary(_ payload: JSONValue, now: Date = Date()) -> UsageSnapshot {
        let result = RustCore.call("parse_usage_summary", ["payload": payload.raw, "now": isoNow(now)])
        var snap = snapshotFromRust(result)
        snap.raw = payload
        return snap
    }

    static func isoNow(_ now: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: now)
    }

    static func snapshotJSON(_ snapshot: UsageSnapshot) -> [String: Any] {
        [
            "used_percent": snapshot.usedPercent,
            "remaining_percent": snapshot.remainingPercent,
            "is_x_subscription": snapshot.isXSubscription,
            "membership_type": snapshot.membershipType,
            "billing_mode": snapshot.billingMode,
            "usage_reset_at": snapshot.usageResetAt as Any,
            "days_remaining": snapshot.daysRemaining as Any,
            "days_elapsed": snapshot.daysElapsed as Any,
            "estimated_usable_days": snapshot.estimatedUsableDays as Any,
            "grok_bot_percent_used": snapshot.grokBotPercentUsed as Any,
        ]
    }

    static func snapshotFromRust(_ value: Any) -> UsageSnapshot {
        let dict = value as? [String: Any] ?? [:]
        func num(_ key: String) -> Double? { dict[key] as? Double ?? (dict[key] as? NSNumber)?.doubleValue }
        func text(_ key: String) -> String { dict[key] as? String ?? "" }
        func optionalText(_ key: String) -> String? { dict[key] as? String }
        func whole(_ key: String) -> Int? { dict[key] as? Int ?? (dict[key] as? NSNumber).map { Int($0.doubleValue) } }
        return UsageSnapshot(
            usedPercent: num("used_percent") ?? 0,
            remainingPercent: num("remaining_percent") ?? 0,
            autoPercentUsed: num("auto_percent_used"),
            apiPercentUsed: num("api_percent_used"),
            totalPercentUsed: num("total_percent_used"),
            membershipType: text("membership_type"),
            billingCycleStart: optionalText("billing_cycle_start"),
            billingCycleEnd: optionalText("billing_cycle_end"),
            daysRemaining: whole("days_remaining"),
            daysElapsed: num("days_elapsed"),
            estimatedUsableDays: num("estimated_usable_days"),
            raw: .null,
            totalTokens: nil,
            modelUsages: [],
            billingMode: text("billing_mode"),
            usedCents: num("used_cents"),
            limitCents: num("limit_cents"),
            remainingCents: num("remaining_cents"),
            onDemandUsedCents: num("on_demand_used_cents"),
            onDemandLimitCents: num("on_demand_limit_cents"),
            pooledUsedCents: num("pooled_used_cents"),
            pooledLimitCents: num("pooled_limit_cents"),
            limitType: text("limit_type"),
            isUnlimited: dict["is_unlimited"] as? Bool ?? false,
            billingCycleEndOverridden: false,
            apiBillingCycleEnd: nil,
            grokBotPercentUsed: num("grok_bot_percent_used"),
            grokBotRemainingPercent: num("grok_bot_remaining_percent"),
            grokBotPeriodStart: optionalText("grok_bot_period_start"),
            grokBotResetAt: optionalText("grok_bot_reset_at"),
            grokBotDaysRemaining: whole("grok_bot_days_remaining"),
            isXSubscription: dict["is_x_subscription"] as? Bool ?? false,
            usageResetAt: optionalText("usage_reset_at"),
            prepaidRemainingCents: num("prepaid_remaining_cents")
        )
    }

    static func copySnapshot(_ from: UsageSnapshot, _ to: inout UsageSnapshot) {
        to.usedPercent = from.usedPercent
        to.remainingPercent = from.remainingPercent
        to.autoPercentUsed = from.autoPercentUsed
        to.apiPercentUsed = from.apiPercentUsed
        to.totalPercentUsed = from.totalPercentUsed
        to.membershipType = from.membershipType
        to.billingCycleStart = from.billingCycleStart
        to.billingCycleEnd = from.billingCycleEnd
        to.daysRemaining = from.daysRemaining
        to.daysElapsed = from.daysElapsed
        to.estimatedUsableDays = from.estimatedUsableDays
        to.billingMode = from.billingMode
        to.usedCents = from.usedCents
        to.limitCents = from.limitCents
        to.remainingCents = from.remainingCents
        to.onDemandUsedCents = from.onDemandUsedCents
        to.onDemandLimitCents = from.onDemandLimitCents
        to.pooledUsedCents = from.pooledUsedCents
        to.pooledLimitCents = from.pooledLimitCents
        to.limitType = from.limitType
        to.isUnlimited = from.isUnlimited
        to.isXSubscription = from.isXSubscription
        to.usageResetAt = from.usageResetAt
        to.prepaidRemainingCents = from.prepaidRemainingCents
        to.grokBotPercentUsed = from.grokBotPercentUsed
        to.grokBotRemainingPercent = from.grokBotRemainingPercent
        to.grokBotPeriodStart = from.grokBotPeriodStart
        to.grokBotResetAt = from.grokBotResetAt
        to.grokBotDaysRemaining = from.grokBotDaysRemaining
    }

    public static func membershipKey(_ raw: String?) -> String {
        RustCore.string("membership_key", ["input": raw ?? ""])
    }

    public static func isXMembership(_ raw: String?) -> Bool {
        RustCore.bool("is_x_membership", ["input": raw ?? ""])
    }

    static func preferGrokDashboard(_ usage: UsageSnapshot?, membership: String) -> Bool {
        if let usage, usage.isXSubscription || isXMembership(usage.membershipType) { return true }
        return isXMembership(membership)
    }

    public static func parseAggregatedUsage(
        _ payload: JSONValue,
        autoPercent: Double? = nil,
        apiPercent: Double? = nil
    ) -> (models: [ModelTokenUsage], total: Int) {
        let result = RustCore.object("parse_aggregated_usage", [
            "payload": payload.raw,
            "auto_percent": autoPercent as Any,
            "api_percent": apiPercent as Any,
        ])
        let models = (result["models"] as? [[String: Any]] ?? []).map { row in
            ModelTokenUsage(
                name: row["name"] as? String ?? "",
                tokens: row["tokens"] as? Int ?? 0,
                cents: row["cents"] as? Double ?? 0,
                tier: row["tier"] as? Int ?? 1,
                usagePercent: row["usage_percent"] as? Double
            )
        }
        return (models, result["total"] as? Int ?? 0)
    }

    typealias Meter = (Double, Double, Double?)

    static func firstPresent(_ obj: JSONValue, _ keys: String...) -> JSONValue {
        for key in keys where obj.object[key] != nil {
            return obj[key]
        }
        return .null
    }

    static func optBool(_ obj: JSONValue, _ keys: String...) -> Bool? {
        for key in keys where obj.object[key] != nil {
            return obj[key].asBool()
        }
        return nil
    }

    public static func timestampToIso(_ value: JSONValue) -> String? {
        if let raw = value.asString()?.trimmingCharacters(in: .whitespacesAndNewlines), !raw.isEmpty {
            if raw.contains("T") || raw.hasSuffix("Z") {
                return parseISO(raw) == nil ? nil : raw
            }
        }
        guard let num = value.asDouble() else { return nil }
        let seconds = abs(num) > 10_000_000_000 ? num / 1000.0 : num
        let dt = Date(timeIntervalSince1970: seconds)
        let ms = Int((seconds.truncatingRemainder(dividingBy: 1)) * 1000.0)
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.timeZone = TimeZone(secondsFromGMT: 0)
        if abs(seconds - seconds.rounded()) < 0.0005 || ms == 0 {
            f.dateFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'"
        } else {
            f.dateFormat = "yyyy-MM-dd'T'HH:mm:ss.SSS'Z'"
        }
        return f.string(from: dt)
    }

    static func object(_ value: JSONValue) -> JSONValue {
        value.isObject ? value : JSONValue([:])
    }

    static func onDemandEnabled(_ block: JSONValue) -> Bool {
        if !block.isObject || block.isEmpty { return false }
        if block.object["enabled"] != nil { return block["enabled"].asBool() }
        return spendMeter(block) != nil
    }

    static func spendMeter(_ block: JSONValue, allowBreakdown: Bool = false) -> Meter? {
        if !block.isObject || block.isEmpty { return nil }
        var used = block["used"].asDouble()
        var limit = block["limit"].asDouble()
        let remaining = block["remaining"].asDouble()
        if (limit == nil || (limit ?? 0) <= 0) && allowBreakdown {
            limit = object(block["breakdown"])["total"].asDouble()
        }
        if used == nil, let remaining, let limit {
            used = max(0, limit - remaining)
        }
        guard let used, let limit else { return nil }
        return (used, limit, remaining)
    }

    static func meterWithLimit(_ meter: Meter?) -> Meter? {
        guard let meter, meter.1 > 0 else { return nil }
        return meter
    }

    struct SpendPercent {
        var used: Double
        var limit: Double
        var remaining: Double?
        var percent: Double
    }

    static func percentFromSpendMeter(_ meter: Meter?) -> SpendPercent? {
        guard let picked = meterWithLimit(meter) else { return nil }
        return SpendPercent(
            used: picked.0,
            limit: picked.1,
            remaining: picked.2,
            percent: clampPercent(picked.0 / picked.1 * 100.0)
        )
    }

    static func percentFromDisplayMessage(_ message: String?) -> Double? {
        let text = message ?? ""
        guard let idx = text.firstIndex(of: "%"), idx > text.startIndex else { return nil }
        let before = String(text[..<idx])
        var start = before.startIndex
        for i in before.indices {
            let ch = before[i]
            if ch.isNumber || ch == "." { continue }
            start = before.index(after: i)
        }
        let raw = String(before[start...]).trimmingCharacters(in: .whitespaces)
        guard !raw.isEmpty, let value = Double(raw) else { return nil }
        return clampPercent(value)
    }

    public static func teamId(from payload: JSONValue) -> Int {
        for key in ["teamId", "owningTeam"] {
            if let n = payload[key].asInt(), n > 0 { return n }
        }
        let team = object(payload["teamUsage"])
        if let n = (team["teamId"].asInt() ?? team["id"].asInt()), n > 0 { return n }
        return -1
    }

    public static func userId(from payload: JSONValue) -> Int {
        for key in ["userId", "numericUserId", "currentUserId"] {
            if let n = payload[key].asInt(), n > 0 { return n }
        }
        let individual = object(payload["individualUsage"])
        if let n = (individual["userId"].asInt() ?? individual["id"].asInt()), n > 0 { return n }
        return -1
    }

    public static func isoToMs(_ iso: String?) -> Int? {
        guard let dt = parseISO(iso) else { return nil }
        return Int(dt.timeIntervalSince1970 * 1000)
    }

    static func sumTokenFields(_ item: JSONValue) -> Int {
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

    static func displayModelName(_ raw: String) -> String {
        let name = raw.trimmingCharacters(in: .whitespaces)
        if name.isEmpty { return "" }
        return modelNameAliases[name] ?? name
    }

    static func modelTier(_ name: String, _ tier: JSONValue) -> Int {
        if let t = tier.asInt() { return t }
        return isFirstPartyModel(name) ? cursorModelTier : 1
    }

    static func allocateUsagePercents(_ models: [ModelTokenUsage], categoryPercent: Double?) -> [ModelTokenUsage] {
        if models.isEmpty { return [] }
        let centsSum = models.reduce(0.0) { $0 + $1.cents }
        let tokensSum = models.reduce(0) { $0 + $1.tokens }
        return models.map { model in
            var share = 0.0
            if centsSum > 1e-6 {
                share = model.cents / centsSum
            } else if tokensSum > 0 {
                share = Double(model.tokens) / Double(tokensSum)
            }
            let pct = categoryPercent.map { round1(share * $0) }
            return ModelTokenUsage(name: model.name, tokens: model.tokens, cents: model.cents, tier: model.tier, usagePercent: pct)
        }
    }

    static func isoOrNone(_ value: String?) -> String? {
        guard let value, !value.isEmpty else { return nil }
        return value
    }

    static func parseISO(_ iso: String?) -> Date? {
        guard let iso, !iso.isEmpty else { return nil }
        let text = iso.replacingOccurrences(of: "Z", with: "+00:00")
        let f1 = ISO8601DateFormatter()
        f1.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = f1.date(from: text) ?? f1.date(from: iso) { return d }
        let f2 = ISO8601DateFormatter()
        f2.formatOptions = [.withInternetDateTime]
        if let d = f2.date(from: text) ?? f2.date(from: iso) { return d }
        let f3 = DateFormatter()
        f3.locale = Locale(identifier: "en_US_POSIX")
        f3.timeZone = TimeZone(secondsFromGMT: 0)
        f3.dateFormat = "yyyy-MM-dd'T'HH:mm:ss.SSSXXXXX"
        if let d = f3.date(from: text) { return d }
        f3.dateFormat = "yyyy-MM-dd'T'HH:mm:ssXXXXX"
        return f3.date(from: text)
    }

    public static func daysUntil(_ iso: String?, now: Date) -> Int? {
        guard let end = parseISO(iso) else { return nil }
        let delta = end.timeIntervalSince(now)
        return max(0, Int(delta / 86_400.0))
    }

    static func daysSince(_ iso: String?, now: Date) -> Double? {
        guard let start = parseISO(iso) else { return nil }
        let hours = now.timeIntervalSince(start) / 3600.0
        if hours < 0 { return 0 }
        return hours / 24.0
    }

    public static func estimateUsableDays(usedPercent: Double, remainingPercent: Double, daysElapsed: Double?) -> Double? {
        guard let daysElapsed else { return nil }
        if remainingPercent <= 0 { return 0 }
        if daysElapsed < 0.04 { return nil }
        if usedPercent < 0.2 { return nil }
        let burn = usedPercent / daysElapsed
        if burn <= 1e-6 { return nil }
        return round1(remainingPercent / burn)
    }
}
