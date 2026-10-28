import Foundation

public enum AccountValidity {
    public static let longTerm = "long_term"
    public static let temporary = "temporary"
    public static let maxDays = 999
    public static let maxHours = 23

    public static func sanitizeKind(_ raw: String?) -> String {
        RustCore.string("sanitize_account_kind", ["input": raw ?? ""])
    }

    static func sanitizeKindCore(_ raw: String?) -> String {
        let key = (raw ?? "").trimmingCharacters(in: .whitespaces).lowercased().replacingOccurrences(of: "-", with: "_")
        if key == "temporary" || key == "temp" || key == "short" { return temporary }
        return longTerm
    }

    public static func clampDays(_ raw: Any?) -> Int { clampInt(raw, lo: 0, hi: maxDays) }
    public static func clampHours(_ raw: Any?) -> Int { clampInt(raw, lo: 0, hi: maxHours) }

    public static func isTemporary(_ account: Account?) -> Bool {
        guard let account else { return false }
        return sanitizeKindCore(account.accountKind) == temporary
    }

    public static func computeEndIso(startAt: String?, days: Int, hours: Int) -> String? {
        let result = RustCore.call("compute_account_end", ["start": startAt ?? "", "days": days, "hours": hours])
        return result as? String
    }

    static func computeEndIsoCore(startAt: String?, days: Int, hours: Int) -> String? {
        guard let start = AccountSync.parseIso(startAt) else { return nil }
        let d = clampDays(days)
        let h = clampHours(hours)
        if d == 0 && h == 0 { return nil }
        let end = start.addingTimeInterval(TimeInterval(d * 86_400 + h * 3_600))
        return AccountSync.nowIso(end)
    }

    public static func accountEndIso(_ account: Account?) -> String? {
        guard isTemporary(account), let account else { return nil }
        return computeEndIsoCore(startAt: account.tempStartAt, days: account.tempValidDays, hours: account.tempValidHours)
    }

    public static func applyEndOverride(_ snap: inout UsageSnapshot, account: Account?, now: Date = Date()) {
        let result = RustCore.object("apply_account_end_override", [
            "account": [
                "account_kind": account?.accountKind ?? "",
                "temp_start_at": account?.tempStartAt ?? "",
                "temp_valid_days": account?.tempValidDays ?? 0,
                "temp_valid_hours": account?.tempValidHours ?? 0,
            ],
            "api_end": snap.billingCycleEnd as Any,
            "now": isoNow(now),
            "days_remaining": snap.daysRemaining ?? 0,
        ])
        snap.billingCycleEnd = result["billing_cycle_end"] as? String
        snap.daysRemaining = result["days_remaining"] as? Int ?? (result["days_remaining"] as? NSNumber)?.intValue
        snap.billingCycleEndOverridden = result["overridden"] as? Bool ?? false
        snap.apiBillingCycleEnd = result["api_billing_cycle_end"] as? String
    }

    static func isoNow(_ now: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: now)
    }

    static func applyEndOverrideCore(_ snap: inout UsageSnapshot, account: Account?, now: Date = Date()) {
        guard let account, sanitizeKindCore(account.accountKind) == temporary, let end = computeEndIsoCore(startAt: account.tempStartAt, days: account.tempValidDays, hours: account.tempValidHours) else { return }
        if !snap.billingCycleEndOverridden {
            snap.apiBillingCycleEnd = snap.billingCycleEnd
        }
        snap.billingCycleEnd = end
        snap.daysRemaining = UsageParser.daysUntil(end, now: now)
        snap.billingCycleEndOverridden = true
    }

    public static func storedCycleEnd(_ snap: UsageSnapshot) -> String? {
        if snap.billingCycleEndOverridden {
            return snap.apiBillingCycleEnd ?? ""
        }
        return snap.billingCycleEnd
    }

    static func clampInt(_ raw: Any?, lo: Int, hi: Int) -> Int {
        let n: Int
        if let i = raw as? Int { n = i }
        else if let d = raw as? Double { n = Int(d) }
        else if let s = raw as? String, let v = Int(s) { n = v }
        else if let n0 = raw as? NSNumber { n = n0.intValue }
        else { return 0 }
        return min(hi, max(lo, n))
    }
}
