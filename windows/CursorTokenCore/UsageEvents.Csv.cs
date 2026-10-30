using System.Globalization;
using System.Text;

namespace CursorTokenCore;

public static partial class UsageEvents
{
    public static string AccountCompareToCsv(AccountCompareReport report) =>
        RustCore.String("account_compare_csv", new { report = CompareReportJson(report) });

    static object CompareReportJson(AccountCompareReport report) => new
    {
        rows = report.Rows.Select(row => new
        {
            label = row.Label, channel_label = row.ChannelLabel, membership_type = row.MembershipType,
            window_label = row.WindowLabel, window_days = row.WindowDays, daily_holding_cny = row.DailyHoldingCny,
            total_cny = row.TotalCny, event_count = row.EventCount, total_tokens = row.TotalTokens,
            cny_per_million = row.CnyPerMillion, cny_per_request = row.CnyPerRequest,
            first_party = CategoryJson(row.FirstParty), api = CategoryJson(row.Api), grok_bot = CategoryJson(row.GrokBot),
        }),
        groups = report.Groups.Select(group => new
        {
            channel_label = group.ChannelLabel, daily_holding_cny = group.DailyHoldingCny,
            total_cny = group.TotalCny, event_count = group.EventCount, total_tokens = group.TotalTokens,
            cny_per_million = group.CnyPerMillion, cny_per_request = group.CnyPerRequest,
            first_party = CategoryJson(group.FirstParty), api = CategoryJson(group.Api), grok_bot = CategoryJson(group.GrokBot),
        }),
    };

    static object CategoryJson(AccountCompareCategory cat) => new
    {
        count = cat.Count, tokens = cat.Tokens, cny = cat.Cny,
        cny_per_million = cat.CnyPerMillion, cny_per_request = cat.CnyPerRequest,
    };

    public static string ToCsv(IEnumerable<UsageEvent> events, CnySpendSettings? spend = null, IEnumerable<UsageEvent>? allocationBase = null, ReportAllocationWindow? allocation = null)
    {
        return RustCore.String("usage_events_csv", new
        {
            events = events.Select(CsvEventJson).ToList(),
            spend = spend is null ? null : new
            {
                monthly_plan_usd = spend.Value.MonthlyPlanUsd,
                usd_cny_rate = spend.Value.UsdCnyRate,
                membership_type = spend.Value.MembershipType,
                actual_cny = spend.Value.ActualCny,
            },
            allocation_base = allocationBase?.Select(CsvEventJson).ToList(),
            allocation = allocation is null ? null : new
            {
                account_kind = allocation.Value.AccountKind,
                temp_start_at = allocation.Value.TempStartAt,
                temp_valid_days = allocation.Value.TempValidDays,
                temp_valid_hours = allocation.Value.TempValidHours,
                billing_cycle_start = allocation.Value.BillingCycleStart,
                billing_cycle_end = allocation.Value.BillingCycleEnd,
                now_ms = allocation.Value.NowMs,
            },
        });
    }

    static object CsvEventJson(UsageEvent ev) => new
    {
        id = ev.Id,
        timestamp_ms = ev.TimestampMs, user_email = ev.UserEmail, kind = ev.Kind, model = ev.Model,
        tokens = ev.Tokens, charged_cents = ev.ChargedCents, total_cents = ev.TotalCents,
        is_headless = ev.IsHeadless, allocated_cny = ev.AllocatedCny,
    };

    static string EscapeCsv(string value)
    {
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
