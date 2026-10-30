using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CursorTokenCore;

public static class Token
{
    public const string CookieName = "WorkosCursorSessionToken";
    static readonly char[] Junk = ['\u2026', '\u2022', '\uFEFF', '\u200B', '\u200C', '\u200D', '\u00A0'];

    public static string Normalize(string raw)
    {
        var result = RustCore.Call("normalize_token", new { input = raw ?? "" });
        if (result.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            return result.GetProperty("value").GetString() ?? "";
        var message = result.TryGetProperty("error", out var err) ? err.GetString() ?? "Token 无效" : "Token 无效";
        throw new CursorApiException(message, 401);
    }

    public static List<string> Variants(string token) =>
        RustCore.Strings("token_variants", new { input = token ?? "" }).ToList();

    public static string AccountId(string token) =>
        RustCore.String("account_id", new { input = token ?? "" });

    public static string SafeAccountId(string value) =>
        RustCore.String("safe_account_id", new { input = value ?? "" });

    public static bool IsAuthErrorMessage(string? message) =>
        RustCore.Call("is_auth_error_message", new { message = message ?? "" }).GetBoolean();

    public static bool LooksLikeJwt(string value)
    {
        var parts = value.Split('.');
        return parts.Length == 3 && parts.All(p => p.Length > 0);
    }

    public static Dictionary<string, object?>? JwtPayload(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return null;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload += new string('=', (4 - payload.Length % 4) % 4);
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
        }
        catch { return null; }
    }

    public static string ExtractUserId(string jwt)
    {
        var sub = JwtSubject(jwt);
        return sub.Contains('|') ? sub.Split('|')[^1] : sub;
    }

    public static string ExtractJwt(string token) =>
        RustCore.String("extract_jwt", new { input = token ?? "" });

    public static string JwtSubject(string token)
    {
        var jwt = LooksLikeJwt(token) ? token : ExtractJwt(token);
        var payload = JwtPayload(jwt);
        if (payload is null || !payload.TryGetValue("sub", out var subObj) || subObj is null) return "";
        return subObj is JsonElement je
            ? (je.ValueKind == JsonValueKind.String ? je.GetString() ?? "" : je.ToString())
            : subObj.ToString() ?? "";
    }

    public static string JwtClaim(string token, string name)
    {
        var jwt = LooksLikeJwt(token) ? token : ExtractJwt(token);
        var payload = JwtPayload(jwt);
        if (payload is null || !payload.TryGetValue(name, out var obj) || obj is null) return "";
        return obj is JsonElement je
            ? (je.ValueKind == JsonValueKind.String ? je.GetString() ?? "" : je.ToString())
            : obj.ToString() ?? "";
    }

    /// <summary>JWT <c>type</c> claim: <c>session</c> signs Cursor in; <c>web</c> is a browser cookie.</summary>
    public static string JwtType(string token) =>
        RustCore.String("jwt_type", new { input = token ?? "" });

    public static bool CanWriteToCursor(string token) =>
        RustCore.Bool("can_write_to_cursor", new { input = token ?? "" });

    static string ReplaceFirst(string text, string old, string neu)
    {
        var i = text.IndexOf(old, StringComparison.Ordinal);
        return i < 0 ? text : text[..i] + neu + text[(i + old.Length)..];
    }
}
