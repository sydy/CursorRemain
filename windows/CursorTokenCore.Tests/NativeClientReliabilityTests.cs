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
