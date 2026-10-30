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
    public static bool LooksLikeToken(string? line) =>
        RustCore.Bool("looks_like_token", new { line = line ?? "" });

    public static bool IsSingleToken(string? text) =>
        RustCore.Bool("is_single_token", new { text = text ?? "" });

    public static List<string> TokenValues(string? text) =>
        RustCore.Strings("token_values", new { text = text ?? "" }).ToList();

    public static List<AccountPasteItem> Parse(string? text)
    {
        return RustCore.Call("parse_account_paste", new { text = text ?? "" }).EnumerateArray().Select(row => new AccountPasteItem
        {
            Kind = row.GetProperty("kind").GetString() ?? "",
            Token = row.GetProperty("token").GetString() ?? "",
            Email = row.GetProperty("email").GetString() ?? "",
            Password = row.GetProperty("password").GetString() ?? "",
            Message = row.GetProperty("message").GetString() ?? "",
        }).ToList();
    }
}
