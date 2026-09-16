using System.Globalization;
using System.Text;

namespace CursorTokenCore;

public static partial class UsageEvents
{
    public static string AccountCompareToCsv(AccountCompareReport report)
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

    public static string ToCsv(IEnumerable<UsageEvent> events, CnySpendSettings? spend = null, IEnumerable<UsageEvent>? allocationBase = null)
    {
        var rows = events as IList<UsageEvent> ?? events.ToList();
        Dictionary<string, double> cnyById = [];
        if (spend is not null)
        {
            var baseEvents = allocationBase as IList<UsageEvent> ?? allocationBase?.ToList() ?? rows;
            cnyById = CnyById(baseEvents, spend).byId;
        }
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine(CsvHeader);
        for (var i = 0; i < rows.Count; i++)
        {
            var ev = rows[i];
            string cnyText;
            if (spend is not null)
            {
                var key = ev.Id.Length > 0 ? ev.Id : $"#{i}";
                cnyText = FormatEventCny(ev, cnyById.GetValueOrDefault(key));
            }
            else if (ev.AllocatedCny > 0)
                cnyText = FormatEventCny(ev);
            else
                cnyText = "—";
            sb.Append(EscapeCsv(FormatTime(ev.TimestampMs))).Append(',');
            sb.Append(EscapeCsv(ev.UserEmail)).Append(',');
            sb.Append(EscapeCsv(KindLabel(ev.Kind))).Append(',');
            sb.Append(EscapeCsv(ev.Model)).Append(',');
            sb.Append(EscapeCsv(ev.Tokens.ToString(CultureInfo.InvariantCulture))).Append(',');
            sb.Append(EscapeCsv(FormatCost(ev))).Append(',');
            sb.Append(EscapeCsv(cnyText)).Append(',');
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
