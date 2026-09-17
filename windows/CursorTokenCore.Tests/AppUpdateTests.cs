using System.IO.Compression;
using System.Text.Json;
using CursorTokenCore;
using Xunit;

namespace CursorTokenCore.Tests;

public class AppUpdateTests
{
    static JsonElement Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures", "update_release_cases.json");
            if (File.Exists(candidate)) return JsonDocument.Parse(File.ReadAllText(candidate)).RootElement.Clone();
            dir = dir.Parent;
        }
        var cwd = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var i = 0; i < 12 && cwd is not null; i++)
        {
            var candidate = Path.Combine(cwd.FullName, "fixtures", "update_release_cases.json");
            if (File.Exists(candidate)) return JsonDocument.Parse(File.ReadAllText(candidate)).RootElement.Clone();
            cwd = cwd.Parent;
        }
        throw new FileNotFoundException("update_release_cases.json");
    }

    [Fact]
    public void FixtureCases()
    {
        var root = Load();
        foreach (var row in root.GetProperty("extract_sha").EnumerateArray())
            Assert.Equal(row.GetProperty("sha").GetString(), AppUpdate.ExtractSha(row.GetProperty("text").GetString()));

        foreach (var row in root.GetProperty("same_sha").EnumerateArray())
            Assert.Equal(row.GetProperty("same").GetBoolean(), AppUpdate.SameSha(row.GetProperty("left").GetString(), row.GetProperty("right").GetString()));

        foreach (var row in root.GetProperty("normalize_sha").EnumerateArray())
            Assert.Equal(row.GetProperty("output").GetString(), AppUpdate.NormalizeSha(row.GetProperty("input").GetString()));

        foreach (var row in root.GetProperty("allowed_urls").EnumerateArray())
            Assert.Equal(row.GetProperty("ok").GetBoolean(), AppUpdate.IsAllowedDownloadUrl(row.GetProperty("url").GetString()));

        foreach (var row in root.GetProperty("packaged_windows").EnumerateArray())
            Assert.Equal(row.GetProperty("ok").GetBoolean(), AppUpdate.LooksPackagedWindows(row.GetProperty("path").GetString()));

        foreach (var row in root.GetProperty("packaged_macos").EnumerateArray())
            Assert.Equal(row.GetProperty("ok").GetBoolean(), AppUpdate.LooksPackagedMacos(row.GetProperty("path").GetString()));

        foreach (var row in root.GetProperty("parse_release").EnumerateArray())
        {
            var parsed = AppUpdate.ParseRelease(row.GetProperty("json").GetRawText());
            var exp = row.GetProperty("expected");
            Assert.Equal(exp.GetProperty("tag").GetString(), parsed.Tag);
            Assert.Equal(exp.GetProperty("sha").GetString(), parsed.CommitSha);
            Assert.Equal(exp.GetProperty("page_url").GetString(), parsed.PageUrl);
            Assert.Equal(exp.GetProperty("windows_url").GetString(), AppUpdate.FindAsset(parsed, AppUpdate.WindowsAssetName)?.Url);
            Assert.Equal(exp.GetProperty("macos_url").GetString(), AppUpdate.FindAsset(parsed, AppUpdate.MacosAssetName)?.Url);
            Assert.Equal(exp.GetProperty("windows_asset_id").GetInt64(), AppUpdate.FindAsset(parsed, AppUpdate.WindowsAssetName)?.Id);
            Assert.Equal(exp.GetProperty("macos_asset_id").GetInt64(), AppUpdate.FindAsset(parsed, AppUpdate.MacosAssetName)?.Id);
        }

        foreach (var row in root.GetProperty("parse_release_page").EnumerateArray())
        {
            var parsed = AppUpdate.ParseReleasePage(row.GetProperty("html").GetString() ?? "");
            Assert.Equal(row.GetProperty("sha").GetString(), parsed.CommitSha);
            Assert.Equal(row.GetProperty("windows_url").GetString(), AppUpdate.FindAsset(parsed, AppUpdate.WindowsAssetName)?.Url);
            Assert.Equal(row.GetProperty("macos_url").GetString(), AppUpdate.FindAsset(parsed, AppUpdate.MacosAssetName)?.Url);
        }

        foreach (var row in root.GetProperty("parse_tag_ref").EnumerateArray())
            Assert.Equal(row.GetProperty("sha").GetString(), AppUpdate.ParseTagRefSha(row.GetProperty("json").GetRawText()));

        var sample = AppUpdate.ParseRelease(root.GetProperty("parse_release")[0].GetProperty("json").GetRawText());
        foreach (var row in root.GetProperty("evaluate").EnumerateArray())
        {
            var release = sample;
            if (row.TryGetProperty("clear_release_sha", out var clear) && clear.GetBoolean())
            {
                release = new AppRelease
                {
                    Tag = sample.Tag,
                    CommitSha = "",
                    PageUrl = sample.PageUrl,
                    PublishedAt = sample.PublishedAt,
                    Assets = sample.Assets,
                };
            }
            var asset = row.GetProperty("asset").GetString() == "macos" ? AppUpdate.MacosAssetName : AppUpdate.WindowsAssetName;
            var decision = AppUpdate.Evaluate(
                release,
                asset,
                row.GetProperty("current_sha").GetString() ?? "",
                row.GetProperty("installed_sha").GetString() ?? "",
                row.GetProperty("installed_asset_id").GetInt64());
            Assert.Equal(row.GetProperty("available").GetBoolean(), decision.Available);
            Assert.Equal(row.GetProperty("up_to_date").GetBoolean(), decision.UpToDate);
        }
    }

    [Fact]
    public void PrefersNewAssetThenFallsBackToLegacy()
    {
        var release = new AppRelease
        {
            Tag = "latest",
            CommitSha = "518192b000000000000000000000000000000000",
            Assets =
            [
                new()
                {
                    Name = AppUpdate.LegacyWindowsAssetName,
                    Url = AppUpdate.AssetDownloadUrl(AppUpdate.LegacyWindowsAssetName),
                    Id = 7,
                },
            ],
        };
        var asset = AppUpdate.FindPreferredAsset(release, AppUpdate.WindowsAssetName);
        Assert.NotNull(asset);
        Assert.Equal(AppUpdate.LegacyWindowsAssetName, asset!.Name);
        var decision = AppUpdate.Evaluate(release, AppUpdate.WindowsAssetName, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        Assert.True(decision.Available);
        Assert.Equal(7, decision.Asset?.Id);
    }

    [Fact]
    public void DisplayVersionAndInformationalSha()
    {
        Assert.Equal("2.0.0", AppUpdate.DisplayVersion(""));
        Assert.Equal("2.0.0 (518192b)", AppUpdate.DisplayVersion("518192b000000000000000000000000000000000"));
        Assert.Equal("518192b", AppUpdate.ShaFromInformationalVersion("2.0.0+518192b"));
        Assert.Equal("", AppUpdate.ShaFromInformationalVersion("2.0.0"));
        Assert.True(AppUpdate.ShouldFallbackFromApi(403));
        Assert.True(AppUpdate.ShouldFallbackFromApi(429));
        Assert.False(AppUpdate.ShouldFallbackFromApi(400));
    }

    [Fact]
    public void RememberedInstallAfterHelperKeepsOldShaOnFailure()
    {
        Assert.Null(AppUpdate.RememberedInstallAfterHelper("518192b000000000000000000000000000000000", 99, false));
        var remembered = AppUpdate.RememberedInstallAfterHelper("518192B000000000000000000000000000000000", 99, true);
        Assert.NotNull(remembered);
        Assert.Equal("518192b000000000000000000000000000000000", remembered.Value.Sha);
        Assert.Equal(99, remembered.Value.AssetId);
    }

    [Fact]
    public void ShouldAutoCheckRespectsInterval()
    {
        var now = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        Assert.True(AppUpdate.ShouldAutoCheck("", now, false));
        Assert.True(AppUpdate.ShouldAutoCheck(AppUpdate.NowIso(now.AddHours(-13)), now, false));
        Assert.False(AppUpdate.ShouldAutoCheck(AppUpdate.NowIso(now.AddHours(-1)), now, false));
        Assert.True(AppUpdate.ShouldAutoCheck(AppUpdate.NowIso(now.AddHours(-1)), now, true));
    }

    [Fact]
    public void ExtractZipRejectsPathEscapeAndFindsExe()
    {
        var root = Path.Combine(Path.GetTempPath(), "ctt-update-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var zip = Path.Combine(root, "ok.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("CursorRemain.exe");
                using var w = new StreamWriter(entry.Open());
                w.Write("exe");
            }
            var dest = Path.Combine(root, "out");
            AppUpdate.ExtractZipSafe(zip, dest);
            Assert.Equal(Path.Combine(dest, "CursorRemain.exe"), AppUpdate.FindExtractedWindowsExe(dest));

            var legacyZip = Path.Combine(root, "legacy.zip");
            using (var archive = ZipFile.Open(legacyZip, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("CursorTokenTray.exe");
                using var w = new StreamWriter(entry.Open());
                w.Write("old");
            }
            var legacyDest = Path.Combine(root, "legacy-out");
            AppUpdate.ExtractZipSafe(legacyZip, legacyDest);
            Assert.Equal(Path.Combine(legacyDest, "CursorTokenTray.exe"), AppUpdate.FindExtractedWindowsExe(legacyDest));

            var evil = Path.Combine(root, "evil.zip");
            using (var archive = ZipFile.Open(evil, ZipArchiveMode.Create))
                archive.CreateEntry("../escape.exe");
            Assert.Throws<InvalidOperationException>(() => AppUpdate.ExtractZipSafe(evil, Path.Combine(root, "evil-out")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void ConfigRoundtripsAutoUpdateFields()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ctt-update-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var cfg = new AppConfig
            {
                AutoUpdateEnabled = false,
                UpdateLastCheckAt = "2026-09-14T03:20:00Z",
                UpdateLastError = "x",
                UpdateInstalledSha = "518192b",
                UpdateInstalledAssetId = 101,
            };
            ConfigStore.Save(cfg, dir);
            var loaded = ConfigStore.Load(dir);
            Assert.False(loaded.AutoUpdateEnabled);
            Assert.Equal("2026-09-14T03:20:00Z", loaded.UpdateLastCheckAt);
            Assert.Equal("x", loaded.UpdateLastError);
            Assert.Equal("518192b", loaded.UpdateInstalledSha);
            Assert.Equal(101, loaded.UpdateInstalledAssetId);

            var fresh = ConfigStore.Normalize(JsonDocument.Parse("{}").RootElement);
            Assert.True(fresh.AutoUpdateEnabled);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
