using System.Text.RegularExpressions;

namespace CursorTokenCore;

public sealed class AccountPasteItem
{
    public string Kind { get; init; } = "";
    public string Token { get; init; } = "";
    public string Email { get; init; } = "";
    public string Password { get; init; } = "";
    public string Message { get; init; } = "";
}

public static class CursorAccountPaste
{
    const string EmailToken = @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}";
    static readonly Regex LabeledBoth = new(
        @"^(?:账号|帐号|账户|邮箱|用户名)\s*[：:]\s*(" + EmailToken + @")\s*密码\s*[：:]\s*(.+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    static readonly Regex LabeledEmail = new(
        @"^(?:账号|帐号|账户|邮箱|用户名)\s*[：:]\s*(" + EmailToken + @")\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    static readonly Regex LabeledPassword = new(
        @"^密码\s*[：:]\s*(.+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    static readonly Regex EmailAtStart = new(
        @"^(" + EmailToken + @")(.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool LooksLikeToken(string? line)
    {
        var text = (line ?? "").Trim();
        if (text.Length == 0) return false;
        var lower = text.ToLowerInvariant();
        if (lower.Contains("workoscursorsessiontoken=")) return true;
        if (lower.Contains("%3a%3a") || text.Contains("::")) return true;
        var parts = text.Split('.');
        return parts.Length == 3 && parts.All(p => p.Length > 0);
    }

    public static bool IsSingleToken(string? text)
    {
        var items = Parse(text);
        return items.Count == 1 && items[0].Kind == "token";
    }

    public static List<AccountPasteItem> Parse(string? text)
    {
        var raw = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        var items = new List<AccountPasteItem>();
        var pendingEmail = "";

        void FlushPending()
        {
            if (pendingEmail.Length == 0) return;
            items.Add(Item("error", email: pendingEmail, message: "只有账号没有密码"));
            pendingEmail = "";
        }

        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (TryLabeledBoth(line, out var bothEmail, out var bothPassword))
            {
                FlushPending();
                items.Add(Item("credentials", email: bothEmail, password: bothPassword));
                continue;
            }

            var emailOnly = ParseLabeledEmail(line);
            if (emailOnly.Length > 0)
            {
                FlushPending();
                pendingEmail = emailOnly;
                continue;
            }

            if (TryLabeledPassword(line, out var passwordOnly))
            {
                if (pendingEmail.Length > 0 && passwordOnly.Length > 0)
                {
                    items.Add(Item("credentials", email: pendingEmail, password: passwordOnly));
                    pendingEmail = "";
                }
                else if (pendingEmail.Length > 0)
                    FlushPending();
                else
                    items.Add(Item("error", message: "只有密码没有账号"));
                continue;
            }

            if (TryEmailSeparator(line, out var sepEmail, out var sepPassword))
            {
                FlushPending();
                items.Add(Item("credentials", email: sepEmail, password: sepPassword));
                continue;
            }

            if (LooksLikeToken(line))
            {
                FlushPending();
                items.Add(Item("token", token: line));
                continue;
            }

            FlushPending();
            items.Add(Item("error", message: "无法识别"));
        }

        FlushPending();
        return items;
    }

    static AccountPasteItem Item(string kind, string token = "", string email = "", string password = "", string message = "") =>
        new() { Kind = kind, Token = token, Email = email, Password = password, Message = message };

    static bool TryLabeledBoth(string line, out string email, out string password)
    {
        email = "";
        password = "";
        var match = LabeledBoth.Match(line);
        if (!match.Success) return false;
        email = CursorPasswordLogin.SanitizeEmail(match.Groups[1].Value);
        password = match.Groups[2].Value.Trim();
        return email.Length > 0 && password.Length > 0;
    }

    static string ParseLabeledEmail(string line)
    {
        var match = LabeledEmail.Match(line);
        return match.Success ? CursorPasswordLogin.SanitizeEmail(match.Groups[1].Value) : "";
    }

    static bool TryLabeledPassword(string line, out string password)
    {
        password = "";
        var match = LabeledPassword.Match(line);
        if (!match.Success) return false;
        password = match.Groups[1].Value.Trim();
        return true;
    }

    static bool TryEmailSeparator(string line, out string email, out string password)
    {
        email = "";
        password = "";
        var match = EmailAtStart.Match(line);
        if (!match.Success) return false;
        email = CursorPasswordLogin.SanitizeEmail(match.Groups[1].Value);
        if (email.Length == 0) return false;
        var rest = match.Groups[2].Value;
        if (rest.Length == 0) return false;
        var stripped = rest.TrimStart();
        if (stripped.StartsWith("----", StringComparison.Ordinal))
            password = stripped[4..].Trim();
        else if (stripped.StartsWith(':') || stripped.StartsWith('：'))
            password = stripped[1..].Trim();
        else if (rest[0] is ' ' or '\t')
            password = rest.Trim();
        else
            return false;
        return password.Length > 0;
    }
}
