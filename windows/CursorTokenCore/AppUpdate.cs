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
    public const string RepoName = "CursorTokenTray";
    public const string LatestTag = "latest";
    public const string WindowsAssetName = "CursorTokenTray-windows.zip";
    public const string MacosAssetName = "CursorTokenTray-macos.zip";
    public const string ProductVersion = "2.0.0";
    public const long MaxZipBytes = 80L * 1024 * 1024;
    public static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(12);
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    public static string LatestReleasePageUrl =>
        $"https://github.com/{RepoOwner}/{RepoName}/releases/tag/{LatestTag}";

    public static string ApiLatestReleaseUrl =>
        $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/tags/{LatestTag}";

    public static string ApiLatestRefUrl =>
        $"https://api.github.com/repos/{RepoOwner}/{RepoName}/git/refs/tags/{LatestTag}";

    public static string UserAgent => $"CursorTokenTray/{ProductVersion}";

    public static string PlatformAssetName =>
        OperatingSystem.IsWindows() ? WindowsAssetName : MacosAssetName;

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
        if (!name.Equals("CursorTokenTray.exe", StringComparison.OrdinalIgnoreCase)) return false;
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
            return app.EndsWith("/CursorTokenTray.app", StringComparison.OrdinalIgnoreCase);
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
        if (page.Length == 0) page = LatestReleasePageUrl;
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
            CommitSha = sha,
            PageUrl = page,
            PublishedAt = Str(raw, "published_at"),
            Assets = assets,
        };
    }

    public static string ParseTagRefSha(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("object", out var obj))
            return NormalizeSha(Str(obj, "sha"));
        return NormalizeSha(Str(doc.RootElement, "sha"));
    }

    public static AppReleaseAsset? FindAsset(AppRelease release, string assetName) =>
        release.Assets.FirstOrDefault(a => a.Name.Equals(assetName, StringComparison.OrdinalIgnoreCase));

    public static UpdateDecision Evaluate(
        AppRelease release,
        string assetName,
        string currentSha,
        string installedSha = "",
        long installedAssetId = 0)
    {
        var asset = FindAsset(release, assetName);
        if (asset is null)
            return new UpdateDecision { Message = "最新发布没有本平台安装包" };
        if (!IsAllowedDownloadUrl(asset.Url))
            return new UpdateDecision { Message = "更新地址无效" };
        if (asset.Size > MaxZipBytes)
            return new UpdateDecision { Message = "安装包过大，已取消更新" };

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

    static UpdateDecision Available(AppRelease release, AppReleaseAsset asset) =>
        new()
        {
            Available = true,
            Message = $"发现新版本 {ShortSha(release.CommitSha)}",
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
        foreach (var file in Directory.EnumerateFiles(destDir, "CursorTokenTray.exe", SearchOption.AllDirectories))
            return file;
        return null;
    }

    public static string? FindExtractedMacApp(string destDir)
    {
        foreach (var dir in Directory.EnumerateDirectories(destDir, "*.app", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(dir).Equals("CursorTokenTray.app", StringComparison.OrdinalIgnoreCase))
                return dir;
        }
        return null;
    }

    static string Str(JsonElement raw, string key) =>
        raw.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
