using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CursorTokenCore;

public sealed class DoubaoQuotaResult
{
    public UsageSnapshot? Snapshot { get; }
    public string? Error { get; }
    public bool AuthError { get; }

    public DoubaoQuotaResult(UsageSnapshot? snapshot, string? error, bool authError)
    {
        Snapshot = snapshot;
        Error = error;
        AuthError = authError;
    }
}

/// <summary>
/// Doubao quota client (option A: plain HTTP). The endpoint has no public docs:
/// HTTP status is always 200, success is decided by the body's <c>code</c>.
/// </summary>
public sealed class DoubaoClient
{
    public const string QuotaUrl = "https://www.doubao.com/alice/commerce/sale/subscription/quota/summary/";
    /// <summary>Chat page. Doubao has no separate subscription page: the SSR payload here also carries the account name.</summary>
    public const string SubscriptionUrl = "https://www.doubao.com/chat/";
    const string BrowserUserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    public static readonly string Body = "{\"product_line\":\"membership\"}";

    readonly HttpClient _http;

    public DoubaoClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<UsageSnapshot> FetchQuota(string rawCookie, double timeout = 20, CancellationToken ct = default)
    {
        var cookie = DoubaoCredentials.Normalize(rawCookie);
        if (cookie.Length == 0) throw new CursorApiException("未配置豆包 Cookie", 401);
        var payload = await RequestQuota(cookie, timeout, ct);
        var result = UsageParser.ParseDoubaoQuota(payload);
        if (result.Snapshot is not null) return result.Snapshot;
        throw new CursorApiException(result.Error ?? "查询失败：接口返回格式异常", result.AuthError ? 401 : null);
    }

    /// <summary>取账号真实用户名：`/chat` 页面的 SSR 数据里带昵称，取不到返回空串。</summary>
    public async Task<string> FetchAccountName(string rawCookie, double timeout = 20, CancellationToken ct = default)
    {
        var cookie = DoubaoCredentials.Normalize(rawCookie);
        if (cookie.Length == 0) throw new CursorApiException("未配置豆包 Cookie", 401);
        using var req = new HttpRequestMessage(HttpMethod.Get, SubscriptionUrl);
        req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
        req.Headers.TryAddWithoutValidation("Origin", "https://www.doubao.com");
        req.Headers.TryAddWithoutValidation("Referer", "https://www.doubao.com/chat/");
        req.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
        req.Headers.TryAddWithoutValidation("Cookie", cookie);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeout));
        using var resp = await _http.SendAsync(req, cts.Token);
        var status = (int)resp.StatusCode;
        if (status is 401 or 403) throw new CursorApiException(DoubaoCredentials.AuthMessage, 401);
        if (status >= 400) throw new CursorApiException($"HTTP {status}", status);
        var html = await resp.Content.ReadAsStringAsync(cts.Token);
        return html.Length == 0 ? "" : UsageParser.DoubaoAccountName(html);
    }

    /// <summary>Login probe only: any code other than 710012001 counts as logged in.</summary>
    public static async Task<bool> CookieIsLoggedIn(string cookie, double timeout = 12, CancellationToken ct = default)
    {
        var client = new DoubaoClient();
        try
        {
            var payload = await client.RequestQuota(DoubaoCredentials.Normalize(cookie), timeout, ct);
            return !UsageParser.ParseDoubaoQuota(payload).AuthError;
        }
        catch
        {
            return false;
        }
    }

    internal async Task<JsonBag> RequestQuota(string cookie, double timeout, CancellationToken ct)
    {
        CursorApiException? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, QuotaUrl);
                req.Content = new StringContent(Body, Encoding.UTF8, "application/json");
                req.Headers.TryAddWithoutValidation("Accept", "application/json");
                req.Headers.TryAddWithoutValidation("Origin", "https://www.doubao.com");
                req.Headers.TryAddWithoutValidation("Referer", "https://www.doubao.com/chat/");
                req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 CursorRemain/1.0");
                req.Headers.TryAddWithoutValidation("Cookie", cookie);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(timeout));
                using var resp = await _http.SendAsync(req, cts.Token);
                var status = (int)resp.StatusCode;
                var text = await resp.Content.ReadAsStringAsync(cts.Token);
                if (status is 429 or >= 500)
                {
                    last = new CursorApiException($"HTTP {status}", status);
                    await Task.Delay(250 * (1 << attempt), ct);
                    continue;
                }
                if (status >= 400)
                    throw new CursorApiException($"HTTP {status}: {SafePrefix(text, 200)}", status);
                if (string.IsNullOrWhiteSpace(text)) return JsonBag.Null;
                try { return JsonBag.Parse(text); }
                catch { throw new CursorApiException("接口返回非 JSON"); }
            }
            catch (CursorApiException err)
            {
                if (err.Message.StartsWith("HTTP 4")) throw;
                last = err;
                if (attempt == 2) throw;
            }
            catch (Exception err) when (err is HttpRequestException or TaskCanceledException)
            {
                last = new CursorApiException(err is TaskCanceledException ? "请求超时" : "网络错误");
                if (attempt == 2) throw last;
                await Task.Delay(250 * (1 << attempt), ct);
            }
        }
        throw last ?? new CursorApiException("网络错误");
    }

    static string SafePrefix(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var safe = new StringBuilder();
        foreach (var ch in text)
        {
            if (safe.Length >= max) break;
            safe.Append(ch < 128 && !char.IsControl(ch) ? ch : '?');
        }
        return safe.ToString();
    }
}
