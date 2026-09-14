using System.Globalization;
using CursorTokenCore;

namespace CursorTokenTray;

sealed class SettingsForm : Form
{
    readonly TableLayoutPanel _root = new()
    {
        ColumnCount = 1,
        Dock = DockStyle.Fill,
        Padding = new Padding(12, 12, 12, 8),
    };
    readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    readonly TextBox _token = new()
    {
        Multiline = true,
        Height = 120,
        MinimumSize = new Size(0, 120),
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
    };
    readonly TextBox _interval = new() { Width = 80 };
    readonly TextBox _planUsd = new() { Width = 80 };
    readonly TextBox _actualCny = new() { Width = 80 };
    readonly ComboBox _channel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    readonly TextBox _cnyRate = new() { Width = 80 };
    readonly Label _spendHint = new()
    {
        Text = "月费填 0 则按套餐预填：Pro $20 / Pro+ $60 / Ultra $200。年付请填折合月费。实际成本在「账户」里按账号填写。",
        AutoSize = true,
        ForeColor = Color.DimGray,
        Margin = new Padding(0, 0, 0, 8),
    };
    readonly Label _actualHint = new()
    {
        Text = "仅当前账号，填折合月费。短期号请买价÷天数×30。企业 / 团队额度不是真实支出；填了则按套餐内费用分摊，优先于月费。按需仍按费用×汇率。",
        AutoSize = true,
        ForeColor = Color.DimGray,
        Margin = new Padding(0, 0, 0, 8),
    };
    readonly TextBox _thresholds = new() { Width = 180 };
    readonly CheckBox _notify = new() { Text = "启用用量通知", AutoSize = true, Margin = new Padding(0, 6, 0, 4) };
    readonly CheckBox _exhaust = new() { Text = "启用耗尽风险通知", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
    readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    readonly CheckBox _auto = new() { Text = "开机自启", AutoSize = true, Margin = new Padding(0, 6, 0, 8) };
    readonly CheckBox _autoUpdate = new() { Text = "自动检查并安装更新", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
    readonly Label _updateVersion = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 4, 0, 4) };
    readonly Label _updateStatus = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 4) };
    readonly Label _updateHint = new()
    {
        Text = "对照 GitHub Releases 的 Latest 构建。打包版会下载替换后重启；开发运行则打开下载页。",
        AutoSize = true,
        ForeColor = Color.DimGray,
        Margin = new Padding(0, 0, 0, 8),
    };
    readonly Button _checkUpdate = ActionButton("检查更新");
    readonly TextBox _cloudEmail = new() { Width = 220 };
    readonly TextBox _cloudPassword = new() { Width = 220, UseSystemPasswordChar = true };
    readonly Label _cloudAccount = new() { AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
    readonly Label _syncStatus = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 4, 0, 4) };
    readonly Label _syncHint = new()
    {
        Text = "登录后自动同步账号、设置和用量。数据用登录密码在本地加密，服务器看不到 Token。",
        AutoSize = true,
        ForeColor = Color.DimGray,
        Margin = new Padding(0, 4, 0, 8),
    };
    readonly FlowLayoutPanel _cloudAuth = new() { AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
    readonly FlowLayoutPanel _cloudActions = new() { AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
    readonly TableLayoutPanel _cloudEmailRow;
    readonly TableLayoutPanel _cloudPasswordRow;
    readonly Button _cloudLogin = ActionButton("登录");
    readonly Button _cloudRegister = ActionButton("注册");
    readonly Button _cloudLogout = ActionButton("退出登录");
    readonly Button _syncNow = ActionButton("立即同步");
    readonly Button _syncExport = ActionButton("导出…");
    readonly Button _syncImport = ActionButton("导入…");
    readonly ComboBox _accounts = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    readonly DateTimePicker _startAt = new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "yyyy-MM-dd HH:mm",
        Width = 180,
        ShowUpDown = true,
        MinDate = new DateTime(2000, 1, 1),
        MaxDate = new DateTime(2100, 1, 1),
    };
    readonly NumericUpDown _days = new() { Minimum = 0, Maximum = AccountValidity.MaxDays, Width = 64, DecimalPlaces = 0 };
    readonly NumericUpDown _hours = new() { Minimum = 0, Maximum = AccountValidity.MaxHours, Width = 64, DecimalPlaces = 0 };
    readonly Label _endAt = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 4, 0, 4) };
    readonly FlowLayoutPanel _tempFields = new()
    {
        AutoSize = true,
        WrapContents = true,
        FlowDirection = FlowDirection.LeftToRight,
        Margin = new Padding(0, 0, 0, 4),
    };
    readonly Label _addCaption = Caption("添加账号（每行一个 Token 或邮箱密码，请勿分享；已保存的不会显示）");
    readonly Label _status = new() { AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
    readonly Label _hint = new()
    {
        Text = "可粘贴 Token，或 name@example.com:密码、账号：邮箱密码：密码，多行则逐个添加。邮箱密码会打开官方登录页；验证码请在窗口里完成。此会话只能查用量。密码会加密保存并随云同步。Windows 还可从 Cursor 应用或 Firefox 导入。",
        AutoSize = true,
        ForeColor = Color.DimGray,
        Margin = new Padding(0, 4, 0, 8),
    };
    AppConfig _cfg;
    readonly Action<AppConfig> _onSaved;
    readonly Func<string?, Task<ImportResult>> _import;
    readonly Func<bool, Task<string>> _checkUpdateAction;

    bool _importing;
    bool _loading;

    public SettingsForm(AppConfig cfg, Action<AppConfig> onSaved, Func<string?, Task<ImportResult>> import, bool startImport, Func<bool, Task<string>> checkUpdate)
    {
        _cfg = cfg; _onSaved = onSaved; _import = import; _checkUpdateAction = checkUpdate;
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = AppPaths.SettingsTitle;
        var icon = AppWindow.CreateIcon();
        if (icon is not null) Icon = icon;
        ClientSize = new Size(SettingsLayout.DesignWidth, SettingsLayout.DesignHeight);
        MinimumSize = new Size(SettingsLayout.MinWidth, SettingsLayout.MinHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        var rename = ActionButton("重命名");
        var del = ActionButton("删除");
        var login = ActionButton("登录到 Cursor");
        var cur = ActionButton("从 Cursor 导入");
        var add = ActionButton("添加");
        var ff = ActionButton("Firefox 登录");
        var cookie = ActionButton("仅导入 Cookie");
        _kind.Items.AddRange(["长期账号", "临时账号"]);
        _tempFields.Controls.Add(LabeledSpin("开始时间", _startAt));
        _tempFields.Controls.Add(LabeledSpin("有效", _days, "天"));
        _tempFields.Controls.Add(LabeledSpin("", _hours, "小时"));
        _channel.Items.AddRange(["未标", "自费", "第三方"]);
        _mode.Items.AddRange(["圆环百分比", "纯数字", "仅色点"]);
        _cloudEmailRow = FieldRow("邮箱", _cloudEmail);
        _cloudPasswordRow = FieldRow("密码", _cloudPassword);
        _cloudAuth.Controls.AddRange([_cloudLogin, _cloudRegister]);
        _cloudActions.Controls.AddRange([_syncNow, _cloudLogout, _syncExport, _syncImport]);
        _tabs.TabPages.AddRange([
            MakeTab(SettingsLayout.AccountTab,
                Caption("当前账号"),
                _accounts,
                Flow(rename, del, login),
                FieldRow("账号类型", _kind),
                _tempFields,
                _endAt,
                FieldRow("渠道", _channel),
                FieldRow("实际成本（人民币）", _actualCny),
                _actualHint,
                _addCaption,
                _token,
                Flow(cur, add, ff, cookie),
                _status,
                _hint),
            MakeTab(SettingsLayout.NotifyTab,
                Caption("刷新与通知"),
                FieldRow("刷新间隔（分钟）", _interval),
                FieldRow("月费（美元）", _planUsd),
                FieldRow("美元兑人民币", _cnyRate),
                _spendHint,
                FieldRow("告警阈值", _thresholds),
                _notify,
                _exhaust),
            MakeTab(SettingsLayout.TrayTab,
                Caption("托盘与启动"),
                FieldRow("托盘图标", _mode),
                _auto,
                Caption("更新"),
                _autoUpdate,
                _updateVersion,
                Flow(_checkUpdate),
                _updateStatus,
                _updateHint),
            MakeTab(SettingsLayout.SyncTab,
                Caption("云同步"),
                _cloudAccount,
                _cloudEmailRow,
                _cloudPasswordRow,
                _cloudAuth,
                _cloudActions,
                _syncStatus,
                _syncHint),
        ]);
        var cancel = ActionButton("取消");
        var apply = ActionButton("应用");
        var save = ActionButton("保存");
        var actions = Flow(save, apply, cancel);
        actions.FlowDirection = FlowDirection.RightToLeft;
        actions.Dock = DockStyle.Fill;
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _root.RowCount = 2;
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _root.Controls.Add(_tabs, 0, 0);
        _root.Controls.Add(actions, 0, 1);
        Controls.Add(_root);
        LoadFrom(_cfg);
        _accounts.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            ReadKindInto(_cfg.ActiveAccount);
            ReadActualCnyInto(_cfg.ActiveAccount);
            ReadChannelInto(_cfg.ActiveAccount);
            if (_accounts.SelectedItem is AccountItem item) { _cfg.SetActiveAccount(item.Id); NotifySaved(); }
            WriteKindFrom(_cfg.ActiveAccount);
            WriteActualCnyFrom(_cfg.ActiveAccount);
            WriteChannelFrom(_cfg.ActiveAccount);
        };
        rename.Click += (_, _) => RenameActive();
        login.Click += async (_, _) => await LoginToCursor();
        del.Click += (_, _) =>
        {
            if (_cfg.ActiveAccount is null) return;
            if (MessageBox.Show($"确定删除「{_cfg.ActiveAccount.DisplayLabel}」？", "删除账号", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            _cfg.RemoveAccount(_cfg.ActiveAccount.Id);
            LoadFrom(_cfg); NotifySaved();
        };
        _kind.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            if (_kind.SelectedIndex == 1 && _days.Value == 0 && _hours.Value == 0) _days.Value = 1;
            ReadKindInto(_cfg.ActiveAccount);
            UpdateTempVisibility();
            UpdateEndLabel();
            NotifySaved();
        };
        _startAt.ValueChanged += (_, _) => OnValidityEdited();
        _days.ValueChanged += (_, _) => OnValidityEdited();
        _hours.ValueChanged += (_, _) => OnValidityEdited();
        add.Click += async (_, _) => await AddPastedAccounts();
        cur.Click += async (_, _) => await DoImport("cursor-app");
        cookie.Click += async (_, _) => await DoImport(null);
        ff.Click += async (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://cursor.com/dashboard") { UseShellExecute = true }); } catch { }
            await DoImport("firefox", waitForLogin: true);
        };
        cancel.Click += (_, _) => Close();
        apply.Click += (_, _) => Persist(false);
        save.Click += (_, _) => { Persist(true); Close(); };
        _cloudLogin.Click += async (_, _) => await DoCloudAuth(register: false);
        _cloudRegister.Click += async (_, _) => await DoCloudAuth(register: true);
        _cloudLogout.Click += async (_, _) => await DoCloudLogout();
        _syncNow.Click += async (_, _) => await DoSync();
        _syncExport.Click += (_, _) => DoExport();
        _syncImport.Click += (_, _) => DoImportFile();
        _checkUpdate.Click += async (_, _) => await DoCheckUpdate();
        ResumeLayout(false);
        if (startImport) BeginInvoke(async () => await DoImport("cursor-app"));
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitToContent();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(FitToContent);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        WrapText();
    }

    void FitToContent()
    {
        WrapText();
        var work = Screen.FromControl(this).WorkingArea;
        var (w, h) = UiLayout.FitWindow(
            SettingsLayout.DesignWidth,
            SettingsLayout.DesignHeight,
            SettingsLayout.MinWidth,
            SettingsLayout.MinHeight,
            DeviceDpi,
            work.Width,
            work.Height);
        ClientSize = new Size(w, h);
        WrapText();
    }

    void WrapText()
    {
        var inner = Math.Max(200, ClientSize.Width - 56);
        foreach (var label in new[] { _addCaption, _status, _hint, _actualHint, _syncStatus, _syncHint, _endAt, _spendHint })
            label.MaximumSize = new Size(inner, 0);
    }

    void RenameActive()
    {
        if (_cfg.ActiveAccount is null) return;
        using var prompt = new Form
        {
            Text = "重命名",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96F, 96F),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimizeBox = false,
            MaximizeBox = false,
            Padding = new Padding(16),
        };
        var field = new TextBox { Text = _cfg.ActiveAccount.Label, Width = 320, MinimumSize = new Size(260, 0) };
        var ok = ActionButton("确定");
        ok.DialogResult = DialogResult.OK;
        var cancelR = ActionButton("取消");
        cancelR.DialogResult = DialogResult.Cancel;
        var box = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
        };
        box.Controls.Add(field);
        box.Controls.Add(Flow(ok, cancelR));
        prompt.Controls.Add(box);
        prompt.AcceptButton = ok;
        prompt.CancelButton = cancelR;
        if (prompt.ShowDialog(this) != DialogResult.OK) return;
        _cfg.RenameAccount(_cfg.ActiveAccount.Id, field.Text);
        LoadFrom(_cfg); NotifySaved();
    }

    static TabPage MakeTab(string title, params Control[] children)
    {
        var page = new TabPage(title)
        {
            AutoScroll = true,
            UseVisualStyleBackColor = true,
            Padding = new Padding(4),
        };
        var body = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Padding = new Padding(8),
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var child in children)
            body.Controls.Add(child);
        page.Controls.Add(body);
        return page;
    }

    static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 8, 0, 4),
    };

    static FlowLayoutPanel LabeledSpin(string label, Control field, string? suffix = null)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 12, 4),
        };
        if (!string.IsNullOrEmpty(label))
        {
            row.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Margin = new Padding(0, 6, 8, 0),
            });
        }
        field.Margin = new Padding(0, 2, 4, 2);
        row.Controls.Add(field);
        if (!string.IsNullOrEmpty(suffix))
        {
            row.Controls.Add(new Label
            {
                Text = suffix,
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 0),
            });
        }
        return row;
    }

    static Button ActionButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = new Padding(0, 0, 8, 4),
    };

    static FlowLayoutPanel Flow(params Control[] items)
    {
        var p = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 4, 0, 4),
        };
        foreach (var c in items) p.Controls.Add(c);
        return p;
    }

    static TableLayoutPanel FieldRow(string label, Control field)
    {
        var row = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 0, 6),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 12, 0),
        }, 0, 0);
        field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        field.Margin = new Padding(0, 2, 0, 2);
        row.Controls.Add(field, 1, 0);
        return row;
    }

    static TableLayoutPanel PathRow(TextBox path, Button browse)
    {
        var row = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        path.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        row.Controls.Add(path, 0, 0);
        browse.Margin = new Padding(8, 0, 0, 0);
        row.Controls.Add(browse, 1, 0);
        return row;
    }

    public void FocusToken()
    {
        _tabs.SelectedIndex = 0;
        BeginInvoke(() =>
        {
            if (IsDisposed) return;
            _token.Focus();
            _token.SelectAll();
        });
    }

    public void StartImport()
    {
        _tabs.SelectedIndex = 0;
        BeginInvoke(async () => await DoImport("cursor-app"));
    }

    void LoadFrom(AppConfig cfg)
    {
        _loading = true;
        try
        {
            _cfg = cfg;
            _accounts.Items.Clear();
            foreach (var a in cfg.Accounts)
                _accounts.Items.Add(new AccountItem(a.Id, a.Caption(a.Id == cfg.ActiveAccountId)));
            var idx = cfg.Accounts.FindIndex(a => a.Id == cfg.ActiveAccountId);
            if (idx >= 0) _accounts.SelectedIndex = idx;
            _token.Text = "";
            _token.PlaceholderText = "每行一个：Token，或 name@example.com:密码，或 账号：邮箱密码：密码";
            _interval.Text = cfg.RefreshIntervalMinutes.ToString();
            var membership = cfg.ActiveAccount?.MembershipType ?? "";
            var plan = cfg.MonthlyPlanUsd > 0 ? cfg.MonthlyPlanUsd : UsageEvents.DefaultMonthlyPlanUsd(membership);
            _planUsd.Text = plan.ToString("0.##", CultureInfo.InvariantCulture);
            WriteActualCnyFrom(cfg.ActiveAccount);
            WriteChannelFrom(cfg.ActiveAccount);
            _cnyRate.Text = cfg.UsdCnyRate.ToString("0.##", CultureInfo.InvariantCulture);
            _thresholds.Text = string.Join(",", cfg.AlertThresholds);
            _notify.Checked = cfg.NotifyEnabled;
            _exhaust.Checked = cfg.NotifyExhaustionRisk;
            _mode.SelectedIndex = cfg.TrayDisplayMode switch { "number" => 1, "dot" => 2, _ => 0 };
            _auto.Checked = cfg.AutostartEnabled;
            _autoUpdate.Checked = cfg.AutoUpdateEnabled;
            _updateVersion.Text = "当前版本  " + AppUpdate.DisplayVersion();
            if (string.IsNullOrWhiteSpace(_updateStatus.Text))
                _updateStatus.Text = string.IsNullOrWhiteSpace(cfg.UpdateLastError)
                    ? (string.IsNullOrWhiteSpace(cfg.UpdateLastCheckAt) ? "" : "上次检查 " + cfg.UpdateLastCheckAt)
                    : cfg.UpdateLastError;
            _cloudEmail.Text = cfg.CloudEmail;
            _cloudPassword.Text = "";
            _cloudPassword.PlaceholderText = cfg.CloudLoggedIn ? "已保存，登录后用于加密" : "至少 8 位，也用于加密同步数据";
            _cloudAccount.Text = cfg.CloudLoggedIn ? "已登录  " + cfg.CloudEmail : "未登录";
            _cloudEmail.Enabled = !cfg.CloudLoggedIn;
            _cloudPassword.Enabled = !cfg.CloudLoggedIn;
            _cloudEmailRow.Visible = !cfg.CloudLoggedIn;
            _cloudPasswordRow.Visible = !cfg.CloudLoggedIn;
            _cloudAuth.Visible = !cfg.CloudLoggedIn;
            _cloudLogout.Visible = cfg.CloudLoggedIn;
            _syncNow.Visible = cfg.CloudLoggedIn;
            _syncStatus.Text = SyncStatusText(cfg);
            WriteKindFrom(cfg.ActiveAccount);
        }
        finally { _loading = false; }
    }

    void OnValidityEdited()
    {
        if (_loading) return;
        ReadKindInto(_cfg.ActiveAccount);
        UpdateEndLabel();
    }

    void ReadActualCnyInto(Account? acc)
    {
        if (acc is null) return;
        if (TryParseDecimal(_actualCny.Text, out var actualCny))
            _cfg.SetActualCny(acc.Id, actualCny);
    }

    void WriteActualCnyFrom(Account? acc)
    {
        var value = acc?.ActualCny ?? _cfg.ActualCny;
        _actualCny.Text = value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    void ReadChannelInto(Account? acc)
    {
        if (acc is null) return;
        var value = _channel.SelectedIndex switch { 1 => UsageEvents.ChannelSelfPay, 2 => UsageEvents.ChannelThirdParty, _ => "" };
        _cfg.SetChannel(acc.Id, value);
    }

    void WriteChannelFrom(Account? acc)
    {
        var prev = _loading;
        _loading = true;
        try
        {
            _channel.Enabled = acc is not null;
            _channel.SelectedIndex = UsageEvents.SanitizeChannel(acc?.Channel) switch
            {
                UsageEvents.ChannelSelfPay => 1,
                UsageEvents.ChannelThirdParty => 2,
                _ => 0,
            };
        }
        finally { _loading = prev; }
    }

    void ReadKindInto(Account? acc)
    {
        if (acc is null) return;
        var kind = _kind.SelectedIndex == 1 ? AccountValidity.Temporary : AccountValidity.LongTerm;
        var start = new DateTimeOffset(DateTime.SpecifyKind(_startAt.Value, DateTimeKind.Local)).ToUniversalTime();
        _cfg.UpdateAccountValidity(acc.Id, kind, AccountSync.NowIso(start), (int)_days.Value, (int)_hours.Value);
    }

    void WriteKindFrom(Account? acc)
    {
        var prev = _loading;
        _loading = true;
        try
        {
            var enabled = acc is not null;
            _kind.Enabled = enabled;
            _startAt.Enabled = enabled;
            _days.Enabled = enabled;
            _hours.Enabled = enabled;
            if (acc is null)
            {
                _kind.SelectedIndex = 0;
                _days.Value = 0;
                _hours.Value = 0;
                _startAt.Value = DateTime.Now;
            }
            else
            {
                _kind.SelectedIndex = AccountValidity.IsTemporary(acc) ? 1 : 0;
                var start = AccountSync.ParseIso(acc.TempStartAt)?.ToLocalTime().DateTime ?? DateTime.Now;
                if (start < _startAt.MinDate) start = _startAt.MinDate;
                if (start > _startAt.MaxDate) start = _startAt.MaxDate;
                _startAt.Value = start;
                _days.Value = acc.TempValidDays;
                _hours.Value = acc.TempValidHours;
            }
            UpdateTempVisibility();
            UpdateEndLabel();
        }
        finally { _loading = prev; }
    }

    void UpdateTempVisibility()
    {
        var show = _kind.SelectedIndex == 1;
        _tempFields.Visible = show;
        _endAt.Visible = show;
        if (IsHandleCreated) BeginInvoke(WrapText);
    }

    void UpdateEndLabel()
    {
        if (_kind.SelectedIndex != 1)
        {
            _endAt.Text = "";
            return;
        }
        var start = new DateTimeOffset(DateTime.SpecifyKind(_startAt.Value, DateTimeKind.Local)).ToUniversalTime();
        var end = AccountValidity.ComputeEndIso(AccountSync.NowIso(start), (int)_days.Value, (int)_hours.Value);
        _endAt.Text = string.IsNullOrEmpty(end)
            ? "请设置有效时间（天和小时可组合）"
            : "结束时间  " + StatusText.FormatResetDate(end, includeTime: true) + "（按开始时间计算，覆盖管理端重置日）";
    }

    static string SyncStatusText(AppConfig cfg)
    {
        if (!string.IsNullOrWhiteSpace(cfg.SyncLastError)) return cfg.SyncLastError;
        if (!string.IsNullOrWhiteSpace(cfg.SyncLastAt)) return "上次同步 " + cfg.SyncLastAt;
        return cfg.CloudLoggedIn ? "尚未同步" : "";
    }

    async Task AddPastedAccounts()
    {
        if (_importing) return;
        var items = CursorAccountPaste.Parse(_token.Text);
        if (items.Count == 0)
        {
            _status.Text = "请粘贴 Token 或邮箱密码";
            return;
        }
        _importing = true;
        var ok = 0;
        var fail = 0;
        string? lastId = null;
        try
        {
            foreach (var item in items)
            {
                if (item.Kind == "token")
                {
                    try
                    {
                        var (acc, _) = _cfg.UpsertAccount(item.Token, activate: true);
                        lastId = acc.Id;
                        ok++;
                        NotifySaved();
                    }
                    catch { fail++; }
                    continue;
                }
                if (item.Kind == "credentials")
                {
                    _status.Text = "正在打开登录页…";
                    using var dlg = new PasswordLoginForm(item.Email, item.Password);
                    if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.Token))
                    {
                        fail++;
                        continue;
                    }
                    try
                    {
                        var snap = await new CursorClient().FetchUsageSummary(dlg.Token);
                        var (acc, _) = _cfg.UpsertAccount(
                            dlg.Token,
                            email: item.Email,
                            password: item.Password,
                            membershipType: snap.MembershipType,
                            remaining: snap.RemainingPercent,
                            activate: true);
                        lastId = acc.Id;
                        ok++;
                        NotifySaved();
                    }
                    catch
                    {
                        try
                        {
                            var (acc, _) = _cfg.UpsertAccount(
                                dlg.Token,
                                email: item.Email,
                                password: item.Password,
                                activate: true);
                            lastId = acc.Id;
                            ok++;
                            NotifySaved();
                        }
                        catch { fail++; }
                    }
                    continue;
                }
                fail++;
            }
            if (lastId is not null) _cfg.SetActiveAccount(lastId);
            _token.Text = "";
            Persist(false);
            if (!IsDisposed)
                _status.Text = fail == 0 ? $"已添加 {ok} 个账号" : $"成功 {ok} / 失败 {fail}";
        }
        finally { _importing = false; }
    }

    async Task LoginToCursor()
    {
        if (_importing) return;
        _importing = true;
        _status.Text = "正在写入 Cursor…";
        try
        {
            var result = await CursorLoginUi.Run(_cfg.ActiveAccount, this);
            if (!IsDisposed) _status.Text = result.Message;
        }
        finally { _importing = false; }
    }

    async Task DoImport(string? prefer, bool waitForLogin = false)
    {
        if (_importing) return;
        _importing = true;
        _status.Text = waitForLogin ? "请在浏览器登录，正在等待 Cookie…" : "正在导入…";
        try
        {
            if (waitForLogin)
            {
                var deadline = DateTime.UtcNow.AddSeconds(180);
                while (DateTime.UtcNow < deadline && !IsDisposed)
                {
                    var result = await _import(prefer);
                    if (result.Ok)
                    {
                        ApplyImport(result);
                        return;
                    }
                    await Task.Delay(2000);
                }
                if (!IsDisposed) _status.Text = "等待登录超时，请手动粘贴 Token。";
                return;
            }
            var once = await _import(prefer);
            _status.Text = once.Message;
            if (!once.Ok) return;
            ApplyImport(once);
        }
        finally { _importing = false; }
    }

    void ApplyImport(ImportResult result)
    {
        _status.Text = result.Message;
        _cfg.UpsertAccount(result.Token, membershipType: result.MembershipType, remaining: result.RemainingPercent, activate: true);
        _token.Text = "";
        Persist(false);
    }

    void CopyRuntimeFromDisk()
    {
        try
        {
            var live = ConfigStore.Load();
            foreach (var acc in _cfg.Accounts)
            {
                var src = live.Accounts.FirstOrDefault(a => a.Id == acc.Id);
                if (src is null) continue;
                acc.AlertNotifiedLevels = [.. src.AlertNotifiedLevels];
                acc.AuthErrorNotified = src.AuthErrorNotified;
                acc.ExhaustionNotified = src.ExhaustionNotified;
                acc.LowQuotaNotified = src.LowQuotaNotified;
                acc.LastRemaining = src.LastRemaining;
                acc.LastError = src.LastError;
                acc.UpdatedAt = src.UpdatedAt;
                if (string.IsNullOrEmpty(acc.MembershipType)) acc.MembershipType = src.MembershipType;
            }
            _cfg.LowQuotaNotified = live.LowQuotaNotified;
            _cfg.AuthErrorNotified = live.AuthErrorNotified;
            _cfg.AlertNotifiedLevels = [.. live.AlertNotifiedLevels];
            _cfg.ExhaustionNotified = live.ExhaustionNotified;
            _cfg.UpdateLastCheckAt = live.UpdateLastCheckAt;
            _cfg.UpdateLastError = live.UpdateLastError;
            _cfg.UpdateInstalledSha = live.UpdateInstalledSha;
            _cfg.UpdateInstalledAssetId = live.UpdateInstalledAssetId;
        }
        catch { }
    }

    public void SetUpdateStatus(string status)
    {
        if (IsDisposed) return;
        _updateStatus.Text = string.IsNullOrWhiteSpace(status) ? _updateStatus.Text : status;
    }

    async Task DoCheckUpdate()
    {
        if (_checkUpdate.Enabled == false) return;
        Persist(false);
        _checkUpdate.Enabled = false;
        _updateStatus.Text = "正在检查更新…";
        try
        {
            var status = await _checkUpdateAction(true);
            if (!IsDisposed) _updateStatus.Text = status;
        }
        catch (Exception ex)
        {
            if (!IsDisposed) _updateStatus.Text = "检查更新失败: " + ex.Message;
        }
        finally
        {
            if (!IsDisposed) _checkUpdate.Enabled = true;
        }
    }

    void NotifySaved()
    {
        CopyRuntimeFromDisk();
        _onSaved(_cfg);
    }

    void Persist(bool _)
    {
        CopyRuntimeFromDisk();
        if (int.TryParse(_interval.Text, out var n) && n >= 1) _cfg.RefreshIntervalMinutes = n;
        if (TryParseDecimal(_planUsd.Text, out var planUsd))
            _cfg.MonthlyPlanUsd = UsageEvents.ClampMonthlyPlanUsd(planUsd);
        ReadActualCnyInto(_cfg.ActiveAccount);
        ReadChannelInto(_cfg.ActiveAccount);
        if (TryParseDecimal(_cnyRate.Text, out var rate))
            _cfg.UsdCnyRate = UsageEvents.ClampUsdCnyRate(rate);
        _cfg.AlertThresholds = ConfigStore.ParseThresholds(_thresholds.Text);
        _cfg.NotifyEnabled = _notify.Checked;
        _cfg.NotifyExhaustionRisk = _exhaust.Checked;
        _cfg.TrayDisplayMode = _mode.SelectedIndex switch { 1 => "number", 2 => "dot", _ => "ring" };
        _cfg.AutostartEnabled = _auto.Checked;
        _cfg.AutoUpdateEnabled = _autoUpdate.Checked;
        ReadKindInto(_cfg.ActiveAccount);
        if (CursorAccountPaste.IsSingleToken(_token.Text))
            try { _cfg.UpsertAccount(_token.Text, activate: true); } catch { }
        _onSaved(_cfg);
        LoadFrom(_cfg);
    }

    string ExportPassphrase()
    {
        if (!string.IsNullOrWhiteSpace(_cfg.SyncSecret)) return _cfg.SyncSecret;
        if (!string.IsNullOrWhiteSpace(_cloudPassword.Text)) return _cloudPassword.Text.Trim();
        return "";
    }

    async Task DoCloudAuth(bool register)
    {
        var email = _cloudEmail.Text.Trim();
        var password = _cloudPassword.Text.Trim();
        if (email.Length == 0 || password.Length == 0)
        {
            _syncStatus.Text = "请填写邮箱和密码";
            return;
        }
        _syncStatus.Text = register ? "正在注册…" : "正在登录…";
        try
        {
            var result = register
                ? await CloudSync.RegisterAsync(email, password)
                : await CloudSync.LoginAsync(email, password);
            CloudSync.ApplySession(_cfg, result.Email.Length > 0 ? result.Email : email, password, result.Access, result.Refresh);
            var status = await CloudSync.ReconcileAsync(_cfg);
            _syncStatus.Text = status.Ok ? status.Message : status.Message;
            NotifySaved();
            LoadFrom(_cfg);
        }
        catch (Exception ex) { _syncStatus.Text = ex.Message; }
    }

    async Task DoCloudLogout()
    {
        await CloudSync.LogoutAsync(_cfg);
        _syncStatus.Text = "已退出登录";
        NotifySaved();
        LoadFrom(_cfg);
    }

    async Task DoSync()
    {
        var status = await CloudSync.ReconcileAsync(_cfg);
        _syncStatus.Text = status.Message;
        _status.Text = status.Message;
        NotifySaved();
        LoadFrom(_cfg);
    }

    void DoExport()
    {
        var secret = ExportPassphrase();
        using var dlg = new SaveFileDialog
        {
            Filter = "同步文件|*.sync|JSON|*.json",
            FileName = AccountSync.Filename,
            Title = "导出加密账号包",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var dest = AccountSync.ExportToFile(_cfg, dlg.FileName, secret);
            _syncStatus.Text = "已导出到 " + dest;
        }
        catch (Exception ex) { _syncStatus.Text = ex.Message; }
    }

    void DoImportFile()
    {
        var secret = ExportPassphrase();
        using var dlg = new OpenFileDialog
        {
            Filter = "同步文件|*.sync;*.json|所有文件|*.*",
            Title = "导入加密账号包",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            AccountSync.ImportFromFile(_cfg, dlg.FileName, secret);
            _syncStatus.Text = "已从文件合并账号";
            NotifySaved();
            LoadFrom(_cfg);
        }
        catch (Exception ex) { _syncStatus.Text = ex.Message; }
    }

    static bool TryParseDecimal(string text, out double value)
    {
        var cleaned = (text ?? "").Trim().Replace("，", ".");
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    sealed record AccountItem(string Id, string Caption)
    {
        public override string ToString() => Caption;
    }
}

static class CursorLoginUi
{
    public static async Task<CursorAuthApplyResult> Run(Account? acc, IWin32Window? owner)
    {
        if (acc is null)
            return new CursorAuthApplyResult(false, "请先选择账号");
        if (acc.TokenDecryptFailed || string.IsNullOrWhiteSpace(acc.Token))
            return new CursorAuthApplyResult(false, "当前账号没有可用 Token");
        var values = CursorAuth.BuildValues(
            acc.Token,
            email: !string.IsNullOrWhiteSpace(acc.Email) ? acc.Email : CursorAuth.LooksLikeEmail(acc.Label) ? acc.Label : null,
            membershipType: acc.MembershipType,
            displayName: acc.DisplayLabel);
        if (values.Values is null)
            return new CursorAuthApplyResult(false, values.Error);
        var target = CursorAuth.ResolveTarget();
        var running = target is not null && CursorAuth.IsRunning(target);
        if (running)
        {
            var answer = owner is null
                ? MessageBox.Show(CursorAuth.ConfirmCloseMessage, "登录到 Cursor", MessageBoxButtons.OKCancel, MessageBoxIcon.Question)
                : MessageBox.Show(owner, CursorAuth.ConfirmCloseMessage, "登录到 Cursor", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (answer != DialogResult.OK)
                return new CursorAuthApplyResult(false, "已取消");
        }
        var token = acc.Token;
        var email = !string.IsNullOrWhiteSpace(acc.Email) ? acc.Email : CursorAuth.LooksLikeEmail(acc.Label) ? acc.Label : null;
        var membership = acc.MembershipType;
        var display = acc.DisplayLabel;
        return await Task.Run(() => CursorAuth.Apply(
            token,
            email: email,
            membershipType: membership,
            displayName: display,
            closeIfRunning: true,
            relaunch: true));
    }
}
