using System.Text.Json;
using CursorTokenCore;

namespace CursorRemain;

static class LoginProfileStore
{
    static string FilePath => Path.Combine(AppPaths.ConfigDirectory(), "login-profiles.json");

    public static Dictionary<string, string> Load()
    {
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
            return CursorPasswordLogin.CleanProfileIndex(raw);
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    public static void Save(Dictionary<string, string> index)
    {
        var cleaned = CursorPasswordLogin.CleanProfileIndex(index);
        Directory.CreateDirectory(AppPaths.ConfigDirectory());
        File.WriteAllText(FilePath, JsonSerializer.Serialize(cleaned));
    }

    public static string Assign(string? email, string? accountId = null)
    {
        var current = Load();
        var key = CursorPasswordLogin.ProfileKey(email, accountId);
        var assigned = CursorPasswordLogin.AssignProfile(current, key, Guid.NewGuid().ToString("D"));
        if (!Same(current, assigned.Index)) Save(assigned.Index);
        return assigned.ProfileId;
    }

    public static void Bind(string? email, string token, string? profileId)
    {
        var ident = CursorPasswordLogin.ValidProfileId(profileId);
        if (ident.Length == 0) ident = Guid.NewGuid().ToString("D");
        var index = Load();
        foreach (var key in new[]
        {
            CursorPasswordLogin.ProfileKey(email),
            CursorPasswordLogin.ProfileKey(Token.JwtClaim(token, "email")),
            CursorPasswordLogin.ProfileKey("", Token.AccountId(token)),
        })
        {
            if (key.Length == 0) continue;
            index = CursorPasswordLogin.BindProfile(index, key, ident);
        }
        Save(index);
    }

    public static string DirectoryFor(string? profileId)
    {
        var id = CursorPasswordLogin.ValidProfileId(profileId);
        if (id.Length == 0) id = Guid.NewGuid().ToString("D");
        var root = Path.GetFullPath(Path.Combine(AppPaths.ConfigDirectory(), "login-profiles"));
        var path = Path.GetFullPath(Path.Combine(root, id));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(root, Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(path);
        return path;
    }

    static bool Same(Dictionary<string, string> left, Dictionary<string, string> right)
    {
        if (left.Count != right.Count) return false;
        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var other) || other != value) return false;
        }
        return true;
    }
}
