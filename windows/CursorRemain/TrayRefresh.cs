using CursorTokenCore;

namespace CursorRemain;

sealed partial class TrayContext
{
    async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshAll();
                _ = TryReconcileAsync(save: true);
                TrimProcessMemory();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { CrashLog.Write(ex); }
            if (ct.IsCancellationRequested) break;
            if (_refreshNow)
            {
                _refreshNow = false;
                continue;
            }
            var minutes = Math.Max(1, _config.RefreshIntervalMinutes);
            var delay = TimeSpan.FromSeconds(Math.Max(60, minutes * 60));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _delayCts = linked;
            try { await Task.Delay(delay, linked.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            finally
            {
                if (ReferenceEquals(_delayCts, linked)) _delayCts = null;
            }
            _refreshNow = false;
        }
    }

    sealed record RefreshTarget(string Id, string Token, string Provider, bool TokenDecryptFailed);
    sealed record RefreshOutcome(string Id, string Provider, UsageSnapshot? Snap, string? Error, bool AuthError, string Stamp);

    /// <summary>Doubao refreshes the quota about every 5 minutes; manual refresh ignores this.</summary>
    static readonly TimeSpan DoubaoMinInterval = TimeSpan.FromMinutes(5);
    readonly DoubaoClient _doubaoClient = new();
    readonly Dictionary<string, DateTimeOffset> _doubaoNextFetch = new(StringComparer.Ordinal);
    /// <summary>真实用户名只拉一次；失败后不跟着每轮额度刷新反复拉整页 HTML。</summary>
    readonly HashSet<string> _doubaoNameTried = new(StringComparer.Ordinal);
    UsageSnapshot? _doubaoUsage;
    string? _doubaoError;
    string? _doubaoUpdated;
    string? _doubaoOutcomeId;

    async Task RefreshAll()
    {
        var generation = Interlocked.Increment(ref _refreshGeneration);
        var forced = _refreshNow;
        var allTargets = _config.Accounts
            .Select(a => new RefreshTarget(a.Id, a.Token, a.Provider, a.TokenDecryptFailed))
            .ToList();
        if (allTargets.Count == 0)
        {
            _usage = null;
            _error = "未配置 Token，请打开设置粘贴";
            _updated = null;
            _doubaoUsage = null;
            _doubaoError = null;
            _doubaoUpdated = null;
            UpdateUi();
            return;
        }
        var activeId = _config.ActiveAccountId;
        var cursorTargets = allTargets.Where(a => a.Provider != AccountProvider.Doubao).ToList();
        var doubaoTargets = allTargets.Where(a => a.Provider == AccountProvider.Doubao)
            .Where(a => forced || DoubaoFetchAllowed(_doubaoNextFetch, a.Id))
            .ToList();
        var activeTarget = cursorTargets.FirstOrDefault(a => a.Id == activeId);
        var others = cursorTargets.Where(a => a.Id != activeId).ToList();

        var outcomes = new List<RefreshOutcome>();
        if (activeTarget is { Id: { Length: > 0 } })
        {
            var activeOutcome = await FetchOne(activeTarget);
            ApplyActiveOutcome(activeOutcome, generation);
            outcomes.Add(activeOutcome);
        }

        if (others.Count > 0)
        {
            var otherOutcomes = await BoundedWork.MapAsync(others, async acc =>
            {
                var o = await FetchOne(acc);
                ApplyActiveOutcome(o, generation);
                return o;
            });
            outcomes.AddRange(otherOutcomes);
        }

        if (doubaoTargets.Count > 0)
        {
            var doubaoOutcomes = await BoundedWork.MapAsync(doubaoTargets, async acc => await FetchOne(acc));
            outcomes.AddRange(doubaoOutcomes);
            var now = DateTimeOffset.UtcNow;
            var interval = DoubaoMinInterval > TimeSpan.FromMinutes(Math.Max(1, _config.RefreshIntervalMinutes))
                ? DoubaoMinInterval
                : TimeSpan.FromMinutes(Math.Max(1, _config.RefreshIntervalMinutes));
            foreach (var o in doubaoOutcomes) _doubaoNextFetch[o.Id] = now + interval;
        }

        var notices = new List<(string Title, string Body, bool Warn)>();
        // 账号还没有名字时补一次真实用户名（豆包昵称在 /chat 页面的 SSR 数据里）。
        var doubaoNameFills = await FetchMissingDoubaoNames();
        // 内存当前账号集合是结构权威：后台刷新从磁盘重建副本时，不能把本轮之前已删除的账号带回。
        var authoritativeIds = _config.Accounts.Select(a => a.Id).ToList();
        AppConfig cfg;
        try
        {
            cfg = ConfigStore.Update(live =>
            {
                foreach (var o in outcomes)
                {
                    var acc = live.Accounts.FirstOrDefault(a => a.Id == o.Id);
                    if (acc is null) continue;
                    if (acc.IsDoubao)
                    {
                        if (o.Snap is { } doubaoSnap)
                        {
                            live.ApplySnapshot(o.Id, doubaoSnap.MembershipType, doubaoSnap.RemainingPercent, "", o.Stamp);
                            acc.AuthErrorNotified = false;
                            // No billing cycle for Doubao, so exhaustion-risk alerts stay off this release.
                            foreach (var n in AlertLogic.Evaluate(live, acc, doubaoSnap, exhaustionRisk: false))
                                notices.Add((n.Title, n.Body, false));
                        }
                        else if (o.Error is not null)
                        {
                            live.ApplySnapshot(o.Id, error: o.Error, updatedAt: o.Stamp);
                            if (o.AuthError && !acc.AuthErrorNotified)
                            {
                                acc.AuthErrorNotified = true;
                                if (live.NotifyEnabled)
                                {
                                    var body = string.IsNullOrEmpty(acc.DisplayLabel) ? o.Error : $"账号「{acc.DisplayLabel}」：{o.Error}";
                                    notices.Add(("豆包登录已失效", body, true));
                                }
                            }
                        }
                        continue;
                    }
                    if (o.Snap is { } snap)
                    {
                        AccountValidity.ApplyEndOverride(snap, acc);
                        live.ApplySnapshot(o.Id, snap.MembershipType, snap.RemainingPercent, "", o.Stamp, snap.BillingCycleStart, AccountValidity.StoredCycleEnd(snap));
                        acc.AuthErrorNotified = false;
                        foreach (var n in AlertLogic.Evaluate(live, acc, snap))
                            notices.Add((n.Title, n.Body, false));
                    }
                    else if (o.Error is not null)
                    {
                        live.ApplySnapshot(o.Id, error: o.Error, updatedAt: o.Stamp);
                        if (o.AuthError && !acc.AuthErrorNotified)
                        {
                            acc.AuthErrorNotified = true;
                            if (live.NotifyEnabled)
                            {
                                var body = string.IsNullOrEmpty(acc.DisplayLabel) ? o.Error : $"账号「{acc.DisplayLabel}」：{o.Error}";
                                notices.Add(("Token 需要更新", body, true));
                            }
                        }
                    }
                }
                live.SyncLegacyFields();
                foreach (var (id, name) in doubaoNameFills)
                {
                    var acc = live.Accounts.FirstOrDefault(a => a.Id == id && a.IsDoubao);
                    if (acc is not null && string.IsNullOrWhiteSpace(acc.Label)) acc.Label = name;
                }
            }, liveAccountIds: authoritativeIds);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            cfg = _config;
        }

        _config = cfg;
        var active = outcomes.FirstOrDefault(o => o.Id == cfg.ActiveAccountId && o.Provider != AccountProvider.Doubao);
        if (active is not null) ApplyActiveOutcome(active, generation);
        else if (cfg.CursorAccounts.Count == 0)
        {
            _usage = null;
            _error = cfg.HasAnyAccount ? "未添加 Cursor 账号" : "未配置 Token，请打开设置粘贴";
            _updated = null;
        }
        var doubao = outcomes.FirstOrDefault(o => o.Id == cfg.ActiveDoubaoAccount?.Id);
        if (doubao is not null) ApplyDoubaoOutcome(doubao);
        else if (cfg.ActiveDoubaoAccount is null) { _doubaoUsage = null; _doubaoError = null; _doubaoUpdated = null; }
        UpdateUi();
        OnUi(() =>
        {
            foreach (var n in notices)
                _icon.ShowBalloonTip(n.Warn ? 5000 : 4000, n.Title, n.Body, n.Warn ? ToolTipIcon.Warning : ToolTipIcon.Info);
            if (_settings is { IsDisposed: false }) _settings.FinishAccountCheck(!_refreshNow);
        });
    }

    /// <summary>豆包账号真实用户名：从 `/chat` 页面的 SSR 数据里补，每个账号只试一次。</summary>
    async Task<Dictionary<string, string>> FetchMissingDoubaoNames()
    {
        var fills = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var acc in _config.DoubaoAccounts)
        {
            if (!string.IsNullOrWhiteSpace(acc.Label)) continue;
            if (acc.TokenDecryptFailed || string.IsNullOrWhiteSpace(acc.Token)) continue;
            if (!_doubaoNameTried.Add(acc.Id)) continue;
            try
            {
                var name = await _doubaoClient.FetchAccountName(acc.Token, 20);
                if (!string.IsNullOrWhiteSpace(name)) fills[acc.Id] = name.Trim();
            }
            catch
            {
                // 名字只是展示项，失败保持套餐名兜底，不打扰用户。
            }
        }
        return fills;
    }

    void ApplyActiveOutcome(RefreshOutcome o, long generation)
    {
        if (o.Provider == AccountProvider.Doubao) return;
        if (!RefreshGeneration.ShouldApply(o.Id, _config.ActiveAccountId, generation, Volatile.Read(ref _refreshGeneration)))
            return;
        if (o.Snap is { } snap) { _usage = snap; _error = null; _updated = o.Stamp; }
        else if (o.Error is not null) { _usage = null; _error = o.Error; _updated = o.Stamp; }
        UpdateUi();
    }

    static bool DoubaoFetchAllowed(Dictionary<string, DateTimeOffset> next, string id) =>
        !next.TryGetValue(id, out var at) || DateTimeOffset.UtcNow >= at;

    // Only the active Doubao account's own result may paint the flyout block.
    UsageSnapshot? CurrentDoubaoUsage => _doubaoOutcomeId == _config.ActiveDoubaoAccountId ? _doubaoUsage : null;
    string? CurrentDoubaoError => _doubaoOutcomeId == _config.ActiveDoubaoAccountId ? _doubaoError : null;
    string? CurrentDoubaoUpdated => _doubaoOutcomeId == _config.ActiveDoubaoAccountId ? _doubaoUpdated : null;

    void ApplyDoubaoOutcome(RefreshOutcome o)
    {
        _doubaoOutcomeId = o.Id;
        if (o.Snap is { } snap) { _doubaoUsage = snap; _doubaoError = null; }
        else if (o.Error is not null) { _doubaoUsage = null; _doubaoError = o.Error; }
        _doubaoUpdated = o.Stamp;
    }

    async Task<RefreshOutcome> FetchOne(RefreshTarget acc)
    {
        var stamp = AccountSync.NowIso();
        if (acc.TokenDecryptFailed)
            return new RefreshOutcome(acc.Id, acc.Provider, null, TokenProtector.DecryptFailedMessage, false, stamp);
        if (string.IsNullOrWhiteSpace(acc.Token))
            return new RefreshOutcome(acc.Id, acc.Provider, null,
                acc.Provider == AccountProvider.Doubao ? "未配置豆包 Cookie，请重新登录" : "未配置 Token，请打开设置粘贴",
                false, stamp);
        if (acc.Provider == AccountProvider.Doubao)
        {
            try
            {
                var snap = await _doubaoClient.FetchQuota(acc.Token, 20);
                UsageHistory.Append(snap.RemainingPercent, snap.AutoPercentUsed, snap.ApiPercentUsed, accountId: acc.Id);
                return new RefreshOutcome(acc.Id, acc.Provider, snap, null, false, stamp);
            }
            catch (CursorApiException err)
            {
                return new RefreshOutcome(acc.Id, acc.Provider, null, err.Message, err.IsAuthError, stamp);
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                return new RefreshOutcome(acc.Id, acc.Provider, null, "刷新失败：" + ex.Message, false, stamp);
            }
        }
        try
        {
            var snap = await _client.FetchUsageSummary(acc.Token, 20);
            UsageHistory.Append(snap.RemainingPercent, snap.AutoPercentUsed, snap.ApiPercentUsed, accountId: acc.Id);
            return new RefreshOutcome(acc.Id, acc.Provider, snap, null, false, stamp);
        }
        catch (CursorApiException err)
        {
            return new RefreshOutcome(acc.Id, acc.Provider, null, err.Message, err.IsAuthError, stamp);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            return new RefreshOutcome(acc.Id, acc.Provider, null, "刷新失败：" + ex.Message, false, stamp);
        }
    }
}
