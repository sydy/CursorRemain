using CursorTokenCore;
using Xunit;

namespace CursorTokenCore.Tests;

public class AppPathsTests
{
    [Fact]
    public void MigratesLegacyDirectoryWhenDestMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "cremain-mig-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "CursorTokenTray");
        var dest = Path.Combine(root, "CursorRemain");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "config.json"), "{\"refresh_interval_minutes\":9}");
        File.WriteAllText(Path.Combine(source, "usage_history.jsonl"), "x\n");
        try
        {
            Assert.True(AppPaths.TryMigrateLegacyDirectory(source, dest));
            Assert.True(File.Exists(Path.Combine(dest, "config.json")));
            Assert.True(File.Exists(Path.Combine(dest, "usage_history.jsonl")));
            Assert.False(Directory.Exists(source));
            Assert.False(AppPaths.TryMigrateLegacyDirectory(source, dest));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void LeavesDestAloneWhenItAlreadyHasConfig()
    {
        var root = Path.Combine(Path.GetTempPath(), "cremain-keep-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "CursorTokenTray");
        var dest = Path.Combine(root, "CursorRemain");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(source, "config.json"), "{\"from\":\"old\"}");
        File.WriteAllText(Path.Combine(dest, "config.json"), "{\"from\":\"new\"}");
        try
        {
            Assert.False(AppPaths.TryMigrateLegacyDirectory(source, dest));
            Assert.Equal("{\"from\":\"new\"}", File.ReadAllText(Path.Combine(dest, "config.json")));
            Assert.True(File.Exists(Path.Combine(source, "config.json")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void CopiesIntoExistingEmptyDest()
    {
        var root = Path.Combine(Path.GetTempPath(), "cremain-copy-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "CursorTokenTray");
        var dest = Path.Combine(root, "CursorRemain");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(source, "config.json"), "{\"ok\":true}");
        try
        {
            Assert.True(AppPaths.TryMigrateLegacyDirectory(source, dest));
            Assert.True(File.Exists(Path.Combine(dest, "config.json")));
            Assert.True(File.Exists(Path.Combine(source, "config.json")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void OverrideDirectorySkipsDefaultName()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cremain-ovr-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(dir, AppPaths.ConfigDirectory(dir));
        Assert.Equal(AppPaths.AppName, "CursorRemain");
        Assert.Equal(AppPaths.LegacyAppName, "CursorTokenTray");
        Assert.Equal("Cursor 余量", AppPaths.DisplayName);
    }
}
