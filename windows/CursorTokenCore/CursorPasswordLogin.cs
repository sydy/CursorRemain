using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CursorTokenCore;

public readonly record struct LoginPollResult(string Token, bool Pending, string Message);

public static class CursorPasswordLogin
{
    public const string LoginUrl = "https://cursor.com/login";
    public const string LoginDeepUrlBase = "https://cursor.com/loginDeepControl";
    public const string PollUrl = "https://api2.cursor.sh/auth/poll";
    public const int TimeoutSeconds = 180;
    static readonly string[] CallbackKeys =
    [
        "workoscursorsessiontoken",
        "accesstoken",
        "access_token",
        "token",
        "cookie",
        "session_token",
    ];
    static readonly Regex EmailRe = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant);

    public static bool LooksLikeEmail(string? value) => CursorAuth.LooksLikeEmail(value);

    public static string SanitizeEmail(string? raw) =>
        RustCore.String("sanitize_email", new { input = raw ?? "" });

    public static string DefaultLabel(string email, string? existingLabel = null) =>
        RustCore.String("default_account_label", new { email, existing = existingLabel ?? "" });

    public static string? TokenFromCookies(IEnumerable<(string Name, string Value)> cookies) =>
        RustCore.String("token_from_cookies", new { cookies = cookies.Select(c => new { name = c.Name, value = c.Value }).ToList() });

    public static string AutofillScript(string email, string password) =>
        RustCore.String("autofill_script", new { email, password });

    public static string CodeChallenge(string? verifier) =>
        RustCore.String("code_challenge", new { verifier = verifier ?? "" });

    public static string LoginDeepUrl(string? uuid, string? verifier) =>
        RustCore.String("login_deep_url", new { uuid = uuid ?? "", verifier = verifier ?? "" });

    public static string TokenFromCallback(string? text) =>
        RustCore.String("token_from_callback", new { input = text ?? "" });

    public static string ValidProfileId(string? value)
    {
        var text = (value ?? "").Trim().ToLowerInvariant();
        return Regex.IsMatch(text, "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$") ? text : "";
    }

    public static string ProfileKey(string? email, string? accountId = null) =>
        RustCore.String("profile_key", new { email = email ?? "", account_id = accountId ?? "" });

    public static string CanonicalProfileKey(string? key)
    {
        var text = key ?? "";
        if (text.StartsWith("email:", StringComparison.Ordinal)) return ProfileKey(text[6..]);
        if (text.StartsWith("id:", StringComparison.Ordinal)) return ProfileKey("", text[3..]);
        return "";
    }

    public static Dictionary<string, string> CleanProfileIndex(IEnumerable<KeyValuePair<string, string>>? index)
    {
        var cleaned = new Dictionary<string, string>(StringComparer.Ordinal);
        if (index is null) return cleaned;
        foreach (var (key, value) in index)
        {
            var name = CanonicalProfileKey(key);
            var profileId = ValidProfileId(value);
            if (name.Length > 0 && profileId.Length > 0) cleaned[name] = profileId;
        }
        return cleaned;
    }

    public static (Dictionary<string, string> Index, string ProfileId) AssignProfile(
        IEnumerable<KeyValuePair<string, string>>? index, string? key, string? newId)
    {
        var result = RustCore.Call("assign_profile", new
        {
            index = (index ?? []).ToDictionary(kv => kv.Key, kv => kv.Value),
            key = key ?? "",
            new_id = newId ?? "",
        });
        var stored = result.GetProperty("index").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        return (stored, result.GetProperty("profile_id").GetString() ?? "");
    }

    public static Dictionary<string, string> BindProfile(
        IEnumerable<KeyValuePair<string, string>>? index, string? key, string? profileId)
    {
        var cleaned = CleanProfileIndex(index);
        var name = CanonicalProfileKey(key);
        var ident = ValidProfileId(profileId);
        if (name.Length == 0 || ident.Length == 0) return cleaned;
        cleaned[name] = ident;
        return cleaned;
    }

    public static string TokenFromPoll(JsonElement payload) =>
        RustCore.String("token_from_poll", payload);

    public static async Task<LoginPollResult> PollSession(string? uuid, string? verifier, CancellationToken ct = default)
    {
        var loginId = (uuid ?? "").Trim();
        var secret = (verifier ?? "").Trim();
        if (loginId.Length == 0 || secret.Length == 0)
            return new LoginPollResult("", false, "请先生成登录地址");
        using var http = new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        try
        {
            using var body = new StringContent(
                JsonSerializer.Serialize(new Dictionary<string, string> { ["uuid"] = loginId, ["verifier"] = secret }),
                Encoding.UTF8,
                "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, PollUrl) { Content = body };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("x-cursor-client-type", "ide");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 CursorRemain/1.0");
            using var response = await http.SendAsync(request, ct);
            if ((int)response.StatusCode == 404)
                return await PollSessionGet(http, loginId, secret, ct);
            if (response.IsSuccessStatusCode)
            {
                var token = TokenFromPoll(await ReadJson(response, ct));
                return token.Length == 0
                    ? new LoginPollResult("", false, "登录结果里没有 Cookie")
                    : new LoginPollResult(token, false, "");
            }
            return new LoginPollResult("", false, "获取失败");
        }
        catch (OperationCanceledException) { throw; }
        catch { return new LoginPollResult("", false, "获取失败"); }
    }

    static async Task<LoginPollResult> PollSessionGet(HttpClient http, string uuid, string verifier, CancellationToken ct)
    {
        var address = $"{PollUrl}?uuid={PercentEncode(uuid)}&verifier={PercentEncode(verifier)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("x-cursor-client-type", "ide");
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 CursorRemain/1.0");
        using var response = await http.SendAsync(request, ct);
        if ((int)response.StatusCode == 404)
            return new LoginPollResult("", true, "登录尚未完成，请在浏览器登录后再获取");
        if (!response.IsSuccessStatusCode)
            return new LoginPollResult("", false, "获取失败");
        var token = TokenFromPoll(await ReadJson(response, ct));
        return token.Length == 0
            ? new LoginPollResult("", false, "登录结果里没有 Cookie")
            : new LoginPollResult(token, false, "");
    }

    static async Task<JsonElement> ReadJson(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text)) return default;
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch { return default; }
    }

    static string ReadString(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value)) return "";
        return value.ValueKind == JsonValueKind.String ? (value.GetString() ?? "").Trim() : "";
    }

    static string PercentEncode(string value)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value ?? ""))
        {
            var ch = (char)b;
            if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || "-._~".Contains(ch))
                sb.Append(ch);
            else
                sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    static bool LooksLikeSessionToken(string value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0 || text.Any(char.IsWhiteSpace)) return false;
        var lower = text.ToLowerInvariant();
        if (lower.Contains("workoscursorsessiontoken=") || lower.Contains("%3a%3a") || text.Contains("::"))
            return true;
        var parts = text.Split('.');
        return parts.Length == 3 && parts.All(part => part.Length > 0);
    }

    static Dictionary<string, string> QueryMap(string query)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in (query ?? "").Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var rawKey = eq >= 0 ? part[..eq] : part;
            var rawValue = eq >= 0 ? part[(eq + 1)..] : "";
            var name = PercentDecode(rawKey).Trim().ToLowerInvariant();
            if (name.Length == 0 || found.ContainsKey(name)) continue;
            found[name] = PercentDecode(rawValue).Trim();
        }
        return found;
    }

    static string PercentDecode(string value)
    {
        try { return Uri.UnescapeDataString(value ?? ""); }
        catch { return value ?? ""; }
    }

    static string NormalizedToken(string value)
    {
        var text = (value ?? "").Trim();
        if (text.Length == 0) return "";
        try { return Token.Normalize(text); }
        catch { return text; }
    }
}
