using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using CursorTokenCore;

namespace CursorTokenTray;

sealed class FlyoutForm : Form
{
    readonly Action _dismissBalloon, _refresh, _web, _settings, _copy, _report, _compare;
    readonly System.Windows.Forms.Timer _activateTimer = new() { Interval = 220 };
    readonly System.Windows.Forms.Timer _holdTimer = new() { Interval = 180 };
    readonly System.Windows.Forms.Timer _hideTimer = new() { Interval = 160 };
    readonly List<HitTarget> _hits = [];
    UsageSnapshot? _usage;
    string? _error;
    string? _updated;
    string? _accountLabel;
    List<HistoryPoint> _history = [];
    double? _dailyAvg;
    string? _hover;
    int? _sparkHover;
    RectangleF _sparkPlotBox;
    SparkPlot? _sparkPlot;
    bool _holdOpen;

    protected override bool ShowWithoutActivation => true;

    public FlyoutForm(Action dismissBalloon, Action refresh, Action web, Action settings, Action copy, Action report, Action compare)
    {
        _dismissBalloon = dismissBalloon;
        _refresh = refresh;
        _web = web;
        _settings = settings;
        _copy = copy;
        _report = report;
        _compare = compare;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        ApplyPalette();

        _activateTimer.Tick += (_, _) =>
        {
            _activateTimer.Stop();
            if (IsDisposed || !Visible) { _holdOpen = false; return; }
            Activate();
            if (IsHandleCreated) SetForegroundWindow(Handle);
            _holdTimer.Stop();
            _holdTimer.Start();
        };
        _holdTimer.Tick += (_, _) => { _holdOpen = false; _holdTimer.Stop(); };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!_holdOpen && !ContainsFocus) Hide();
        };
        Deactivate += (_, _) =>
        {
            if (_holdOpen) return;
            _hideTimer.Stop();
            _hideTimer.Start();
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            _holdOpen = false;
            _activateTimer.Stop();
            _holdTimer.Stop();
            _hideTimer.Stop();
            Hide();
        };
        MouseMove += (_, e) =>
        {
            var id = HitAt(e.Location);
            Cursor = id is null ? Cursors.Default : Cursors.Hand;
            var sparkChanged = UpdateSparkHover(e.Location);
            if (id == _hover && !sparkChanged) return;
            _hover = id;
            Invalidate();
        };
        MouseLeave += (_, _) =>
        {
            if (_hover is null && _sparkHover is null) return;
            _hover = null;
            _sparkHover = null;
            Cursor = Cursors.Default;
            Invalidate();
        };
        MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            switch (HitAt(e.Location))
            {
                case "copy": _copy(); break;
                case "refresh": _refresh(); break;
                case "report": _report(); break;
                case "compare": _compare(); break;
                case "settings": _settings(); break;
                case "web": _web(); break;
            }
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int CsDropShadow = 0x00020000;
            const int WsExToolwindow = 0x00000080;
            var cp = base.CreateParams;
            cp.ClassStyle |= CsDropShadow;
            cp.ExStyle |= WsExToolwindow;
            return cp;
        }
    }

    public void Render(UsageSnapshot? usage, string? error, string? updated, AppConfig cfg, List<HistoryPoint>? history = null, double? dailyAvg = null)
    {
        _usage = usage;
        _error = error;
        _updated = updated;
        _accountLabel = cfg.ActiveAccount?.DisplayLabel;
        if (history is not null) _history = history;
        _dailyAvg = dailyAvg;
        ApplyPalette();
        Invalidate();
    }

    public void PopupNear(Point anchor)
    {
        try { _dismissBalloon(); } catch { }
        _hideTimer.Stop();
        _holdOpen = true;
        _holdTimer.Stop();
        _activateTimer.Stop();
        if (!IsHandleCreated) CreateHandle();
        ApplyScaledSize();
        var screen = Screen.FromPoint(anchor).WorkingArea;
        var (x, y) = UiLayout.FitPopup(screen.Left, screen.Top, screen.Right, screen.Bottom, Width, Height, anchor.X, anchor.Y);
        Location = new Point(x, y);
        if (!Visible) Show();
        else BringToFront();
        _activateTimer.Start();
    }

    public void PopupNearTray() => PopupNear(Cursor.Position);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyScaledSize();
        TryRoundCorners();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyScaledSize();
        Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyRoundedRegion();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Palette.Window);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var pal = Palette;
        var s = UiLayout.DpiScale(DeviceDpi);
        var pad = Px(FlyoutLayout.Padding);
        var gap = Px(FlyoutLayout.ColumnGap);
        var leftW = Px(FlyoutLayout.LeftWidth);
        var radius = Px(FlyoutLayout.CornerRadius);
        using (var path = RoundRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), radius))
        using (var border = new Pen(pal.Border, Math.Max(1f, s)))
            g.DrawPath(border, path);

        _hits.Clear();
        var btnH = Px(FlyoutLayout.ToolButtonHeight);
        var btnGap = 4 * s;
        var contentBottom = Height - pad - btnH - btnGap;
        var left = new RectangleF(pad, pad, leftW, Math.Max(0, contentBottom - pad));
        DrawLeft(g, left, pal, s);
        var right = new RectangleF(pad + leftW + gap, pad, Width - pad * 2 - leftW - gap, Math.Max(0, contentBottom - pad));
        DrawRight(g, right, pal, s);
        DrawButtons(g, new RectangleF(pad, contentBottom + btnGap, Width - pad * 2, btnH), pal, s);
    }

    void DrawLeft(Graphics g, RectangleF box, FlyoutPalette pal, float s)
    {
        var error = _usage is null && _error is { Length: > 0 };
        var unlimited = _usage?.IsUnlimited == true;
        var remaining = _usage?.RemainingPercent;
        var tone = ToneColor(remaining, error, unlimited);
        var ring = Px(FlyoutLayout.RingSize);
        var gauge = new RectangleF(box.X + (box.Width - ring) / 2, box.Y, ring, ring);
        DrawGauge(g, gauge, remaining, error, unlimited, tone, pal, s);

        var caption = error || _usage is null
            ? (_error ?? "等待刷新…")
            : StatusText.FormatPlanCaption(_usage.MembershipType, _accountLabel);
        using var planFont = UiFont(9f);
        var planTop = gauge.Bottom + 10 * s;
        var planSize = g.MeasureString(caption, planFont, (int)box.Width);
        var planRect = new RectangleF(box.X, planTop, box.Width, Math.Min(planSize.Height + 4, 42 * s));
        DrawString(g, caption, planFont, pal.Secondary, planRect, StringAlignment.Center);

        var link = UsageParser.DashboardLinkLabel(_usage);
        using var linkFont = UiFont(8.25f);
        var linkSize = g.MeasureString(link, linkFont);
        var linkRect = new RectangleF(
            box.X + (box.Width - linkSize.Width) / 2,
            planRect.Bottom + 6 * s,
            linkSize.Width + 4,
            linkSize.Height + 4);
        DrawString(g, link, linkFont, pal.Accent, linkRect, StringAlignment.Center, StringAlignment.Center);
        _hits.Add(new("web", Rectangle.Round(linkRect)));
    }

    void DrawGauge(Graphics g, RectangleF box, double? remaining, bool error, bool unlimited, Color tone, FlyoutPalette pal, float s)
    {
        var line = Math.Max(2f, Px(FlyoutLayout.RingLine));
        var inset = line / 2 + 0.5f;
        var ring = RectangleF.Inflate(box, -inset, -inset);
        using (var track = new Pen(pal.Track, line))
            g.DrawEllipse(track, ring);
        float progress = error ? 0 : unlimited ? 1 : remaining is { } r ? (float)Math.Clamp(r / 100.0, 0, 1) : 0;
        if (progress >= 0.995f)
        {
            using var pen = new Pen(tone, line);
            g.DrawEllipse(pen, ring);
        }
        else if (progress > 0.004f)
        {
            using var pen = new Pen(tone, line) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pen, ring, -90, progress * 360);
        }

        using var capFont = UiFont(8.25f);
        using var numFont = UiFont(unlimited ? 16f : 21f, FontStyle.Bold);
        using var pctFont = UiFont(11f, FontStyle.Bold);
        using var pillFont = UiFont(8f);
        var cx = box.X + box.Width / 2;
        var cy = box.Y + box.Height / 2;
        DrawString(g, "剩余", capFont, pal.Secondary, new RectangleF(box.X, cy - 36 * s, box.Width, 16 * s), StringAlignment.Center, StringAlignment.Center);

        if (unlimited)
            DrawString(g, "不限量", numFont, pal.Text, new RectangleF(box.X, cy - 18 * s, box.Width, 28 * s), StringAlignment.Center, StringAlignment.Center);
        else if (remaining is { } pct && !error)
        {
            var num = pct.ToString("0.0");
            var numSize = g.MeasureString(num, numFont);
            var pctSize = g.MeasureString("%", pctFont);
            var total = numSize.Width + pctSize.Width - 4 * s;
            var numRect = new RectangleF(cx - total / 2, cy - 16 * s, numSize.Width, 28 * s);
            var pctRect = new RectangleF(numRect.Right - 4 * s, cy - 10 * s, pctSize.Width, 22 * s);
            DrawString(g, num, numFont, pal.Text, numRect, StringAlignment.Near, StringAlignment.Center);
            DrawString(g, "%", pctFont, pal.Secondary, pctRect, StringAlignment.Near, StringAlignment.Center);
        }
        else
            DrawString(g, error ? "—" : "…", numFont, pal.Secondary, new RectangleF(box.X, cy - 18 * s, box.Width, 28 * s), StringAlignment.Center, StringAlignment.Center);

        var pill = StatusText.StatusPillText(remaining, error);
        var pillSize = g.MeasureString(pill, pillFont);
        var pillRect = new RectangleF(cx - (pillSize.Width + 20 * s) / 2, cy + 16 * s, pillSize.Width + 20 * s, pillSize.Height + 6 * s);
        using (var pillPath = RoundRect(pillRect, pillRect.Height / 2))
        using (var pillBg = new SolidBrush(Color.FromArgb(64, tone)))
            g.FillPath(pillBg, pillPath);
        DrawString(g, pill, pillFont, pal.Text, pillRect, StringAlignment.Center, StringAlignment.Center);
    }

    void DrawRight(Graphics g, RectangleF box, FlyoutPalette pal, float s)
    {
        var y = box.Y;
        var error = _usage is null && _error is { Length: > 0 };
        using var capFont = UiFont(8.25f);
        using var valueFont = UiFont(8.25f, FontStyle.Bold);
        using var smallFont = UiFont(7.5f);
        var barH = Px(FlyoutLayout.BarHeight);

        if (_usage is { } usage && !error)
        {
            if (usage.ShowsAmount)
            {
                var hasBar = usage.UsedCents is { } && usage.LimitCents is > 0;
                var amountH = 16 * s + (hasBar ? 6 * s + barH : 0);
                y = DrawCard(g, box.X, y, box.Width, pal, amountH, inner =>
                {
                    var row = inner;
                    row.Height = 16 * s;
                    DrawLabeled(g, "金额", UsageParser.FormatSpendRange(usage.UsedCents, usage.LimitCents), capFont, valueFont, pal, row);
                    if (usage.UsedCents is { } used && usage.LimitCents is { } limit && limit > 0)
                        DrawBar(g, new RectangleF(inner.X, inner.Y + 20 * s, inner.Width, barH), used / limit, Color.FromArgb(48, 209, 88), pal);
                });
            }
            if (usage.AutoPercentUsed is not null || usage.ApiPercentUsed is not null || usage.ShowsGrokBot)
            {
                var rows = (usage.AutoPercentUsed is null ? 0 : 1) + (usage.ApiPercentUsed is null ? 0 : 1) + (usage.ShowsGrokBot ? 1 : 0);
                var meterH = rows * (18 * s + barH) + Math.Max(0, rows - 1) * 8 * s;
                y = DrawCard(g, box.X, y, box.Width, pal, meterH, inner =>
                {
                    if (usage.AutoPercentUsed is { } auto)
                    {
                        inner.Y = DrawMeter(g, inner, "First-party", auto, Color.FromArgb(50, 180, 170), capFont, valueFont, pal, s);
                        inner.Y += 8 * s;
                    }
                    if (usage.ApiPercentUsed is { } api)
                    {
                        inner.Y = DrawMeter(g, inner, "API", api, Color.FromArgb(142, 142, 147), capFont, valueFont, pal, s);
                        if (usage.ShowsGrokBot) inner.Y += 8 * s;
                    }
                    if (usage.ShowsGrokBot && usage.GrokBotPercentUsed is { } grok)
                        DrawMeter(g, inner, "Grok Bot", grok, Color.FromArgb(99, 102, 241), capFont, valueFont, pal, s);
                });
            }
            var parts = new List<string>();
            if (usage.TotalTokens is > 0)
                parts.Add("Token  " + UsageParser.FormatTokenCount(usage.TotalTokens));
            if (usage.BillingCycleEnd is { } end)
                parts.Add(StatusText.CycleEndLabel(usage) + "  " + StatusText.FormatResetDate(end, usage.BillingCycleEndOverridden));
            var infoH = 14 * s + (parts.Count > 0 ? 18 * s : 0);
            y = DrawCard(g, box.X, y, box.Width, pal, infoH, inner =>
            {
                if (parts.Count > 0)
                {
                    DrawString(g, string.Join("    ", parts), capFont, pal.Secondary, new RectangleF(inner.X, inner.Y, inner.Width, 16 * s));
                    inner.Y += 18 * s;
                }
                var est = StatusText.FormatEstimateCaption(usage);
                DrawString(g, est, capFont, EstimateColor(est, pal), new RectangleF(inner.X, inner.Y, inner.Width, 16 * s));
            });
        }
        else if (_updated is { Length: > 0 })
        {
            DrawString(g, "更新  " + _updated, capFont, pal.Secondary, new RectangleF(box.X, y, box.Width, 16 * s));
            y += 20 * s;
        }

        DrawSparkSection(g, new RectangleF(box.X, y + 4 * s, box.Width, Math.Max(0, box.Bottom - (y + 4 * s))), pal, s, smallFont);
    }

    float DrawCard(Graphics g, float x, float y, float width, FlyoutPalette pal, float innerHeight, Action<RectangleF> content)
    {
        var pad = Px(FlyoutLayout.CardPadding);
        var height = innerHeight + pad * 2;
        var card = new RectangleF(x, y, width, height);
        using (var path = RoundRect(card, Px(FlyoutLayout.CardRadius)))
        using (var bg = new SolidBrush(pal.Card))
            g.FillPath(bg, path);
        content(new RectangleF(x + pad, y + pad, width - pad * 2, innerHeight));
        return y + height + Px(FlyoutLayout.CardGap);
    }

    float DrawMeter(Graphics g, RectangleF box, string title, double percent, Color color, Font cap, Font value, FlyoutPalette pal, float s)
    {
        using (var dot = new SolidBrush(color))
            g.FillEllipse(dot, box.X, box.Y + 4 * s, 6 * s, 6 * s);
        DrawString(g, title, cap, pal.Secondary, new RectangleF(box.X + 12 * s, box.Y, box.Width / 2, 16 * s));
        DrawString(g, percent.ToString("0.0") + "%", value, pal.Text, new RectangleF(box.X, box.Y, box.Width, 16 * s), StringAlignment.Far);
        var barY = box.Y + 18 * s;
        DrawBar(g, new RectangleF(box.X, barY, box.Width, Px(FlyoutLayout.BarHeight)), percent / 100, color, pal);
        return barY + Px(FlyoutLayout.BarHeight);
    }

    static void DrawLabeled(Graphics g, string title, string value, Font cap, Font valueFont, FlyoutPalette pal, RectangleF row)
    {
        DrawString(g, title, cap, pal.Secondary, row, StringAlignment.Near);
        DrawString(g, value, valueFont, pal.Text, row, StringAlignment.Far);
    }

    static void DrawBar(Graphics g, RectangleF box, double fraction, Color color, FlyoutPalette pal)
    {
        var r = box.Height / 2;
        using (var track = RoundRect(box, r))
        using (var bg = new SolidBrush(pal.Track))
            g.FillPath(bg, track);
        var w = (float)(box.Width * Math.Clamp(fraction, 0, 1));
        if (w < 1) return;
        using var fill = RoundRect(new RectangleF(box.X, box.Y, Math.Max(w, box.Height), box.Height), r);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, fill);
    }

    void DrawSparkSection(Graphics g, RectangleF box, FlyoutPalette pal, float s, Font smallFont)
    {
        _sparkPlot = null;
        _sparkPlotBox = RectangleF.Empty;
        if (box.Height < 12 * s) return;
        var current = CurrentRemaining();
        var caption = SparklineGeometry.Caption(_history.Count, _dailyAvg, current);
        var titleH = Math.Min(Px(FlyoutLayout.SparkTitleHeight), 16 * s);
        var axisH = Math.Min(Px(FlyoutLayout.SparkAxisHeight), 14 * s);
        var captionH = Math.Min(Px(FlyoutLayout.SparkCaptionHeight), 16 * s);
        var hoverText = SparkHoverText();
        DrawString(g, hoverText ?? SparklineCopy.Title, smallFont, pal.Secondary, new RectangleF(box.X, box.Y, box.Width, titleH));

        var y = box.Y + titleH;
        if (_history.Count < 2)
        {
            _sparkPlot = null;
            _sparkPlotBox = RectangleF.Empty;
            DrawString(g, caption, smallFont, pal.Secondary, new RectangleF(box.X, y, box.Width, Math.Min(captionH + 4 * s, box.Bottom - y)));
            return;
        }

        var sparkH = Math.Min(Px(FlyoutLayout.SparkHeight), box.Bottom - y - axisH - captionH);
        if (sparkH >= 12 * s)
        {
            var plot = DrawSparkline(g, new RectangleF(box.X, y, box.Width, sparkH), pal, s, current);
            y += sparkH + 1 * s;
            var axisStart = plot is { } drawn
                ? SparklineGeometry.AxisStartLabel(drawn.T0, drawn.T1)
                : SparklineCopy.AxisStart;
            DrawString(g, axisStart, smallFont, pal.Secondary, new RectangleF(box.X, y, box.Width / 2, axisH));
            DrawString(g, SparklineCopy.AxisEnd, smallFont, pal.Secondary, new RectangleF(box.X + box.Width / 2, y, box.Width / 2, axisH), StringAlignment.Far);
            y += axisH;
        }
        DrawString(g, caption, smallFont, pal.Secondary, new RectangleF(box.X, y, box.Width, captionH));
    }

    SparkPlot? DrawSparkline(Graphics g, RectangleF box, FlyoutPalette pal, float s, double? current)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var plot = SparklineGeometry.Layout(_history, box.Width, box.Height, now, CycleStartTs());
        _sparkPlot = plot;
        _sparkPlotBox = box;
        if (plot.Points.Length < 2) return plot;

        var error = _usage is null && _error is { Length: > 0 };
        var unlimited = _usage?.IsUnlimited == true;
        var tone = ToneColor(current ?? plot.Points[^1].Remaining, error, unlimited);
        var points = plot.Points.Select(p => new PointF(box.X + p.X, box.Y + p.Y)).ToArray();
        var ribbon = SparklineGeometry.RibbonOffset(box.Height);
        PointF Below(PointF p) => new(p.X, Math.Min(box.Bottom, p.Y + ribbon));
        using (var fillPath = new GraphicsPath())
        {
            var ribbonPts = new PointF[points.Length * 2];
            for (var i = 0; i < points.Length; i++) ribbonPts[i] = points[i];
            for (var i = 0; i < points.Length; i++)
                ribbonPts[points.Length + i] = Below(points[points.Length - 1 - i]);
            fillPath.AddLines(ribbonPts);
            fillPath.CloseFigure();
            using var brush = new LinearGradientBrush(box, Color.FromArgb(70, tone), Color.FromArgb(10, tone), LinearGradientMode.Vertical);
            g.FillPath(brush, fillPath);
        }
        using (var pen = new Pen(tone, Math.Max(1.5f, 1.5f * s)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLines(pen, points);

        if (plot.Reset is { } reset)
        {
            var rx = box.X + reset.X;
            using var dash = new Pen(Color.FromArgb(140, pal.Secondary), Math.Max(1f, s)) { DashStyle = DashStyle.Dash };
            g.DrawLine(dash, rx, box.Y, rx, box.Bottom);
            using var resetFont = UiFont(6.75f);
            var label = SparklineCopy.Reset;
            var labelSize = g.MeasureString(label, resetFont);
            var labelRect = new RectangleF(Math.Min(rx + 2 * s, box.Right - labelSize.Width), box.Y, labelSize.Width, 12 * s);
            DrawString(g, label, resetFont, pal.Secondary, labelRect);
        }

        if (_sparkHover is { } hi && hi >= 0 && hi < points.Length)
        {
            var hp = points[hi];
            using var hair = new Pen(Color.FromArgb(160, pal.Secondary), 1f);
            g.DrawLine(hair, hp.X, box.Y, hp.X, box.Bottom);
            using var dot = new SolidBrush(tone);
            g.FillEllipse(dot, hp.X - 2.5f * s, hp.Y - 2.5f * s, 5 * s, 5 * s);
        }

        using var markFont = UiFont(6.75f);
        DrawString(g, SparklineGeometry.FormatPercent(plot.Points[0].Remaining), markFont, pal.Secondary,
            new RectangleF(box.X, box.Y, box.Width * 0.45f, 12 * s));
        DrawString(g, SparklineGeometry.FormatPercent(plot.Points[^1].Remaining), markFont, pal.Secondary,
            new RectangleF(box.X + box.Width * 0.45f, box.Bottom - 12 * s, box.Width * 0.55f, 12 * s), StringAlignment.Far, StringAlignment.Far);
        return plot;
    }

    bool UpdateSparkHover(Point pt)
    {
        int? next = null;
        if (_sparkPlot is { Points.Length: > 0 } plot && _sparkPlotBox.Contains(pt))
            next = SparklineGeometry.HitIndex(plot.Points, pt.X - _sparkPlotBox.X);
        if (next == _sparkHover) return false;
        _sparkHover = next;
        return true;
    }

    string? SparkHoverText()
    {
        if (_sparkHover is not { } i || _sparkPlot is not { } plot || i < 0 || i >= plot.Points.Length)
            return null;
        var p = plot.Points[i];
        double? prev = i > 0 ? plot.Points[i - 1].Remaining : null;
        return SparklineGeometry.FormatHover(p.Ts, p.Remaining, prev);
    }

    double? CycleStartTs()
    {
        var ms = UsageParser.IsoToMs(_usage?.BillingCycleStart);
        return ms is { } v ? v / 1000.0 : null;
    }

    double? CurrentRemaining()
    {
        if (_usage is { RemainingPercent: var pct }) return pct;
        return _history.Count > 0 ? _history[^1].Remaining : null;
    }

    void DrawButtons(Graphics g, RectangleF box, FlyoutPalette pal, float s)
    {
        var items = new (string Id, string Icon, string Label)[]
        {
            ("copy", "\uE8C8", "复制"),
            ("refresh", "\uE72C", "刷新"),
            ("report", "\uE9D9", "报表"),
            ("compare", "\uE9F9", "对比"),
            ("settings", "\uE713", "设置"),
        };
        using var font = UiFont(8f);
        using var iconFont = IconFont(8f);
        using var measure = new StringFormat(StringFormat.GenericTypographic);
        var hasIcon = iconFont is not null;
        var sizes = new (float Width, float Height)[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var text = g.MeasureString(items[i].Label, font, int.MaxValue, measure);
            sizes[i] = FlyoutLayout.ToolButtonSize(text.Width, text.Height, hasIcon, s);
        }
        var gap = Px(FlyoutLayout.ToolButtonGap);
        var (_, _, frames) = FlyoutLayout.ArrangeToolButtons(sizes, box.Width, gap);
        var padX = FlyoutLayout.ToolButtonPadX * s;
        var iconSlot = hasIcon ? FlyoutLayout.ToolButtonIcon * s : 0;
        var iconTextGap = hasIcon ? FlyoutLayout.ToolButtonIconGap * s : 0;
        for (var i = 0; i < items.Length; i++)
        {
            var it = items[i];
            var fr = frames[i];
            var text = g.MeasureString(it.Label, font, int.MaxValue, measure);
            var contentW = iconSlot + iconTextGap + text.Width;
            var rect = new RectangleF(box.X + fr.X, box.Y + Math.Max(0, (box.Height - fr.Height) / 2), fr.Width, Math.Max(box.Height, fr.Height));
            var bg = _hover == it.Id ? pal.ButtonHover : pal.Button;
            using (var path = RoundRect(rect, rect.Height / 2))
            using (var brush = new SolidBrush(bg))
                g.FillPath(brush, path);
            var inner = Math.Max(0, rect.Width - padX * 2);
            var tx = rect.X + padX + Math.Max(0, (inner - contentW) / 2);
            if (hasIcon)
            {
                DrawFittedString(g, it.Icon, iconFont!, pal.Secondary, new RectangleF(tx, rect.Y, iconSlot, rect.Height), StringAlignment.Center, StringAlignment.Center);
                tx += iconSlot + iconTextGap;
            }
            DrawFittedString(g, it.Label, font, pal.Secondary, new RectangleF(tx, rect.Y, Math.Max(0, rect.Right - padX - tx), rect.Height), StringAlignment.Near, StringAlignment.Center);
            _hits.Add(new(it.Id, Rectangle.Round(rect)));
        }
    }

    string? HitAt(Point pt)
    {
        for (var i = _hits.Count - 1; i >= 0; i--)
            if (_hits[i].Rect.Contains(pt)) return _hits[i].Id;
        return null;
    }

    void ApplyPalette()
    {
        Palette = FlyoutPalette.For(AppsUseLightTheme());
        BackColor = Palette.Window;
        ForeColor = Palette.Text;
    }

    FlyoutPalette Palette { get; set; } = FlyoutPalette.For(false);

    void ApplyScaledSize()
    {
        var w = UiLayout.ScalePx(FlyoutLayout.Width, DeviceDpi);
        var h = UiLayout.ScalePx(FlyoutLayout.Height, DeviceDpi);
        ClientSize = new Size(w, h);
        ApplyRoundedRegion();
    }

    void ApplyRoundedRegion()
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
        var radius = UiLayout.ScalePx(FlyoutLayout.CornerRadius, DeviceDpi);
        using var path = RoundRect(new RectangleF(0, 0, Width, Height), radius);
        var region = new Region(path);
        var old = Region;
        Region = region;
        old?.Dispose();
    }

    void TryRoundCorners()
    {
        try
        {
            const int DwmwaWindowCornerPreference = 33;
            const int DwmwcpRound = 2;
            var pref = DwmwcpRound;
            _ = DwmSetWindowAttribute(Handle, DwmwaWindowCornerPreference, ref pref, sizeof(int));
        }
        catch { }
    }

    int Px(int design) => UiLayout.ScalePx(design, DeviceDpi);

    static Color ToneColor(double? remaining, bool error, bool unlimited)
    {
        var (r, g, b) = RemainingTone.Rgb(remaining, error, unlimited);
        return Color.FromArgb(r, g, b);
    }

    static Color EstimateColor(string text, FlyoutPalette pal)
    {
        if (text.Contains("可撑过")) return Color.FromArgb(48, 209, 88);
        if (text.Contains("耗尽") || text.Contains("紧张")) return Color.FromArgb(231, 76, 60);
        return pal.Secondary;
    }

    static Font UiFont(float pt, FontStyle style = FontStyle.Regular)
        => new("Segoe UI", Math.Max(7f, pt), style, GraphicsUnit.Point);

    static Font? IconFont(float pt)
    {
        foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            try
            {
                using var family = new FontFamily(name);
                return new Font(family, Math.Max(7f, pt), FontStyle.Regular, GraphicsUnit.Point);
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    static void DrawString(Graphics g, string text, Font font, Color color, RectangleF rect,
        StringAlignment align = StringAlignment.Near, StringAlignment valign = StringAlignment.Near, bool noWrap = false)
    {
        using var brush = new SolidBrush(color);
        using var sf = new StringFormat
        {
            Alignment = align,
            LineAlignment = valign,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.LineLimit | (noWrap ? StringFormatFlags.NoWrap : 0),
        };
        g.DrawString(text, font, brush, rect, sf);
    }

    static void DrawFittedString(Graphics g, string text, Font font, Color color, RectangleF rect,
        StringAlignment align = StringAlignment.Near, StringAlignment valign = StringAlignment.Center)
    {
        using var brush = new SolidBrush(color);
        using var sf = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = align,
            LineAlignment = valign,
            Trimming = StringTrimming.None,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.LineLimit,
        };
        g.DrawString(text, font, brush, rect, sf);
    }

    static GraphicsPath RoundRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1f, radius * 2);
        if (d > rect.Width) d = rect.Width;
        if (d > rect.Height) d = rect.Height;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    static bool AppsUseLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _activateTimer.Dispose();
            _holdTimer.Dispose();
            _hideTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    readonly record struct HitTarget(string Id, Rectangle Rect);
}

readonly struct FlyoutPalette
{
    public required Color Window { get; init; }
    public required Color Card { get; init; }
    public required Color Text { get; init; }
    public required Color Secondary { get; init; }
    public required Color Accent { get; init; }
    public required Color Track { get; init; }
    public required Color Border { get; init; }
    public required Color Button { get; init; }
    public required Color ButtonHover { get; init; }

    public static FlyoutPalette For(bool light) => light
        ? new()
        {
            Window = Color.FromArgb(246, 246, 248),
            Card = Color.FromArgb(18, 0, 0, 0),
            Text = Color.FromArgb(28, 28, 30),
            Secondary = Color.FromArgb(110, 110, 115),
            Accent = Color.FromArgb(0, 122, 255),
            Track = Color.FromArgb(28, 0, 0, 0),
            Border = Color.FromArgb(40, 0, 0, 0),
            Button = Color.FromArgb(22, 0, 0, 0),
            ButtonHover = Color.FromArgb(40, 0, 0, 0),
        }
        : new()
        {
            Window = Color.FromArgb(36, 36, 38),
            Card = Color.FromArgb(22, 255, 255, 255),
            Text = Color.FromArgb(245, 245, 247),
            Secondary = Color.FromArgb(152, 152, 157),
            Accent = Color.FromArgb(10, 132, 255),
            Track = Color.FromArgb(36, 255, 255, 255),
            Border = Color.FromArgb(40, 255, 255, 255),
            Button = Color.FromArgb(24, 255, 255, 255),
            ButtonHover = Color.FromArgb(42, 255, 255, 255),
        };
}
