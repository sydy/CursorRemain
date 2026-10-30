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

    static string AccountCompareToCsvCore(AccountCompareReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("账号,渠道,套餐,窗口,窗口天数,日均持有,窗口实付,请求,Token,¥/百万Token,¥/次,First-party次数,First-party Token,First-party实付,First-party ¥/百万,First-party ¥/次,API次数,API Token,API实付,API ¥/百万,API ¥/次,Grok Bot次数,Grok Bot Token,Grok Bot实付,Grok Bot ¥/百万,Grok Bot ¥/次");
        foreach (var row in report.Rows)
            sb.AppendLine(CompareCsvCells(row.Label, row.ChannelLabel, row.MembershipType, row.WindowLabel, row.WindowDays, row.DailyHoldingCny, row.TotalCny, row.EventCount, row.TotalTokens, row.CnyPerMillion, row.CnyPerRequest, row.FirstParty, row.Api, row.GrokBot));
        foreach (var group in report.Groups)
            sb.AppendLine(CompareCsvCells(group.ChannelLabel + "合计", group.ChannelLabel, "", "", 0, group.DailyHoldingCny, group.TotalCny, group.EventCount, group.TotalTokens, group.CnyPerMillion, group.CnyPerRequest, group.FirstParty, group.Api, group.GrokBot));
        return sb.ToString();
    }

    static string CompareCsvCells(
        string name, string channel, string membership, string window, double days,
        double dailyHolding, double totalCny, int eventCount, long totalTokens,
        double? perMillion, double? perRequest,
        AccountCompareCategory firstParty, AccountCompareCategory api, AccountCompareCategory grokBot)
    {
        static string[] CatCells(AccountCompareCategory cat) =>
        [
            cat.Count.ToString(CultureInfo.InvariantCulture),
            cat.Tokens.ToString(CultureInfo.InvariantCulture),
            cat.Cny.ToString("0.0000", CultureInfo.InvariantCulture),
            cat.CnyPerMillion is { } m ? m.ToString("0.0000", CultureInfo.InvariantCulture) : "",
            cat.CnyPerRequest is { } r ? r.ToString("0.0000", CultureInfo.InvariantCulture) : "",
        ];
        var cols = new List<string>
        {
            EscapeCsv(name),
            EscapeCsv(channel),
            EscapeCsv(membership),
            EscapeCsv(window),
            days > 0 ? days.ToString("0.00", CultureInfo.InvariantCulture) : "",
            dailyHolding.ToString("0.0000", CultureInfo.InvariantCulture),
            totalCny.ToString("0.0000", CultureInfo.InvariantCulture),
            eventCount.ToString(CultureInfo.InvariantCulture),
            totalTokens.ToString(CultureInfo.InvariantCulture),
            perMillion is { } pm ? pm.ToString("0.0000", CultureInfo.InvariantCulture) : "",
            perRequest is { } pr ? pr.ToString("0.0000", CultureInfo.InvariantCulture) : "",
        };
        cols.AddRange(CatCells(firstParty));
        cols.AddRange(CatCells(api));
        cols.AddRange(CatCells(grokBot));
        return string.Join(",", cols);
    }

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

    static string ToCsvCore(IEnumerable<UsageEvent> events, CnySpendSettings? spend = null, IEnumerable<UsageEvent>? allocationBase = null, ReportAllocationWindow? allocation = null)
    {
        var rows = events as IList<UsageEvent> ?? events.ToList();
        Dictionary<string, double> cnyById = [];
        var rate = 0.0;
        if (spend is not null)
        {
            var baseEvents = allocationBase as IList<UsageEvent> ?? allocationBase?.ToList() ?? rows;
            var allocated = CnyById(baseEvents, spend, allocation);
            cnyById = allocated.byId;
            rate = allocated.rate;
        }
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine(CsvHeader);
        for (var i = 0; i < rows.Count; i++)
        {
            var ev = rows[i];
            double? amount = null;
            if (spend is not null)
            {
                var key = ev.Id.Length > 0 ? ev.Id : $"#{i}";
                amount = cnyById.GetValueOrDefault(key);
            }
            else if (ev.AllocatedCny > 0)
                amount = ev.AllocatedCny;
            var cnyText = amount is null ? "—" : FormatEventCny(ev, amount);
            var discount = FormatEventDiscount(ev, amount ?? 0, rate);
            sb.Append(EscapeCsv(FormatTimeCore(ev.TimestampMs))).Append(',');
            sb.Append(EscapeCsv(ev.UserEmail)).Append(',');
            sb.Append(EscapeCsv(KindLabelCore(ev.Kind))).Append(',');
            sb.Append(EscapeCsv(ev.Model)).Append(',');
            sb.Append(EscapeCsv(ev.Tokens.ToString(CultureInfo.InvariantCulture))).Append(',');
            sb.Append(EscapeCsv(FormatCostCore(ev))).Append(',');
            sb.Append(EscapeCsv(cnyText)).Append(',');
            sb.Append(EscapeCsv(discount)).Append(',');
            sb.Append(EscapeCsv(ev.IsHeadless ? "是" : "否"));
            sb.AppendLine();
        }
        return sb.ToString();
    }

    static string EscapeCsv(string value)
    {
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
