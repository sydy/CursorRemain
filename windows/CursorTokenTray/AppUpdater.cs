using System.Diagnostics;
using System.Net.Http.Headers;
using CursorTokenCore;

namespace CursorTokenTray;

static class AppUpdater
{
    static readonly HttpClient Http = CreateClient();
    static bool _busy;

    static HttpClient CreateClient()
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, UseCookies = false })
        {
            Timeout = TimeSpan.FromMinutes(3),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(AppUpdate.UserAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    public static bool Busy => _busy;

    public static bool CanSelfUpdate =>
        AppUpdate.LooksPackagedWindows(Environment.ProcessPath ?? Application.ExecutablePath);

    public static async Task<string> RunAsync(
        AppConfig cfg,
        bool manual,
        Func<string, bool>? confirmApply,
        Action? restart,
        CancellationToken ct = default)
    {
        if (_busy) return "正在检查更新…";
        if (!manual && !cfg.AutoUpdateEnabled) return "";
        if (!manual && !CanSelfUpdate) return "";
        if (!AppUpdate.ShouldAutoCheck(cfg.UpdateLastCheckAt, DateTimeOffset.UtcNow, manual))
            return string.IsNullOrWhiteSpace(cfg.UpdateLastError) ? "已是最新版本" : cfg.UpdateLastError;

        _busy = true;
        try
        {
            var release = await FetchLatest(ct);
            var decision = AppUpdate.Evaluate(
                release,
                AppUpdate.WindowsAssetName,
                AppUpdate.CurrentCommitSha(),
                cfg.UpdateInstalledSha,
                cfg.UpdateInstalledAssetId);
            RememberCheck(cfg, decision.UpToDate ? "" : (decision.Available ? "" : decision.Message));
            if (decision.UpToDate) return decision.Message;
            if (!decision.Available || decision.Asset is null)
                return string.IsNullOrWhiteSpace(decision.Message) ? "检查更新失败" : decision.Message;

            if (!CanSelfUpdate)
            {
                if (manual) OpenDownloadPage(decision.Release?.PageUrl);
                return decision.Message + "。请从下载页安装";
            }

            if (manual && confirmApply is not null && !confirmApply(decision.Message))
                return "已取消更新";

            var applied = await DownloadAndStage(decision.Asset, ct);
            if (!LaunchHelper(applied))
            {
                var fail = "已下载更新，但无法启动安装脚本";
                RememberCheck(cfg, fail);
                return fail;
            }
            if (AppUpdate.RememberedInstallAfterHelper(release.CommitSha, decision.Asset.Id, true) is { } remembered)
                RememberInstalled(cfg, remembered.Sha, remembered.AssetId);
            restart?.Invoke();
            return decision.Message + "，即将重启";
        }
        catch (OperationCanceledException)
        {
            return "已取消更新";
        }
        catch (Exception ex)
        {
            var msg = "检查更新失败: " + ex.Message;
            RememberCheck(cfg, msg);
            if (manual && msg.Contains("无法检查更新", StringComparison.Ordinal))
                OpenDownloadPage();
            return msg;
        }
        finally
        {
            _busy = false;
        }
    }

    public static void OpenDownloadPage(string? url = null)
    {
        var target = string.IsNullOrWhiteSpace(url) ? AppUpdate.LatestReleasePageUrl : url;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { CrashLog.Write(ex); }
    }

    static async Task<AppRelease> FetchLatest(CancellationToken ct)
    {
        Exception? apiError = null;
        try
        {
            using var apiCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            apiCts.CancelAfter(TimeSpan.FromSeconds(10));
            using var req = new HttpRequestMessage(HttpMethod.Get, AppUpdate.ApiLatestReleaseUrl);
            req.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            using var resp = await Http.SendAsync(req, apiCts.Token);
            var body = await resp.Content.ReadAsStringAsync(apiCts.Token);
            if (resp.IsSuccessStatusCode)
            {
                var release = AppUpdate.ParseRelease(body);
                if (release.CommitSha.Length == 0)
                {
                    try
                    {
                        using var refReq = new HttpRequestMessage(HttpMethod.Get, AppUpdate.ApiLatestRefUrl);
                        refReq.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
                        using var refResp = await Http.SendAsync(refReq, apiCts.Token);
                        if (refResp.IsSuccessStatusCode)
                        {
                            var sha = AppUpdate.ParseTagRefSha(await refResp.Content.ReadAsStringAsync(apiCts.Token));
                            if (sha.Length > 0)
                                release = new AppRelease
                                {
                                    Tag = release.Tag,
                                    CommitSha = sha,
                                    PageUrl = release.PageUrl,
                                    PublishedAt = release.PublishedAt,
                                    Assets = release.Assets,
                                };
                        }
                    }
                    catch (Exception ex) { CrashLog.Write(ex); }
                }
                if (release.Assets.Count == 0)
                    release = new AppRelease
                    {
                        Tag = release.Tag,
                        CommitSha = release.CommitSha,
                        PageUrl = release.PageUrl,
                        PublishedAt = release.PublishedAt,
                        Assets = AppUpdate.KnownAssets(),
                    };
                return release;
            }
            if (!AppUpdate.ShouldFallbackFromApi((int)resp.StatusCode))
                throw new InvalidOperationException(AppUpdate.HttpStatusMessage((int)resp.StatusCode));
            apiError = new InvalidOperationException(AppUpdate.HttpStatusMessage((int)resp.StatusCode));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            apiError = ex;
        }

        try
        {
            using var pageCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            pageCts.CancelAfter(TimeSpan.FromSeconds(20));
            using var req = new HttpRequestMessage(HttpMethod.Get, AppUpdate.LatestReleasePageUrl);
            req.Headers.Accept.Clear();
            req.Headers.Accept.ParseAdd("text/html");
            using var resp = await Http.SendAsync(req, pageCts.Token);
            var html = await resp.Content.ReadAsStringAsync(pageCts.Token);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(AppUpdate.HttpStatusMessage((int)resp.StatusCode));
            return AppUpdate.ParseReleasePage(html);
        }
        catch (Exception pageError) when (apiError is not null)
        {
            throw new InvalidOperationException(
                "无法检查更新（" + ShortError(apiError) + "）。也可打开下载页手动安装",
                pageError);
        }
    }

    static string ShortError(Exception ex) =>
        ex is OperationCanceledException or TaskCanceledException ? "连接超时" : ex.Message;

    static async Task<string> DownloadAndStage(AppReleaseAsset asset, CancellationToken ct)
    {
        if (!AppUpdate.IsAllowedDownloadUrl(asset.Url))
            throw new InvalidOperationException("更新地址无效");
        var root = Path.Combine(Path.GetTempPath(), "CursorTokenTray-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var zip = Path.Combine(root, AppUpdate.WindowsAssetName);
        using (var req = new HttpRequestMessage(HttpMethod.Get, asset.Url))
        {
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            if (resp.RequestMessage?.RequestUri is { } final && !AppUpdate.IsAllowedDownloadUrl(final.ToString()))
                throw new InvalidOperationException("更新地址无效");
            var length = resp.Content.Headers.ContentLength;
            if (length > AppUpdate.MaxZipBytes)
                throw new InvalidOperationException("安装包过大，已取消更新");
            await using var input = await resp.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(zip);
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                var n = await input.ReadAsync(buffer, ct);
                if (n == 0) break;
                total += n;
                if (total > AppUpdate.MaxZipBytes)
                    throw new InvalidOperationException("安装包过大，已取消更新");
                await output.WriteAsync(buffer.AsMemory(0, n), ct);
            }
        }
        var extract = Path.Combine(root, "extract");
        AppUpdate.ExtractZipSafe(zip, extract);
        return AppUpdate.FindExtractedWindowsExe(extract)
            ?? throw new InvalidOperationException("安装包里没有 CursorTokenTray.exe");
    }

    static bool LaunchHelper(string newExe)
    {
        var dest = Environment.ProcessPath ?? Application.ExecutablePath;
        if (string.IsNullOrWhiteSpace(dest)) return false;
        var bat = Path.Combine(Path.GetTempPath(), "CursorTokenTray-apply-" + Guid.NewGuid().ToString("N") + ".cmd");
        var script =
            "@echo off\r\n" +
            "setlocal EnableExtensions\r\n" +
            "set \"PID=%~1\"\r\n" +
            "set \"SRC=%~2\"\r\n" +
            "set \"DST=%~3\"\r\n" +
            "set /a _i=0\r\n" +
            ":wait\r\n" +
            "if %_i% GEQ 80 goto copy\r\n" +
            "ping -n 2 127.0.0.1 >nul\r\n" +
            "tasklist /FI \"PID eq %PID%\" 2>nul | findstr /I /C:\" %PID% \" >nul\r\n" +
            "if not errorlevel 1 (\r\n" +
            "  set /a _i+=1\r\n" +
            "  goto wait\r\n" +
            ")\r\n" +
            ":copy\r\n" +
            "copy /Y \"%SRC%\" \"%DST%\" >nul\r\n" +
            "if errorlevel 1 (\r\n" +
            "  ping -n 2 127.0.0.1 >nul\r\n" +
            "  copy /Y \"%SRC%\" \"%DST%\" >nul\r\n" +
            ")\r\n" +
            "start \"\" \"%DST%\"\r\n" +
            "del \"%~f0\"\r\n";
        File.WriteAllText(bat, script);
        var args = $"/c \"\"{bat}\" {Environment.ProcessId} \"{newExe}\" \"{dest}\"\"";
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        return Process.Start(psi) is not null;
    }

    static void RememberCheck(AppConfig cfg, string error)
    {
        try
        {
            ConfigStore.Update(live =>
            {
                live.UpdateLastCheckAt = AppUpdate.NowIso();
                live.UpdateLastError = error;
                cfg.UpdateLastCheckAt = live.UpdateLastCheckAt;
                cfg.UpdateLastError = live.UpdateLastError;
                cfg.AutoUpdateEnabled = live.AutoUpdateEnabled;
                cfg.UpdateInstalledSha = live.UpdateInstalledSha;
                cfg.UpdateInstalledAssetId = live.UpdateInstalledAssetId;
            });
        }
        catch (Exception ex) { CrashLog.Write(ex); }
    }

    static void RememberInstalled(AppConfig cfg, string sha, long assetId)
    {
        try
        {
            ConfigStore.Update(live =>
            {
                live.UpdateLastCheckAt = AppUpdate.NowIso();
                live.UpdateLastError = "";
                live.UpdateInstalledSha = AppUpdate.NormalizeSha(sha);
                live.UpdateInstalledAssetId = assetId;
                cfg.UpdateLastCheckAt = live.UpdateLastCheckAt;
                cfg.UpdateLastError = "";
                cfg.UpdateInstalledSha = live.UpdateInstalledSha;
                cfg.UpdateInstalledAssetId = live.UpdateInstalledAssetId;
            });
        }
        catch (Exception ex) { CrashLog.Write(ex); }
    }
}
