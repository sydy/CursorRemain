using System.Runtime.InteropServices;
using CursorTokenCore;

namespace CursorRemain;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        UiChrome.Install();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => CrashLog.Write(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) CrashLog.Write(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write(e.Exception);
            e.SetObserved();
        };

        using var mutex = new Mutex(true, @"Local\CursorRemain_SingleInstance_v2", out var created);
        if (!created)
        {
            MessageBox.Show("余量已经在托盘运行。", "已在后台运行", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Application.Run(new TrayContext());
    }
}

static class AppWindow
{
    public static Icon? CreateIcon()
    {
        try
        {
            var path = Environment.ProcessPath ?? Application.ExecutablePath;
            if (string.IsNullOrEmpty(path)) return null;
            return Icon.ExtractAssociatedIcon(path);
        }
        catch
        {
            return null;
        }
    }
}

sealed class HiddenSyncForm : Form
{
    public HiddenSyncForm()
    {
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        Size = new Size(1, 1);
        Opacity = 0;
        ShowIcon = false;
        Text = "";
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExToolwindow = 0x00000080;
            const int WsExNoActivate = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolwindow | WsExNoActivate;
            return cp;
        }
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!IsHandleCreated) CreateHandle();
        base.SetVisibleCore(false);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }
}

sealed partial class TrayContext : ApplicationContext
{
    readonly HiddenSyncForm _sync;
    readonly NotifyIcon _icon;
    readonly ContextMenuStrip _menu;
    readonly ToolStripMenuItem _dashboardItem;
    readonly ToolStripMenuItem _switcher;
    readonly CursorClient _client = new();
    AppConfig _config;
    UsageSnapshot? _usage;
    string? _error;
    string? _updated;
    SettingsForm? _settings;
    FlyoutForm? _flyout;
    ReportForm? _report;
    CompareForm? _compare;
    CancellationTokenSource _cts = new();
    CancellationTokenSource? _delayCts;
    readonly SemaphoreSlim _reconcileGate = new(1, 1);
    bool _reconcileAgain;
    bool _refreshNow;
    long _refreshGeneration;
    (int? Remaining, bool Error, string Mode, int Size)? _iconKey;
    string _lastCloudNotify = "";

    public TrayContext()
    {
        _config = ConfigStore.Load();
        if (_config.SyncEnabled)
            _ = TryReconcileAsync(save: true);
        Autostart.Apply(_config.AutostartEnabled);
        _sync = new HiddenSyncForm();
        _ = _sync.Handle;
        _menu = new ContextMenuStrip();
        _dashboardItem = new ToolStripMenuItem();
        _switcher = new ToolStripMenuItem("切换账号");
        BuildMenu();
        _icon = new NotifyIcon
        {
            Visible = true,
            Text = AppPaths.DisplayName,
            Icon = IconRenderer.Make(null, false, _config.TrayDisplayMode),
            ContextMenuStrip = _menu,
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowFlyout(Cursor.Position);
        };
        _sync.DpiChanged += (_, _) =>
        {
            _iconKey = null;
            UpdateUi();
        };
        _sync.BeginInvoke(() =>
        {
            if (string.IsNullOrEmpty(_config.SessionToken))
                OpenSettings(true, false);
            _ = LoopAsync(_cts.Token);
            _ = UpdateLoopAsync(_cts.Token);
        });
    }

    void OnUi(Action action)
    {
        if (_sync.IsDisposed) return;
        if (_sync.InvokeRequired)
        {
            try { _sync.BeginInvoke(action); } catch (ObjectDisposedException) { }
            return;
        }
        action();
    }

    void BuildMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add("显示状态", null, (_, _) => ShowFlyout(Cursor.Position));
        _menu.Items.Add("立即刷新", null, (_, _) => RequestRefresh());
        _dashboardItem.Text = UsageParser.DashboardMenuLabel(_usage);
        _dashboardItem.Click -= DashboardClick;
        _dashboardItem.Click += DashboardClick;
        _menu.Items.Add(_dashboardItem);
        _menu.Items.Add("用量报表…", null, (_, _) => OpenReport());
        _menu.Items.Add("账号对比…", null, (_, _) => OpenCompare());
        _menu.Items.Add(_switcher);
        _menu.Items.Add("在 Cursor 登录当前账号…", null, (_, _) => LoginToCursor());
        _menu.Items.Add("导入 Token…", null, (_, _) => OpenSettings(true, true));
        _menu.Items.Add("设置…", null, (_, _) => OpenSettings(false, false));
        _menu.Items.Add("检查更新…", null, (_, _) => _ = CheckUpdateAsync(true));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => Exit());
        _menu.Opening -= MenuOpening;
        _menu.Opening += MenuOpening;
        RefreshAccountMenu();
    }

    void DashboardClick(object? sender, EventArgs e) => OpenDashboard();

    void MenuOpening(object? sender, EventArgs e)
    {
        if (_sync.IsHandleCreated) SetForegroundWindow(_sync.Handle);
    }

    void RefreshAccountMenu()
    {
        if (_switcher.DropDown.Visible) return;
        _switcher.DropDownItems.Clear();
        if (_config.Accounts.Count == 0)
            _switcher.DropDownItems.Add(new ToolStripMenuItem("暂无账号") { Enabled = false });
        else
        {
            foreach (var acc in _config.Accounts)
            {
                var live = acc.Id == _config.ActiveAccountId ? _usage?.RemainingPercent : null;
                var title = StatusText.FormatAccountMenuTitle(acc.DisplayLabel, live ?? acc.LastRemaining);
                var item = new ToolStripMenuItem(title) { Checked = acc.Id == _config.ActiveAccountId, Tag = acc.Id };
                item.Click += (_, _) =>
                {
                    if (item.Tag is string id) { _config.SetActiveAccount(id); ApplyConfig(_config, true); }
                };
                _switcher.DropDownItems.Add(item);
            }
        }
    }

    void RequestRefresh()
    {
        Interlocked.Increment(ref _refreshGeneration);
        _refreshNow = true;
        try { _delayCts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    void UpdateUi()
    {
        OnUi(() =>
        {
            try { UpdateUiCore(); }
            catch (Exception ex) { CrashLog.Write(ex); }
        });
    }

    void UpdateUiCore()
    {
        var remaining = _error is not null && !_error.StartsWith("未配置") ? (double?)null : _usage?.RemainingPercent;
        var error = _error is not null && !_error.StartsWith("未配置");
        var mode = _config.TrayDisplayMode;
        var bucket = remaining is null ? (int?)null : (int)Math.Round(remaining.Value);
        var size = IconRenderer.PixelSize();
        var iconKey = (bucket, error, mode, size);
        if (_iconKey != iconKey)
        {
            _iconKey = iconKey;
            var old = _icon.Icon;
            _icon.Icon = IconRenderer.Make(remaining, error, mode, size);
            old?.Dispose();
        }
        var label = _config.ActiveAccount?.DisplayLabel ?? "";
        var tip = error ? (_error ?? "异常")
            : remaining is { } r ? (string.IsNullOrEmpty(label) ? $"{r:0}%" : $"{label} · {r:0}%")
            : AppPaths.DisplayName;
        if (tip.Length > 63) tip = tip[..63];
        _icon.Text = tip;
        _dashboardItem.Text = UsageParser.DashboardMenuLabel(_usage);
        if (!_menu.Visible) RefreshAccountMenu();
        if (_flyout is { Visible: true, IsDisposed: false })
        {
            var hist = UsageHistory.LoadRecent(7, _config.ActiveAccount?.Id);
            _flyout.Render(_usage, _error, _updated, _config, hist, UsageHistory.DailyAvgBurn(hist));
        }
    }

    void ShowFlyout(Point? anchor = null)
    {
        OnUi(() =>
        {
            try
            {
                if (_flyout is { IsDisposed: true }) _flyout = null;
                _flyout ??= new FlyoutForm(
                    () =>
                    {
                        try { _icon.ShowBalloonTip(1, " ", " ", ToolTipIcon.None); } catch { }
                    },
                    RequestRefresh,
                    OpenDashboard,
                    () => OpenSettings(Token.IsAuthErrorMessage(_error), false),
                    () =>
                    {
                        try
                        {
                            var text = StatusText.FormatSummary(_usage, _error, _updated, _config.ActiveAccount?.DisplayLabel);
                            Clipboard.SetText(text);
                        }
                        catch { }
                    },
                    OpenReport,
                    OpenCompare);
                var hist = UsageHistory.LoadRecent(7, _config.ActiveAccount?.Id);
                _flyout.Render(_usage, _error, _updated, _config, hist, UsageHistory.DailyAvgBurn(hist));
                _flyout.PopupNear(anchor ?? Cursor.Position);
            }
            catch (Exception ex) { CrashLog.Write(ex); }
        });
    }

    void OpenDashboard()
    {
        var url = UsageParser.DashboardUrl(_usage);
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    async void LoginToCursor()
    {
        try
        {
            var result = await CursorLoginUi.Run(_config.ActiveAccount, _settings is { IsDisposed: false } s ? s : null);
            if (result.Message == "已取消") return;
            OnUi(() => _icon.ShowBalloonTip(
                5000,
                result.Ok ? "已写入 Cursor" : "未能登录 Cursor",
                result.Message,
                result.Ok ? ToolTipIcon.Info : ToolTipIcon.Warning));
        }
        catch (Exception ex) { CrashLog.Write(ex); }
    }

    void OpenCompare()
    {
        OnUi(() =>
        {
            try
            {
                _flyout?.Hide();
                if (_compare is { IsDisposed: false })
                {
                    _compare.Reload();
                    _compare.Show();
                    _compare.Activate();
                    return;
                }
                _compare = new CompareForm(_client, () => new CompareForm.CompareState(
                    _config.Accounts.ToList(),
                    _config.ActiveAccountId,
                    _config.MonthlyPlanUsd,
                    _config.UsdCnyRate,
                    (id, membership, start, end) =>
                    {
                        _config = ConfigStore.Update(live =>
                        {
                            live.ApplySnapshot(id, membershipType: membership, billingCycleStart: start, billingCycleEnd: end);
                        });
                    }));
                _compare.FormClosed += (_, _) => _compare = null;
                _compare.Show();
            }
            catch (Exception ex) { CrashLog.Write(ex); }
        });
    }

    void OpenReport()
    {
        OnUi(() =>
        {
            try
            {
                _flyout?.Hide();
                if (_report is { IsDisposed: false })
                {
                    _report.Show();
                    _report.Activate();
                    _report.RequestSync();
                    return;
                }
                _report = new ReportForm(_client, () =>
                {
                    var usage = _usage;
                    return new ReportForm.ReportState(
                        _config.ActiveAccount?.Token ?? _config.SessionToken,
                        _config.ActiveAccountId,
                        usage,
                        usage?.IsTeamAccount == true,
                        _config.SpendSettings(usage?.MembershipType),
                        _config.ActiveAccount?.ReportStartDate ?? "",
                        _config.ActiveAccount?.ReportEndDate ?? "",
                        ReportAllocationWindow.FromAccount(_config.ActiveAccount, usage),
                        _config.ActiveAccount?.DisplayLabel ?? "");
                }, (id, start, end) =>
                {
                    _config = ConfigStore.Update(live => live.SetReportRange(id, start, end));
                });
                _report.FormClosed += (_, _) => _report = null;
                _report.Show();
            }
            catch (Exception ex) { CrashLog.Write(ex); }
        });
    }

    public void OpenSettings(bool focusToken, bool startImport)
    {
        OnUi(() =>
        {
            try
            {
                _flyout?.Hide();
                if (_settings is { IsDisposed: false })
                {
                    _settings.Show();
                    _settings.Activate();
                    if (focusToken) _settings.FocusToken();
                    if (startImport) _settings.StartImport();
                    return;
                }
                _settings = new SettingsForm(_config, cfg => ApplyConfig(cfg, true), async prefer =>
                {
                    return await SessionImporter.ImportAndValidate(_client, SessionImporter.DefaultPreferBrowsers(), SessionImporter.OnlyBrowsers(prefer), _config.ExistingTokenVariants());
                }, startImport, CheckUpdateAsync);
                _settings.FormClosed += (_, _) => _settings = null;
                _settings.Show();
                if (focusToken) _settings.FocusToken();
            }
            catch (Exception ex) { CrashLog.Write(ex); }
        });
    }

    public void ApplyConfig(AppConfig cfg, bool refresh)
    {
        var prevAuto = _config.AutostartEnabled;
        _config = cfg;
        if (prevAuto != cfg.AutostartEnabled) Autostart.Apply(cfg.AutostartEnabled);
        if (refresh) RequestRefresh();
        UpdateUi();
        if (_report is { IsDisposed: false }) _report.RequestSync();
        if (_compare is { IsDisposed: false }) _compare.Reload();
        _ = PersistAndSyncAsync(cfg);
    }

    async Task PersistAndSyncAsync(AppConfig cfg)
    {
        try { await Task.Run(() => ConfigStore.Save(cfg)); }
        catch (Exception)
        {
            OnUi(() => _icon.ShowBalloonTip(4000, "保存失败", "无法写入配置（文件忙碌或加密失败），请稍后再试。", ToolTipIcon.Warning));
            return;
        }
        await TryReconcileAsync(save: true);
    }

    async Task TryReconcileAsync(bool save)
    {
        if (!_config.SyncEnabled) return;
        if (!await _reconcileGate.WaitAsync(0))
        {
            _reconcileAgain = true;
            return;
        }
        try
        {
            do
            {
                _reconcileAgain = false;
                var status = await CloudSync.ReconcileAsync(_config);
                if (save || status.Changed)
                {
                    try { await Task.Run(() => ConfigStore.Save(_config)); }
                    catch (Exception ex) { CrashLog.Write(ex); }
                }
                NotifyCloudSync(status);
                if (status.Changed) RequestRefresh();
            } while (_reconcileAgain && _config.SyncEnabled);
        }
        catch (Exception ex) { CrashLog.Write(ex); }
        finally { _reconcileGate.Release(); }
    }

    async Task UpdateLoopAsync(CancellationToken ct)
    {
        try { await Task.Delay(AppUpdate.StartupDelay, ct); }
        catch (OperationCanceledException) { return; }
        await CheckUpdateAsync(false);
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(AppUpdate.AutoCheckInterval, ct); }
            catch (OperationCanceledException) { return; }
            await CheckUpdateAsync(false);
        }
    }

    async Task<string> CheckUpdateAsync(bool manual)
    {
        try
        {
            var status = await AppUpdater.RunAsync(
                _config,
                manual,
                manual ? ConfirmUpdate : null,
                Exit,
                _cts.Token);
            try { _config = ConfigStore.Load(); }
            catch (Exception ex) { CrashLog.Write(ex); }
            OnUi(() =>
            {
                _settings?.SetUpdateStatus(status);
                if (!manual && status.Contains("发现新版本", StringComparison.Ordinal))
                    _icon.ShowBalloonTip(5000, "自动更新", status, ToolTipIcon.Info);
                else if (manual && status.StartsWith("检查更新失败", StringComparison.Ordinal))
                    _icon.ShowBalloonTip(5000, "检查更新", status, ToolTipIcon.Warning);
            });
            return status;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            return "检查更新失败: " + ex.Message;
        }
    }

    bool ConfirmUpdate(string message)
    {
        bool Ask() =>
            MessageBox.Show(
                message + "。安装后会自动重启。",
                "安装更新",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information) == DialogResult.OK;
        if (_sync.IsDisposed) return Ask();
        if (_sync.InvokeRequired)
        {
            var ok = false;
            try { _sync.Invoke(() => ok = Ask()); } catch (ObjectDisposedException) { return false; }
            return ok;
        }
        return Ask();
    }

    void Exit()
    {
        _cts.Cancel();
        try { _delayCts?.Cancel(); } catch (ObjectDisposedException) { }
        _delayCts?.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _flyout?.Dispose();
        _report?.Dispose();
        _compare?.Dispose();
        _settings?.Dispose();
        _sync.Dispose();
        Application.Exit();
    }

    void NotifyCloudSync(SyncStatus status)
    {
        var body = StatusText.FormatCloudSyncNotify(status.Ok, status.Message);
        if (body.Length == 0)
        {
            _lastCloudNotify = "";
            return;
        }
        if (body == _lastCloudNotify) return;
        _lastCloudNotify = body;
        OnUi(() => _icon.ShowBalloonTip(5000, "云同步失败", body, ToolTipIcon.Warning));
    }

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);
}

static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "CursorRemain";
    const string LegacyValueName = "CursorTokenTray";
    static string StartupDir => Environment.GetFolderPath(Environment.SpecialFolder.Startup);

    public static void Apply(bool enabled)
    {
        foreach (var name in new[] { "CursorRemain", "CursorTokenTray" })
        {
            foreach (var ext in new[] { ".lnk", ".vbs", ".cmd" })
            {
                try { File.Delete(Path.Combine(StartupDir, name + ext)); } catch { }
            }
        }

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;
            try { key.DeleteValue(LegacyValueName, false); } catch { }
            if (!enabled)
            {
                try { key.DeleteValue(ValueName, false); } catch { }
                return;
            }
            var exe = Environment.ProcessPath ?? Application.ExecutablePath;
            key.SetValue(ValueName, "\"" + exe.Replace("\"", "") + "\"");
        }
        catch { }
    }
}
