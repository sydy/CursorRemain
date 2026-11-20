using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CursorTokenCore;

/// <summary>
/// Doubao credential rules. The Cookie header is a full login state: it is only
/// stored encrypted on this machine and must never reach a log.
/// </summary>
public static class DoubaoCredentials
{
    public const string AuthMessage = "豆包登录已失效，请重新登录或粘贴新的 Cookie";
    public const string LoginUrl = "https://www.doubao.com/";
    public const string CookieDomainSuffix = "doubao.com";
    public static readonly TimeSpan MaxFetchInterval = TimeSpan.FromMinutes(5);

    public static string Normalize(string? raw) =>
        RustCore.String("normalize_doubao_cookie", new { input = raw ?? "" });

    public static string AccountId(string cookie) => AccountId(null, cookie);

    public static string AccountId(JsonBag? payload, string cookie)
    {
        var body = payload?.RawElement ?? EmptyPayload();
        var result = RustCore.Call("doubao_identity", new { payload = body, cookie = cookie ?? "" });
        return result.TryGetProperty("account_id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString() ?? ""
            : "";
    }

    public static string UserId(JsonBag? payload, string cookie)
    {
        var body = payload?.RawElement ?? EmptyPayload();
        var result = RustCore.Call("doubao_identity", new { payload = body, cookie = cookie ?? "" });
        return result.TryGetProperty("user_id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString() ?? ""
            : "";
    }

    static JsonElement EmptyPayload() => JsonDocument.Parse("{}").RootElement.Clone();

    public static bool IsValidAccountId(string? raw)
    {
        var text = (raw ?? "").Trim();
        if (!text.StartsWith("doubao_", StringComparison.Ordinal)) return false;
        var rest = text["doubao_".Length..];
        return rest.Length > 0 && rest.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');
    }

    public static string RandomAccountId()
    {
        var hex = Guid.NewGuid().ToString("N");
        return "doubao_" + hex[..12];
    }

    public static bool IsAuthCode(int code) =>
        RustCore.Bool("is_doubao_auth_code", new { code });

    /// <summary>
    /// Login windows isolate site data per account. doubao.com needs a UUID,
    /// so the account id is hashed into a stable one.
    /// </summary>
    public static string ProfileUuid(string accountId)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes("doubao-profile|" + accountId));
        var bytes = digest[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    /// <summary>Only a real login session counts. Visitor cookies such as uid_tt/odin_tt appear before the QR scan finishes.</summary>
    public static bool LooksLoggedIn(string cookie)
    {
        if (UserId(null, cookie).Length == 0) return false;
        var normalized = Normalize(cookie);
        return new[] { "sessionid", "sessionid_ss" }
            .Any(name => normalized.Contains(name + "=", StringComparison.Ordinal));
    }
}
