using CursorTokenCore;
using Xunit;

namespace CursorTokenCore.Tests;

/// <summary>删除账号后的持久化与后台刷新回灌回归。</summary>
public class DeletePersistTests
{
    static string NewDir() => Path.Combine(Path.GetTempPath(), "cursorremain-del-" + Guid.NewGuid().ToString("N"));

    static AppConfig Seed(string dir)
    {
        var cfg = new AppConfig();
        cfg.Accounts.Add(new Account { Id = "user_C1", Provider = AccountProvider.Cursor, Token = "tok-c1" });
        cfg.UpsertDoubaoAccount("sessionid=aaa; uid_tt=111", existingId: "doubao_D1", activate: true);
        cfg.UpsertDoubaoAccount("sessionid=bbb; uid_tt=222", existingId: "doubao_D2", activate: false);
        ConfigStore.Save(cfg, dir);
        return cfg;
    }

    [Fact]
    public void DeletedDoubaoStaysGoneAfterDiskRoundTrip()
    {
        var dir = NewDir();
        try
        {
            Seed(dir);
            var form = ConfigStore.Load(dir);

            Assert.True(form.RemoveAccount("doubao_D1", AccountProvider.Doubao));
            ConfigStore.Save(form, dir);

            var reloaded = ConfigStore.Load(dir);
            Assert.DoesNotContain(reloaded.Accounts, a => a.Id == "doubao_D1");
            Assert.Contains(reloaded.Accounts, a => a.Id == "doubao_D2");
            Assert.Equal("doubao_D2", reloaded.ActiveDoubaoAccountId);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void BackgroundUpdateDoesNotResurrectDeletedDoubao()
    {
        var dir = NewDir();
        try
        {
            Seed(dir);
            // 设置表单在内存实例上删除，但落盘尚未完成（异步 Save 排队中）。
            var form = ConfigStore.Load(dir);
            Assert.True(form.RemoveAccount("doubao_D1", AccountProvider.Doubao));

            // 后台一轮刷新：从磁盘重读、只套本次 outcome（不含 D1），再写回。
            // 传入内存当前账号集合作为结构权威，磁盘旧副本里残留的 D1 不得回写。
            var authoritativeIds = form.Accounts.Select(a => a.Id).ToList();
            _ = ConfigStore.Update(live => { /* outcome 只更新现有账号字段，不做结构变更 */ }, dir, authoritativeIds);

            var reloaded = ConfigStore.Load(dir);
            Assert.DoesNotContain(reloaded.Accounts, a => a.Id == "doubao_D1");
            Assert.Contains(reloaded.Accounts, a => a.Id == "doubao_D2");
            Assert.Equal("doubao_D2", reloaded.ActiveDoubaoAccountId);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void PrunedCursorAccountIsTombstonedAndActiveFallsBack()
    {
        var dir = NewDir();
        try
        {
            var cfg = new AppConfig();
            cfg.Accounts.Add(new Account { Id = "user_C1", Provider = AccountProvider.Cursor, Token = "tok-c1" });
            cfg.Accounts.Add(new Account { Id = "user_C2", Provider = AccountProvider.Cursor, Token = "tok-c2" });
            cfg.SetActiveAccount("user_C2");
            ConfigStore.Save(cfg, dir);

            var form = ConfigStore.Load(dir);
            Assert.True(form.RemoveAccount("user_C2", AccountProvider.Cursor));

            var authoritativeIds = form.Accounts.Select(a => a.Id).ToList();
            _ = ConfigStore.Update(live => { }, dir, authoritativeIds);

            var reloaded = ConfigStore.Load(dir);
            Assert.DoesNotContain(reloaded.Accounts, a => a.Id == "user_C2");
            Assert.Contains(reloaded.DeletedAccounts, d => d.Id == "user_C2");
            Assert.Equal("user_C1", reloaded.ActiveAccountId);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
