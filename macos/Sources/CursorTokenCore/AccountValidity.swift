import Foundation

public enum AccountValidity {
    public static let longTerm = "long_term"
    public static let temporary = "temporary"
    public static let maxDays = 999
    public static let maxHours = 23

    public static func sanitizeKind(_ raw: String?) -> String {
        RustCore.string("sanitize_account_kind", ["input": raw ?? ""])
    }

    public static func clampDays(_ raw: Any?) -> Int {
        RustCore.intValue("clamp_account_days", ["value": raw as Any])
    }
    public static func clampHours(_ raw: Any?) -> Int {
        RustCore.intValue("clamp_account_hours", ["value": raw as Any])
    }

    public static func isTemporary(_ account: Account?) -> Bool {
        guard let account else { return false }
        return sanitizeKind(account.accountKind) == temporary
    }

    public static func computeEndIso(startAt: String?, days: Int, hours: Int) -> String? {
        let result = RustCore.call("compute_account_end", ["start": startAt ?? "", "days": days, "hours": hours])
        return result as? String
    }

    public static func accountEndIso(_ account: Account?) -> String? {
        guard isTemporary(account), let account else { return nil }
        return computeEndIso(startAt: account.tempStartAt, days: account.tempValidDays, hours: account.tempValidHours)
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

    public static func storedCycleEnd(_ snap: UsageSnapshot) -> String? {
        if snap.billingCycleEndOverridden {
            return snap.apiBillingCycleEnd ?? ""
        }
        return snap.billingCycleEnd
    }
}
