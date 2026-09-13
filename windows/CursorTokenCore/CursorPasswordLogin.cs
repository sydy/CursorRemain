using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CursorTokenCore;

public static class CursorPasswordLogin
{
    public const string LoginUrl = "https://cursor.com/login";
    public const int TimeoutSeconds = 180;
    static readonly Regex EmailRe = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant);

    public static bool LooksLikeEmail(string? value) => CursorAuth.LooksLikeEmail(value);

    public static string SanitizeEmail(string? raw)
    {
        var text = (raw ?? "").Trim().ToLowerInvariant();
        if (!LooksLikeEmail(text) || !EmailRe.IsMatch(text)) return "";
        return text;
    }

    public static string DefaultLabel(string email, string? existingLabel = null)
    {
        var current = (existingLabel ?? "").Trim();
        if (current.Length > 0) return current;
        return SanitizeEmail(email);
    }

    public static string? TokenFromCookies(IEnumerable<(string Name, string Value)> cookies)
    {
        foreach (var (name, value) in cookies)
        {
            if (!name.Equals(Token.CookieName, StringComparison.OrdinalIgnoreCase)) continue;
            var raw = (value ?? "").Trim();
            if (raw.Length == 0) return "";
            try { return Token.Normalize(raw); }
            catch { return raw; }
        }
        return "";
    }

    public static string AutofillScript(string email, string password)
    {
        var payload = JsonSerializer.Serialize(
            new Dictionary<string, string>
            {
                ["email"] = SanitizeEmail(email),
                ["password"] = password ?? "",
            },
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        return
            "(function(){" +
            $"const c={payload};" +
            "function setNative(el,val){" +
            "if(!el)return;" +
            "const proto=el.tagName==='TEXTAREA'?window.HTMLTextAreaElement.prototype:window.HTMLInputElement.prototype;" +
            "const desc=Object.getOwnPropertyDescriptor(proto,'value');" +
            "if(desc&&desc.set)desc.set.call(el,val);else el.value=val;" +
            "el.dispatchEvent(new Event('input',{bubbles:true}));" +
            "el.dispatchEvent(new Event('change',{bubbles:true}));" +
            "}" +
            "if(document.querySelector('iframe[src*=\"challenges.cloudflare\"],iframe[src*=\"turnstile\"],input[autocomplete=\"one-time-code\"]'))" +
            "return 'need-user';" +
            "const emailEl=document.querySelector('input[type=\"email\"],input[name=\"email\"],input[autocomplete=\"username\"],input[autocomplete=\"email\"]');" +
            "if(emailEl&&c.email)setNative(emailEl,c.email);" +
            "const passEl=document.querySelector('input[type=\"password\"]');" +
            "if(passEl&&c.password)setNative(passEl,c.password);" +
            "const buttons=[...document.querySelectorAll('button,[type=submit]')];" +
            "const go=buttons.find(b=>/continue|sign in|log in|登录|继续/i.test((b.innerText||b.value||'')));" +
            "if(go&&!window.__cttClicked){window.__cttClicked=Date.now();go.click();return 'clicked';}" +
            "return passEl&&c.password?'filled':'waiting';" +
            "})();";
    }
}
