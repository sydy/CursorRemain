using System.Globalization;
using System.Text.Json;

namespace CursorTokenCore;

public static class AccountValidity
{
    public const string LongTerm = "long_term";
    public const string Temporary = "temporary";
    public const int MaxDays = 999;
    public const int MaxHours = 23;

    public static string SanitizeKind(string? raw) =>
        RustCore.String("sanitize_account_kind", new { input = raw ?? "" });

    public static int ClampDays(object? raw) =>
        RustCore.Call("clamp_account_days", new { value = raw }).GetInt32();
    public static int ClampHours(object? raw) =>
        RustCore.Call("clamp_account_hours", new { value = raw }).GetInt32();

    public static bool IsTemporary(Account? account) =>
        account is not null && SanitizeKind(account.AccountKind) == Temporary;

    public static string? ComputeEndIso(string? startAt, int days, int hours)
    {
        var result = RustCore.Call("compute_account_end", new { start = startAt ?? "", days, hours });
        return result.ValueKind == JsonValueKind.String ? result.GetString() : null;
    }

    public static string? AccountEndIso(Account? account)
    {
        if (!IsTemporary(account) || account is null) return null;
        return ComputeEndIso(account.TempStartAt, account.TempValidDays, account.TempValidHours);
    }

    public static void ApplyEndOverride(UsageSnapshot snap, Account? account, DateTimeOffset? now = null)
    {
        var result = RustCore.Call("apply_account_end_override", new
        {
            account = new
            {
                account_kind = account?.AccountKind ?? "",
                temp_start_at = account?.TempStartAt ?? "",
                temp_valid_days = account?.TempValidDays ?? 0,
                temp_valid_hours = account?.TempValidHours ?? 0,
            },
            api_end = snap.BillingCycleEnd,
            now = (now ?? DateTimeOffset.UtcNow).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            days_remaining = snap.DaysRemaining ?? 0,
        });
        snap.BillingCycleEnd = result.GetProperty("billing_cycle_end").ValueKind == JsonValueKind.String ? result.GetProperty("billing_cycle_end").GetString() : null;
        snap.DaysRemaining = result.GetProperty("days_remaining").GetInt32();
        snap.BillingCycleEndOverridden = result.GetProperty("overridden").GetBoolean();
        snap.ApiBillingCycleEnd = result.GetProperty("api_billing_cycle_end").ValueKind == JsonValueKind.String ? result.GetProperty("api_billing_cycle_end").GetString() : null;
    }

    /// <summary>
    /// 写回账号的是 Cursor 账单周期，不是临时账号的展示到期日。
    /// 已被覆盖且接口没有结束时间时返回空字符串，用来清掉之前误存的到期日。
    /// </summary>
    public static string? StoredCycleEnd(UsageSnapshot snap) =>
        snap.BillingCycleEndOverridden ? snap.ApiBillingCycleEnd ?? "" : snap.BillingCycleEnd;

    public static bool ValidityEquals(Account a, string kind, string startAt, int days, int hours) =>
        RustCore.Bool("validity_equals", new
        {
            a_kind = a.AccountKind ?? "",
            a_start = a.TempStartAt ?? "",
            a_days = (object)a.TempValidDays,
            a_hours = (object)a.TempValidHours,
            b_kind = kind ?? "",
            b_start = startAt ?? "",
            b_days = (object)days,
            b_hours = (object)hours,
        });
}
