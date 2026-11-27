using System.Text.Json;
using CursorTokenCore;
using Xunit;

namespace CursorTokenCore.Tests;

/// <summary>Doubao fixtures shared with the macOS tests and the Rust core.</summary>
public class DoubaoFixtureTests
{
    static string FixturePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(name);
    }

    static JsonElement Load(string name) =>
        JsonDocument.Parse(File.ReadAllText(FixturePath(name))).RootElement.Clone();

    static double Num(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    static string Text(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static bool Flag(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    static UsageWindow WindowFromRust(JsonElement raw) => new()
    {
        Key = Text(raw, "key"),
        Label = Text(raw, "label"),
        UsedPercent = Num(raw, "used_percent"),
        LessThanOnePercent = Flag(raw, "less_than_one_percent"),
        Exhausted = Flag(raw, "exhausted"),
        Unlimited = Flag(raw, "unlimited"),
        UsedAmount = raw.TryGetProperty("used_amount", out var ua) && ua.ValueKind == JsonValueKind.String ? ua.GetString() : null,
        TotalAmount = raw.TryGetProperty("total_amount", out var ta) && ta.ValueKind == JsonValueKind.String ? ta.GetString() : null,
        ResetAt = raw.TryGetProperty("reset_at", out var ra) && ra.ValueKind == JsonValueKind.String ? ra.GetString() : null,
        Count = raw.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null,
    };

    [Fact]
    public void QuotaCases()
    {
        foreach (var cse in Load("doubao_quota_cases.json").EnumerateArray())
        {
            var name = Text(cse, "name");
            var payload = JsonBag.Parse(cse.GetProperty("payload").GetRawText());
            var result = UsageParser.ParseDoubaoQuota(payload, DateTimeOffset.Parse("2025-11-20T00:00:00Z"));
            var expected = cse.GetProperty("expected");
            if (expected.TryGetProperty("error_contains", out var err))
            {
                Assert.True((result.Error ?? "").Contains(err.GetString() ?? "", StringComparison.Ordinal), name);
                Assert.Equal(Flag(expected, "auth_error"), result.AuthError);
                Assert.Null(result.Snapshot);
                continue;
            }
            var snap = result.Snapshot ?? throw new InvalidOperationException(name);
            Assert.Equal(Num(expected, "used_percent"), snap.UsedPercent, 4);
            Assert.Equal(Num(expected, "remaining_percent"), snap.RemainingPercent, 4);
            Assert.Equal(Text(expected, "membership_type"), snap.MembershipType);
            Assert.Equal("percent", snap.BillingMode);
            var wanted = expected.GetProperty("windows").EnumerateArray().ToList();
            Assert.Equal(wanted.Count, snap.Windows.Count);
            for (var i = 0; i < wanted.Count; i++)
            {
                var window = snap.Windows[i];
                var want = wanted[i];
                Assert.Equal(Text(want, "key"), window.Key);
                Assert.Equal(Text(want, "label"), window.Label);
                Assert.Equal(Num(want, "used_percent"), window.UsedPercent, 4);
                Assert.Equal(Flag(want, "less_than_one_percent"), window.LessThanOnePercent);
                Assert.Equal(Flag(want, "exhausted"), window.Exhausted);
                Assert.Equal(Flag(want, "unlimited"), window.Unlimited);
                Assert.Equal(want.TryGetProperty("reset_at", out var ra) && ra.ValueKind == JsonValueKind.String ? ra.GetString() : null, window.ResetAt);
                if (want.TryGetProperty("count", out var c) && c.ValueKind == JsonValueKind.Number)
                    Assert.Equal(c.GetInt32(), window.Count);
            }
        }
    }

    [Fact]
    public void FormatCases()
    {
        var root = Load("doubao_format_cases.json");
        foreach (var row in root.GetProperty("window_text").EnumerateArray())
            Assert.Equal(Text(row, "output"), UsageParser.DoubaoWindowText(WindowFromRust(row.GetProperty("window"))));
        foreach (var row in root.GetProperty("reset_text").EnumerateArray())
        {
            var now = DateTimeOffset.Parse(Text(row, "now"));
            Assert.Equal(Text(row, "output"), UsageParser.DoubaoResetText(WindowFromRust(row.GetProperty("window")), now));
        }
        foreach (var row in root.GetProperty("cookie_normalize").EnumerateArray())
            Assert.Equal(Text(row, "output"), DoubaoCredentials.Normalize(Text(row, "input")));
        foreach (var row in root.GetProperty("provider").EnumerateArray())
            Assert.Equal(Text(row, "output"), AccountProvider.Sanitize(Text(row, "input")));
        foreach (var row in root.GetProperty("identity").EnumerateArray())
        {
            var cookie = Text(row, "cookie");
            var id = DoubaoCredentials.AccountId(JsonBag.Parse(row.GetProperty("payload").GetRawText()), cookie);
            Assert.Equal(Text(row, "account_id"), id);
        }
    }

    [Fact]
    public void AccountNameCases()
    {
        foreach (var cse in Load("doubao_account_name_cases.json").EnumerateArray())
        {
            Assert.Equal(Text(cse, "expected"), UsageParser.DoubaoAccountName(Text(cse, "html")));
        }
    }

    [Fact]
    public void RemoveAccountRequiresMatchingProvider()
    {
        var cfg = new AppConfig();
        cfg.Accounts.Add(new Account { Id = "user_test_keep", Provider = AccountProvider.Cursor, Token = "tok" });
        var (doubao, _) = cfg.UpsertDoubaoAccount("sessionid=abc; uid_tt=87654321");
        // 用 Cursor 身份去删豆包账号必须失败，且不能误删 Cursor 账号。
        Assert.False(cfg.RemoveAccount(doubao.Id, AccountProvider.Cursor));
        Assert.Single(cfg.CursorAccounts);
        Assert.Single(cfg.DoubaoAccounts);
        // 反过来用豆包身份删 Cursor 账号也必须失败。
        Assert.False(cfg.RemoveAccount("user_test_keep", AccountProvider.Doubao));
        Assert.Single(cfg.CursorAccounts);
        Assert.True(cfg.RemoveAccount(doubao.Id, AccountProvider.Doubao));
        Assert.Single(cfg.CursorAccounts);
    }

    [Fact]
    public void LooksLoggedInRequiresScanSession()
    {
        // 页面刚加载只有访客 Cookie，不能当成登录成功。
        Assert.False(DoubaoCredentials.LooksLoggedIn("uid_tt=87654321; odin_tt=abc"));
        Assert.True(DoubaoCredentials.LooksLoggedIn("sessionid=abc123; uid_tt=87654321"));
    }

    [Fact]
    public void DoubaoAccountsStayLocalAndOffCursorPaths()
    {
        var cfg = new AppConfig();
        var snap = UsageParser.ParseDoubaoQuota(
            JsonBag.Parse("{\"code\":0,\"data\":{\"member_info\":{\"user_id\":\"12345678\",\"membership_type\":\"高级\"},\"window_limit_section\":{\"usage_exhausted\":false,\"window_limit_groups\":[{\"window_limits\":[{\"window_type\":4,\"used_percent\":65}]}]}}}"),
            DateTimeOffset.Parse("2025-11-20T00:00:00Z"));
        var (acc, _) = cfg.UpsertDoubaoAccount("sessionid=abc; uid_tt=87654321", membershipType: snap.Snapshot!.MembershipType, remaining: snap.Snapshot.RemainingPercent);
        Assert.True(acc.IsDoubao);
        Assert.Single(cfg.DoubaoAccounts);
        Assert.Empty(cfg.CursorAccounts);
        Assert.Null(cfg.ActiveAccount);
        Assert.Equal(acc.Id, cfg.ActiveDoubaoAccount?.Id);
        Assert.StartsWith("doubao_", acc.Id, StringComparison.Ordinal);

        var snapCfg = AccountSync.SnapshotFromConfig(cfg);
        Assert.Empty(snapCfg.Accounts);
        Assert.Equal(acc.Id, cfg.ActiveDoubaoAccountId);

        cfg.RemoveAccount(acc.Id);
        Assert.Empty(cfg.DoubaoAccounts);
        Assert.False(cfg.DeletedAccounts.Any(d => d.Id == acc.Id));
    }
}
