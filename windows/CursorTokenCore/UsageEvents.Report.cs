using System.Globalization;
using System.Text;

namespace CursorTokenCore;

public static partial class UsageEvents
{
    public static UsageReport BuildReport(IEnumerable<UsageEvent> events, UsageReportFilter? filter = null, CnySpendSettings? spend = null)
    {
        filter ??= new UsageReportFilter();
        var kind = (filter.Kind ?? "").Trim().ToLowerInvariant();
        var category = (filter.Category ?? "").Trim().ToLowerInvariant();
        var model = (filter.Model ?? "").Trim();
        var owning = (filter.OwningUser ?? "").Trim();
        var startMs = ReportDateStartMs(filter.StartDate);
        var endMs = ReportDateEndMs(filter.EndDate);
        var source = events as IList<UsageEvent> ?? events.ToList();
        var (cnyById, planCny, onDemandCny, monthly, rate, actual, usesActual) = CnyById(source, spend);
        var selected = new List<UsageEvent>();
        for (var i = 0; i < source.Count; i++)
        {
            var ev = source[i];
            if (kind.Length > 0 && ev.Kind != kind) continue;
            if (category.Length > 0 && ClassifyCategory(ev.Model) != category) continue;
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
            switch (ClassifyCategory(ev.Model))
            {
                case CategoryGrokBot: grokBot++; break;
                case CategoryFirstParty: firstParty++; break;
                default: api++; break;
            }
            if (ev.IsHeadless) headless++;
            var day = EventDate(ev.TimestampMs);
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
