using CursorTokenCore;
using Xunit;

namespace CursorTokenCore.Tests;

public class NativeClientReliabilityTests
{
    [Fact]
    public void RefreshGenerationIgnoresStaleOrInactiveOutcomes()
    {
        Assert.True(RefreshGeneration.ShouldApply("a", "a", 3, 3));
        Assert.False(RefreshGeneration.ShouldApply("a", "a", 2, 3));
        Assert.False(RefreshGeneration.ShouldApply("old", "a", 3, 3));
        Assert.False(RefreshGeneration.ShouldApply("a", "b", 4, 4));
    }

    [Fact]
    public async Task BoundedWorkCapsConcurrency()
    {
        var current = 0;
        var max = 0;
        var gate = new object();
        var items = Enumerable.Range(0, 8).ToList();
        var results = await BoundedWork.MapAsync(items, async i =>
        {
            var now = Interlocked.Increment(ref current);
            lock (gate) { if (now > max) max = now; }
            await Task.Delay(30);
            Interlocked.Decrement(ref current);
            return i;
        }, maxConcurrent: 2);
        Assert.Equal(items, results);
        Assert.InRange(max, 1, 2);
        Assert.Equal(2, BoundedWork.AccountRefreshLimit);
    }

    [Fact]
    public void NormalizeReportRangeSwapsInvertedDates()
    {
        var swapped = UsageEvents.NormalizeReportRange("2026-09-15", "2026-09-01");
        Assert.Equal("2026-09-01", swapped.Start);
        Assert.Equal("2026-09-15", swapped.End);
        Assert.True(swapped.Swapped);
        var ordered = UsageEvents.NormalizeReportRange("2026-09-01", "2026-09-15");
        Assert.False(ordered.Swapped);
        Assert.Equal(("2026-09-08", "", false), UsageEvents.NormalizeReportRange("2026-09-08", ""));
        var mid = UsageEvents.ReportDateStartMs("2026-09-08")!.Value + 12L * 3600 * 1000;
        var ev = new UsageEvent { Id = "mid", TimestampMs = mid, Model = "opus", Kind = UsageEvents.KindIncluded, Tokens = 10 };
        var report = UsageEvents.BuildReport([ev], new UsageReportFilter { StartDate = "2026-09-15", EndDate = "2026-09-01" });
        Assert.Equal(1, report.EventCount);
        Assert.Equal("mid", report.Events[0].Id);
    }

    [Fact]
    public void PrioritizeKeepsRelativeOrder()
    {
        var items = new[] { "b", "a", "c", "d" };
        Assert.Equal(["a", "b", "c", "d"], BoundedWork.Prioritize(items, x => x == "a"));
        Assert.Equal(items, BoundedWork.Prioritize(items, _ => false));
    }

    [Fact]
    public void FlyoutAndReportCopyMatchAuthState()
    {
        Assert.Equal("设置", StatusText.FlyoutSettingsTitle(null));
        Assert.Equal("设置", StatusText.FlyoutSettingsTitle("HTTP 429 Too Many Requests"));
        Assert.Equal("粘贴 Token", StatusText.FlyoutSettingsTitle("Token 已过期或无效，请重新粘贴 WorkosCursorSessionToken"));
        Assert.Equal("粘贴 Token", StatusText.FlyoutSettingsTitle("未配置 Token，请打开设置粘贴"));
        Assert.Equal("正在同步本周期明细…", StatusText.FormatReportSyncProgress(1));
        Assert.Equal("正在同步本周期明细…第 4 页", StatusText.FormatReportSyncProgress(4));
        Assert.Equal(["aaa.bbb.ccc", "ddd.eee.fff"], CursorAccountPaste.TokenValues("aaa.bbb.ccc\nddd.eee.fff"));
        Assert.Empty(CursorAccountPaste.TokenValues("name@example.com:secret"));
    }

    [Fact]
    public void CompareSyncAndTrimStatusCopy()
    {
        Assert.Equal("已同步 3 个账号  ·  12:00:00", StatusText.FormatCompareSync(3, [], "12:00:00"));
        var failed = StatusText.FormatCompareSync(2, ["工作号：Token 过期", "临时号：未配置 Token"], "12:01:00");
        Assert.Contains("工作号：Token 过期", failed);
        Assert.Contains("等4个", StatusText.FormatCompareSync(1, ["a：1", "b：2", "c：3", "d：4"], "12:02:00"));
        Assert.Equal("登录已过期，请重新登录", StatusText.FormatSyncStatus("", "登录已过期，请重新登录"));
        var mixed = StatusText.FormatSyncStatus("2026-09-19T04:00:00.000Z", "用量明细因体积限制裁掉了 12 条最旧记录");
        Assert.Contains("上次同步", mixed);
        Assert.Contains("体积限制", mixed);
        Assert.True(AccountSync.IsTrimNote("用量明细因体积限制裁掉了 3 条最旧记录"));
        Assert.Equal("", AccountSync.FormatTrimNote(0));
        Assert.Equal("已上传到云端；用量明细因体积限制裁掉了 2 条最旧记录", AccountSync.AppendTrimNote("已上传到云端", "用量明细因体积限制裁掉了 2 条最旧记录"));
    }

    [Fact]
    public void AppendWithAccountIdDoesNotLoadConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ctt-hist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            UsageHistory.Append(12.5, auto: 1, api: 2, ts: ts, accountId: "user_01A", directory: dir);
            Assert.False(File.Exists(AppPaths.ConfigPath(dir)));
            var points = UsageHistory.LoadRecent(7, "user_01A", dir);
            Assert.Single(points);
            Assert.Equal(12.5, points[0].Remaining);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void LoadReturnsCachedConfigWhenLockBusy()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ctt-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var prevWait = ConfigStore.LockWaitMilliseconds;
        ConfigStore.LockWaitMilliseconds = 200;
        try
        {
            var cfg = new AppConfig { RefreshIntervalMinutes = 12 };
            ConfigStore.Save(cfg, dir);
            Assert.Equal(12, ConfigStore.Load(dir).RefreshIntervalMinutes);

            using var held = new FileStream(
                Path.Combine(dir, "config.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            File.WriteAllText(AppPaths.ConfigPath(dir), "{not-json");
            var loaded = ConfigStore.Load(dir);
            Assert.False(loaded.LoadError);
            Assert.Equal(12, loaded.RefreshIntervalMinutes);
        }
        finally
        {
            ConfigStore.LockWaitMilliseconds = prevWait;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void SaveThrowsWhenLockBusy()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ctt-lockw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var prevWait = ConfigStore.LockWaitMilliseconds;
        ConfigStore.LockWaitMilliseconds = 150;
        try
        {
            ConfigStore.Save(new AppConfig(), dir);
            using var held = new FileStream(
                Path.Combine(dir, "config.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            Assert.Throws<ConfigLockException>(() => ConfigStore.Save(new AppConfig { RefreshIntervalMinutes = 30 }, dir));
        }
        finally
        {
            ConfigStore.LockWaitMilliseconds = prevWait;
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
