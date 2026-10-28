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

    static UsageReport BuildReportCore(IEnumerable<UsageEvent> events, UsageReportFilter? filter = null, CnySpendSettings? spend = null, ReportAllocationWindow? allocation = null)
    {
        filter ??= new UsageReportFilter();
        var kind = (filter.Kind ?? "").Trim().ToLowerInvariant();
        var category = (filter.Category ?? "").Trim().ToLowerInvariant();
        var model = (filter.Model ?? "").Trim();
        var owning = (filter.OwningUser ?? "").Trim();
        var (startDate, endDate, _) = NormalizeReportRangeCore(filter.StartDate, filter.EndDate);
        var startMs = ReportDateStartMs(startDate);
        var endMs = ReportDateEndMs(endDate);
        var source = events as IList<UsageEvent> ?? events.ToList();
        var (cnyById, planCny, onDemandCny, monthly, rate, actual, usesActual, windowPlan) = CnyById(source, spend, allocation);
        var selected = new List<UsageEvent>();
        for (var i = 0; i < source.Count; i++)
        {
            var ev = source[i];
            if (kind.Length > 0 && ev.Kind != kind) continue;
            if (category.Length > 0 && ClassifyCategoryCore(ev.Model) != category) continue;
            if (model.Length > 0 && ev.Model != model) continue;
            if (filter.Headless is { } h && ev.IsHeadless != h) continue;
            if (owning.Length > 0 && ev.OwningUser != owning) continue;
            if (startMs is { } s && ev.TimestampMs < s) continue;
            if (endMs is { } e && ev.TimestampMs >= e) continue;
            var key = ev.Id.Length > 0 ? ev.Id : $"#{i}";
            ev.AllocatedCny = cnyById.TryGetValue(key, out var cny) ? cny : 0;
            selected.Add(ev);
        }
        selected = selected.OrderByDescending(ev => ev.TimestampMs).ToList();

        var dailyMap = new Dictionary<string, (long tokens, double cents, int count, double cny)>(StringComparer.Ordinal);
        var modelMap = new Dictionary<string, (long tokens, double cents, int count, int headless, double cny)>(StringComparer.Ordinal);
        var included = 0; var free = 0; var onDemand = 0; var other = 0; var headless = 0;
        var firstParty = 0; var api = 0; var grokBot = 0;
        long totalTokens = 0;
        double totalCents = 0;
        double totalCny = 0;
        var hasCost = false;
        foreach (var ev in selected)
        {
            var cents = CostCents(ev);
            totalTokens += ev.Tokens;
            totalCents += cents;
            totalCny += ev.AllocatedCny;
            if (cents > 0) hasCost = true;
            if (ev.Kind == KindIncluded) included++;
            else if (ev.Kind == KindFree) free++;
            else if (ev.Kind == KindOnDemand) onDemand++;
            else other++;
            switch (ClassifyCategoryCore(ev.Model))
            {
                case CategoryGrokBot: grokBot++; break;
                case CategoryFirstParty: firstParty++; break;
                default: api++; break;
            }
            if (ev.IsHeadless) headless++;
            var day = EventDateCore(ev.TimestampMs);
            dailyMap.TryGetValue(day, out var d);
            dailyMap[day] = (d.tokens + ev.Tokens, d.cents + cents, d.count + 1, d.cny + ev.AllocatedCny);
            var name = string.IsNullOrEmpty(ev.Model) ? "—" : ev.Model;
            modelMap.TryGetValue(name, out var m);
            modelMap[name] = (m.tokens + ev.Tokens, m.cents + cents, m.count + 1, m.headless + (ev.IsHeadless ? 1 : 0), m.cny + ev.AllocatedCny);
        }
        return new UsageReport
        {
            EventCount = selected.Count,
            TotalTokens = totalTokens,
            TotalCents = totalCents,
            HasCost = hasCost,
            IncludedCount = included,
            FreeCount = free,
            OnDemandCount = onDemand,
            OtherCount = other,
            HeadlessCount = headless,
            FirstPartyCount = firstParty,
            ApiCount = api,
            GrokBotCount = grokBot,
            ActualCny = actual,
            UsesActualCny = usesActual,
            WindowPlanCny = windowPlan,
            Daily = dailyMap.OrderBy(kv => kv.Key).Select(kv => new DailyUsageRow(kv.Key, kv.Value.tokens, kv.Value.cents, kv.Value.count, kv.Value.cny)).ToList(),
            Models = modelMap.Select(kv => new ModelUsageRow(kv.Key, kv.Value.tokens, kv.Value.cents, kv.Value.count, kv.Value.headless, kv.Value.cny))
                .OrderByDescending(m => m.Tokens).ThenByDescending(m => m.Cents).ThenByDescending(m => m.Count).ToList(),
            Events = selected,
            TotalCny = totalCny,
            PlanCny = planCny,
            OnDemandCny = onDemandCny,
            UsdCnyRate = rate,
            MonthlyPlanUsd = monthly,
        };
    }
}
