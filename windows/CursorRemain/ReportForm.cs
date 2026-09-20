using System.Globalization;
using System.Reflection;
using CursorTokenCore;

namespace CursorRemain;

sealed class ReportForm : Form
{
    public readonly record struct ReportState(string Token, string AccountId, UsageSnapshot? Usage, bool IsTeam, CnySpendSettings Spend, string ReportStartDate = "", string ReportEndDate = "", ReportAllocationWindow? Allocation = null, string AccountLabel = "");

    const int DesignWidth = 1100;
    const int DesignHeight = 880;
    const int DesignMinWidth = 940;
    const int DesignMinHeight = 640;
    const int DesignHeaderH = 28;
    const int DesignRowH = 24;

    readonly CursorClient _client;
    readonly Func<ReportState> _state;
    readonly Action<string, string, string>? _persistDates;
    readonly ComboBox _scope = new FlatCombo();
    readonly ComboBox _kind = new FlatCombo();
    readonly ComboBox _category = new FlatCombo();
    readonly FlatCombo _model = new();
    readonly ComboBox _cloud = new FlatCombo();
    readonly DateTimePicker _startDate = new FlatDatePicker
    {
        ShowCheckBox = true,
        Checked = false,
    };
    readonly DateTimePicker _endDate = new FlatDatePicker
    {
        ShowCheckBox = true,
        Checked = false,
    };
    readonly Label _status = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 8, 0, 0) };
    readonly KpiStrip _kpi = new();
    readonly Label _mix = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = Padding.Empty };
    readonly Panel _mixHost = new()
    {
        AutoSize = false,
        Size = new Size(1, 1),
        Margin = new Padding(24, 0, 0, 0),
    };
    readonly WrapBar _summaryBar = new()
    {
        Dock = DockStyle.Top,
        Margin = new Padding(0, 8, 0, 0),
    };
    readonly UsageChartPanel _chart = new();
    readonly DataGridView _models = MakeGrid();
    readonly DataGridView _grid = MakeGrid();
    readonly Button _syncBtn = UiChrome.Button("同步", UiButtonKind.Primary);
    readonly Button _exportBtn = UiChrome.Button("导出 CSV");
    readonly WrapBar _filterBar = new()
    {
        Dock = DockStyle.Top,
        Margin = new Padding(0, 2, 8, 0),
    };
    readonly FlowLayoutPanel _actionBar = new()
    {
        AutoSize = true,
        WrapContents = false,
        FlowDirection = FlowDirection.LeftToRight,
        Anchor = AnchorStyles.Right | AnchorStyles.Top,
        Margin = new Padding(0, 2, 0, 0),
    };
    readonly Control _scopeTag;
    readonly Label _detailsLabel = UiChrome.Heading("明细");
    readonly TableLayoutPanel _root = new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 4,
        Padding = new Padding(24, 20, 24, 20),
    };
    readonly SplitContainer _split = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Horizontal,
        SplitterWidth = 8,
        TabStop = false,
        // Min sizes are applied in PlaceSplitter after the control has a real height.
    };
    static readonly int[] ModelMinWidths = [160, 72, 72, 72, 56, 56, 48];
    static readonly int[] DetailMinWidths = [110, 100, 56, 140, 64, 72, 72, 48];
    List<UsageEvent> _all = [];
    IReadOnlyList<UsageEvent> _detailEvents = [];
    bool _syncing;
    bool _teamScope;
    bool _ready;
    bool _loadingDates;
    bool _splitReady;
    int _lastWrapW;
    int _renderGen;
    string _loadedAccountId = "";
    string _mixKinds = "";
    string _mixSources = "";
    string _mixSpend = "";

    public ReportForm(CursorClient client, Func<ReportState> state, Action<string, string, string>? persistDates = null)
    {
        _client = client;
        _state = state;
        _persistDates = persistDates;
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = "用量报表";
        var icon = AppWindow.CreateIcon();
        if (icon is not null) Icon = icon;
        MinimumSize = new Size(DesignMinWidth, DesignMinHeight);
        StartPosition = FormStartPosition.CenterScreen;
        Width = DesignWidth;
        Height = DesignHeight;
        WindowState = FormWindowState.Maximized;
        Opacity = 0;

        _scope.Items.AddRange(["仅自己", "全员"]);
        _scope.SelectedIndex = 0;
        _kind.Items.AddRange(["全部类型", "套餐内", "免费", "按需"]);
        _kind.SelectedIndex = 0;
        _category.Items.AddRange(["全部额度", "First-party", "API", "Grok Bot"]);
        _category.SelectedIndex = 0;
        _cloud.Items.AddRange(["全部来源", "本机", "云端 Agent"]);
        _cloud.SelectedIndex = 0;
        _model.Items.Add("全部模型");
        _model.SelectedIndex = 0;

        _models.Columns.Add(new DataGridViewTextBoxColumn { Name = "model", HeaderText = "模型", FillWeight = 36 });
        _models.Columns.Add(new DataGridViewTextBoxColumn { Name = "tokens", HeaderText = "Token", FillWeight = 16, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _models.Columns.Add(new DataGridViewTextBoxColumn { Name = "cost", HeaderText = "费用", FillWeight = 12, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _models.Columns.Add(new DataGridViewTextBoxColumn { Name = "cny", HeaderText = "实付", FillWeight = 12, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _models.Columns.Add(new DataGridViewTextBoxColumn { Name = "discount", HeaderText = "折扣", FillWeight = 10, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _models.Columns.Add(new DataGridViewTextBoxColumn { Name = "count", HeaderText = "次数", FillWeight = 10, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _models.Columns.Add(new DataGridViewTextBoxColumn { Name = "cloud", HeaderText = "云端", FillWeight = 8, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "date", HeaderText = "日期 (本地时间)", FillWeight = 15 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "user", HeaderText = "用户", FillWeight = 14 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "kind", HeaderText = "类型", FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "model", HeaderText = "模型", FillWeight = 18 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "tokens", HeaderText = "Token", FillWeight = 8, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "cost", HeaderText = "费用", FillWeight = 12, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "cny", HeaderText = "实付", FillWeight = 12, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "cloud", HeaderText = "云端", FillWeight = 8, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
        _grid.VirtualMode = true;
        _grid.CellValueNeeded += OnDetailCellValue;
        foreach (DataGridViewColumn col in _grid.Columns)
            col.SortMode = DataGridViewColumnSortMode.NotSortable;

        _scopeTag = FilterTag("范围", _scope);
        _filterBar.Controls.Add(FilterTag("开始日期", _startDate, first: true));
        _filterBar.Controls.Add(FilterTag("结束日期", _endDate));
        _filterBar.Controls.Add(_scopeTag);
        _filterBar.Controls.Add(FilterTag("类型", _kind));
        _filterBar.Controls.Add(FilterTag("额度", _category));
        _filterBar.Controls.Add(FilterTag("模型", _model));
        _filterBar.Controls.Add(FilterTag("来源", _cloud));
        _actionBar.Controls.Add(_syncBtn);
        _actionBar.Controls.Add(_exportBtn);
        var toolbar = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0),
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        toolbar.Controls.Add(_filterBar, 0, 0);
        toolbar.Controls.Add(_actionBar, 1, 0);
        _status.Margin = new Padding(0, 6, 0, 0);
        toolbar.SetColumnSpan(_status, 2);
        toolbar.Controls.Add(_status, 0, 1);
        _kpi.WrapContents = false;
        _kpi.Margin = new Padding(0, 0, 8, 0);
        _mixHost.Controls.Add(_mix);
        _summaryBar.Controls.Add(_kpi);
        _summaryBar.Controls.Add(_mixHost);
        _summaryBar.Layout += (_, _) => SyncMixHost();

        _root.ColumnStyles.Clear();
        _root.RowStyles.Clear();
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        _root.Controls.Add(toolbar, 0, 0);
        _root.Controls.Add(_summaryBar, 0, 1);
        _root.Controls.Add(_chart, 0, 2);
        var modelWrap = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        modelWrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        modelWrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        modelWrap.Controls.Add(UiChrome.Heading("按模型"), 0, 0);
        modelWrap.Controls.Add(_models, 0, 1);
        var detailsWrap = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        detailsWrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailsWrap.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detailsWrap.Controls.Add(_detailsLabel, 0, 0);
        detailsWrap.Controls.Add(_grid, 0, 1);
        _split.Panel1.Padding = new Padding(0, 8, 0, 0);
        _split.Panel2.Padding = new Padding(0, 4, 0, 0);
        _split.Panel1.Controls.Add(modelWrap);
        _split.Panel2.Controls.Add(detailsWrap);
        _root.Controls.Add(_split, 0, 3);
        _split.Resize += (_, _) => PlaceSplitter();
        Controls.Add(_root);

        _scope.SelectedIndexChanged += (_, _) =>
        {
            if (!_ready || _loadingDates) return;
            _teamScope = _scope.SelectedIndex == 1;
            _ = SyncAsync(false);
        };
        _kind.SelectedIndexChanged += (_, _) => { if (_ready && !_loadingDates) _ = RenderAsync(); };
        _category.SelectedIndexChanged += (_, _) => { if (_ready && !_loadingDates) _ = RenderAsync(); };
        _model.SelectedIndexChanged += (_, _) => { if (_ready && !_loadingDates) _ = RenderAsync(); };
        _cloud.SelectedIndexChanged += (_, _) => { if (_ready && !_loadingDates) _ = RenderAsync(); };
        _startDate.ValueChanged += (_, _) => OnDateChanged();
        _endDate.ValueChanged += (_, _) => OnDateChanged();
        _syncBtn.Click += (_, _) => _ = SyncAsync(true);
        _exportBtn.Click += (_, _) => ExportCsv();
        Shown += (_, _) => _ = SyncAsync(false);
        ResumeLayout(false);
        UiChrome.Apply(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyDpiLayout(resizeWindow: true);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        BeginInvoke(() => ApplyDpiLayout(resizeWindow: false));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        WrapKpi();
        AdaptChart();
    }

    void ApplyDpiLayout(bool resizeWindow)
    {
        var dpi = DeviceDpi;
        var wide = ClientSize.Width >= UiLayout.ScalePx(1280, dpi);
        _scope.Width = UiLayout.ScalePx(120, dpi);
        _kind.Width = UiLayout.ScalePx(110, dpi);
        _category.Width = UiLayout.ScalePx(120, dpi);
        _model.Width = UiLayout.ScalePx(wide ? 180 : 160, dpi);
        _cloud.Width = UiLayout.ScalePx(120, dpi);
        _startDate.Width = UiLayout.ScalePx(118, dpi);
        _endDate.Width = UiLayout.ScalePx(118, dpi);
        UiChrome.Equalize(dpi, _syncBtn, _exportBtn);
        _kpi.ValuePt = wide ? 13f : 12f;
        _kpi.ItemGap = wide ? 40 : 28;

        var shortWin = WindowState != FormWindowState.Maximized && ClientSize.Height < UiLayout.ScalePx(820, dpi);
        _chart.ApplyDpi(dpi, compact: shortWin);
        AdaptChart();

        var headerH = UiLayout.ScalePx(wide ? 32 : DesignHeaderH, dpi);
        var rowH = UiLayout.ScalePx(wide ? 26 : DesignRowH, dpi);
        ApplyGridMetrics(_models, headerH, rowH, ModelMinWidths, dpi);
        ApplyGridMetrics(_grid, headerH, rowH, DetailMinWidths, dpi);
        ApplyColumnSizing(dpi);

        var work = Screen.FromControl(this).WorkingArea;
        var (w, h) = UiLayout.FitWindow(DesignWidth, DesignHeight, DesignMinWidth, DesignMinHeight, dpi, work.Width, work.Height);
        MinimumSize = new Size(Math.Min(w, UiLayout.ScalePx(DesignMinWidth, dpi)), Math.Min(h, UiLayout.ScalePx(DesignMinHeight, dpi)));
        if (resizeWindow && WindowState != FormWindowState.Maximized)
            Size = new Size(w, h);
        WrapKpi();
    }

    static void ApplyGridMetrics(DataGridView grid, int headerH, int rowH, int[] minDesignWidths, int dpi)
    {
        grid.ColumnHeadersHeight = headerH;
        grid.RowTemplate.Height = rowH;
        for (var i = 0; i < grid.Columns.Count && i < minDesignWidths.Length; i++)
        {
            grid.Columns[i].MinimumWidth = UiLayout.ScalePx(minDesignWidths[i], dpi);
        }
    }

    void WrapKpi()
    {
        var inner = Math.Max(200, ClientSize.Width - _root.Padding.Horizontal - 8);
        _lastWrapW = ClientSize.Width;
        _status.AutoEllipsis = true;
        _status.MaximumSize = Size.Empty;
        _kpi.Compact();
        _mix.MinimumSize = Size.Empty;
        var kpiSize = _kpi.GetPreferredSize(new Size(int.MaxValue, 0));
        var remain = inner - kpiSize.Width - 16;
        var gap = 24;
        var hostW = Math.Max(1, remain - gap - 16);
        FillMixText(hostW);
        if (remain >= UiLayout.ScalePx(200, DeviceDpi) + gap)
        {
            var valueH = KpiValueBottom();
            if (valueH < 8)
            {
                var padB = _kpi.Controls.Count > 0 ? _kpi.Controls[0].Margin.Bottom : 0;
                valueH = Math.Max(8, kpiSize.Height - padB);
            }
            _mix.AutoSize = true;
            _mix.MaximumSize = new Size(hostW, 0);
            var mixW = _mix.GetPreferredSize(new Size(hostW, 0)).Width;
            hostW = Math.Max(1, Math.Min(hostW, mixW + 4));
            _mix.AutoSize = false;
            _mix.TextAlign = ContentAlignment.BottomLeft;
            _mix.Dock = DockStyle.Fill;
            _mixHost.AutoSize = false;
            _mixHost.Margin = new Padding(gap, 0, 0, 0);
            _mixHost.MaximumSize = new Size(hostW, valueH);
            _mixHost.Size = new Size(hostW, valueH);
        }
        else
        {
            _mix.Dock = DockStyle.Top;
            _mix.AutoSize = true;
            _mix.TextAlign = ContentAlignment.TopLeft;
            _mix.MaximumSize = new Size(inner, 0);
            _mixHost.AutoSize = true;
            _mixHost.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _mixHost.MaximumSize = new Size(inner, 0);
            _mixHost.Margin = new Padding(0, 2, 0, 10);
        }
        _summaryBar.WrapTo(inner);
        LayoutToolbar();
    }

    void FillMixText(int remain)
    {
        if (string.IsNullOrEmpty(_mixKinds) && string.IsNullOrEmpty(_mixSources))
        {
            _mix.Text = _mixSpend;
            return;
        }
        var joined = string.IsNullOrEmpty(_mixSources) ? _mixKinds : _mixKinds + "    " + _mixSources;
        var joinW = TextRenderer.MeasureText(joined, _mix.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
        var head = joinW <= Math.Max(1, remain)
            ? joined
            : string.IsNullOrEmpty(_mixSources) ? _mixKinds : _mixKinds + "\n" + _mixSources;
        _mix.Text = string.IsNullOrWhiteSpace(_mixSpend) ? head : head + "\n" + _mixSpend;
    }

    int KpiValueBottom()
    {
        var max = 0;
        foreach (Control box in _kpi.Controls)
        {
            if (box.Controls.Count < 2) continue;
            max = Math.Max(max, box.Top + box.Controls[1].Bottom);
        }
        return max;
    }

    bool _syncingMix;

    void SyncMixHost()
    {
        if (_syncingMix || _mixHost.Left <= _kpi.Left + 8) return;
        var h = KpiValueBottom();
        if (h < 8 || Math.Abs(_mixHost.Height - h) < 2) return;
        _syncingMix = true;
        try { _mixHost.Height = h; }
        finally { _syncingMix = false; }
    }

    void LayoutToolbar()
    {
        var inner = Math.Max(80, ClientSize.Width - _root.Padding.Horizontal);
        var actionsW = _actionBar.GetPreferredSize(Size.Empty).Width + _actionBar.Margin.Horizontal;
        var filterW = Math.Max(80, inner - actionsW - 8);
        _filterBar.WrapTo(filterW);
    }

    bool _adapting;

    void AdaptChart()
    {
        if (_adapting || _root.RowStyles.Count <= 3) return;
        _adapting = true;
        try
        {
            var shortWin = WindowState != FormWindowState.Maximized && ClientSize.Height < UiLayout.ScalePx(820, DeviceDpi);
            var chartShare = shortWin ? 34f : 40f;
            var row = _root.RowStyles[2];
            if (row.SizeType == SizeType.Percent && Math.Abs(row.Height - chartShare) < 0.5f) return;
            _root.RowStyles[2].SizeType = SizeType.Percent;
            _root.RowStyles[2].Height = chartShare;
            _root.RowStyles[3].SizeType = SizeType.Percent;
            _root.RowStyles[3].Height = 100f - chartShare;
        }
        finally
        {
            _adapting = false;
        }
    }

    static DataGridView MakeGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(grid, true);
        UiChrome.StyleGrid(grid);
        grid.DataError += (_, e) => e.ThrowException = false;
        return grid;
    }

    public void RequestSync() => _ = SyncAsync(false);

    static Control FilterTag(string label, Control field, bool first = false)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(first ? 0 : 10, 0, 0, 0),
        };
        row.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 8, 6, 0),
        });
        field.Margin = new Padding(0, 2, 0, 2);
        row.Controls.Add(field);
        return row;
    }

    void UpdateScopeVisible(bool isTeam)
    {
        _scopeTag.Visible = isTeam;
        if (!isTeam)
        {
            _teamScope = false;
            if (_scope.SelectedIndex != 0) _scope.SelectedIndex = 0;
        }
    }

    async Task SyncAsync(bool forceFull)
    {
        if (_syncing) return;
        _syncing = true;
        _ready = true;
        _syncBtn.Enabled = false;
        var st = _state();
        LoadSavedDates(st);
        UpdateScopeVisible(st.IsTeam);
        if (string.IsNullOrWhiteSpace(st.Token))
        {
            _status.Text = StatusText.FormatReportSyncResult(0, 0, "", hasToken: false);
            _syncing = false;
            _syncBtn.Enabled = true;
            Reveal();
            return;
        }
        _status.Text = StatusText.FormatReportCacheStatus(0, st.AccountLabel);
        var accountId = st.AccountId;
        var team = _teamScope;
        var cached = await Task.Run(() => UsageEvents.Load(accountId, team));
        if (IsDisposed)
        {
            _syncing = false;
            return;
        }
        _all = cached;
        FillModels();
        await RenderAsync();
        _status.Text = StatusText.FormatReportCacheStatus(_all.Count, st.AccountLabel);
        try
        {
            var beforeCount = _all.Count;
            var beforeStamp = _all.Count == 0 ? 0 : _all[0].TimestampMs;
            var result = await UsageEvents.SyncAsync(_client, st.Token, st.AccountId, st.Usage, _teamScope, onPage: page =>
            {
                var text = StatusText.FormatReportSyncProgress(page);
                void Apply() { if (!IsDisposed) _status.Text = text; }
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke(Apply);
                else Apply();
            });
            var changed = forceFull || result.Fetched > 0 || result.Events.Count != beforeCount
                || (result.Events.Count > 0 && result.Events[0].TimestampMs != beforeStamp);
            _all = result.Events;
            if (changed)
            {
                FillModels();
                await RenderAsync();
            }
            var stamp = DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            _status.Text = StatusText.FormatReportSyncResult(
                _all.Count, result.Fetched, stamp, result.Truncated, result.TotalAvailable, result.Note);
        }
        catch (CursorApiException ex)
        {
            FillModels();
            await RenderAsync();
            _status.Text = StatusText.FormatReportSyncError(ex.Message);
        }
        catch (Exception ex)
        {
            FillModels();
            await RenderAsync();
            _status.Text = StatusText.FormatReportSyncError(ex.Message);
        }
        finally
        {
            _syncing = false;
            _syncBtn.Enabled = true;
        }
    }

    UsageReportFilter CurrentFilter()
    {
        var kind = _kind.SelectedIndex switch { 1 => UsageEvents.KindIncluded, 2 => UsageEvents.KindFree, 3 => UsageEvents.KindOnDemand, _ => "" };
        var category = _category.SelectedIndex switch
        {
            1 => UsageEvents.CategoryFirstParty,
            2 => UsageEvents.CategoryApi,
            3 => UsageEvents.CategoryGrokBot,
            _ => "",
        };
        var model = _model.SelectedIndex > 0 ? _model.SelectedItem?.ToString() ?? "" : "";
        bool? cloud = _cloud.SelectedIndex switch { 1 => false, 2 => true, _ => null };
        var rawStart = _startDate.Checked ? _startDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
        var rawEnd = _endDate.Checked ? _endDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
        var (start, end, _) = UsageEvents.NormalizeReportRange(rawStart, rawEnd);
        return new UsageReportFilter
        {
            Kind = kind,
            Category = category,
            Model = model,
            Headless = cloud,
            StartDate = start,
            EndDate = end,
        };
    }

    void LoadSavedDates(ReportState st)
    {
        if (st.AccountId == _loadedAccountId) return;
        _loadingDates = true;
        ResetFilters();
        ApplyDatePicker(_startDate, st.ReportStartDate);
        ApplyDatePicker(_endDate, st.ReportEndDate);
        _loadedAccountId = st.AccountId;
        _loadingDates = false;
    }

    void ResetFilters()
    {
        _kind.SelectedIndex = 0;
        _category.SelectedIndex = 0;
        if (_model.Items.Count > 0) _model.SelectedIndex = 0;
        _cloud.SelectedIndex = 0;
        _teamScope = false;
        if (_scope.SelectedIndex != 0) _scope.SelectedIndex = 0;
    }

    static void ApplyDatePicker(DateTimePicker picker, string raw)
    {
        var date = UsageEvents.SanitizeReportDate(raw);
        if (date.Length == 0)
        {
            picker.Checked = false;
            return;
        }
        if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            if (dt < picker.MinDate) dt = picker.MinDate;
            if (dt > picker.MaxDate) dt = picker.MaxDate;
            picker.Value = dt;
            picker.Checked = true;
        }
        else picker.Checked = false;
    }

    void OnDateChanged()
    {
        if (_loadingDates || !_ready) return;
        var st = _state();
        var start = _startDate.Checked ? _startDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
        var end = _endDate.Checked ? _endDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
        var (normStart, normEnd, swapped) = UsageEvents.NormalizeReportRange(start, end);
        if (swapped)
        {
            _loadingDates = true;
            ApplyDatePicker(_startDate, normStart);
            ApplyDatePicker(_endDate, normEnd);
            _loadingDates = false;
        }
        _persistDates?.Invoke(st.AccountId, normStart, normEnd);
        _ = RenderAsync();
    }

    void FillModels()
    {
        var selected = _model.SelectedItem?.ToString();
        var names = _all.Select(e => e.Model).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(n => n).ToList();
        _model.BeginUpdate();
        _model.Items.Clear();
        _model.Items.Add("全部模型");
        foreach (var name in names) _model.Items.Add(name);
        var idx = selected is null or "全部模型" ? 0 : _model.Items.IndexOf(selected);
        _model.SelectedIndex = idx >= 0 ? idx : 0;
        _model.EndUpdate();
        _model.FitDropDownWidth();
    }

    async Task RenderAsync()
    {
        var gen = ++_renderGen;
        var filter = CurrentFilter();
        var all = _all;
        var spend = _state().Spend;
        var allocation = _state().Allocation;
        var hourly = _chart.Hourly;
        var hidden = _chart.HiddenSnapshot();
        var built = await Task.Run(() =>
        {
            var report = UsageEvents.BuildReport(all, filter, spend, allocation);
            var chart = UsageEvents.BuildChart(report.Events, hourly, hidden);
            return (report, chart);
        });
        if (IsDisposed || gen != _renderGen) return;
        PaintOnce(() => ApplyReport(built.report, built.chart));
        Reveal();
    }

    bool _revealed;

    void PaintOnce(Action apply)
    {
        NativeTheme.SetRedraw(this, false);
        SuspendLayout();
        try { apply(); }
        finally
        {
            ResumeLayout(true);
            NativeTheme.SetRedraw(this, true);
        }
    }

    void Reveal()
    {
        if (_revealed) return;
        _revealed = true;
        if (Math.Abs(Opacity - 1) > 0.01) Opacity = 1;
    }

    void ApplyReport(UsageReport report, UsageChartSeries chart)
    {
        ApplySummary(report, chart);
        _pendingReport = report;
        ApplyDetails();
    }

    UsageReport? _pendingReport;

    void ApplySummary(UsageReport report, UsageChartSeries chart)
    {
        _mixKinds = $"套餐内 {report.IncludedCount} · 免费 {report.FreeCount} · 按需 {report.OnDemandCount}";
        _mixSources = $"First-party {report.FirstPartyCount} · API {report.ApiCount} · Grok Bot {report.GrokBotCount}";
        if (report.HeadlessCount > 0) _mixSources += $" · 云端 {report.HeadlessCount}";
        var discount = UsageEvents.FormatDiscount(report.TotalCny, report.TotalCents, report.UsdCnyRate);
        var quota = QuotaKpi();
        _mixSpend = SpendKpi(report).Trim();
        _kpi.Bind(
            ("请求", report.EventCount.ToString(CultureInfo.InvariantCulture)),
            ("Token", UsageParser.FormatTokenCount(report.TotalTokens)),
            ("费用", report.HasCost ? UsageParser.FormatUsdCents(report.TotalCents) : ""),
            ("折扣", discount == "—" ? "" : discount),
            ("每百万Token", StatusText.FormatReportPerMillionKpi(report.TotalCny, report.TotalTokens)),
            ("企业额度（展示）", quota.Length == 0 ? "" : quota.Replace("企业额度（展示）", "", StringComparison.Ordinal).Trim()));
        WrapKpi();
        _chart.Bind(report.Events, chart);
        AdaptChart();
        _exportBtn.Enabled = report.Events.Count > 0;
    }

    void ApplyDetails()
    {
        if (IsDisposed || _pendingReport is null) return;
        var report = _pendingReport;
        _models.SuspendLayout();
        _models.Rows.Clear();
        foreach (var row in report.Models)
        {
            _models.Rows.Add(
                row.Name,
                UsageParser.FormatTokenCount(row.Tokens),
                row.Cents > 0 ? UsageParser.FormatUsdCents(row.Cents) : "—",
                row.Cny > 0 ? UsageEvents.FormatCny(row.Cny) : "—",
                UsageEvents.FormatDiscount(row.Cny, row.Cents, report.UsdCnyRate),
                row.Count.ToString(CultureInfo.InvariantCulture),
                row.HeadlessCount > 0 ? row.HeadlessCount.ToString(CultureInfo.InvariantCulture) : "—");
        }
        _models.ResumeLayout();
        if (_models.Columns["cloud"] is { } modelCloud)
            modelCloud.Visible = report.Models.Any(row => row.HeadlessCount > 0);

        _detailEvents = report.Events;
        if (_grid.Columns["user"] is { } userCol)
            userCol.Visible = _state().IsTeam;
        if (_grid.RowCount != _detailEvents.Count)
            _grid.RowCount = _detailEvents.Count;
        else
            _grid.Invalidate();
        var empty = StatusText.FormatReportFilterEmpty(_all.Count);
        _detailsLabel.Text = _all.Count == 0 || report.Events.Count > 0 || empty.Length == 0
            ? "明细"
            : "明细 · " + empty;
        var wide = ClientSize.Width >= UiLayout.ScalePx(1280, DeviceDpi);
        ApplyGridMetrics(_models, UiLayout.ScalePx(wide ? 32 : DesignHeaderH, DeviceDpi), UiLayout.ScalePx(wide ? 26 : DesignRowH, DeviceDpi), ModelMinWidths, DeviceDpi);
        ApplyGridMetrics(_grid, UiLayout.ScalePx(wide ? 32 : DesignHeaderH, DeviceDpi), UiLayout.ScalePx(wide ? 26 : DesignRowH, DeviceDpi), DetailMinWidths, DeviceDpi);
        ApplyColumnSizing(DeviceDpi);
        PlaceSplitter();
    }

    void ApplyColumnSizing(int dpi)
    {
        FitFill(_models, dpi, ModelMinWidths, null);
        FitFill(_grid, dpi, DetailMinWidths, DetailSample);
    }

    string DetailSample(DataGridViewColumn col, int row)
    {
        if ((uint)row >= (uint)_detailEvents.Count) return "";
        var ev = _detailEvents[row];
        return col.Name switch
        {
            "date" => UsageEvents.FormatTime(ev.TimestampMs),
            "user" => ev.UserEmail,
            "kind" => UsageEvents.KindLabel(ev.Kind),
            "model" => ev.Model,
            "tokens" => UsageParser.FormatTokenCount(ev.Tokens),
            "cost" => DetailCost(ev),
            "cny" => UsageEvents.FormatEventCny(ev),
            "cloud" => ev.IsHeadless ? "是" : "否",
            _ => "",
        };
    }

    void FitFill(DataGridView grid, int dpi, int[] minDesign, Func<DataGridViewColumn, int, string>? sample)
    {
        var pad = grid.DefaultCellStyle.Padding.Horizontal + UiLayout.ScalePx(12, dpi);
        var font = grid.DefaultCellStyle.Font ?? grid.Font;
        var headerFont = grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font;
        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var visible = new List<DataGridViewColumn>();
        foreach (DataGridViewColumn col in grid.Columns)
        {
            if (!col.Visible) continue;
            visible.Add(col);
            var align = col.DefaultCellStyle.Alignment;
            if (align == DataGridViewContentAlignment.NotSet)
                align = DataGridViewContentAlignment.MiddleLeft;
            col.HeaderCell.Style.Alignment = align;
            col.HeaderCell.Style.Padding = grid.ColumnHeadersDefaultCellStyle.Padding;
        }
        if (visible.Count == 0) return;

        var weights = new float[visible.Count];
        for (var i = 0; i < visible.Count; i++)
        {
            var col = visible[i];
            var min = col.Index < minDesign.Length
                ? UiLayout.ScalePx(minDesign[col.Index], dpi)
                : UiLayout.ScalePx(48, dpi);
            var w = TextRenderer.MeasureText(col.HeaderText, headerFont, Size.Empty, flags).Width;
            var take = sample is null ? Math.Min(32, grid.Rows.Count) : Math.Min(32, _detailEvents.Count);
            for (var r = 0; r < take; r++)
            {
                var text = sample is null
                    ? Convert.ToString(grid.Rows[r].Cells[col.Index].Value)
                    : sample(col, r);
                if (!string.IsNullOrEmpty(text))
                    w = Math.Max(w, TextRenderer.MeasureText(text, font, Size.Empty, flags).Width);
            }
            w += pad;
            if (col.Name is "model" or "date" or "user")
                w += w / 5;
            weights[i] = Math.Max(min, w);
            col.MinimumWidth = min;
            col.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }
        var sum = weights.Sum();
        if (sum <= 0) return;
        for (var i = 0; i < visible.Count; i++)
            visible[i].FillWeight = Math.Max(1f, weights[i] / sum * 100f);
    }

    void PlaceSplitter()
    {
        if (_splitReady || _split.Height <= 0) return;
        var readyAt = WindowState == FormWindowState.Maximized
            ? UiLayout.ScalePx(280, DeviceDpi)
            : UiLayout.ScalePx(160, DeviceDpi);
        if (_split.Height < readyAt) return;
        var p1 = UiLayout.ScalePx(168, DeviceDpi);
        var p2 = UiLayout.ScalePx(180, DeviceDpi);
        if (_split.Height > p1 + p2 + _split.SplitterWidth)
        {
            try
            {
                _split.Panel1MinSize = p1;
                _split.Panel2MinSize = p2;
            }
            catch (InvalidOperationException)
            {
                // Splitter not measured yet; keep the constructor mins.
            }
        }
        var min = _split.Panel1MinSize;
        var max = Math.Max(min, _split.Height - _split.Panel2MinSize - _split.SplitterWidth);
        if (max <= min) return;
        var want = (int)(_split.Height * 0.48);
        _split.SplitterDistance = Math.Clamp(want, min, max);
        _splitReady = true;
    }

    string QuotaKpi()
    {
        var usage = _state().Usage;
        if (usage is null || !usage.ShowsAmount) return "";
        return $"    企业额度（展示） {UsageParser.FormatSpendRange(usage.UsedCents, usage.LimitCents)}";
    }

    static string SpendKpi(UsageReport report) =>
        StatusText.FormatReportSpendKpi(report.TotalCny, report.PlanCny, report.OnDemandCny, report.UsdCnyRate, report.UsesActualCny, report.WindowPlanCny);

    void OnDetailCellValue(object? sender, DataGridViewCellValueEventArgs e)
    {
        if ((uint)e.RowIndex >= (uint)_detailEvents.Count) return;
        if (e.ColumnIndex < 0 || e.ColumnIndex >= _grid.Columns.Count) return;
        var ev = _detailEvents[e.RowIndex];
        e.Value = _grid.Columns[e.ColumnIndex].Name switch
        {
            "date" => UsageEvents.FormatTime(ev.TimestampMs),
            "user" => ev.UserEmail,
            "kind" => UsageEvents.KindLabel(ev.Kind),
            "model" => ev.Model,
            "tokens" => UsageParser.FormatTokenCount(ev.Tokens),
            "cost" => DetailCost(ev),
            "cny" => UsageEvents.FormatEventCny(ev),
            "cloud" => ev.IsHeadless ? "是" : "否",
            _ => "",
        };
    }

    static string DetailCost(UsageEvent ev)
    {
        if (ev.Kind == UsageEvents.KindFree) return "免费";
        var cents = UsageEvents.CostCents(ev);
        return cents > 0 ? UsageParser.FormatUsdCents(cents) : "—";
    }

    void ExportCsv()
    {
        var spend = _state().Spend;
        var allocation = _state().Allocation;
        var report = UsageEvents.BuildReport(_all, CurrentFilter(), spend, allocation);
        if (report.Events.Count == 0)
        {
            _status.Text = StatusText.FormatExportEmpty();
            return;
        }
        using var dlg = new SaveFileDialog
        {
            Filter = "CSV 文件 (*.csv)|*.csv",
            FileName = StatusText.FormatExportFilename("cursor-usage", _state().AccountLabel),
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, UsageEvents.ToCsv(report.Events, spend, _all, allocation));
            _status.Text = "已导出 " + dlg.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "用量报表", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

/// <summary>
/// Flow layout that measures wrap height against a real width. The stock
/// <see cref="FlowLayoutPanel"/> treats an empty proposed width as "stack
/// every child", which inflates AutoSize rows into a tall blank band.
/// </summary>
sealed class WrapBar : FlowLayoutPanel
{
    public WrapBar()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = true;
    }

    public void WrapTo(int width)
    {
        var w = Math.Max(1, width);
        MaximumSize = new Size(w, 10000);
        Width = w;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var w = proposedSize.Width;
        if (w <= 1) w = MaximumSize.Width;
        if (w <= 1) w = Width;
        if (w <= 1 && Parent is not null) w = Math.Max(1, Parent.ClientSize.Width);
        return base.GetPreferredSize(new Size(Math.Max(1, w), 0));
    }
}
