using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CursorTokenCore;

public sealed class AppReleaseAsset
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Url { get; init; } = "";
    public long Size { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class AppRelease
{
    public string Tag { get; init; } = "";
    public string Version { get; init; } = "";
    public string Body { get; init; } = "";
    public string CommitSha { get; init; } = "";
    public string PageUrl { get; init; } = "";
    public string PublishedAt { get; init; } = "";
    public List<AppReleaseAsset> Assets { get; init; } = [];
}

public sealed class UpdateDecision
{
    public bool UpToDate { get; init; }
    public bool Available { get; init; }
    public string Message { get; init; } = "";
    public AppRelease? Release { get; init; }
    public AppReleaseAsset? Asset { get; init; }
}

public static class AppUpdate
{
    public const string RepoOwner = "sydy";
    public const string RepoName = "CursorRemain";
    public const string LatestTag = "latest";
    public const string WindowsAssetName = "CursorRemain-windows.zip";
    public const string WindowsLightAssetName = "CursorRemain-windows-light.zip";
    public const string MacosAssetName = "CursorRemain-macos.zip";
    public const string LegacyWindowsAssetName = "CursorTokenTray-windows.zip";
    public const string LegacyMacosAssetName = "CursorTokenTray-macos.zip";
    public const string WindowsExeName = "CursorRemain.exe";
    public const string LegacyWindowsExeName = "CursorTokenTray.exe";
    public const string MacAppName = "CursorRemain.app";
    public const string LegacyMacAppName = "CursorTokenTray.app";
    public const string WindowsDesktopSharedFramework = "Microsoft.WindowsDesktop.App";
    public const long MaxZipBytes = 140L * 1024 * 1024;
    public const long MaxLightZipBytes = 40L * 1024 * 1024;
    public static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(12);
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    public static string ProductVersion
    {
        get
        {
            var version = VersionFromInformational(
                typeof(AppUpdate).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
            return version.Length > 0 ? version : "0.0.0";
        }
    }

    public static string OfficialLatestPageUrl =>
        $"https://github.com/{RepoOwner}/{RepoName}/releases/latest";

    public static string LatestReleasePageUrl =>
        $"https://github.com/{RepoOwner}/{RepoName}/releases/tag/{LatestTag}";

    public static string ApiOfficialLatestUrl =>
        $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    public static string ApiLatestReleaseUrl =>
        $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/tags/{LatestTag}";

    public static string ApiLatestRefUrl =>
        $"https://api.github.com/repos/{RepoOwner}/{RepoName}/git/refs/tags/{LatestTag}";

    public static string UserAgent => $"CursorRemain/{ProductVersion} (+https://github.com/{RepoOwner}/{RepoName})";

    public static string AssetDownloadUrl(string assetName, string? tag = null)
    {
        var releaseTag = string.IsNullOrWhiteSpace(tag) ? LatestTag : tag.Trim();
        return $"https://github.com/{RepoOwner}/{RepoName}/releases/download/{releaseTag}/{assetName}";
    }

    public static bool ShouldFallbackFromApi(int statusCode) =>
        statusCode is 401 or 403 or 404 or 429 or >= 500;

    public static string PlatformAssetName =>
        OperatingSystem.IsWindows() ? WindowsAssetName : MacosAssetName;

    public static bool IsWindowsLightAssetName(string? name) =>
        (name ?? "").Equals(WindowsLightAssetName, StringComparison.OrdinalIgnoreCase);

    public static string PreferredWindowsAssetName(bool preferLight) =>
        preferLight ? WindowsLightAssetName : WindowsAssetName;

    public static long MaxBytesForAsset(string? name) =>
        IsWindowsLightAssetName(name) ? MaxLightZipBytes : MaxZipBytes;

    public static bool RunsOnSharedFramework(string? assemblyPath = null)
    {
        var path = assemblyPath ?? typeof(object).Assembly.Location;
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/shared/Microsoft.NETCore.App/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"/shared/{WindowsDesktopSharedFramework}/", StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> DesktopRuntimeSearchRoots(
        IEnumerable<string>? searchRoots = null,
        string? dotnetRoot = null)
    {
        if (searchRoots is not null)
        {
            foreach (var root in searchRoots)
            {
                if (!string.IsNullOrWhiteSpace(root))
                    yield return root;
            }
            yield break;
        }

        foreach (var key in new[] { "DOTNET_ROOT_X64", "DOTNET_ROOT" })
        {
            var env = dotnetRoot ?? Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(env))
                yield return Path.Combine(env, "shared", WindowsDesktopSharedFramework);
            if (dotnetRoot is not null) break;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
            yield return Path.Combine(programFiles, "dotnet", "shared", WindowsDesktopSharedFramework);

        var programW6432 = Environment.GetEnvironmentVariable("ProgramW6432");
        if (!string.IsNullOrWhiteSpace(programW6432))
            yield return Path.Combine(programW6432, "dotnet", "shared", WindowsDesktopSharedFramework);
    }

    public static bool HasWindowsDesktopRuntime(
        IEnumerable<string>? searchRoots = null,
        string? dotnetRoot = null)
    {
        foreach (var root in DesktopRuntimeSearchRoots(searchRoots, dotnetRoot))
        {
            if (ContainsDesktopRuntime8(root)) return true;
        }
        return false;
    }

    public static bool ContainsDesktopRuntime8(string? sharedFrameworkDir)
    {
        if (string.IsNullOrWhiteSpace(sharedFrameworkDir) || !Directory.Exists(sharedFrameworkDir))
            return false;
        foreach (var dir in Directory.EnumerateDirectories(sharedFrameworkDir))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith("8.", StringComparison.Ordinal)
                && File.Exists(Path.Combine(dir, "System.Windows.Forms.dll")))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Light (framework-dependent) updates are safe when this process already
    /// runs on the shared Desktop Runtime, or the machine has .NET 8 Desktop.
    /// Self-contained installs without a system runtime keep the full zip.
    /// </summary>
    public static bool CanUseWindowsLightUpdate(
        bool? hasDesktopRuntime = null,
        bool? runsOnSharedFramework = null)
    {
        if (runsOnSharedFramework ?? RunsOnSharedFramework())
            return true;
        return hasDesktopRuntime ?? HasWindowsDesktopRuntime();
    }

    public static string NormalizeSha(string? raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        if (s.StartsWith("sha-", StringComparison.Ordinal)) s = s[4..];
        if (s.Length is < 7 or > 40) return "";
        foreach (var c in s)
        {
            var hex = c is (>= '0' and <= '9') or (>= 'a' and <= 'f');
            if (!hex) return "";
        }
        return s;
    }

    public static string ExtractSha(string? text)
    {
        var raw = text ?? "";
        var full = Regex.Match(raw, @"(?i)\b([0-9a-f]{40})\b");
        if (full.Success) return NormalizeSha(full.Groups[1].Value);
        var shortSha = Regex.Match(raw, @"(?i)(?:commit/|`)([0-9a-f]{7,40})(?:`|\b)");
        return shortSha.Success ? NormalizeSha(shortSha.Groups[1].Value) : "";
    }

    public static bool SameSha(string? left, string? right)
    {
        var a = NormalizeSha(left);
        var b = NormalizeSha(right);
        if (a.Length == 0 || b.Length == 0) return false;
        var min = Math.Min(a.Length, b.Length);
        if (min < 7) return a == b;
        return a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal);
    }

    public static string ShaFromInformationalVersion(string? informational)
    {
        var info = informational ?? "";
        var plus = info.LastIndexOf('+');
        return plus >= 0 ? NormalizeSha(info[(plus + 1)..]) : "";
    }

    public static string VersionFromInformational(string? informational)
    {
        var info = (informational ?? "").Trim();
        if (info.Length == 0) return "";
        var plus = info.IndexOf('+');
        if (plus >= 0) info = info[..plus];
        return NormalizeProductVersion(info);
    }

    public static string NormalizeProductVersion(string? raw)
    {
        var value = (raw ?? "").Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        var match = Regex.Match(value, @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?");
        return match.Success ? match.Value : "";
    }

    public static string ExtractProductVersion(string? text)
    {
        var raw = text ?? "";
        var tagged = Regex.Match(raw, @"(?i)\bv?(\d+\.\d+\.\d+)\b");
        if (tagged.Success) return tagged.Groups[1].Value;
        return NormalizeProductVersion(raw);
    }

    public static string ProductVersionFromRelease(string? tag, string? body = null)
    {
        var fromTag = NormalizeProductVersion(tag);
        if (fromTag.Length > 0) return fromTag;
        var raw = body ?? "";
        var labeled = Regex.Match(raw, @"(?i)(?:\*\*)?版本(?:\*\*)?\s*[:：]\s*`?v?(\d+\.\d+\.\d+)");
        if (labeled.Success) return labeled.Groups[1].Value;
        return ExtractProductVersion(raw);
    }

    public static int CompareProductVersions(string? left, string? right)
    {
        if (!TryParseProductVersion(left, out var a) || !TryParseProductVersion(right, out var b))
            return 0;
        var cmp = a[0].CompareTo(b[0]);
        if (cmp != 0) return cmp;
        cmp = a[1].CompareTo(b[1]);
        if (cmp != 0) return cmp;
        return a[2].CompareTo(b[2]);
    }

    public static bool TryParseProductVersion(string? raw, out int[] parts)
    {
        parts = [0, 0, 0];
        var version = NormalizeProductVersion(raw);
        if (version.Length == 0) return false;
        var core = version.Split('-', 2)[0];
        var bits = core.Split('.');
        if (bits.Length != 3) return false;
        for (var i = 0; i < 3; i++)
        {
            if (!int.TryParse(bits[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 0)
                return false;
            parts[i] = n;
        }
        return true;
    }

    public static string FormatReleaseLabel(string? version, string? sha)
    {
        var ver = NormalizeProductVersion(version);
        var shortSha = ShortSha(sha);
        if (ver.Length > 0 && shortSha.Length > 0 && shortSha != LatestTag)
            return $"{ver} ({shortSha})";
        if (ver.Length > 0) return ver;
        return shortSha.Length > 0 ? shortSha : LatestTag;
    }

    public static AppRelease? ChooseRelease(AppRelease? official, AppRelease? rolling, string? currentVersion = null)
    {
        if (official is null) return rolling;
        if (rolling is null) return official;
        var current = NormalizeProductVersion(currentVersion ?? ProductVersion);
        var officialVer = NormalizeProductVersion(official.Version);
        if (officialVer.Length == 0)
            officialVer = ProductVersionFromRelease(official.Tag, official.Body);
        if (current.Length > 0 && officialVer.Length > 0 && CompareProductVersions(officialVer, current) > 0)
            return official;
        return rolling;
    }

    public static string CurrentCommitSha()
    {
        foreach (var asm in new[] { Assembly.GetEntryAssembly(), Assembly.GetExecutingAssembly() })
        {
            var info = asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var sha = ShaFromInformationalVersion(info);
            if (sha.Length > 0) return sha;
        }
        return "";
    }

    public static string DisplayVersion(string? sha = null)
    {
        var value = NormalizeSha(sha ?? CurrentCommitSha());
        if (value.Length > 7) value = value[..7];
        return value.Length == 0 ? ProductVersion : $"{ProductVersion} ({value})";
    }

    public static bool IsAllowedDownloadUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        var host = uri.Host.Trim().ToLowerInvariant();
        return host == "github.com"
            || host.EndsWith(".github.com", StringComparison.Ordinal)
            || host == "githubusercontent.com"
            || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
    }

    public static bool LooksPackagedWindows(string? processPath)
    {
        var path = (processPath ?? "").Trim();
        if (path.Length == 0) return false;
        var normalized = path.Replace('/', '\\');
        var slash = normalized.LastIndexOf('\\');
        var name = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        if (!IsOurWindowsExe(name)) return false;
        var lower = normalized.ToLowerInvariant();
        if (lower.Contains("\\bin\\", StringComparison.Ordinal)) return false;
        if (lower.Contains("\\obj\\", StringComparison.Ordinal)) return false;
        if (lower.Contains("\\.build\\", StringComparison.Ordinal)) return false;
        return true;
    }

    public static bool LooksPackagedMacos(string? bundleOrExePath)
    {
        var path = (bundleOrExePath ?? "").Trim().Replace('\\', '/');
        if (path.Length == 0) return false;
        var lower = path.ToLowerInvariant();
        if (lower.Contains("/.build/") || lower.Contains("/deriveddata/")) return false;
        if (lower.EndsWith(".app", StringComparison.Ordinal) || lower.Contains(".app/contents/", StringComparison.Ordinal))
        {
            var app = AppBundlePath(path);
            return IsOurMacApp(Path.GetFileName(app));
        }
        return false;
    }

    public static string AppBundlePath(string path)
    {
        var value = path.Replace('\\', '/').TrimEnd('/');
        const string marker = ".app/";
        var idx = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return value[..(idx + 4)];
        return value;
    }

    public static bool CanSelfUpdate(string? processPath = null, string? macosBundlePath = null)
    {
        if (OperatingSystem.IsWindows())
            return LooksPackagedWindows(processPath ?? Environment.ProcessPath);
        if (OperatingSystem.IsMacOS())
            return LooksPackagedMacos(macosBundlePath ?? processPath ?? "");
        return false;
    }

    public static AppRelease ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseRelease(doc.RootElement);
    }

    public static AppRelease ParseRelease(JsonElement raw)
    {
        var tag = Str(raw, "tag_name");
        var page = Str(raw, "html_url");
        if (page.Length == 0)
        {
            page = tag.Length > 0 && tag != LatestTag
                ? $"https://github.com/{RepoOwner}/{RepoName}/releases/tag/{tag}"
                : LatestReleasePageUrl;
        }
        var body = Str(raw, "body");
        var sha = ExtractSha(body);
        if (sha.Length == 0) sha = NormalizeSha(Str(raw, "target_commitish"));
        var assets = new List<AppReleaseAsset>();
        if (raw.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var name = Str(item, "name");
                var url = Str(item, "browser_download_url");
                if (name.Length == 0 || url.Length == 0) continue;
                long id = 0;
                if (item.TryGetProperty("id", out var idEl))
                {
                    if (idEl.ValueKind == JsonValueKind.Number) idEl.TryGetInt64(out id);
                    else if (idEl.ValueKind == JsonValueKind.String) long.TryParse(idEl.GetString(), out id);
                }
                long size = 0;
                if (item.TryGetProperty("size", out var sizeEl) && sizeEl.ValueKind == JsonValueKind.Number)
                    sizeEl.TryGetInt64(out size);
                DateTimeOffset updated = default;
                var updatedRaw = Str(item, "updated_at");
                if (updatedRaw.Length > 0)
                    DateTimeOffset.TryParse(updatedRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out updated);
                assets.Add(new AppReleaseAsset
                {
                    Id = id,
                    Name = name,
                    Url = url,
                    Size = size,
                    UpdatedAt = updated,
                });
            }
        }
        return new AppRelease
        {
            Tag = tag.Length == 0 ? LatestTag : tag,
            Version = ProductVersionFromRelease(tag, body),
            Body = body,
            CommitSha = sha,
            PageUrl = page,
            PublishedAt = Str(raw, "published_at"),
            Assets = assets,
        };
    }

    public static List<AppReleaseAsset> KnownAssets(string? tag = null) =>
    [
        new() { Name = WindowsAssetName, Url = AssetDownloadUrl(WindowsAssetName, tag) },
        new() { Name = MacosAssetName, Url = AssetDownloadUrl(MacosAssetName, tag) },
    ];

    public static AppRelease ParseReleasePage(string html)
    {
        var sha = ExtractSha(html);
        if (sha.Length == 0)
        {
            var commit = Regex.Match(html ?? "", @"(?i)/commit/([0-9a-f]{7,40})");
            if (commit.Success) sha = NormalizeSha(commit.Groups[1].Value);
        }
        if (sha.Length == 0)
            throw new InvalidOperationException("发布页里找不到提交哈希");
        var version = ProductVersionFromRelease("", html);
        return new AppRelease
        {
            Tag = LatestTag,
            Version = version,
            Body = html ?? "",
            CommitSha = sha,
            PageUrl = LatestReleasePageUrl,
            Assets = KnownAssets(),
        };
    }

    public static string HttpStatusMessage(int statusCode) => statusCode switch
    {
        401 or 403 => "GitHub 接口拒绝访问（403）",
        404 => "找不到正式版或 Latest 发布",
        429 => "GitHub 请求过于频繁，请稍后重试",
        _ => $"GitHub {statusCode}",
    };

    public static string ParseTagRefSha(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("object", out var obj))
            return NormalizeSha(Str(obj, "sha"));
        return NormalizeSha(Str(doc.RootElement, "sha"));
    }

    public static AppReleaseAsset? FindAsset(AppRelease release, string assetName) =>
        release.Assets.FirstOrDefault(a => a.Name.Equals(assetName, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<string> AssetNameCandidates(string preferred)
    {
        if (preferred.Equals(WindowsLightAssetName, StringComparison.OrdinalIgnoreCase)
            || preferred.Equals(WindowsAssetName, StringComparison.OrdinalIgnoreCase)
            || preferred.Equals(LegacyWindowsAssetName, StringComparison.OrdinalIgnoreCase))
        {
            if (preferred.Equals(WindowsLightAssetName, StringComparison.OrdinalIgnoreCase))
                return [WindowsLightAssetName, WindowsAssetName, LegacyWindowsAssetName];
            return [WindowsAssetName, LegacyWindowsAssetName];
        }
        if (preferred.Equals(MacosAssetName, StringComparison.OrdinalIgnoreCase)
            || preferred.Equals(LegacyMacosAssetName, StringComparison.OrdinalIgnoreCase))
            return [MacosAssetName, LegacyMacosAssetName];
        return [preferred];
    }

    public static AppReleaseAsset? FindPreferredAsset(AppRelease release, string preferredName)
    {
        foreach (var name in AssetNameCandidates(preferredName))
        {
            var asset = FindAsset(release, name);
            if (asset is null) continue;
            if (!IsAllowedDownloadUrl(asset.Url)) continue;
            if (asset.Size > MaxBytesForAsset(name)) continue;
            return asset;
        }
        return null;
    }

    public static bool IsOurWindowsExe(string? fileName)
    {
        var name = (fileName ?? "").Trim();
        return name.Equals(WindowsExeName, StringComparison.OrdinalIgnoreCase)
            || name.Equals(LegacyWindowsExeName, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsOurMacApp(string? fileName)
    {
        var name = (fileName ?? "").Trim();
        return name.Equals(MacAppName, StringComparison.OrdinalIgnoreCase)
            || name.Equals(LegacyMacAppName, StringComparison.OrdinalIgnoreCase);
    }

    public static UpdateDecision Evaluate(
        AppRelease release,
        string assetName,
        string currentSha,
        string installedSha = "",
        long installedAssetId = 0,
        string? currentVersion = null)
    {
        var asset = FindPreferredAsset(release, assetName);
        if (asset is null)
            return new UpdateDecision { Message = "最新发布没有本平台安装包" };
        if (!IsAllowedDownloadUrl(asset.Url))
            return new UpdateDecision { Message = "更新地址无效" };
        if (asset.Size > MaxBytesForAsset(asset.Name))
            return new UpdateDecision { Message = "安装包过大，已取消更新" };

        var remoteVersion = NormalizeProductVersion(release.Version);
        if (remoteVersion.Length == 0)
            remoteVersion = ProductVersionFromRelease(release.Tag, release.Body);
        var localVersion = NormalizeProductVersion(currentVersion ?? ProductVersion);
        if (remoteVersion.Length > 0 && localVersion.Length > 0)
        {
            var cmp = CompareProductVersions(remoteVersion, localVersion);
            if (cmp < 0)
                return new UpdateDecision { UpToDate = true, Message = "已是最新版本", Release = release, Asset = asset };
            if (cmp > 0)
                return Available(release, asset);
        }

        var localSha = NormalizeSha(currentSha);
        if (localSha.Length == 0) localSha = NormalizeSha(installedSha);
        var remoteSha = NormalizeSha(release.CommitSha);
        if (remoteSha.Length > 0 && SameSha(localSha, remoteSha))
            return new UpdateDecision { UpToDate = true, Message = "已是最新版本", Release = release, Asset = asset };
        if (remoteSha.Length == 0 && installedAssetId > 0 && installedAssetId == asset.Id)
            return new UpdateDecision { UpToDate = true, Message = "已是最新版本", Release = release, Asset = asset };
        if (remoteSha.Length > 0 && SameSha(localSha, remoteSha) == false && localSha.Length > 0)
            return Available(release, asset);
        if (localSha.Length == 0)
            return Available(release, asset);
        if (remoteSha.Length > 0 && !SameSha(localSha, remoteSha))
            return Available(release, asset);
        return new UpdateDecision { UpToDate = true, Message = "已是最新版本", Release = release, Asset = asset };
    }

    public readonly record struct RememberedInstall(string Sha, long AssetId);

    /// <summary>
    /// Persist the installed SHA only after the replace helper actually started.
    /// A failed launch must keep the previous SHA so the same release can be retried.
    /// </summary>
    public static RememberedInstall? RememberedInstallAfterHelper(string sha, long assetId, bool helperStarted) =>
        helperStarted ? new RememberedInstall(NormalizeSha(sha), assetId) : null;

    static UpdateDecision Available(AppRelease release, AppReleaseAsset asset) =>
        new()
        {
            Available = true,
            Message = $"发现新版本 {FormatReleaseLabel(release.Version.Length > 0 ? release.Version : ProductVersionFromRelease(release.Tag, release.Body), release.CommitSha)}",
            Release = release,
            Asset = asset,
        };

    public static string ShortSha(string? sha)
    {
        var value = NormalizeSha(sha);
        if (value.Length == 0) return LatestTag;
        return value.Length > 7 ? value[..7] : value;
    }

    public static bool ShouldAutoCheck(string lastCheckAt, DateTimeOffset now, bool manual)
    {
        if (manual) return true;
        var last = ParseIso(lastCheckAt);
        if (last is null) return true;
        return now - last.Value >= AutoCheckInterval;
    }

    public static DateTimeOffset? ParseIso(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return null;
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            return dt;
        return null;
    }

    public static string NowIso(DateTimeOffset? now = null) =>
        (now ?? DateTimeOffset.UtcNow).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    public static void ExtractZipSafe(string zipPath, string destDir)
    {
        Directory.CreateDirectory(destDir);
        var destFull = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/')) continue;
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal))
                throw new InvalidOperationException("安装包路径不安全");
            var dest = Path.GetFullPath(Path.Combine(destDir, relative));
            if (!dest.StartsWith(destFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("安装包路径不安全");
            var parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(dest);
                continue;
            }
            entry.ExtractToFile(dest, true);
        }
    }

    public static string? FindExtractedWindowsExe(string destDir)
    {
        foreach (var wanted in new[] { WindowsExeName, LegacyWindowsExeName })
        {
            foreach (var file in Directory.EnumerateFiles(destDir, wanted, SearchOption.AllDirectories))
                return file;
        }
        return null;
    }

    public static string? FindExtractedMacApp(string destDir)
    {
        foreach (var dir in Directory.EnumerateDirectories(destDir, "*.app", SearchOption.AllDirectories))
        {
            if (IsOurMacApp(Path.GetFileName(dir)))
                return dir;
        }
        return null;
    }

    static string Str(JsonElement raw, string key) =>
        raw.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
