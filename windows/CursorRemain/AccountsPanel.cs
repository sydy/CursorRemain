using System.Drawing.Drawing2D;
using CursorTokenCore;

namespace CursorRemain;

enum RowTone
{
    Muted,
    Accent,
    Good,
    Warn,
    Danger,
}

/// <summary>Everything a table row needs to paint itself, snapshotted at refresh time.</summary>
readonly record struct AccountRowModel(
    Account Account,
    string Plan,
    string Stamp,
    string Pill,
    int? Percent,
    bool Active,
    bool Selected,
    bool Expanded,
    string StatusText,
    RowTone StatusTone);

/// <summary>Column geometry shared by the card header and every row, so header labels stay on their columns.</summary>
readonly struct AccountColumns
{
    public readonly int Left;
    public readonly int PadRight;
    public readonly int QuotaX;
    public readonly int QuotaW;
    public readonly int StatusX;
    public readonly int StatusW;
    public readonly int OpsX;
    public readonly int OpsW;

    AccountColumns(int left, int padRight, int quotaX, int quotaW, int statusX, int statusW, int opsX, int opsW)
    {
        Left = left;
        PadRight = padRight;
        QuotaX = quotaX;
        QuotaW = quotaW;
        StatusX = statusX;
        StatusW = statusW;
        OpsX = opsX;
        OpsW = opsW;
    }

    public static AccountColumns Compute(int width, int dpi)
    {
        var left = UiLayout.ScalePx(14, dpi);
        var padRight = UiLayout.ScalePx(14, dpi);
        var opsW = UiLayout.ScalePx(52, dpi);
        var statusW = UiLayout.ScalePx(80, dpi);
        var quotaW = UiLayout.ScalePx(136, dpi);
        var quotaX = Math.Max(left + UiLayout.ScalePx(92, dpi), width - padRight - opsW - statusW - quotaW);
        var statusX = quotaX + quotaW;
        var opsX = statusX + statusW;
        return new AccountColumns(left, padRight, quotaX, quotaW, statusX, statusW, opsX, opsW);
    }
}

static class AccountFonts
{
    public static readonly Font Name = UiChrome.UiFont(9f, FontStyle.Bold);
    public static readonly Font Meta = UiChrome.UiFont(8f);
    public static readonly Font Pill = UiChrome.UiFont(8f);
    public static readonly Font Caption = UiChrome.UiFont(8f);
}

static class AccountPaint
{
    public static Color Mix(Color a, Color b, double t)
    {
        var k = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (int)Math.Round(a.A + (b.A - a.A) * k),
            (int)Math.Round(a.R + (b.R - a.R) * k),
            (int)Math.Round(a.G + (b.G - a.G) * k),
            (int)Math.Round(a.B + (b.B - a.B) * k));
    }

    /// <summary>琥珀色不在 FormTone 调色板里，这里单独给额度中档用。</summary>
    public static Color Warn => UiChrome.UseLightTheme()
        ? Color.FromArgb(176, 106, 0)
        : Color.FromArgb(240, 180, 60);

    public static Color Of(RowTone tone) => tone switch
    {
        RowTone.Accent => UiChrome.ColorOf(UiChrome.Tone.Accent),
        RowTone.Good => UiChrome.ColorOf(UiChrome.Tone.Good),
        RowTone.Warn => Warn,
        RowTone.Danger => UiChrome.ColorOf(UiChrome.Tone.Danger),
        _ => UiChrome.ColorOf(UiChrome.Tone.Secondary),
    };

    /// <summary>额度分档：≥70% 绿、≥35% 琥珀、其余红，和效果图一致。</summary>
    public static RowTone TierOf(int pct) => pct >= 70 ? RowTone.Good : pct >= 35 ? RowTone.Warn : RowTone.Danger;
}

/// <summary>
/// The account table: a painted header strip, one painted row per account, and the
/// inline editor pinned directly under the selected row. Scrolls with the settings page instead of
/// owning a scrollbar, so the editor never detaches from the row it belongs to.
/// </summary>
sealed class AccountsCard : Panel
{
    public const int HeaderDesignHeight = 30;
    public const int RowDesignHeight = 46;

    public event Action<Account>? RowSelected;
    public event Action<Account>? RowToggleClicked;

    readonly List<AccountRowModel> _models = [];
    Control? _editor;
    bool _laying;
    int _hover = -1;

    public AccountsCard()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        Margin = new Padding(0, 0, 0, 8);
    }

    int Dpi => DeviceDpi > 0 ? DeviceDpi : 96;
    int S(int px) => UiLayout.ScalePx(px, Dpi);

    // 自绘卡片没有天然首选宽度（Panel 默认 200），而外层纵向栈按首选宽度收敛；
    // 这里按 UiForms 写入的 MinimumSize 上报，整条 AutoSize 链才不会被压窄。
    public override Size GetPreferredSize(Size proposed)
        => new(Math.Max(MinimumSize.Width, S(320)), Math.Max(MinimumSize.Height, Height));

    public int HeaderHeight => S(HeaderDesignHeight);
    public int RowHeight => S(RowDesignHeight);

    public bool EditorMounted => _editor is not null;

    public int RowCount => _models.Count;

    public void SetContent(IEnumerable<AccountRowModel> models, Control? editor)
    {
        _models.Clear();
        _models.AddRange(models);
        if (_editor != editor)
        {
            if (_editor is not null)
            {
                _editor.SizeChanged -= OnEditorSizeChanged;
                if (_editor.Parent == this) Controls.Remove(_editor);
            }
            _editor = editor;
            if (_editor is not null)
            {
                _editor.SizeChanged += OnEditorSizeChanged;
                Controls.Add(_editor);
            }
        }
        _hover = -1;
        PerformLayout();
        Invalidate();
    }

    void OnEditorSizeChanged(object? sender, EventArgs e)
    {
        if (_laying) return;
        PerformLayout();
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiChrome.ApplyRoundRegion(this, S(10));
    }

    int RowTop(int index)
    {
        var y = HeaderHeight;
        var rowH = RowHeight;
        for (var i = 0; i < index && i < _models.Count; i++)
        {
            y += rowH;
            if (_models[i].Expanded && _editor is not null)
                y += _editor.Height + S(4);
        }
        return y;
    }

    Rectangle RowBounds(int index, int width)
        => new(0, RowTop(index), width, RowHeight);

    int IndexAt(Point p)
    {
        if (p.Y < HeaderHeight) return -1;
        for (var i = 0; i < _models.Count; i++)
        {
            var top = RowTop(i);
            if (p.Y >= top && p.Y < top + RowHeight) return i;
        }
        return -1;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_laying) return;
        var width = ClientSize.Width;
        if (width <= 4) return;
        _laying = true;
        try
        {
            var y = HeaderHeight;
            var rowH = RowHeight;
            for (var i = 0; i < _models.Count; i++)
            {
                y += rowH;
                if (_models[i].Expanded && _editor is { } inline)
                {
                    inline.Width = width;
                    inline.PerformLayout();
                    inline.SetBounds(0, y, width, inline.Height);
                    y += inline.Height + S(4);
                }
            }
            var want = Math.Max(S(70), y);
            if (Height != want)
                Height = want;
        }
        finally
        {
            _laying = false;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = IndexAt(e.Location);
        if (index != _hover)
        {
            var width = ClientSize.Width;
            if (_hover >= 0 && _hover < _models.Count) Invalidate(RowBounds(_hover, width));
            _hover = index;
            if (_hover >= 0) Invalidate(RowBounds(_hover, width));
        }
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hover >= 0 && _hover < _models.Count)
        {
            var width = ClientSize.Width;
            Invalidate(RowBounds(_hover, width));
        }
        _hover = -1;
        Cursor = Cursors.Default;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        var index = IndexAt(e.Location);
        if (index < 0) return;
        var model = _models[index];
        var cols = AccountColumns.Compute(ClientSize.Width, Dpi);
        if (e.X >= cols.OpsX) RowToggleClicked?.Invoke(model.Account);
        else RowSelected?.Invoke(model.Account);
    }

    static string InitialOf(string label)
        => string.IsNullOrWhiteSpace(label) ? "?" : System.Globalization.StringInfo.GetNextTextElement(label.Trim());

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(UiChrome.ColorOf(UiChrome.Tone.Window));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var pal = UiChrome.Tone;
        var header = HeaderHeight;
        var cols = AccountColumns.Compute(Width, Dpi);

        using (var strip = new SolidBrush(UiChrome.ColorOf(pal.Header)))
            g.FillRectangle(strip, 0, 0, Width, header);
        using (var line = new SolidBrush(UiChrome.ColorOf(pal.Hairline)))
            g.FillRectangle(line, 0, header - 1, Width, 1);

        var secondary = UiChrome.ColorOf(pal.Secondary);
        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(g, "账号", AccountFonts.Caption, new Rectangle(cols.Left, 0, cols.QuotaX - cols.Left, header), secondary, flags);
        TextRenderer.DrawText(g, "额度", AccountFonts.Caption, new Rectangle(cols.QuotaX, 0, cols.QuotaW, header), secondary, flags);
        TextRenderer.DrawText(g, "状态", AccountFonts.Caption, new Rectangle(cols.StatusX, 0, cols.StatusW, header), secondary, flags);
        TextRenderer.DrawText(g, "操作", AccountFonts.Caption, new Rectangle(cols.OpsX, 0, Width - cols.OpsX - cols.PadRight, header), secondary,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        // 所有行都画在同一张图上：行不再是独立子窗口，滚动/切换时只重画这一层。
        var y = header;
        for (var i = 0; i < _models.Count; i++)
        {
            DrawRow(g, _models[i], i, y);
            y += RowHeight;
            if (_models[i].Expanded && _editor is not null)
                y += _editor.Height + S(4);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        UiChrome.DrawRoundBorder(g, UiChrome.ColorOf(pal.Hairline), UiChrome.ColorOf(pal.Window), Width, Height, S(10));
        g.SmoothingMode = SmoothingMode.None;
    }

    void DrawRow(Graphics g, AccountRowModel model, int index, int top)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var pal = UiChrome.Tone;
        var cols = AccountColumns.Compute(Width, Dpi);
        var window = UiChrome.ColorOf(pal.Window);
        var text = UiChrome.ColorOf(pal.Text);
        var secondary = UiChrome.ColorOf(pal.Secondary);
        var accent = UiChrome.ColorOf(pal.Accent);
        var selected = model.Selected;
        var rowH = RowHeight;
        var mid = top + rowH / 2;

        var bg = selected
            ? AccountPaint.Mix(window, UiChrome.ColorOf(pal.Selection), 0.55)
            : index == _hover ? UiChrome.ColorOf(pal.Button) : window;
        using (var fill = new SolidBrush(bg))
            g.FillRectangle(fill, 0, top, Width, rowH);

        if (selected)
        {
            using var bar = new SolidBrush(accent);
            g.FillRectangle(bar, 0, top, S(2), rowH);
        }

        // Avatar
        var avatar = S(24);
        var ax = cols.Left;
        var ay = mid - avatar / 2;
        using (var path = UiChrome.RoundRect(new Rectangle(ax, ay, avatar, avatar), S(6)))
        using (var face = new SolidBrush(model.Active ? AccountPaint.Mix(bg, accent, 0.20) : UiChrome.ColorOf(pal.Button)))
            g.FillPath(face, path);
        TextRenderer.DrawText(g, InitialOf(model.Account.DisplayLabel), AccountFonts.Meta, new Rectangle(ax, ay, avatar, avatar),
            model.Active ? accent : secondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        // Name + kind pill: the pill hugs the name instead of floating at the column edge.
        var nameX = ax + avatar + S(9);
        var pillW = string.IsNullOrEmpty(model.Pill)
            ? 0
            : TextRenderer.MeasureText(model.Pill, AccountFonts.Pill, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width + S(10);
        var nameRoom = Math.Max(S(40), cols.QuotaX - S(6) - nameX - (pillW > 0 ? pillW + S(8) : 0));
        var rawNameW = TextRenderer.MeasureText(model.Account.DisplayLabel, AccountFonts.Name, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
        var nameW = Math.Max(S(20), Math.Min(rawNameW + S(12), nameRoom));
        TextRenderer.DrawText(g, model.Account.DisplayLabel, AccountFonts.Name,
            new Rectangle(nameX, top + S(3), nameW, S(20)), text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (pillW > 0)
        {
            var pillRect = new Rectangle(nameX + nameW + S(8), top + S(6), pillW, S(15));
            using (var path = UiChrome.RoundRect(pillRect, S(4)))
            using (var face = new SolidBrush(model.Account.IsDoubao ? AccountPaint.Mix(bg, accent, 0.18) : UiChrome.ColorOf(pal.Hairline)))
                g.FillPath(face, path);
            TextRenderer.DrawText(g, model.Pill, AccountFonts.Pill, pillRect,
                model.Account.IsDoubao ? accent : secondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        var meta = string.Join("  ·  ", new[] { model.Plan, model.Stamp }.Where(x => !string.IsNullOrEmpty(x)));
        if (meta.Length > 0)
            TextRenderer.DrawText(g, meta, AccountFonts.Meta,
                new Rectangle(nameX, top + S(23), Math.Max(S(30), cols.QuotaX - S(6) - nameX), S(16)), secondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        // Quota bar + percent
        var trackW = Math.Max(S(24), cols.QuotaW - S(42));
        var trackH = S(4);
        var trackY = mid - trackH / 2;
        using (var path = UiChrome.RoundRect(new Rectangle(cols.QuotaX, trackY, trackW, trackH), trackH / 2))
        using (var track = new SolidBrush(UiChrome.ColorOf(pal.Hairline)))
            g.FillPath(track, path);
        var pct = model.Percent is { } value ? Math.Clamp(value, 0, 100) : (int?)null;
        if (pct is { } p && p > 0)
        {
            var tone = AccountPaint.TierOf(p);
            var fillW = Math.Max(S(4), (int)Math.Round(trackW * p / 100.0));
            using var path = UiChrome.RoundRect(new Rectangle(cols.QuotaX, trackY, fillW, trackH), trackH / 2);
            using var fill = new SolidBrush(AccountPaint.Of(tone));
            g.FillPath(fill, path);
        }
        var pctText = pct is { } shown ? shown + "%" : "—";
        TextRenderer.DrawText(g, pctText, AccountFonts.Meta,
            new Rectangle(cols.QuotaX + trackW + S(6), mid - S(9), S(36), S(18)),
            pct is { } c ? AccountPaint.Of(AccountPaint.TierOf(c)) : secondary,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        // Status dot + label
        var dot = S(6);
        using (var brush = new SolidBrush(AccountPaint.Of(model.StatusTone)))
            g.FillEllipse(brush, cols.StatusX, mid - dot / 2, dot, dot);
        TextRenderer.DrawText(g, model.StatusText, AccountFonts.Meta,
            new Rectangle(cols.StatusX + S(10), mid - S(9), Math.Max(S(30), cols.StatusW - S(12)), S(18)),
            AccountPaint.Of(model.StatusTone),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        // Row action
        TextRenderer.DrawText(g, model.Expanded ? "收起" : "编辑", AccountFonts.Meta,
            new Rectangle(cols.OpsX, mid - S(9), Width - cols.OpsX - cols.PadRight, S(18)),
            selected || model.Expanded ? accent : secondary,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        g.SmoothingMode = SmoothingMode.None;
        using (var line = new SolidBrush(UiChrome.ColorOf(pal.Hairline)))
            g.FillRectangle(line, S(14), top + rowH - 1, Math.Max(1, Width - S(28)), 1);
        g.SmoothingMode = SmoothingMode.AntiAlias;
    }
}

sealed class AccountEditorPanel : Panel
{
    public const string DoubaoNote = "豆包额度只在本机显示，Cookie 加密保存且不参与云同步；拉取间隔最短 5 分钟。";

    public event EventHandler? RenameClicked;
    public event EventHandler? DeleteClicked;
    public event EventHandler? ReloginClicked;

    readonly TableLayoutPanel _fields;
    readonly Control _startRow;
    readonly Control _durationRow;
    readonly Control _endRow;
    readonly Label _note;
    readonly Label _costNote;
    Rectangle _link1 = Rectangle.Empty;
    Rectangle _link2 = Rectangle.Empty;
    int _linkHover;
    string _caption = "编辑";
    string _name = "";
    string _link1Text = "重命名";
    string _link2Text = "删除账号";
    bool _doubao;
    bool _laying;
    int _padDpi = -1;

    public AccountEditorPanel(Control kindRow, Control channelRow, Control costRow, Control startRow, Control durationRow, Control endRow)
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        Margin = Padding.Empty;

        _fields = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 2,
            Dock = DockStyle.None,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        for (var i = 0; i < 3; i++)
            _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3f));
        _fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AddCell(kindRow, 0, 0);
        AddCell(channelRow, 1, 0);
        AddCell(costRow, 2, 0);
        AddCell(startRow, 0, 1);
        _fields.Controls.Add(durationRow, 1, 1);
        _fields.SetColumnSpan(durationRow, 2);
        durationRow.Dock = DockStyle.Fill;
        durationRow.Margin = new Padding(0, 0, 10, 2);

        _startRow = startRow;
        _durationRow = durationRow;
        _startRow.Visible = false;
        _durationRow.Visible = false;
        _endRow = endRow;
        _endRow.Visible = false;

        _costNote = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Text = "仅当前账号；短期号按 买价 ÷ 天数 × 30 折算，填了实际成本后按它分摊。",
            Margin = Padding.Empty,
        };

        _note = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Text = DoubaoNote,
            Visible = false,
            Margin = Padding.Empty,
        };

        Controls.Add(_fields);
        Controls.Add(_endRow);
        Controls.Add(_costNote);
        Controls.Add(_note);
    }

    void AddCell(Control control, int column, int row)
    {
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 0, 10, 2);
        _fields.Controls.Add(control, column, row);
    }

    int Dpi => DeviceDpi > 0 ? DeviceDpi : 96;
    int S(int px) => UiLayout.ScalePx(px, Dpi);

    bool _temporary;

    public void SetAccount(Account? account, bool doubao)
    {
        _doubao = doubao;
        _caption = doubao ? "豆包账号" : "编辑";
        _name = account?.DisplayLabel ?? "";
        _link1Text = doubao ? "重新登录" : "重命名";
        _link2Text = doubao ? "删除豆包账号" : "删除账号";
        _fields.Visible = !doubao;
        _costNote.Visible = !doubao;
        _note.Visible = doubao;
        _startRow.Visible = _temporary && !doubao;
        _durationRow.Visible = _temporary && !doubao;
        _endRow.Visible = _temporary && !doubao;
        PerformLayout();
        Invalidate();
    }

    /// <summary>Mirrors the 临时账号 toggle: the 截止 hint only makes sense for temporary accounts.</summary>
    public void SetTemporary(bool temporary)
    {
        _temporary = temporary;
        var show = temporary && !_doubao;
        if (_startRow.Visible == show && _durationRow.Visible == show && _endRow.Visible == show) return;
        _startRow.Visible = show;
        _durationRow.Visible = show;
        _endRow.Visible = show;
        PerformLayout();
        Invalidate();
    }

    void SyncPadding()
    {
        var dpi = Dpi;
        if (_padDpi == dpi) return;
        _padDpi = dpi;
        Padding = new Padding(S(16), S(10), S(14), S(6));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_laying) return;
        _laying = true;
        try
        {
            SyncPadding();
            var width = ClientSize.Width;
            if (width < S(80)) return;
            var left = Padding.Left;
            var inner = Math.Max(S(60), width - Padding.Horizontal);
            var y = Padding.Top + S(2);

            using (var font = UiChrome.UiFont(9f, FontStyle.Bold))
            {
                _link2 = LinkBounds(_link2Text, left + inner, y, font);
                _link1 = LinkBounds(_link1Text, _link2.Left - S(16), y, font);
            }
            var headH = Math.Max(S(18), _link2.Height);
            y += headH + S(8);

            if (_fields.Visible)
            {
                _fields.Width = inner;
                _fields.PerformLayout();
                _fields.Location = new Point(left, y);
                y += _fields.Height + S(2);
            }

            if (_endRow.Visible)
            {
                _endRow.Width = inner;
                if (_endRow is FieldRowPanel endFit) endFit.Fit(Dpi);
                _endRow.Location = new Point(left, y);
                y += _endRow.Height + S(2);
            }

            var block = _doubao ? _note : _costNote;
            block.MaximumSize = new Size(inner, 0);
            if (block.Visible)
            {
                var blockH = Math.Max(S(16), block.GetPreferredSize(new Size(inner, int.MaxValue)).Height);
                block.SetBounds(left, y, inner, blockH);
                y += blockH + S(2);
            }

            var want = Math.Max(S(88), y + Padding.Bottom);
            if (Height != want)
                Height = want;
        }
        finally
        {
            _laying = false;
        }
    }

    static Rectangle LinkBounds(string text, int rightX, int y, Font font)
    {
        var size = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        return new Rectangle(rightX - size.Width, y, size.Width, size.Height);
    }

    Color LinkColor(int index) => index == 2
        ? UiChrome.ColorOf(UiChrome.Tone.Danger)
        : UiChrome.ColorOf(UiChrome.Tone.Secondary);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hover = _link1.Contains(e.Location) ? 1 : _link2.Contains(e.Location) ? 2 : 0;
        Cursor = hover == 0 ? Cursors.Default : Cursors.Hand;
        if (hover != _linkHover)
        {
            _linkHover = hover;
            Invalidate(new Rectangle(Math.Min(_link1.Left, _link2.Left) - 2, _link1.Top - 2, Math.Max(_link1.Width, _link2.Width) + 4, _link1.Height + 4));
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _linkHover = 0;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        if (_link1.Contains(e.Location))
        {
            if (_doubao) ReloginClicked?.Invoke(this, EventArgs.Empty);
            else RenameClicked?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (_link2.Contains(e.Location))
            DeleteClicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(UiChrome.ColorOf(UiChrome.Tone.Window));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var pal = UiChrome.Tone;
        using (var bar = new SolidBrush(UiChrome.ColorOf(pal.Accent)))
            g.FillRectangle(bar, 0, 0, S(2), Height);

        var secondary = UiChrome.ColorOf(pal.Secondary);
        var text = UiChrome.ColorOf(pal.Text);
        var y = Padding.Top + S(2);
        var capSize = TextRenderer.MeasureText(_caption, AccountFonts.Caption, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, _caption, AccountFonts.Caption,
            new Rectangle(Padding.Left, y, capSize.Width + 2, Math.Max(S(18), capSize.Height)), secondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        var nameX = Padding.Left + capSize.Width + S(8);
        var nameW = Math.Max(S(40), _link1.Left - S(10) - nameX);
        TextRenderer.DrawText(g, _name, AccountFonts.Name,
            new Rectangle(nameX, y, nameW, Math.Max(S(18), capSize.Height)), text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        using var font = UiChrome.UiFont(9f, FontStyle.Bold);
        var c1 = LinkColor(1);
        var c2 = LinkColor(2);
        if (_linkHover == 1) c1 = AccountPaint.Mix(c1, text, 0.5);
        if (_linkHover == 2) c2 = AccountPaint.Mix(c2, text, 0.45);
        TextRenderer.DrawText(g, _link1Text, font, _link1, c1, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, _link2Text, font, _link2, c2, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Page header: 账号 + counts/sync on the left, 检查状态 / 添加账号 on the right.</summary>
sealed class AccountHeaderRow : Panel
{
    readonly Label _title;
    readonly Label _meta;
    readonly Button _check;
    readonly Button _add;
    bool _laying;

    public AccountHeaderRow(Label title, Label meta, Button check, Button add)
    {
        DoubleBuffered = true;
        Margin = new Padding(0, 0, 0, 10);
        _title = title;
        _meta = meta;
        _check = check;
        _add = add;
        _title.AutoSize = true;
        _meta.AutoSize = true;
        Controls.Add(_title);
        Controls.Add(_meta);
        Controls.Add(_check);
        Controls.Add(_add);
    }

    int Dpi => DeviceDpi > 0 ? DeviceDpi : 96;
    int S(int px) => UiLayout.ScalePx(px, Dpi);

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_laying) return;
        _laying = true;
        try
        {
            var width = ClientSize.Width;
            var height = Math.Max(S(34), Math.Max(_add.Height, _title.Height) + S(10));
            if (Height != height) Height = height;
            _add.Location = new Point(width - _add.Width, (height - _add.Height) / 2);
            _check.Location = new Point(_add.Left - S(8) - _check.Width, (height - _check.Height) / 2);
            var titleY = (height - _title.Height) / 2;
            _title.Location = new Point(0, titleY);
            var metaX = _title.Right + S(10);
            var metaW = Math.Max(S(60), _check.Left - S(10) - metaX);
            _meta.MaximumSize = new Size(metaW, 0);
            _meta.Location = new Point(metaX, titleY + Math.Max(0, (_title.Height - _meta.Height) / 2));
        }
        finally
        {
            _laying = false;
        }
    }
}

/// <summary>Compact 豆包 block: title + state, one wrapping note, and the cookie/login actions.</summary>
sealed class DoubaoCard : Panel
{
    public override Size GetPreferredSize(Size proposed)
        => new(Math.Max(MinimumSize.Width, Math.Max(240, Width)), Math.Max(MinimumSize.Height, Height));

    readonly Label _title;
    readonly Label _state;
    readonly Label _hint;
    readonly FlowLayoutPanel _actions;
    bool _laying;

    public DoubaoCard(Label title, Label state, Label hint, FlowLayoutPanel actions)
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        Margin = new Padding(0, 4, 0, 6);
        _title = title;
        _state = state;
        _hint = hint;
        _actions = actions;
        Controls.Add(_title);
        Controls.Add(_state);
        Controls.Add(_hint);
        Controls.Add(_actions);
    }

    int Dpi => DeviceDpi > 0 ? DeviceDpi : 96;
    int S(int px) => UiLayout.ScalePx(px, Dpi);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UiChrome.ApplyRoundRegion(this, S(10));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_laying) return;
        _laying = true;
        try
        {
            var width = ClientSize.Width;
            if (width < S(80)) return;
            var pad = S(14);
            var inner = width - pad * 2;
            var y = pad - S(4);
            _title.Location = new Point(pad, y);
            _state.MaximumSize = new Size(inner, 0);
            _state.Location = new Point(pad + _title.Width + S(8), y + Math.Max(0, (_title.Height - _state.Height) / 2));
            y += Math.Max(_title.Height, _state.Height) + S(4);
            _hint.MaximumSize = new Size(inner, 0);
            var hintH = Math.Max(S(16), _hint.GetPreferredSize(new Size(inner, int.MaxValue)).Height);
            _hint.SetBounds(pad, y, inner, hintH);
            y += hintH + S(10);
            _actions.Width = inner;
            _actions.PerformLayout();
            var actionsH = Math.Max(S(28), _actions.GetPreferredSize(new Size(inner, int.MaxValue)).Height);
            _actions.SetBounds(pad, y, inner, actionsH);
            y += actionsH + pad - S(2);
            if (Height != y) Height = y;
        }
        finally
        {
            _laying = false;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiChrome.RoundRect(new RectangleF(0.5f, 0.5f, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), S(10));
        using var fill = new SolidBrush(UiChrome.ColorOf(UiChrome.Tone.Header));
        g.FillPath(fill, path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var pal = UiChrome.Tone;
        UiChrome.DrawRoundBorder(g, UiChrome.ColorOf(pal.Hairline), UiChrome.ColorOf(pal.Window), Width, Height, S(10));
    }
}
