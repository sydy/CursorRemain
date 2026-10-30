using System.Globalization;
using System.Text.Json;
using System.Text;

namespace CursorTokenCore;

public static partial class UsageEvents
{
    public static UsageReport BuildReport(IEnumerable<UsageEvent> events, UsageReportFilter? filter = null, CnySpendSettings? spend = null, ReportAllocationWindow? allocation = null)
    {
        {
            filter ??= new UsageReportFilter();
            var result = RustCore.Call("build_usage_report", new
            {
                events = events.Select(EventJson).ToList(),
                filter = new
                {
                    kind = filter.Kind ?? "",
                    category = filter.Category ?? "",
                    model = filter.Model ?? "",
                    headless = filter.Headless,
                    owning_user = filter.OwningUser ?? "",
                    start_date = filter.StartDate ?? "",
                    end_date = filter.EndDate ?? "",
                },
                spend = spend is null ? null : new
                {
                    monthly_plan_usd = spend.Value.MonthlyPlanUsd,
                    usd_cny_rate = spend.Value.UsdCnyRate,
                    membership_type = spend.Value.MembershipType,
                    actual_cny = spend.Value.ActualCny,
                },
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
            return ReportFromRust(result);
        }
    }

    static UsageReport ReportFromRust(JsonElement result)
    {
        static long Whole(JsonElement row, string key) => row.GetProperty(key).GetInt64();
        return new UsageReport
        {
            EventCount = result.GetProperty("event_count").GetInt32(),
            TotalTokens = result.GetProperty("total_tokens").GetInt64(),
            TotalCents = result.GetProperty("total_cents").GetDouble(),
            HasCost = result.GetProperty("has_cost").GetBoolean(),
            IncludedCount = result.GetProperty("included_count").GetInt32(),
            FreeCount = result.GetProperty("free_count").GetInt32(),
            OnDemandCount = result.GetProperty("on_demand_count").GetInt32(),
            OtherCount = result.GetProperty("other_count").GetInt32(),
            HeadlessCount = result.GetProperty("headless_count").GetInt32(),
            FirstPartyCount = result.GetProperty("first_party_count").GetInt32(),
            ApiCount = result.GetProperty("api_count").GetInt32(),
            GrokBotCount = result.GetProperty("grok_bot_count").GetInt32(),
            Daily = result.GetProperty("daily").EnumerateArray().Select(row => new DailyUsageRow(row.GetProperty("date").GetString() ?? "", Whole(row, "tokens"), row.GetProperty("cents").GetDouble(), row.GetProperty("count").GetInt32())).ToList(),
            Models = result.GetProperty("models").EnumerateArray().Select(row => new ModelUsageRow(row.GetProperty("name").GetString() ?? "", Whole(row, "tokens"), row.GetProperty("cents").GetDouble(), row.GetProperty("count").GetInt32(), row.GetProperty("headless_count").GetInt32(), row.TryGetProperty("cny", out var cny) ? cny.GetDouble() : 0)).ToList(),
            Events = result.GetProperty("events").EnumerateArray().Select(row =>
            {
                var ev = EventFromRust(row);
                ev.AllocatedCny = row.TryGetProperty("allocated_cny", out var cny) && cny.ValueKind == JsonValueKind.Number ? cny.GetDouble() : 0;
                return ev;
            }).ToList(),
            TotalCny = result.TryGetProperty("total_cny", out var totalCny) ? totalCny.GetDouble() : 0,
            PlanCny = result.TryGetProperty("plan_cny", out var plan) ? plan.GetDouble() : 0,
            OnDemandCny = result.TryGetProperty("on_demand_cny", out var demand) ? demand.GetDouble() : 0,
            UsdCnyRate = result.TryGetProperty("usd_cny_rate", out var rate) ? rate.GetDouble() : 0,
            MonthlyPlanUsd = result.TryGetProperty("monthly_plan_usd", out var monthly) ? monthly.GetDouble() : 0,
            ActualCny = result.TryGetProperty("actual_cny", out var actual) ? actual.GetDouble() : 0,
            UsesActualCny = result.TryGetProperty("uses_actual_cny", out var uses) && uses.ValueKind == JsonValueKind.True,
            WindowPlanCny = result.TryGetProperty("window_plan_cny", out var window) ? window.GetDouble() : 0,
        };
    }
}
