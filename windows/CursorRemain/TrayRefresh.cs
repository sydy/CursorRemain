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

    sealed record RefreshOutcome(string Id, UsageSnapshot? Snap, string? Error, bool AuthError, string Stamp);

    async Task RefreshAll()
    {
        var generation = Interlocked.Increment(ref _refreshGeneration);
        var targets = _config.Accounts.Select(a => (a.Id, a.Token, a.TokenDecryptFailed)).ToList();
        if (targets.Count == 0)
        {
            _usage = null;
            _error = "未配置 Token，请打开设置粘贴";
            _updated = null;
            UpdateUi();
            return;
        }
        var activeId = _config.ActiveAccountId;
        var ordered = targets.OrderBy(a => a.Id == activeId ? 0 : 1).ToList();
        var outcomes = new List<RefreshOutcome>(ordered.Count);
        var remaining = ordered.Select(async acc =>
        {
            var o = await FetchOne(acc);
            ApplyActiveOutcome(o, generation);
            return o;
        });
        outcomes.AddRange(await Task.WhenAll(remaining));

        var notices = new List<(string Title, string Body, bool Warn)>();
        AppConfig cfg;
        try
        {
            cfg = ConfigStore.Update(live =>
            {
                foreach (var o in outcomes)
                {
                    var acc = live.Accounts.FirstOrDefault(a => a.Id == o.Id);
                    if (acc is null) continue;
                    if (o.Snap is { } snap)
                    {
                        AccountValidity.ApplyEndOverride(snap, acc);
                        live.ApplySnapshot(o.Id, snap.MembershipType, snap.RemainingPercent, "", o.Stamp, snap.BillingCycleStart, snap.BillingCycleEnd);
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
            });
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            cfg = _config;
        }

        _config = cfg;
        var active = outcomes.FirstOrDefault(o => o.Id == cfg.ActiveAccountId);
        if (active is not null) ApplyActiveOutcome(active, generation);
        else if (cfg.Accounts.Count == 0) { _usage = null; _error = "未配置 Token，请打开设置粘贴"; _updated = null; UpdateUi(); }
        else UpdateUi();
        OnUi(() =>
        {
            foreach (var n in notices)
                _icon.ShowBalloonTip(n.Warn ? 5000 : 4000, n.Title, n.Body, n.Warn ? ToolTipIcon.Warning : ToolTipIcon.Info);
        });
    }

    void ApplyActiveOutcome(RefreshOutcome o, long generation)
    {
        if (!RefreshGeneration.ShouldApply(o.Id, _config.ActiveAccountId, generation, Volatile.Read(ref _refreshGeneration)))
            return;
        if (o.Snap is { } snap) { _usage = snap; _error = null; _updated = o.Stamp; }
        else if (o.Error is not null) { _usage = null; _error = o.Error; _updated = o.Stamp; }
        UpdateUi();
    }

    async Task<RefreshOutcome> FetchOne((string Id, string Token, bool TokenDecryptFailed) acc)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        if (acc.TokenDecryptFailed)
            return new RefreshOutcome(acc.Id, null, TokenProtector.DecryptFailedMessage, false, stamp);
        if (string.IsNullOrWhiteSpace(acc.Token))
            return new RefreshOutcome(acc.Id, null, "未配置 Token，请打开设置粘贴", false, stamp);
        try
        {
            var snap = await _client.FetchUsageSummary(acc.Token, 20);
            UsageHistory.Append(snap.RemainingPercent, snap.AutoPercentUsed, snap.ApiPercentUsed, accountId: acc.Id);
            return new RefreshOutcome(acc.Id, snap, null, false, stamp);
        }
        catch (CursorApiException err)
        {
            return new RefreshOutcome(acc.Id, null, err.Message, err.IsAuthError, stamp);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            return new RefreshOutcome(acc.Id, null, "刷新失败: " + ex.Message, false, stamp);
        }
    }
}
