using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CursorTokenCore;

public record ModelTokenUsage(string Name, int Tokens, double Cents, int Tier, double? UsagePercent = null)
{
    public bool IsCursorModel => Tier == UsageParser.CursorModelTier;
    public bool IsGrokBot => UsageParser.IsGrokBotModel(Name);
}

public sealed class UsageSnapshot
{
    public double UsedPercent { get; set; }
    public double RemainingPercent { get; set; }
    public double? AutoPercentUsed { get; set; }
    public double? ApiPercentUsed { get; set; }
    public double? TotalPercentUsed { get; set; }
    public string MembershipType { get; set; } = "";
    public string? BillingCycleStart { get; set; }
    public string? BillingCycleEnd { get; set; }
    public int? DaysRemaining { get; set; }
    public double? DaysElapsed { get; set; }
    public double? EstimatedUsableDays { get; set; }
    public JsonBag Raw { get; set; } = JsonBag.Null;
    public int? TotalTokens { get; set; }
    public List<ModelTokenUsage> ModelUsages { get; set; } = [];
    public string BillingMode { get; set; } = "percent";
    public double? UsedCents { get; set; }
    public double? LimitCents { get; set; }
    public double? RemainingCents { get; set; }
    public double? OnDemandUsedCents { get; set; }
    public double? OnDemandLimitCents { get; set; }
    public double? PooledUsedCents { get; set; }
    public double? PooledLimitCents { get; set; }
    public string LimitType { get; set; } = "";
    public bool IsUnlimited { get; set; }
    public bool BillingCycleEndOverridden { get; set; }
    /// <summary>Cursor 返回的账单周期结束时间。临时账号展示用的到期日不会写进这里。</summary>
    public string? ApiBillingCycleEnd { get; set; }
    public double? GrokBotPercentUsed { get; set; }
    public double? GrokBotRemainingPercent { get; set; }
    public string? GrokBotPeriodStart { get; set; }
    public string? GrokBotResetAt { get; set; }
    public int? GrokBotDaysRemaining { get; set; }
    public bool ShowsAmount => UsedCents is not null && LimitCents is > 0 && (BillingMode == "amount" || IsTeamAccount);
    public bool ShowsGrokBot => GrokBotPercentUsed is not null;
    public bool IsXSubscription { get; set; }
    public string? UsageResetAt { get; set; }
    public double? PrepaidRemainingCents { get; set; }
    public bool IsTeamAccount => !IsXSubscription && !UsageParser.IsXMembership(MembershipType) && UsageParser.IsTeamMembership(MembershipType, LimitType);
}

public sealed record GrokBotUsage(
    double PercentUsed,
    double RemainingPercent,
    string? PeriodStart,
    string? ResetAt,
    int? DaysRemaining);

public static class UsageParser
{
    public const string CursorBase = "https://cursor.com";
    public const string UsageUrl = "https://cursor.com/dashboard/usage";
    public const string BillingUrl = "https://cursor.com/dashboard/billing";
    public const string GrokDashboardUrl = "https://grok.com/dashboard";
    public const int CursorModelTier = 2;
    public static readonly string[] UsageEndpoints = ["/api/usage-summary", "/api/dashboard/usage-summary"];
    public const string AggregatedEndpoint = "/api/dashboard/get-aggregated-usage-events";
    public const string FilteredEndpoint = "/api/dashboard/get-filtered-usage-events";
    public const string SandUsageEndpoint = "/api/dashboard/get-sand-usage-status";
    public const double SandUsageTimeout = 8;
    public const int UsageEventsPageSize = 100;
    public const int UsageEventsMaxPages = 50;

    static readonly HashSet<string> TeamMemberships = ["enterprise", "enterprise_trial", "team", "teams", "business"];
    static readonly Dictionary<string, string> MembershipLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pro"] = "Pro", ["pro_plus"] = "Pro+", ["pro+"] = "Pro+", ["ultra"] = "Ultra",
        ["free"] = "Free", ["hobby"] = "Hobby", ["enterprise"] = "Enterprise",
        ["enterprise_trial"] = "Enterprise", ["team"] = "Team", ["teams"] = "Team",
        ["business"] = "Business", ["unpaid"] = "Free",
        ["plus"] = "Plus", ["premium"] = "Premium", ["super"] = "Super",
        ["x_plus"] = "Plus", ["x_premium"] = "Premium", ["x_super"] = "Super",
        ["x_ultra"] = "Ultra", ["x-ultra"] = "Ultra",
    };
    static readonly HashSet<string> XMemberships = ["plus", "premium", "super", "x_plus", "x_premium", "x_super", "x_ultra"];

    public static string FormatMembershipType(string? raw) =>
        RustCore.String("format_membership", new { input = raw ?? "" });

    public static bool IsTeamMembership(string? membership, string? limitType = null) =>
        RustCore.Bool("is_team_membership", new { membership = membership ?? "", limit_type = limitType ?? "" });

    public static string MembershipKey(string? raw) =>
        RustCore.String("membership_key", new { input = raw ?? "" });

    public static bool IsXMembership(string? raw) =>
        RustCore.Bool("is_x_membership", new { input = raw ?? "" });

    static bool PreferGrokDashboard(UsageSnapshot? usage, string membership) =>
        usage is { IsXSubscription: true } || IsXMembership(usage?.MembershipType) || IsXMembership(membership);

    public static string DashboardUrl(UsageSnapshot? usage = null, string membership = "", string limitType = "")
    {
        if (usage is not null) { membership = usage.MembershipType; limitType = usage.LimitType; }
        return RustCore.String("dashboard_url", new { membership, limit_type = limitType, x = PreferGrokDashboard(usage, membership) });
    }

    public static string DashboardMenuLabel(UsageSnapshot? usage = null, string membership = "", string limitType = "")
    {
        if (usage is not null) { membership = usage.MembershipType; limitType = usage.LimitType; }
        return RustCore.String("dashboard_menu_label", new { membership, limit_type = limitType, x = PreferGrokDashboard(usage, membership) });
    }

    public static string DashboardButtonLabel(UsageSnapshot? usage = null, string membership = "", string limitType = "")
    {
        if (usage is not null) { membership = usage.MembershipType; limitType = usage.LimitType; }
        return RustCore.String("dashboard_button_label", new { membership, limit_type = limitType, x = PreferGrokDashboard(usage, membership) });
    }

    public static string DashboardLinkLabel(UsageSnapshot? usage)
    {
        var membership = usage?.MembershipType ?? "";
        return RustCore.String("dashboard_link_label", new
        {
            membership,
            x = usage is { IsXSubscription: true } || IsXMembership(membership),
            team = usage?.IsTeamAccount == true,
        });
    }

    public static string FormatUsdCents(double? cents) =>
        RustCore.String("format_usd_cents", new { cents });

    public static string FormatSpendRange(double? used, double? limit) =>
        RustCore.String("format_spend_range", new { used, limit });

    public static bool IsGrokBotModel(string? name) =>
        RustCore.Bool("is_grok_bot_model", new { input = name ?? "" });

    public static bool IsFirstPartyModel(string? name) =>
        RustCore.Bool("is_first_party_model", new { input = name ?? "" });

    public static string UsageCategory(string? name) =>
        RustCore.String("usage_category", new { input = name ?? "" });

    public static void ApplySandUsage(UsageSnapshot snapshot, JsonBag payload, DateTimeOffset? now = null)
    {
        var result = RustCore.Call("apply_sand_usage", new
        {
            snapshot = SnapshotJson(snapshot),
            payload = payload.RawElement,
            now = now?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
        });
        var parsed = SnapshotFromRust(result);
        var raw = snapshot.Raw;
        var models = snapshot.ModelUsages;
        var tokens = snapshot.TotalTokens;
        CopySnapshot(parsed, snapshot);
        snapshot.Raw = raw;
        snapshot.ModelUsages = models;
        snapshot.TotalTokens = tokens;
    }

    internal static Dictionary<string, object?> SnapshotJson(UsageSnapshot snapshot) => new()
    {
        ["used_percent"] = snapshot.UsedPercent,
        ["remaining_percent"] = snapshot.RemainingPercent,
        ["is_x_subscription"] = snapshot.IsXSubscription,
        ["membership_type"] = snapshot.MembershipType,
        ["billing_mode"] = snapshot.BillingMode,
        ["used_cents"] = snapshot.UsedCents,
        ["limit_cents"] = snapshot.LimitCents,
        ["pooled_used_cents"] = snapshot.PooledUsedCents,
        ["pooled_limit_cents"] = snapshot.PooledLimitCents,
        ["on_demand_used_cents"] = snapshot.OnDemandUsedCents,
        ["on_demand_limit_cents"] = snapshot.OnDemandLimitCents,
        ["is_unlimited"] = snapshot.IsUnlimited,
        ["shows_amount"] = snapshot.ShowsAmount,
        ["is_team_account"] = snapshot.IsTeamAccount,
        ["limit_type"] = snapshot.LimitType,
        ["billing_cycle_end"] = snapshot.BillingCycleEnd,
        ["billing_cycle_end_overridden"] = snapshot.BillingCycleEndOverridden,
        ["total_tokens"] = snapshot.TotalTokens,
        ["auto_percent_used"] = snapshot.AutoPercentUsed,
        ["api_percent_used"] = snapshot.ApiPercentUsed,
        ["prepaid_remaining_cents"] = snapshot.PrepaidRemainingCents,
        ["usage_reset_at"] = snapshot.UsageResetAt,
        ["days_remaining"] = snapshot.DaysRemaining,
        ["days_elapsed"] = snapshot.DaysElapsed,
        ["estimated_usable_days"] = snapshot.EstimatedUsableDays,
        ["grok_bot_percent_used"] = snapshot.GrokBotPercentUsed,
    };

    static void CopySnapshot(UsageSnapshot from, UsageSnapshot to)
    {
        to.UsedPercent = from.UsedPercent;
        to.RemainingPercent = from.RemainingPercent;
        to.AutoPercentUsed = from.AutoPercentUsed;
        to.ApiPercentUsed = from.ApiPercentUsed;
        to.TotalPercentUsed = from.TotalPercentUsed;
        to.MembershipType = from.MembershipType;
        to.BillingCycleStart = from.BillingCycleStart;
        to.BillingCycleEnd = from.BillingCycleEnd;
        to.DaysRemaining = from.DaysRemaining;
        to.DaysElapsed = from.DaysElapsed;
        to.EstimatedUsableDays = from.EstimatedUsableDays;
        to.BillingMode = from.BillingMode;
        to.UsedCents = from.UsedCents;
        to.LimitCents = from.LimitCents;
        to.RemainingCents = from.RemainingCents;
        to.OnDemandUsedCents = from.OnDemandUsedCents;
        to.OnDemandLimitCents = from.OnDemandLimitCents;
        to.PooledUsedCents = from.PooledUsedCents;
        to.PooledLimitCents = from.PooledLimitCents;
        to.LimitType = from.LimitType;
        to.IsUnlimited = from.IsUnlimited;
        to.IsXSubscription = from.IsXSubscription;
        to.UsageResetAt = from.UsageResetAt;
        to.PrepaidRemainingCents = from.PrepaidRemainingCents;
        to.GrokBotPercentUsed = from.GrokBotPercentUsed;
        to.GrokBotRemainingPercent = from.GrokBotRemainingPercent;
        to.GrokBotPeriodStart = from.GrokBotPeriodStart;
        to.GrokBotResetAt = from.GrokBotResetAt;
        to.GrokBotDaysRemaining = from.GrokBotDaysRemaining;
    }

    public static GrokBotUsage? ParseSandUsageStatus(JsonBag payload, DateTimeOffset? now = null)
    {
        var result = RustCore.Call("parse_sand_usage", new
        {
            payload = payload.RawElement,
            now = now?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
        });
        if (result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        return new GrokBotUsage(
            Num(result, "percent_used") ?? 0,
            Num(result, "remaining_percent") ?? 0,
            NullStr(result, "period_start"),
            NullStr(result, "reset_at"),
            IntOrNull(result, "days_remaining"));
    }

    public static string FormatTokenCount(double? count) =>
        RustCore.String("format_token_count", new { n = count });

    public static UsageSnapshot ParseUsageSummary(JsonBag payload, DateTimeOffset? now = null)
    {
        var result = RustCore.Call("parse_usage_summary", new
        {
            payload = payload.RawElement,
            now = now?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
        });
        var snap = SnapshotFromRust(result);
        snap.Raw = payload;
        return snap;
    }

    static UsageSnapshot SnapshotFromRust(JsonElement result)
    {
        var snap = new UsageSnapshot
        {
            UsedPercent = Num(result, "used_percent") ?? 0,
            RemainingPercent = Num(result, "remaining_percent") ?? 0,
            AutoPercentUsed = Num(result, "auto_percent_used"),
            ApiPercentUsed = Num(result, "api_percent_used"),
            TotalPercentUsed = Num(result, "total_percent_used"),
            MembershipType = Str(result, "membership_type"),
            BillingCycleStart = NullStr(result, "billing_cycle_start"),
            BillingCycleEnd = NullStr(result, "billing_cycle_end"),
            DaysRemaining = IntOrNull(result, "days_remaining"),
            DaysElapsed = Num(result, "days_elapsed"),
            EstimatedUsableDays = Num(result, "estimated_usable_days"),
            BillingMode = Str(result, "billing_mode"),
            UsedCents = Num(result, "used_cents"),
            LimitCents = Num(result, "limit_cents"),
            RemainingCents = Num(result, "remaining_cents"),
            OnDemandUsedCents = Num(result, "on_demand_used_cents"),
            OnDemandLimitCents = Num(result, "on_demand_limit_cents"),
            PooledUsedCents = Num(result, "pooled_used_cents"),
            PooledLimitCents = Num(result, "pooled_limit_cents"),
            LimitType = Str(result, "limit_type"),
            IsUnlimited = result.TryGetProperty("is_unlimited", out var unlimited) && unlimited.ValueKind == JsonValueKind.True,
            IsXSubscription = result.TryGetProperty("is_x_subscription", out var x) && x.ValueKind == JsonValueKind.True,
            UsageResetAt = NullStr(result, "usage_reset_at"),
            PrepaidRemainingCents = Num(result, "prepaid_remaining_cents"),
            GrokBotPercentUsed = Num(result, "grok_bot_percent_used"),
            GrokBotRemainingPercent = Num(result, "grok_bot_remaining_percent"),
            GrokBotPeriodStart = NullStr(result, "grok_bot_period_start"),
            GrokBotResetAt = NullStr(result, "grok_bot_reset_at"),
            GrokBotDaysRemaining = IntOrNull(result, "grok_bot_days_remaining"),
        };
        return snap;
    }

    static double? Num(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    static int? IntOrNull(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    static string Str(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static string? NullStr(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static (List<ModelTokenUsage> models, int total) ParseAggregatedUsage(JsonBag payload, double? autoPercent = null, double? apiPercent = null)
    {
        var result = RustCore.Call("parse_aggregated_usage", new { payload = payload.RawElement, auto_percent = autoPercent, api_percent = apiPercent });
        var models = new List<ModelTokenUsage>();
        if (result.TryGetProperty("models", out var rows))
        {
            foreach (var row in rows.EnumerateArray())
            {
                models.Add(new ModelTokenUsage(
                    Str(row, "name"),
                    IntOrNull(row, "tokens") ?? 0,
                    Num(row, "cents") ?? 0,
                    IntOrNull(row, "tier") ?? 1,
                    Num(row, "usage_percent")));
            }
        }
        return (models, IntOrNull(result, "total") ?? 0);
    }

    public static int TeamId(JsonBag payload)
    {
        foreach (var key in new[] { "teamId", "owningTeam" })
            if (payload[key].AsInt() is > 0 and var n) return n;
        var team = Obj(payload["teamUsage"]);
        if (team["teamId"].AsInt() is > 0 and var a) return a;
        if (team["id"].AsInt() is > 0 and var b) return b;
        return -1;
    }

    public static int UserId(JsonBag payload)
    {
        foreach (var key in new[] { "userId", "numericUserId", "currentUserId" })
            if (payload[key].AsInt() is > 0 and var n) return n;
        var individual = Obj(payload["individualUsage"]);
        if (individual["userId"].AsInt() is > 0 and var a) return a;
        if (individual["id"].AsInt() is > 0 and var b) return b;
        return -1;
    }

    public static long? IsoToMs(string? iso)
    {
        var dt = ParseIso(iso);
        return dt is null ? null : (long)(dt.Value.ToUnixTimeMilliseconds());
    }

    public static double? EstimateUsableDays(double usedPercent, double remainingPercent, double? daysElapsed)
    {
        if (daysElapsed is null) return null;
        if (remainingPercent <= 0) return 0;
        if (daysElapsed < 0.04) return null;
        if (usedPercent < 0.2) return null;
        var burn = usedPercent / daysElapsed.Value;
        if (burn <= 1e-6) return null;
        return Numbers.Round1(remainingPercent / burn);
    }

    static JsonBag Obj(JsonBag v) => v.IsObject ? v : JsonBag.Null;

    public static string? TimestampToIso(JsonBag value)
    {
        var raw = value.AsString()?.Trim();
        if (!string.IsNullOrEmpty(raw) && (raw.Contains('T') || raw.EndsWith("Z", StringComparison.Ordinal)))
            return ParseIso(raw) is null ? null : raw;
        if (value.AsDouble() is not { } num) return null;
        var seconds = Math.Abs(num) > 10_000_000_000 ? num / 1000.0 : num;
        try
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(seconds * 1000.0)).ToUniversalTime();
            return dt.Millisecond == 0
                ? dt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
                : dt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    static DateTimeOffset? ParseIso(string? iso)
    {
        if (string.IsNullOrEmpty(iso)) return null;
        var text = iso.Replace("Z", "+00:00");
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            return dt.ToUniversalTime();
        return null;
    }

    public static int? DaysUntil(string? iso, DateTimeOffset now)
    {
        var end = ParseIso(iso);
        if (end is null) return null;
        return Math.Max(0, (int)((end.Value - now).TotalDays));
    }

    static double? DaysSince(string? iso, DateTimeOffset now)
    {
        var start = ParseIso(iso);
        if (start is null) return null;
        var hours = (now - start.Value).TotalHours;
        if (hours < 0) return 0;
        return hours / 24.0;
    }
}
