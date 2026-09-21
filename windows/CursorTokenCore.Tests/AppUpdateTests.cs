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
            if (exp.TryGetProperty("version", out var versionEl))
                Assert.Equal(versionEl.GetString(), parsed.Version);
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
            if (row.TryGetProperty("tag", out var tagEl))
            {
                release = new AppRelease
                {
                    Tag = tagEl.GetString() ?? release.Tag,
                    Version = row.TryGetProperty("version", out var verEl) ? verEl.GetString() ?? "" : release.Version,
                    Body = release.Body,
                    CommitSha = release.CommitSha,
                    PageUrl = release.PageUrl,
                    PublishedAt = release.PublishedAt,
                    Assets = release.Assets,
                };
            }
            var asset = row.GetProperty("asset").GetString() == "macos" ? AppUpdate.MacosAssetName : AppUpdate.WindowsAssetName;
            var currentVersion = row.TryGetProperty("current_version", out var curVer)
                ? curVer.GetString()
                : AppUpdate.ProductVersion;
            var decision = AppUpdate.Evaluate(
                release,
                asset,
                row.GetProperty("current_sha").GetString() ?? "",
                row.GetProperty("installed_sha").GetString() ?? "",
                row.GetProperty("installed_asset_id").GetInt64(),
                currentVersion);
            Assert.Equal(row.GetProperty("available").GetBoolean(), decision.Available);
            Assert.Equal(row.GetProperty("up_to_date").GetBoolean(), decision.UpToDate);
        }

        foreach (var row in root.GetProperty("extract_version").EnumerateArray())
            Assert.Equal(row.GetProperty("version").GetString(), AppUpdate.ProductVersionFromRelease(row.GetProperty("tag").GetString(), row.GetProperty("body").GetString()));

        foreach (var row in root.GetProperty("compare_versions").EnumerateArray())
            Assert.Equal(row.GetProperty("cmp").GetInt32(), AppUpdate.CompareProductVersions(row.GetProperty("left").GetString(), row.GetProperty("right").GetString()));

        foreach (var row in root.GetProperty("choose_release").EnumerateArray())
        {
            AppRelease? official = null;
            var officialTag = row.GetProperty("official_tag").GetString() ?? "";
            if (officialTag.Length > 0)
            {
                official = new AppRelease
                {
                    Tag = officialTag,
                    Version = row.GetProperty("official_version").GetString() ?? "",
                    Assets = sample.Assets,
                };
            }
            var rolling = new AppRelease { Tag = row.GetProperty("rolling_tag").GetString() ?? "latest", Assets = sample.Assets };
            var chosen = AppUpdate.ChooseRelease(official, rolling, row.GetProperty("current_version").GetString());
            Assert.Equal(row.GetProperty("choice").GetString(), chosen?.Tag == official?.Tag && official is not null ? "official" : "rolling");
        }
    }

    [Fact]
    public void PrefersLightWindowsAssetThenFallsBackToFull()
    {
        var light = new AppReleaseAsset
        {
            Name = AppUpdate.WindowsLightAssetName,
            Url = AppUpdate.AssetDownloadUrl(AppUpdate.WindowsLightAssetName),
            Id = 9,
            Size = 3_000_000,
        };
        var full = new AppReleaseAsset
        {
            Name = AppUpdate.WindowsAssetName,
            Url = AppUpdate.AssetDownloadUrl(AppUpdate.WindowsAssetName),
            Id = 8,
            Size = 60_000_000,
        };
        var release = new AppRelease
        {
            Tag = "v2.1.0",
            Version = "2.1.0",
            CommitSha = "518192b000000000000000000000000000000000",
            Assets = [light, full],
        };
        Assert.Equal(
            new[] { AppUpdate.WindowsLightAssetName, AppUpdate.WindowsAssetName },
            AppUpdate.AssetNameCandidates(AppUpdate.WindowsLightAssetName));
        Assert.Equal(
            new[] { AppUpdate.WindowsAssetName },
            AppUpdate.AssetNameCandidates(AppUpdate.WindowsAssetName));
        Assert.Equal(AppUpdate.WindowsLightAssetName, AppUpdate.PreferredWindowsAssetName(true));
        Assert.Equal(AppUpdate.WindowsAssetName, AppUpdate.PreferredWindowsAssetName(false));

        var lightPick = AppUpdate.FindPreferredAsset(release, AppUpdate.WindowsLightAssetName);
        Assert.Equal(9, lightPick?.Id);
        var fullPick = AppUpdate.FindPreferredAsset(release, AppUpdate.WindowsAssetName);
        Assert.Equal(8, fullPick?.Id);

        var missingLight = new AppRelease
        {
            Tag = release.Tag,
            Version = release.Version,
            CommitSha = release.CommitSha,
            Assets = [full],
        };
        Assert.Equal(8, AppUpdate.FindPreferredAsset(missingLight, AppUpdate.WindowsLightAssetName)?.Id);

        var oversizedLight = new AppRelease
        {
            Tag = release.Tag,
            Version = release.Version,
            CommitSha = release.CommitSha,
            Assets =
            [
                new()
                {
                    Name = AppUpdate.WindowsLightAssetName,
                    Url = AppUpdate.AssetDownloadUrl(AppUpdate.WindowsLightAssetName),
                    Id = 11,
                    Size = AppUpdate.MaxLightZipBytes + 1,
                },
                full,
            ],
        };
        Assert.Equal(8, AppUpdate.FindPreferredAsset(oversizedLight, AppUpdate.WindowsLightAssetName)?.Id);

        var decision = AppUpdate.Evaluate(
            release,
            AppUpdate.WindowsLightAssetName,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            currentVersion: "2.0.0");
        Assert.True(decision.Available);
        Assert.Equal(9, decision.Asset?.Id);
    }

    [Fact]
    public void DetectsDesktopRuntimeAndSharedFramework()
    {
        Assert.True(AppUpdate.RunsOnSharedFramework(
            @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.20\System.Private.CoreLib.dll"));
        Assert.True(AppUpdate.RunsOnSharedFramework(
            "/usr/share/dotnet/shared/Microsoft.WindowsDesktop.App/8.0.20/System.Windows.Forms.dll"));
        Assert.False(AppUpdate.RunsOnSharedFramework(""));
        Assert.False(AppUpdate.RunsOnSharedFramework(@"C:\Users\me\AppData\Local\Temp\.net\CursorRemain\coreclr.dll"));

        var root = Path.Combine(Path.GetTempPath(), "ctt-fx-" + Guid.NewGuid().ToString("N"));
        var runtime = Path.Combine(root, "8.0.20");
        Directory.CreateDirectory(runtime);
        try
        {
            File.WriteAllText(Path.Combine(runtime, "System.Windows.Forms.dll"), "fx");
            Assert.True(AppUpdate.ContainsDesktopRuntime8(root));
            Assert.True(AppUpdate.HasWindowsDesktopRuntime([root]));
            Assert.False(AppUpdate.HasWindowsDesktopRuntime([Path.Combine(root, "missing")]));
            Assert.True(AppUpdate.CanUseWindowsLightUpdate(hasDesktopRuntime: true, runsOnSharedFramework: false));
            Assert.False(AppUpdate.CanUseWindowsLightUpdate(hasDesktopRuntime: false, runsOnSharedFramework: false));
            Assert.True(AppUpdate.CanUseWindowsLightUpdate(hasDesktopRuntime: false, runsOnSharedFramework: true));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void IgnoresLegacyZipWhenCurrentAssetMissing()
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
        Assert.Null(AppUpdate.FindPreferredAsset(release, AppUpdate.WindowsAssetName));
        var decision = AppUpdate.Evaluate(release, AppUpdate.WindowsAssetName, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        Assert.False(decision.Available);
        Assert.Equal("最新发布没有本平台安装包", decision.Message);
    }

    [Fact]
    public void DisplayVersionAndInformationalSha()
    {
        var version = AppUpdate.ProductVersion;
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Equal(version, AppUpdate.DisplayVersion(""));
        Assert.Equal($"{version} (518192b)", AppUpdate.DisplayVersion("518192b000000000000000000000000000000000"));
        Assert.Equal("518192b", AppUpdate.ShaFromInformationalVersion(version + "+518192b"));
        Assert.Equal("", AppUpdate.ShaFromInformationalVersion(version));
        Assert.Equal(version, AppUpdate.NormalizeProductVersion("v" + version));
        Assert.True(AppUpdate.CompareProductVersions(version, version) == 0);
        Assert.True(AppUpdate.ShouldFallbackFromApi(403));
        Assert.True(AppUpdate.ShouldFallbackFromApi(429));
        Assert.False(AppUpdate.ShouldFallbackFromApi(400));

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "VERSION");
            if (File.Exists(candidate))
            {
                Assert.Equal(File.ReadAllText(candidate).Trim(), version);
                return;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("VERSION");
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
    public void PendingInstallConfirmsOnlyAfterNewBinaryMatches()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ctt-pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(AppUpdate.ReadPendingInstall(dir));
            AppUpdate.WritePendingInstall(dir, "0ad980084152a814bdaefeb3bb162184db07d64a", 7);
            var pending = AppUpdate.ReadPendingInstall(dir);
            Assert.NotNull(pending);
            Assert.Equal("0ad980084152a814bdaefeb3bb162184db07d64a", pending.Value.Sha);
            Assert.Equal(7, pending.Value.AssetId);
            Assert.Null(AppUpdate.ConfirmPendingInstall("eb9a0a2a0512609588b3458d87443971eed07ec3", pending));
            Assert.True(AppUpdate.PendingInstallFailed("eb9a0a2a0512609588b3458d87443971eed07ec3", pending));
            Assert.False(AppUpdate.PendingInstallFailed("", pending));
            var confirmed = AppUpdate.ConfirmPendingInstall("0ad9800", pending);
            Assert.NotNull(confirmed);
            Assert.Equal(pending.Value.Sha, confirmed.Value.Sha);
            AppUpdate.ClearPendingInstall(dir);
            Assert.Null(AppUpdate.ReadPendingInstall(dir));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
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
