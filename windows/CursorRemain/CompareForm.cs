using System.Globalization;
using CursorTokenCore;

namespace CursorRemain;

sealed class CompareForm : Form
{
    public readonly record struct CompareState(
        IReadOnlyList<Account> Accounts,
        string ActiveAccountId,
        double MonthlyPlanUsd,
        double UsdCnyRate,
        Action<string, string?, string?, string?> PersistCycle);

    readonly CursorClient _client;
    readonly Func<CompareState> _state;
    readonly Label _status = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 8, 0, 0) };
    readonly Label _hint = new()
    {
        Text = StatusText.FormatCompareHint(false),
        AutoSize = true,
        ForeColor = Color.DimGray,
        Margin = new Padding(0, 6, 0, 10),
    };
    readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        ScrollBars = ScrollBars.Both,
    };
    readonly Button _syncBtn = UiChrome.Button("同步", UiButtonKind.Primary);
    readonly Button _exportBtn = UiChrome.Button("导出 CSV");
    AccountCompareReport _report = new();
    bool _syncing;

    public CompareForm(CursorClient client, Func<CompareState> state)
    {
        _client = client;
        _state = state;
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = "账号对比";
        var icon = AppWindow.CreateIcon();
        if (icon is not null) Icon = icon;
        MinimumSize = new Size(900, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1020;
        Height = 640;
        _grid.ColumnHeadersHeight = 32;
        _grid.RowTemplate.Height = 26;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        foreach (var (name, header, fill, align) in Columns())
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                FillWeight = fill,
                DefaultCellStyle = { Alignment = align },
            });
        }

        var toolbar = UiChrome.ActionBar(_status, _syncBtn, _exportBtn);
        toolbar.Dock = DockStyle.Top;
        _hint.Dock = DockStyle.Top;
        _grid.Dock = DockStyle.Fill;

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
        };
        root.Controls.Add(_grid);
        root.Controls.Add(_hint);
        root.Controls.Add(toolbar);
        Controls.Add(root);
        UiChrome.StyleGrid(_grid);

        _syncBtn.Click += async (_, _) => await SyncAsync();
        _exportBtn.Click += (_, _) => ExportCsv();
        void WrapHint()
        {
            var inner = Math.Max(400, root.ClientSize.Width - 8);
            _hint.MaximumSize = new Size(inner, 0);
            _status.MaximumSize = new Size(Math.Max(160, inner - 220), 0);
        }
        Load += (_, _) =>
        {
            WrapHint();
            LoadCache();
        };
        root.Resize += (_, _) => WrapHint();
        ResumeLayout();
        UiChrome.Apply(this);
        UiChrome.Equalize(DeviceDpi, _syncBtn, _exportBtn);
    }

    public void Reload()
    {
        if (_syncing) return;
        LoadCache();
    }

    public void RefreshTheme()
    {
        UiChrome.Apply(this);
        UiChrome.StyleGrid(_grid);
        Render();
    }

    static (string Name, string Header, float Fill, DataGridViewContentAlignment Align)[] Columns() =>
    [
        ("name", "账号 / 分类", 22, DataGridViewContentAlignment.MiddleLeft),
        ("window", "窗口", 14, DataGridViewContentAlignment.MiddleLeft),
        ("holding", "日均持有", 11, DataGridViewContentAlignment.MiddleRight),
        ("paid", "实付", 11, DataGridViewContentAlignment.MiddleRight),
        ("requests", "请求", 9, DataGridViewContentAlignment.MiddleRight),
        ("tokens", "Token", 11, DataGridViewContentAlignment.MiddleRight),
        ("perM", "¥/百万", 11, DataGridViewContentAlignment.MiddleRight),
        ("perR", "¥/次", 11, DataGridViewContentAlignment.MiddleRight),
    ];

    void LoadCache()
    {
        _report = BuildReport();
        Render();
        if (_report.Rows.Count == 0)
        {
            _status.Text = _state().Accounts.Count == 0
                ? "还没有账号。请先在设置里导入。"
                : "本地还没有明细。点「同步」按各账号最新周期拉取。";
        }
        else
        {
            _status.Text = "已加载本地明细。点「同步」按各账号最新周期拉取。";
        }
    }

    AccountCompareReport BuildReport()
    {
        var state = _state();
        var items = state.Accounts.Select(acc =>
            UsageEvents.CompareInputFromAccount(
                acc,
                UsageEvents.Load(acc.Id, false),
                state.MonthlyPlanUsd,
                state.UsdCnyRate));
        return UsageEvents.BuildAccountCompareReport(items);
    }

    async Task SyncAsync()
    {
        if (_syncing) return;
        var state = _state();
        var accounts = BoundedWork.Prioritize(state.Accounts, acc => acc.Id == state.ActiveAccountId);
        if (accounts.Count == 0)
        {
            _status.Text = "还没有账号。请先在设置里导入。";
            return;
        }
        _syncing = true;
        _syncBtn.Enabled = false;
        _status.Text = "正在同步各账号最新周期…";
        try
        {
            var total = accounts.Count;
            var indexed = accounts.Select((acc, i) => (acc, index: i + 1)).ToList();
            var results = await BoundedWork.MapAsync(indexed, item => SyncOneAsync(item.acc, item.index, total));
            var ok = results.Count(r => r is null);
            var failures = results.Where(r => r is not null).Cast<string>().ToList();
            _report = BuildReport();
            Render();
            var stamp = DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            _status.Text = StatusText.FormatCompareSync(ok, failures, stamp);
        }
        finally
        {
            _syncing = false;
            _syncBtn.Enabled = true;
        }
    }

    async Task<string?> SyncOneAsync(Account acc, int index, int total)
    {
        var name = string.IsNullOrWhiteSpace(acc.DisplayLabel) ? acc.Id : acc.DisplayLabel;
        SetProgress(StatusText.FormatCompareSyncProgress(index, total, name));
        if (acc.TokenDecryptFailed)
            return $"{name}：Token 解不开";
        if (string.IsNullOrWhiteSpace(acc.Token))
            return $"{name}：未配置 Token";
        try
        {
            var snap = await _client.FetchUsageSummary(acc.Token, 20);
            AccountValidity.ApplyEndOverride(snap, acc);
            _state().PersistCycle(acc.Id, snap.MembershipType, snap.BillingCycleStart, AccountValidity.StoredCycleEnd(snap));
            await UsageEvents.SyncAsync(_client, acc.Token, acc.Id, snap, false, onPage: page =>
                SetProgress(StatusText.FormatCompareSyncProgress(index, total, name, page)));
            return null;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            return $"{name}：{StatusText.ShortError(ex is CursorApiException api ? api.Message : ex.Message)}";
        }
    }

    void SetProgress(string text)
    {
        void Apply() { if (!IsDisposed) _status.Text = text; }
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(Apply);
        else Apply();
    }

    void Render()
    {
        _grid.Rows.Clear();
        _grid.SuspendLayout();
        var best = UsageEvents.CompareBestPerMillion(_report.Rows);
        _hint.Text = StatusText.FormatCompareHint(UsageEvents.CompareWindowsMixed(_report.Rows));
        var accounts = _state().Accounts;
        foreach (var group in _report.Groups)
        {
            StyleRow(_grid.Rows[_grid.Rows.Add(Line(group.ChannelLabel, "", "", "", "", "", "", ""))], RowKind.Header);
            foreach (var row in group.Rows)
            {
                var name = StatusText.FormatCompareAccountName(
                    AccountName(accounts, row),
                    row.AccountId == _state().ActiveAccountId);
                var memb = UsageParser.FormatMembershipType(row.MembershipType);
                if (!string.IsNullOrEmpty(memb) && !memb.Equals(name, StringComparison.OrdinalIgnoreCase))
                    name += "  " + memb;
                var acc = accounts.FirstOrDefault(a => a.Id == row.AccountId);
                var note = UsageEvents.CompareRowNote(
                    row,
                    hasToken: acc is null || !string.IsNullOrWhiteSpace(acc.Token),
                    lastError: acc?.LastError ?? "");
                var window = $"{row.WindowLabel} {FormatDays(row.WindowDays)}天";
                if (note.Length > 0) window += " · " + note;
                var accIdx = _grid.Rows.Add(Line(
                    name,
                    window,
                    UsageEvents.FormatCny(row.DailyHoldingCny),
                    UsageEvents.FormatCny(row.TotalCny),
                    FormatCount(row.EventCount),
                    UsageParser.FormatTokenCount(row.TotalTokens),
                    Unit(row.CnyPerMillion),
                    Unit(row.CnyPerRequest)));
                StyleRow(_grid.Rows[accIdx], RowKind.Account);
                if (best is { } bestVal && row.CnyPerMillion is { } perM && UsageEvents.CompareBestEligible(row) && Math.Abs(perM - bestVal) < 1e-9)
                    _grid.Rows[accIdx].Cells["perM"].Style.ForeColor = UiChrome.GoodText();
                AddCategory("First-party", row.FirstParty);
                AddCategory("API", row.Api);
                AddCategory("Grok Bot", row.GrokBot);
            }
            var sumIdx = _grid.Rows.Add(Line(
                group.ChannelLabel + "合计",
                "",
                UsageEvents.FormatCny(group.DailyHoldingCny),
                UsageEvents.FormatCny(group.TotalCny),
                FormatCount(group.EventCount),
                UsageParser.FormatTokenCount(group.TotalTokens),
                Unit(group.CnyPerMillion),
                Unit(group.CnyPerRequest)));
            StyleRow(_grid.Rows[sumIdx], RowKind.Total);
        }
        _grid.ResumeLayout();
        _exportBtn.Enabled = _report.Rows.Count > 0;
    }

    void AddCategory(string name, AccountCompareCategory cat)
    {
        var tokens = cat.Tokens == 0 && cat.Count == 0 ? "—" : UsageParser.FormatTokenCount(cat.Tokens);
        var idx = _grid.Rows.Add(Line(name, "", "", UsageEvents.FormatCny(cat.Cny), FormatCount(cat.Count), tokens, Unit(cat.CnyPerMillion), Unit(cat.CnyPerRequest)));
        StyleRow(_grid.Rows[idx], RowKind.Category);
    }

    enum RowKind { Header, Account, Category, Total }

    void StyleRow(DataGridViewRow row, RowKind kind)
    {
        switch (kind)
        {
            case RowKind.Header:
                row.DefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
                row.DefaultCellStyle.BackColor = UiChrome.HeaderFill();
                break;
            case RowKind.Account:
                row.DefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
                break;
            case RowKind.Category:
                row.DefaultCellStyle.ForeColor = UiChrome.SecondaryText();
                row.Cells["name"].Style.Padding = new Padding(18, 0, 0, 0);
                break;
            case RowKind.Total:
                row.DefaultCellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
                row.DefaultCellStyle.BackColor = UiChrome.HeaderFill();
                break;
        }
        row.DefaultCellStyle.SelectionBackColor = UiChrome.SelectionFill();
        row.DefaultCellStyle.SelectionForeColor = UiChrome.ColorOf(UiChrome.Tone.Text);
    }

    static object[] Line(string name, string window, string holding, string paid, string requests, string tokens, string perM, string perR) =>
        [name, window, holding, paid, requests, tokens, perM, perR];

    static string AccountName(IReadOnlyList<Account> accounts, AccountCompareRow row)
    {
        var acc = accounts.FirstOrDefault(a => a.Id == row.AccountId);
        var custom = (acc?.Label ?? "").Trim();
        if (custom.Length > 0) return custom;
        custom = (row.Label ?? "").Trim();
        if (custom.Length > 0 && custom != row.AccountId) return custom;
        return CompactAccountId(row.AccountId);
    }

    static string CompactAccountId(string raw)
    {
        var aid = (raw ?? "").Trim();
        if (aid.StartsWith("user_", StringComparison.Ordinal) && aid.Length > 18)
        {
            var body = aid[5..];
            if (body.StartsWith("01", StringComparison.Ordinal)) body = body[2..];
            return body[..5] + "…" + body[^2..];
        }
        if (aid.Length > 14) return aid[..12] + "…";
        return aid.Length == 0 ? "未命名账号" : aid;
    }

    static string Unit(double? amount) => UsageEvents.FormatCnyUnit(amount, "");

    static string FormatCount(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    static string FormatDays(double days) =>
        Math.Abs(days - Math.Round(days)) < 0.05
            ? Math.Round(days).ToString("0", CultureInfo.InvariantCulture)
            : days.ToString("0.00", CultureInfo.InvariantCulture);

    void ExportCsv()
    {
        if (_report.Rows.Count == 0)
        {
            _status.Text = StatusText.FormatExportEmpty();
            return;
        }
        using var dlg = new SaveFileDialog
        {
            Filter = "CSV 文件 (*.csv)|*.csv",
            FileName = StatusText.FormatExportFilename("cursor-account-compare"),
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, UsageEvents.AccountCompareToCsv(_report));
            _status.Text = "已导出 " + dlg.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "账号对比", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
