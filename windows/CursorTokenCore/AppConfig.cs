using System.Collections.Concurrent;
using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace CursorTokenCore;

public sealed class Account
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public bool PasswordDecryptFailed { get; set; }
    public string StoredPassword { get; set; } = "";
    public string Token { get; set; } = "";
    public string MembershipType { get; set; } = "";
    public string AccountKind { get; set; } = AccountValidity.LongTerm;
    public string TempStartAt { get; set; } = "";
    public int TempValidDays { get; set; }
    public int TempValidHours { get; set; }
    public double? LastRemaining { get; set; }
    public string LastError { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public string UsageUpdatedAt { get; set; } = "";
    public string SyncUpdatedAt { get; set; } = "";
    public List<int> AlertNotifiedLevels { get; set; } = [];
    public bool AuthErrorNotified { get; set; }
    public bool ExhaustionNotified { get; set; }
    public bool LowQuotaNotified { get; set; }
    /// <summary>True when the on-disk <c>enc:v1:</c> blob could not be decrypted. <see cref="Token"/> is empty; <see cref="StoredToken"/> keeps the blob so a save cannot clobber it.</summary>
    public bool TokenDecryptFailed { get; set; }
    public string StoredToken { get; set; } = "";
    public double ActualCny { get; set; }
    public string Channel { get; set; } = "";
    public string BillingCycleStart { get; set; } = "";
    public string BillingCycleEnd { get; set; } = "";
    public string ReportStartDate { get; set; } = "";
    public string ReportEndDate { get; set; } = "";

    public string DisplayLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Label)) return Label.Trim();
            if (!string.IsNullOrWhiteSpace(Email)) return Email.Trim();
            if (!string.IsNullOrWhiteSpace(MembershipType)) return MembershipType.Trim();
            var aid = Id.Trim();
            if (aid.StartsWith("tok_")) return "未命名账号";
            if (aid.Length > 14) return aid[..12] + "…";
            return aid.Length == 0 ? "未命名账号" : aid;
        }
    }

    public string Caption(bool isActive)
    {
        var parts = new List<string> { DisplayLabel };
        var memb = MembershipType.Trim();
        if (memb.Length > 0 && !memb.Equals(DisplayLabel, StringComparison.OrdinalIgnoreCase)) parts.Add(memb);
        if (AccountValidity.IsTemporary(this)) parts.Add("临时");
        if (LastRemaining is { } r) parts.Add($"剩余 {r:0}%");
        if (!string.IsNullOrWhiteSpace(LastError) && LastRemaining is null) parts.Add("已失效");
        var text = string.Join(" · ", parts);
        if (isActive) text += "  (当前)";
        return text;
    }
}

public sealed class AppConfig
{
    public string SessionToken { get; set; } = "";
    public List<Account> Accounts { get; set; } = [];
    public string ActiveAccountId { get; set; } = "";
    public string ActiveAccountUpdatedAt { get; set; } = "";
    public Dictionary<string, string> SettingsFieldUpdatedAt { get; set; } = new(StringComparer.Ordinal);
    public int RefreshIntervalMinutes { get; set; } = 10;
    public int LowQuotaThreshold { get; set; } = 20;
    public List<int> AlertThresholds { get; set; } = [50, 20, 5];
    public bool NotifyEnabled { get; set; } = true;
    public bool NotifyExhaustionRisk { get; set; } = true;
    public bool AutostartEnabled { get; set; } = true;
    public bool AutoUpdateEnabled { get; set; } = true;
    public string UpdateLastCheckAt { get; set; } = "";
    public string UpdateLastError { get; set; } = "";
    public string UpdateInstalledSha { get; set; } = "";
    public long UpdateInstalledAssetId { get; set; }
    public string TrayDisplayMode { get; set; } = "ring";
    /// <summary><c>system</c>, <c>light</c>, or <c>dark</c>. Local to this machine; not part of cloud sync.</summary>
    public string ColorMode { get; set; } = "system";
    public double MonthlyPlanUsd { get; set; }
    public double ActualCny { get; set; }
    public double UsdCnyRate { get; set; } = UsageEvents.DefaultUsdCnyRate;
    public bool LowQuotaNotified { get; set; }
    public bool AuthErrorNotified { get; set; }
    public List<int> AlertNotifiedLevels { get; set; } = [];
    public bool ExhaustionNotified { get; set; }
    public bool SyncEnabled { get; set; }
    public string SyncSecret { get; set; } = "";
    public string SyncDeviceId { get; set; } = "";
    public string SyncLastAt { get; set; } = "";
    public string SyncLastError { get; set; } = "";
    public bool SyncSecretDecryptFailed { get; set; }
    public string StoredSyncSecret { get; set; } = "";
    public string CloudEmail { get; set; } = "";
    public string CloudAccessToken { get; set; } = "";
    public string CloudRefreshToken { get; set; } = "";
    public int CloudRevision { get; set; }
    public bool CloudAccessDecryptFailed { get; set; }
    public bool CloudRefreshDecryptFailed { get; set; }
    public string StoredCloudAccessToken { get; set; } = "";
    public string StoredCloudRefreshToken { get; set; } = "";
    public bool CloudLoggedIn =>
        !string.IsNullOrWhiteSpace(CloudAccessToken) || !string.IsNullOrWhiteSpace(CloudRefreshToken);
    public List<DeletedAccount> DeletedAccounts { get; set; } = [];
    /// <summary>True when config.json existed but could not be parsed. Save will not clobber it unless the user adds an account.</summary>
    public bool LoadError { get; set; }
    /// <summary>True when a stored <c>enc:v1:</c> session token could not be decrypted.</summary>
    public bool DecryptError { get; set; }
    public string StoredSessionToken { get; set; } = "";

    public Account? ActiveAccount =>
        Accounts.FirstOrDefault(a => a.Id == ActiveAccountId) ?? Accounts.FirstOrDefault();

    public CnySpendSettings SpendSettings(string? membership = null) =>
        new(MonthlyPlanUsd, UsdCnyRate, membership ?? ActiveAccount?.MembershipType ?? "", ActiveAccount?.ActualCny ?? ActualCny);

    public bool SetActualCny(string id, double amount)
    {
        var acc = Accounts.FirstOrDefault(a => a.Id == id);
        if (acc is null) return false;
        var clamped = UsageEvents.ClampActualCny(amount);
        if (acc.ActualCny != clamped)
        {
            acc.ActualCny = clamped;
            AccountSync.TouchAccount(acc);
        }
        else acc.ActualCny = clamped;
        SyncLegacyFields();
        return true;
    }

    public bool SetChannel(string id, string channel)
    {
        var acc = Accounts.FirstOrDefault(a => a.Id == id);
        if (acc is null) return false;
        var sanitized = UsageEvents.SanitizeChannel(channel);
        if (acc.Channel != sanitized)
        {
            acc.Channel = sanitized;
            AccountSync.TouchAccount(acc);
        }
        else acc.Channel = sanitized;
        return true;
    }

    public bool SetReportRange(string id, string start, string end)
    {
        var acc = Accounts.FirstOrDefault(a => a.Id == id);
        if (acc is null) return false;
        var (normStart, normEnd, _) = UsageEvents.NormalizeReportRange(start, end);
        acc.ReportStartDate = normStart;
        acc.ReportEndDate = normEnd;
        return true;
    }

    public (Account acc, bool created) UpsertAccount(string rawToken, string? label = null, string? membershipType = null, double? remaining = null, string? error = null, string? email = null, string? password = null, bool activate = true)
    {
        string token;
        try { token = Token.Normalize(rawToken); }
        catch { token = rawToken.Trim(); }
        if (token.Length == 0) throw new CursorApiException("Token 为空");
        var accountId = Token.AccountId(token);
        if (accountId.Length == 0) throw new CursorApiException("无法从 Token 识别账号");
        var existing = Accounts.FirstOrDefault(a => a.Id == accountId);
        var created = existing is null;
        if (existing is null)
        {
            existing = new Account { Id = accountId, Token = token };
            if (Accounts.Count == 0) CopyLegacyFlags(existing);
            Accounts.Add(existing);
        }
        var identityChanged = created || existing.Token != token;
        existing.Token = token;
        if (email is not null)
        {
            var newEmail = CursorPasswordLogin.SanitizeEmail(email);
            if (existing.Email != newEmail) identityChanged = true;
            existing.Email = newEmail;
            if (label is null)
            {
                var fallback = CursorPasswordLogin.DefaultLabel(newEmail, existing.Label);
                if (existing.Label != fallback) identityChanged = true;
                existing.Label = fallback;
            }
        }
        if (password is not null)
        {
            if (existing.Password != password) identityChanged = true;
            existing.Password = password;
            existing.PasswordDecryptFailed = false;
            existing.StoredPassword = "";
        }
        if (label is not null)
        {
            var newLabel = label.Trim();
            if (existing.Label != newLabel) identityChanged = true;
            existing.Label = newLabel;
        }
        if (membershipType is not null) existing.MembershipType = membershipType.Trim();
        if (remaining is not null) { existing.LastRemaining = Numbers.Round2(remaining.Value); existing.LastError = ""; }
        if (error is not null) existing.LastError = error;
        if (identityChanged || string.IsNullOrWhiteSpace(existing.SyncUpdatedAt))
        {
            AccountSync.TouchAccount(existing);
            AccountSync.ForgetDeleted(this, accountId);
        }
        if (activate)
        {
            if (ActiveAccountId != accountId) AccountSync.TouchActiveAccount(this);
            ActiveAccountId = accountId;
        }
        SyncLegacyFields();
        return (existing, created);
    }

    public bool SetActiveAccount(string id)
    {
        if (!Accounts.Any(a => a.Id == id)) return false;
        if (ActiveAccountId != id) AccountSync.TouchActiveAccount(this);
        ActiveAccountId = id;
        SyncLegacyFields();
        return true;
    }

    public bool RenameAccount(string id, string label)
    {
        var acc = Accounts.FirstOrDefault(a => a.Id == id);
        if (acc is null) return false;
        var newLabel = label.Trim();
        if (acc.Label != newLabel)
        {
            acc.Label = newLabel;
            AccountSync.TouchAccount(acc);
        }
        else acc.Label = newLabel;
        return true;
    }

    public bool UpdateAccountValidity(string id, string kind, string startAt, int days, int hours)
    {
        var acc = Accounts.FirstOrDefault(a => a.Id == id);
        if (acc is null) return false;
        var newKind = AccountValidity.SanitizeKind(kind);
        var newStart = (startAt ?? "").Trim();
        var newDays = AccountValidity.ClampDays(days);
        var newHours = AccountValidity.ClampHours(hours);
        var changed = !AccountValidity.ValidityEquals(acc, newKind, newStart, newDays, newHours);
        acc.AccountKind = newKind;
        acc.TempStartAt = newStart;
        acc.TempValidDays = newDays;
        acc.TempValidHours = newHours;
        if (changed) AccountSync.TouchAccount(acc);
        return true;
    }

    public bool RemoveAccount(string id)
    {
        var n = Accounts.RemoveAll(a => a.Id == id);
        if (n == 0) return false;
        if (ActiveAccountId == id)
        {
            ActiveAccountId = Accounts.FirstOrDefault()?.Id ?? "";
            AccountSync.TouchActiveAccount(this);
        }
        AccountSync.RememberDeleted(this, id);
        SyncLegacyFields();
        return true;
    }

    public HashSet<string> ExistingTokenVariants()
    {
        var skip = new HashSet<string>();
        foreach (var acc in Accounts)
        {
            foreach (var v in Token.Variants(acc.Token)) skip.Add(v);
            if (!string.IsNullOrWhiteSpace(acc.Token)) skip.Add(acc.Token.Trim());
        }
        return skip;
    }

    public void ApplySnapshot(string accountId, string? membershipType = null, double? remaining = null, string? error = null, string? updatedAt = null, string? billingCycleStart = null, string? billingCycleEnd = null)
    {
        var acc = Accounts.FirstOrDefault(a => a.Id == accountId);
        if (acc is null) return;
        var prevRemaining = acc.LastRemaining;
        var prevError = acc.LastError ?? "";
        var prevStart = acc.BillingCycleStart ?? "";
        var prevEnd = acc.BillingCycleEnd ?? "";
        if (membershipType is not null) acc.MembershipType = membershipType.Trim();
        if (remaining is not null) acc.LastRemaining = Numbers.Round2(remaining.Value);
        if (error is not null) acc.LastError = error;
        else if (remaining is not null) acc.LastError = "";
        if (updatedAt is not null) acc.UpdatedAt = updatedAt;
        if (billingCycleStart is not null) acc.BillingCycleStart = billingCycleStart.Trim();
        if (billingCycleEnd is not null) acc.BillingCycleEnd = billingCycleEnd.Trim();
        var usageTouched = remaining is not null || error is not null || billingCycleStart is not null || billingCycleEnd is not null;
        if (usageTouched && (
            acc.LastRemaining != prevRemaining
            || (acc.LastError ?? "") != prevError
            || (acc.BillingCycleStart ?? "") != prevStart
            || (acc.BillingCycleEnd ?? "") != prevEnd
            || string.IsNullOrWhiteSpace(acc.UsageUpdatedAt)))
            acc.UsageUpdatedAt = AccountSync.NowIso();
    }

    public void SyncLegacyFields()
    {
        var acc = ActiveAccount;
        if (acc is null)
        {
            SessionToken = ActiveAccountId = "";
            Accounts = [];
            AlertNotifiedLevels = [];
            AuthErrorNotified = ExhaustionNotified = LowQuotaNotified = false;
            return;
        }
        ActiveAccountId = acc.Id;
        SessionToken = acc.Token;
        AlertNotifiedLevels = [.. acc.AlertNotifiedLevels];
        AuthErrorNotified = acc.AuthErrorNotified;
        ExhaustionNotified = acc.ExhaustionNotified;
        LowQuotaNotified = acc.LowQuotaNotified;
        ActualCny = UsageEvents.ClampActualCny(acc.ActualCny);
    }

    void CopyLegacyFlags(Account account)
    {
        account.AlertNotifiedLevels = AlertNotifiedLevels.Where(x => x is >= 1 and <= 100).OrderBy(x => x).ToList();
        account.AuthErrorNotified = AuthErrorNotified;
        account.ExhaustionNotified = ExhaustionNotified;
        account.LowQuotaNotified = LowQuotaNotified;
        if (account.ActualCny <= 0 && ActualCny > 0)
            account.ActualCny = UsageEvents.ClampActualCny(ActualCny);
    }
}

public static class AppPaths
{
    public const string AppName = "CursorRemain";
    public const string LegacyAppName = "CursorTokenTray";
    public const string DisplayName = "Cursor 余量";
    public const string SettingsTitle = "余量设置";

    public static string ConfigDirectory(string? overrideDir = null)
    {
        if (!string.IsNullOrEmpty(overrideDir)) return overrideDir;
        var dest = Path.Combine(RoamingAppData(), AppName);
        TryMigrateLegacyDirectory(Path.Combine(RoamingAppData(), LegacyAppName), dest);
        return dest;
    }

    public static string LegacyConfigDirectory() => Path.Combine(RoamingAppData(), LegacyAppName);

    static string RoamingAppData() =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>
    /// Move or copy an old CursorTokenTray config folder into CursorRemain.
    /// If dest already has config.json it wins. Returns true when dest received data.
    /// </summary>
    public static bool TryMigrateLegacyDirectory(string? sourceDir, string? destDir)
    {
        var source = (sourceDir ?? "").Trim();
        var dest = (destDir ?? "").Trim();
        if (source.Length == 0 || dest.Length == 0) return false;
        try
        {
            var sourceFull = Path.GetFullPath(source);
            var destFull = Path.GetFullPath(dest);
            if (string.Equals(sourceFull, destFull, StringComparison.OrdinalIgnoreCase)) return false;
            if (File.Exists(Path.Combine(destFull, "config.json"))) return false;
            if (!Directory.Exists(sourceFull)) return false;
            if (!Directory.Exists(destFull))
            {
                var parent = Path.GetDirectoryName(destFull);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                try
                {
                    Directory.Move(sourceFull, destFull);
                    return true;
                }
                catch
                {
                    Directory.CreateDirectory(destFull);
                    CopyDirectory(sourceFull, destFull);
                    return File.Exists(Path.Combine(destFull, "config.json"));
                }
            }
            CopyDirectory(sourceFull, destFull);
            return File.Exists(Path.Combine(destFull, "config.json"));
        }
        catch
        {
            return false;
        }
    }

    static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(dest, Path.GetFileName(file));
            if (!File.Exists(target)) File.Copy(file, target, false);
        }
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(dir);
            if (name.Length == 0) continue;
            CopyDirectory(dir, Path.Combine(dest, name));
        }
    }

    public static string ConfigPath(string? dir = null) => Path.Combine(ConfigDirectory(dir), "config.json");
    public static string ErrorLogPath(string? dir = null) => Path.Combine(ConfigDirectory(dir), "error.log");
    public static string HistoryPath(string? accountId, string? dir = null)
    {
        var root = ConfigDirectory(dir);
        var aid = (accountId ?? "").Trim();
        return aid.Length == 0 ? Path.Combine(root, "usage_history.jsonl") : Path.Combine(root, $"usage_history.{Token.SafeAccountId(aid)}.jsonl");
    }

    public static string UsageEventsPath(string? accountId, bool teamScope, string? dir = null)
    {
        var root = ConfigDirectory(dir);
        var aid = Token.SafeAccountId((accountId ?? "").Trim());
        if (aid.Length == 0) aid = "account";
        return Path.Combine(root, teamScope ? $"usage_events.{aid}.team.jsonl" : $"usage_events.{aid}.jsonl");
    }
}

public sealed class ConfigLockException : IOException
{
    public ConfigLockException() : base("无法获取配置文件锁，请稍后重试") { }
}

public static class ConfigStore
{
    const int DefaultLockWaitMs = 8000;
    static readonly ConcurrentDictionary<string, string> LastGoodJson = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Test hook so lock-timeout tests do not wait the full production interval.</summary>
    internal static int LockWaitMilliseconds { get; set; } = DefaultLockWaitMs;

    public static AppConfig Load(string? directory = null)
    {
        var dir = AppPaths.ConfigDirectory(directory);
        Directory.CreateDirectory(dir);
        try
        {
            return WithLock(dir, () =>
            {
                var cfg = LoadUnlocked(dir);
                Remember(dir, cfg);
                return cfg;
            }, write: false);
        }
        catch (ConfigLockException)
        {
            if (TryLastGood(dir, out var cached)) return cached;
            throw;
        }
    }

    public static void Save(AppConfig cfg, string? directory = null)
    {
        var dir = AppPaths.ConfigDirectory(directory);
        Directory.CreateDirectory(dir);
        WithLock(dir, () =>
        {
            SaveUnlocked(cfg, dir);
            Remember(dir, cfg);
            return 0;
        }, write: true);
    }

    /// <summary>
    /// Reload from disk, apply <paramref name="mutate"/>, and save under the same lock
    /// so a long-running refresh cannot clobber settings saved in the meantime.
    /// </summary>
    public static AppConfig Update(Action<AppConfig> mutate, string? directory = null)
    {
        var dir = AppPaths.ConfigDirectory(directory);
        Directory.CreateDirectory(dir);
        return WithLock(dir, () =>
        {
            var cfg = LoadUnlocked(dir);
            if (cfg.LoadError && cfg.Accounts.Count == 0) return cfg;
            mutate(cfg);
            SaveUnlocked(cfg, dir);
            Remember(dir, cfg);
            return cfg;
        }, write: true);
    }

    static AppConfig LoadUnlocked(string dir)
    {
        var path = AppPaths.ConfigPath(dir);
        if (!File.Exists(path))
        {
            var fresh = new AppConfig();
            SaveUnlocked(fresh, dir);
            return fresh;
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return Normalize(doc.RootElement);
        }
        catch
        {
            TryQuarantine(path);
            return new AppConfig { LoadError = true };
        }
    }

    static void SaveUnlocked(AppConfig cfg, string dir)
    {
        if (cfg.LoadError && cfg.Accounts.Count == 0) return;
        cfg.LoadError = false;
        var path = AppPaths.ConfigPath(dir);
        var json = JsonSerializer.Serialize(ToDict(cfg), new JsonSerializerOptions { WriteIndented = true });
        AtomicWrite(path, json);
    }

    static T WithLock<T>(string dir, Func<T> body, bool write)
    {
        Directory.CreateDirectory(dir);
        var lockPath = Path.Combine(dir, "config.lock");
        var until = DateTime.UtcNow.AddMilliseconds(LockWaitMilliseconds);
        while (true)
        {
            try
            {
                using var fs = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return body();
            }
            catch (IOException) when (DateTime.UtcNow < until)
            {
                Thread.Sleep(25);
            }
            catch (IOException)
            {
                throw new ConfigLockException();
            }
        }
    }

    static string DirKey(string dir) => Path.GetFullPath(dir);

    static void Remember(string dir, AppConfig cfg)
    {
        try { LastGoodJson[DirKey(dir)] = JsonSerializer.Serialize(ToDict(cfg)); }
        catch { }
    }

    static bool TryLastGood(string dir, out AppConfig cfg)
    {
        cfg = null!;
        if (!LastGoodJson.TryGetValue(DirKey(dir), out var json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            cfg = Normalize(doc.RootElement);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void AtomicWrite(string path, string contents)
    {
        var tmp = path + ".tmp";
        var bytes = System.Text.Encoding.UTF8.GetBytes(contents);
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(true);
        }
        if (File.Exists(path))
        {
            try { File.Replace(tmp, path, null); }
            catch
            {
                File.Copy(tmp, path, true);
                try { File.Delete(tmp); } catch { }
            }
        }
        else
        {
            File.Move(tmp, path, true);
        }
        RestrictToOwner(path);
    }

    static void RestrictToOwner(string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var user = WindowsIdentity.GetCurrent().User;
            if (user is null) return;
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
        catch
        {
            // ACL is best-effort; a saved config is still better than failing the write.
        }
    }

    static void TryQuarantine(string path)
    {
        try { File.Copy(path, path + ".corrupt", true); } catch { }
    }

    public static AppConfig Normalize(JsonElement raw)
    {
        var cfg = new AppConfig();
        if (raw.TryGetProperty("session_token", out var st))
        {
            var stored = st.GetString() ?? "";
            if (TokenProtector.TryUnprotect(stored, out var plain))
                cfg.SessionToken = plain;
            else
            {
                cfg.SessionToken = "";
                cfg.DecryptError = true;
                cfg.StoredSessionToken = stored;
            }
        }
        if (raw.TryGetProperty("active_account_id", out var aid)) cfg.ActiveAccountId = aid.GetString() ?? "";
        cfg.ActiveAccountUpdatedAt = Str(raw, "active_account_updated_at").Trim();
        cfg.SettingsFieldUpdatedAt = ParseFieldUpdatedAt(raw);
        if (raw.TryGetProperty("refresh_interval_minutes", out var ri) && ri.TryGetInt32(out var riv)) cfg.RefreshIntervalMinutes = Math.Max(1, riv);
        if (raw.TryGetProperty("low_quota_threshold", out var lq) && lq.TryGetInt32(out var lqv)) cfg.LowQuotaThreshold = Math.Clamp(lqv, 1, 100);
        cfg.NotifyEnabled = Bool(raw, "notify_enabled", true);
        cfg.NotifyExhaustionRisk = Bool(raw, "notify_exhaustion_risk", true);
        cfg.AutostartEnabled = Bool(raw, "autostart_enabled", true);
        cfg.AutoUpdateEnabled = Bool(raw, "auto_update_enabled", true);
        cfg.UpdateLastCheckAt = Str(raw, "update_last_check_at").Trim();
        cfg.UpdateLastError = Str(raw, "update_last_error");
        cfg.UpdateInstalledSha = AppUpdate.NormalizeSha(Str(raw, "update_installed_sha"));
        if (raw.TryGetProperty("update_installed_asset_id", out var uaid))
        {
            if (uaid.ValueKind == JsonValueKind.Number && uaid.TryGetInt64(out var uaidv))
                cfg.UpdateInstalledAssetId = Math.Max(0, uaidv);
            else if (uaid.ValueKind == JsonValueKind.String && long.TryParse(uaid.GetString(), out uaidv))
                cfg.UpdateInstalledAssetId = Math.Max(0, uaidv);
        }
        cfg.LowQuotaNotified = Bool(raw, "low_quota_notified", false);
        cfg.AuthErrorNotified = Bool(raw, "auth_error_notified", false);
        cfg.ExhaustionNotified = Bool(raw, "exhaustion_notified", false);
        if (!string.IsNullOrWhiteSpace(cfg.SessionToken))
        {
            try { cfg.SessionToken = Token.Normalize(cfg.SessionToken); } catch { }
        }
        var mode = Str(raw, "tray_display_mode", "ring").Trim().ToLowerInvariant();
        cfg.TrayDisplayMode = mode is "ring" or "number" or "dot" ? mode : "ring";
        cfg.ColorMode = NormalizeAppearance(Str(raw, "color_mode", "system"));
        cfg.MonthlyPlanUsd = UsageEvents.ClampMonthlyPlanUsd(DoubleVal(raw, "monthly_plan_usd", 0));
        cfg.ActualCny = UsageEvents.ClampActualCny(DoubleVal(raw, "actual_cny", 0));
        cfg.UsdCnyRate = UsageEvents.ClampUsdCnyRate(DoubleVal(raw, "usd_cny_rate", UsageEvents.DefaultUsdCnyRate));
        if (!raw.TryGetProperty("alert_thresholds", out _) && raw.TryGetProperty("low_quota_threshold", out _))
            cfg.AlertThresholds = [cfg.LowQuotaThreshold];
        else
            cfg.AlertThresholds = ParseThresholds(raw.TryGetProperty("alert_thresholds", out var at) ? at : default);
        cfg.AlertNotifiedLevels = ParseIntList(raw.TryGetProperty("alert_notified_levels", out var an) ? an : default);
        cfg.Accounts = ParseAccounts(raw);
        MigrateLegacyActualCny(cfg, raw);
        if (cfg.Accounts.Any(a => a.TokenDecryptFailed)) cfg.DecryptError = true;
        cfg.SyncEnabled = Bool(raw, "sync_enabled", false);
        if (raw.TryGetProperty("sync_secret", out var ss))
        {
            var stored = ss.GetString() ?? "";
            if (TokenProtector.TryUnprotect(stored, out var plain))
                cfg.SyncSecret = plain;
            else
            {
                cfg.SyncSecret = "";
                cfg.SyncSecretDecryptFailed = true;
                cfg.StoredSyncSecret = stored;
            }
        }
        cfg.SyncDeviceId = Str(raw, "sync_device_id").Trim();
        cfg.SyncLastAt = Str(raw, "sync_last_at").Trim();
        cfg.SyncLastError = Str(raw, "sync_last_error");
        cfg.CloudEmail = Str(raw, "cloud_email").Trim().ToLowerInvariant();
        UnprotectField(raw, "cloud_access_token", v => cfg.CloudAccessToken = v, () =>
        {
            cfg.CloudAccessDecryptFailed = true;
            cfg.StoredCloudAccessToken = Str(raw, "cloud_access_token");
        });
        UnprotectField(raw, "cloud_refresh_token", v => cfg.CloudRefreshToken = v, () =>
        {
            cfg.CloudRefreshDecryptFailed = true;
            cfg.StoredCloudRefreshToken = Str(raw, "cloud_refresh_token");
        });
        if (raw.TryGetProperty("cloud_revision", out var cr) && cr.TryGetInt32(out var crv))
            cfg.CloudRevision = Math.Max(0, crv);
        if (!cfg.CloudLoggedIn) cfg.SyncEnabled = false;
        cfg.DeletedAccounts = ParseDeleted(raw);
        return NormalizeAccounts(cfg, raw);
    }

    public static List<int> ParseThresholds(object? value)
    {
        var nums = new List<int>();
        if (value is string s)
        {
            foreach (var p in s.Replace("，", ",").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (double.TryParse(p, out var d)) nums.Add((int)d);
        }
        else if (value is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.String) return ParseThresholds(el.GetString());
            if (el.ValueKind == JsonValueKind.Array)
                foreach (var x in el.EnumerateArray())
                    if (x.TryGetInt32(out var n)) nums.Add(n);
                    else if (x.TryGetDouble(out var d)) nums.Add((int)d);
        }
        else nums.AddRange([50, 20, 5]);
        var cleaned = nums.Where(n => n is >= 1 and <= 100).Distinct().OrderByDescending(x => x).ToList();
        return cleaned.Count == 0 ? [50, 20, 5] : cleaned;
    }

    static AppConfig NormalizeAccounts(AppConfig cfg, JsonElement raw)
    {
        var seen = new HashSet<string>();
        cfg.Accounts = cfg.Accounts.Where(a => seen.Add(a.Id)).ToList();
        var token = cfg.SessionToken;
        try { token = Token.Normalize(token); } catch { token = token.Trim(); }
        var activeId = cfg.ActiveAccountId.Trim();
        if (token.Length > 0)
        {
            var active = cfg.Accounts.FirstOrDefault(a => a.Id == activeId);
            if (active is null || active.Token != token)
                cfg.UpsertAccount(token, activate: true);
        }
        if (cfg.Accounts.Count > 0)
        {
            var ids = cfg.Accounts.Select(a => a.Id).ToHashSet();
            if (!ids.Contains(cfg.ActiveAccountId)) cfg.ActiveAccountId = cfg.Accounts[0].Id;
        }
        else cfg.ActiveAccountId = "";
        cfg.SyncLegacyFields();
        return cfg;
    }

    static void MigrateLegacyActualCny(AppConfig cfg, JsonElement raw)
    {
        var legacy = UsageEvents.ClampActualCny(cfg.ActualCny);
        if (legacy <= 0) return;
        if (!raw.TryGetProperty("accounts", out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        var specified = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in arr.EnumerateArray())
        {
            if (item.TryGetProperty("actual_cny", out _) || item.TryGetProperty("actualCny", out _))
                specified.Add(Str(item, "id").Trim());
        }
        foreach (var acc in cfg.Accounts)
        {
            if (!specified.Contains(acc.Id) && acc.ActualCny <= 0)
                acc.ActualCny = legacy;
        }
    }

    static List<Account> ParseAccounts(JsonElement raw)
    {
        if (!raw.TryGetProperty("accounts", out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
        var list = new List<Account>();
        foreach (var item in arr.EnumerateArray())
        {
            var acc = Sanitize(item);
            if (acc is not null) list.Add(acc);
        }
        return list;
    }

    static Account? Sanitize(JsonElement raw)
    {
        var stored = Str(raw, "token");
        var decryptFailed = !TokenProtector.TryUnprotect(stored, out var tokenRaw);
        string token;
        try { token = decryptFailed ? "" : Token.Normalize(tokenRaw); } catch { token = tokenRaw.Trim(); }
        var id = Str(raw, "id").Trim();
        if (id.Length == 0 && token.Length > 0) id = Token.AccountId(token);
        if (id.Length == 0) return null;
        if (token.Length == 0 && !decryptFailed) return null;
        var acc = new Account { Id = id, Token = token };
        if (decryptFailed)
        {
            acc.TokenDecryptFailed = true;
            acc.StoredToken = stored;
            acc.LastError = TokenProtector.DecryptFailedMessage;
        }
        acc.Label = Str(raw, "label").Trim();
        acc.Email = CursorPasswordLogin.SanitizeEmail(Str(raw, "email"));
        var storedPassword = Str(raw, "password");
        if (storedPassword.Length > 0)
        {
            var passFailed = !TokenProtector.TryUnprotect(storedPassword, out var passwordRaw);
            if (passFailed)
            {
                acc.PasswordDecryptFailed = true;
                acc.StoredPassword = storedPassword;
            }
            else acc.Password = passwordRaw;
        }
        var membership = Str(raw, "membership_type");
        if (membership.Length == 0) membership = Str(raw, "membershipType");
        acc.MembershipType = membership.Trim();
        acc.AccountKind = AccountValidity.SanitizeKind(Str(raw, "account_kind"));
        acc.TempStartAt = Str(raw, "temp_start_at").Trim();
        acc.TempValidDays = AccountValidity.ClampDays(IntVal(raw, "temp_valid_days"));
        acc.TempValidHours = AccountValidity.ClampHours(IntVal(raw, "temp_valid_hours"));
        if (!decryptFailed) acc.LastError = Str(raw, "last_error");
        acc.UpdatedAt = Str(raw, "updated_at");
        acc.UsageUpdatedAt = Str(raw, "usage_updated_at").Trim();
        acc.SyncUpdatedAt = Str(raw, "sync_updated_at");
        if (raw.TryGetProperty("last_remaining", out var lr) && lr.ValueKind is JsonValueKind.Number)
            acc.LastRemaining = Numbers.Round2(lr.GetDouble());
        acc.AlertNotifiedLevels = ParseIntList(raw.TryGetProperty("alert_notified_levels", out var an) ? an : default);
        acc.AuthErrorNotified = Bool(raw, "auth_error_notified", false);
        acc.ExhaustionNotified = Bool(raw, "exhaustion_notified", false);
        acc.LowQuotaNotified = Bool(raw, "low_quota_notified", false);
        if (raw.TryGetProperty("actual_cny", out _) || raw.TryGetProperty("actualCny", out _))
            acc.ActualCny = UsageEvents.ClampActualCny(DoubleVal(raw, "actual_cny", DoubleVal(raw, "actualCny", 0)));
        acc.Channel = UsageEvents.SanitizeChannel(Str(raw, "channel"));
        var cycleStart = Str(raw, "billing_cycle_start");
        if (cycleStart.Length == 0) cycleStart = Str(raw, "billingCycleStart");
        acc.BillingCycleStart = cycleStart.Trim();
        var cycleEnd = Str(raw, "billing_cycle_end");
        if (cycleEnd.Length == 0) cycleEnd = Str(raw, "billingCycleEnd");
        acc.BillingCycleEnd = cycleEnd.Trim();
        var reportStart = Str(raw, "report_start_date");
        if (reportStart.Length == 0) reportStart = Str(raw, "reportStartDate");
        acc.ReportStartDate = UsageEvents.SanitizeReportDate(reportStart);
        var reportEnd = Str(raw, "report_end_date");
        if (reportEnd.Length == 0) reportEnd = Str(raw, "reportEndDate");
        acc.ReportEndDate = UsageEvents.SanitizeReportDate(reportEnd);
        return acc;
    }

    static List<int> ParseIntList(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array) return [];
        return el.EnumerateArray().Select(x => x.TryGetInt32(out var n) ? n : (int?)null).Where(n => n is >= 1 and <= 100).Select(n => n!.Value).Distinct().OrderBy(x => x).ToList();
    }

    static void UnprotectField(JsonElement raw, string key, Action<string> ok, Action fail)
    {
        if (!raw.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String) return;
        var stored = v.GetString() ?? "";
        if (stored.Length == 0) return;
        if (TokenProtector.TryUnprotect(stored, out var plain)) ok(plain);
        else fail();
    }

    public static string NormalizeAppearance(string? raw)
    {
        var mode = (raw ?? "").Trim().ToLowerInvariant();
        return mode is "system" or "light" or "dark" ? mode : "system";
    }

    static bool Bool(JsonElement raw, string key, bool fallback) =>
        raw.TryGetProperty(key, out var v) ? v.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => fallback } : fallback;

    static string Str(JsonElement raw, string key, string fallback = "") =>
        raw.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

    static int? IntVal(JsonElement raw, string key)
    {
        if (!raw.TryGetProperty(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out n)) return n;
        return null;
    }

    static double DoubleVal(JsonElement raw, string key, double fallback)
    {
        if (!raw.TryGetProperty(key, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String
            && double.TryParse((v.GetString() ?? "").Replace("，", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            return d;
        return fallback;
    }

    static object ToDict(AppConfig cfg) => new
    {
        session_token = TokenProtector.DiskToken(cfg.SessionToken, cfg.StoredSessionToken, cfg.DecryptError && string.IsNullOrEmpty(cfg.SessionToken)),
        accounts = cfg.Accounts.Select(a => new Dictionary<string, object?>
        {
            ["id"] = a.Id,
            ["label"] = a.Label,
            ["email"] = a.Email,
            ["password"] = TokenProtector.DiskToken(a.Password, a.StoredPassword, a.PasswordDecryptFailed),
            ["token"] = TokenProtector.DiskToken(a.Token, a.StoredToken, a.TokenDecryptFailed),
            ["membership_type"] = a.MembershipType,
            ["account_kind"] = AccountValidity.SanitizeKind(a.AccountKind),
            ["temp_start_at"] = a.TempStartAt ?? "",
            ["temp_valid_days"] = a.TempValidDays,
            ["temp_valid_hours"] = a.TempValidHours,
            ["last_remaining"] = a.LastRemaining,
            ["last_error"] = a.LastError,
            ["updated_at"] = a.UpdatedAt,
            ["usage_updated_at"] = a.UsageUpdatedAt,
            ["sync_updated_at"] = a.SyncUpdatedAt,
            ["alert_notified_levels"] = a.AlertNotifiedLevels,
            ["auth_error_notified"] = a.AuthErrorNotified,
            ["exhaustion_notified"] = a.ExhaustionNotified,
            ["low_quota_notified"] = a.LowQuotaNotified,
            ["actual_cny"] = a.ActualCny,
            ["channel"] = a.Channel,
            ["billing_cycle_start"] = a.BillingCycleStart,
            ["billing_cycle_end"] = a.BillingCycleEnd,
            ["report_start_date"] = a.ReportStartDate,
            ["report_end_date"] = a.ReportEndDate,
        }).ToList(),
        active_account_id = cfg.ActiveAccountId,
        active_account_updated_at = cfg.ActiveAccountUpdatedAt,
        settings_field_updated_at = cfg.SettingsFieldUpdatedAt,
        refresh_interval_minutes = cfg.RefreshIntervalMinutes,
        low_quota_threshold = cfg.LowQuotaThreshold,
        alert_thresholds = cfg.AlertThresholds,
        notify_enabled = cfg.NotifyEnabled,
        notify_exhaustion_risk = cfg.NotifyExhaustionRisk,
        autostart_enabled = cfg.AutostartEnabled,
        auto_update_enabled = cfg.AutoUpdateEnabled,
        update_last_check_at = cfg.UpdateLastCheckAt,
        update_last_error = cfg.UpdateLastError,
        update_installed_sha = cfg.UpdateInstalledSha,
        update_installed_asset_id = cfg.UpdateInstalledAssetId,
        tray_display_mode = cfg.TrayDisplayMode,
        color_mode = NormalizeAppearance(cfg.ColorMode),
        monthly_plan_usd = cfg.MonthlyPlanUsd,
        actual_cny = cfg.ActualCny,
        usd_cny_rate = cfg.UsdCnyRate,
        low_quota_notified = cfg.LowQuotaNotified,
        auth_error_notified = cfg.AuthErrorNotified,
        alert_notified_levels = cfg.AlertNotifiedLevels,
        exhaustion_notified = cfg.ExhaustionNotified,
        sync_enabled = cfg.SyncEnabled,
        sync_secret = TokenProtector.DiskToken(cfg.SyncSecret, cfg.StoredSyncSecret, cfg.SyncSecretDecryptFailed && string.IsNullOrEmpty(cfg.SyncSecret)),
        sync_device_id = cfg.SyncDeviceId,
        sync_last_at = cfg.SyncLastAt,
        sync_last_error = cfg.SyncLastError,
        cloud_email = cfg.CloudEmail,
        cloud_access_token = TokenProtector.DiskToken(cfg.CloudAccessToken, cfg.StoredCloudAccessToken, cfg.CloudAccessDecryptFailed && string.IsNullOrEmpty(cfg.CloudAccessToken)),
        cloud_refresh_token = TokenProtector.DiskToken(cfg.CloudRefreshToken, cfg.StoredCloudRefreshToken, cfg.CloudRefreshDecryptFailed && string.IsNullOrEmpty(cfg.CloudRefreshToken)),
        cloud_revision = cfg.CloudRevision,
        deleted_accounts = cfg.DeletedAccounts.Select(d => new Dictionary<string, object?>
        {
            ["id"] = d.Id,
            ["deleted_at"] = d.DeletedAt,
        }).ToList(),
    };

    static Dictionary<string, string> ParseFieldUpdatedAt(JsonElement raw)
    {
        if (!raw.TryGetProperty("settings_field_updated_at", out var obj) || obj.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, string>(StringComparer.Ordinal);
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in obj.EnumerateObject())
            rows[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : "";
        return AccountSync.SanitizeFieldUpdatedAt(rows);
    }

    static List<DeletedAccount> ParseDeleted(JsonElement raw)
    {
        if (!raw.TryGetProperty("deleted_accounts", out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
        var rows = new List<DeletedAccount>();
        foreach (var item in arr.EnumerateArray())
            rows.Add(new DeletedAccount { Id = Str(item, "id").Trim(), DeletedAt = Str(item, "deleted_at").Trim() });
        return AccountSync.SanitizeDeleted(rows);
    }
}
