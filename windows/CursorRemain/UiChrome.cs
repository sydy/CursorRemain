using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using CursorTokenCore;
using Microsoft.Win32;

namespace CursorRemain;

enum UiButtonKind
{
    Secondary,
    Primary,
    Danger,
}

static class UiChrome
{
    static string _colorMode = "system";
    static bool _watchSystem;

    public static string ColorMode => _colorMode;

    public static event Action? ThemeChanged;

    public static void SetColorMode(string? mode)
    {
        _colorMode = mode is "light" or "dark" ? mode : "system";
        if (_colorMode == "system")
        {
            if (_watchSystem) return;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _watchSystem = true;
            return;
        }
        StopThemeWatch();
    }

    public static void StopThemeWatch()
    {
        if (!_watchSystem) return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _watchSystem = false;
    }

    static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_colorMode != "system") return;
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle))
            return;
        try { ThemeChanged?.Invoke(); }
        catch { }
    }
    public static bool AppsUseLightTheme() => SystemUsesLightTheme();

    static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch
        {
            return true;
        }
    }

    public static bool UseLightTheme() => _colorMode switch
    {
        "light" => true,
        "dark" => false,
        _ => AppsUseLightTheme(),
    };

    public static FormTone.Palette Tone => FormTone.For(UseLightTheme());

    public static Color ColorOf(FormTone.Rgb rgb) => Color.FromArgb(rgb.R, rgb.G, rgb.B);

    public static Font UiFont(float pt = 9f, FontStyle style = FontStyle.Regular)
    {
        foreach (var name in new[] { "Segoe UI Variable Text", "Segoe UI Variable", "Segoe UI" })
        {
            try
            {
                return new Font(name, Math.Max(8f, pt), style, GraphicsUnit.Point);
            }
            catch (ArgumentException) { }
        }
        return SystemFonts.MessageBoxFont ?? Control.DefaultFont;
    }

    public static Font? IconFont(float pt = 11f)
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

    public static void Install()
    {
        NativeTheme.PreferAppDarkMode();
        try
        {
            Application.SetDefaultFont(UiFont());
        }
        catch
        {
            // keep ApplicationConfiguration font
        }
    }

    public static Button Button(string text, UiButtonKind kind = UiButtonKind.Secondary)
    {
        var btn = new FlatButton
        {
            Text = text,
            AutoSize = false,
            Margin = new Padding(0, 0, 8, 4),
            Padding = new Padding(FormTone.ButtonPadX, 4, FormTone.ButtonPadX, 4),
            MinimumSize = new Size(FormTone.ButtonMinWidth, FormTone.ButtonMinHeight),
            Size = new Size(FormTone.ButtonMinWidth, FormTone.ButtonMinHeight),
            Tag = kind,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        StyleButton(btn, kind);
        SizeToText(btn, 96);
        return btn;
    }

    public static void SizeToText(Button btn, int dpi)
    {
        var owned = btn.Font is null;
        var font = btn.Font ?? UiFont();
        try
        {
            var raw = TextRenderer.MeasureText(
                btn.Text ?? "",
                font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPrefix).Width;
            var padX = UiLayout.ScalePx(FormTone.ButtonPadX, dpi);
            var w = Math.Max(UiLayout.ScalePx(FormTone.ButtonMinWidth, dpi), raw + padX * 2 + 16);
            var h = UiLayout.ScalePx(FormTone.ButtonMinHeight, dpi);
            btn.AutoSize = false;
            btn.Padding = new Padding(padX, UiLayout.ScalePx(4, dpi), padX, UiLayout.ScalePx(4, dpi));
            btn.MinimumSize = new Size(w, h);
            btn.Size = new Size(w, h);
        }
        finally
        {
            if (owned) font.Dispose();
        }
    }

    public static void Equalize(int dpi, params Button[] buttons)
    {
        if (buttons.Length == 0) return;
        var design = Math.Max(FormTone.ButtonMinWidth, 96);
        using var font = UiFont();
        foreach (var btn in buttons)
        {
            var raw = TextRenderer.MeasureText(btn.Text ?? "", font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
            var designText = (int)Math.Ceiling(raw * 96.0 / Math.Max(96, dpi));
            design = Math.Max(design, designText + FormTone.ButtonPadX * 2 + 8);
        }
        var w = UiLayout.ScalePx(design, dpi);
        var h = UiLayout.ScalePx(FormTone.ButtonMinHeight, dpi);
        var padX = UiLayout.ScalePx(FormTone.ButtonPadX, dpi);
        foreach (var btn in buttons)
        {
            btn.AutoSize = false;
            btn.Padding = new Padding(padX, UiLayout.ScalePx(4, dpi), padX, UiLayout.ScalePx(4, dpi));
            btn.MinimumSize = new Size(w, h);
            btn.Size = new Size(w, h);
        }
    }

    public static Label Heading(string text, bool first = false) => new()
    {
        Text = text,
        AutoSize = true,
        Font = UiFont(11f, FontStyle.Bold),
        Margin = new Padding(0, first ? 0 : 12, 0, 6),
    };

    public static Button MenuButton(string text, params (string Title, EventHandler Handler)[] items)
    {
        var btn = Button(text);
        var menu = new ContextMenuStrip();
        foreach (var item in items)
            menu.Items.Add(item.Title, null, item.Handler);
        StyleMenu(menu);
        btn.Click += (_, _) =>
        {
            StyleMenu(menu);
            menu.Show(btn, new Point(0, btn.Height));
        };
        return btn;
    }

    public static HintBlock Hint(string summary, string detail) => new(summary, detail);

    public static void StyleMenu(ContextMenuStrip menu)
    {
        var pal = Tone;
        menu.Renderer = new DarkMenuRenderer();
        menu.RenderMode = ToolStripRenderMode.Professional;
        menu.BackColor = ColorOf(pal.Field);
        menu.ForeColor = ColorOf(pal.Text);
        menu.ShowImageMargin = false;
    }

    /// <summary>
    /// Status on the left, actions packed to the right. A Panel (not an AutoSize
    /// TableLayoutPanel) so the bar cannot grow wider than the parent and shove
    /// buttons off-screen.
    /// </summary>
    public static Panel ActionBar(Control leading, params Button[] actions)
    {
        var pack = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            Padding = new Padding(8, 0, 0, 0),
        };
        foreach (var btn in actions)
            pack.Controls.Add(btn);
        if (leading is Label label)
        {
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
        }
        else
        {
            leading.Dock = DockStyle.Fill;
        }
        var bar = new Panel
        {
            Dock = DockStyle.Fill,
            Height = FormTone.ButtonMinHeight + 12,
            MinimumSize = new Size(0, FormTone.ButtonMinHeight + 12),
            Margin = new Padding(0, 0, 0, 4),
            Padding = Padding.Empty,
        };
        bar.Controls.Add(leading);
        bar.Controls.Add(pack);
        return bar;
    }

    internal const string FieldRowTag = "field-row";

    internal static int FieldPx(int dpi) => UiLayout.ScalePx(FormTone.FieldHeight, dpi);

    internal static void PaintFieldBox(Graphics g, int width, int height, int dpi, bool focused)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var strokeW = focused ? 2f : 1f;
        var radius = UiLayout.ScalePx(FormTone.ButtonRadius, dpi);
        var box = FlyoutLayout.InnerStroke(width, height, radius, strokeW + 2f);
        var bounds = new RectangleF(box.X, box.Y, box.Width, box.Height);
        using var path = RoundRect(bounds, box.Radius);
        using (var br = new SolidBrush(ColorOf(Tone.Field)))
            g.FillPath(br, path);
        using var pen = new Pen(focused ? ColorOf(Tone.Accent) : ColorOf(Tone.Stroke), strokeW);
        g.DrawPath(pen, path);
    }

    internal static void PaintFieldRing(Graphics g, int width, int height, int dpi, bool focused)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var strokeW = focused ? 2f : 1f;
        var radius = UiLayout.ScalePx(FormTone.ButtonRadius, dpi);
        var box = FlyoutLayout.InnerStroke(width, height, radius, strokeW + 2f);
        var bounds = new RectangleF(box.X, box.Y, box.Width, box.Height);
        using var path = RoundRect(bounds, box.Radius);
        using var pen = new Pen(focused ? ColorOf(Tone.Accent) : ColorOf(Tone.Stroke), strokeW);
        g.DrawPath(pen, path);
    }

    internal static void ApplyRoundRegion(Control control, int radius)
    {
        if (!control.IsHandleCreated || control.Width <= 1 || control.Height <= 1) return;
        var d = FlyoutLayout.RoundRegionDiameter(radius);
        var (right, bottom) = FlyoutLayout.RoundRegionExtent(control.Width, control.Height);
        var hrgn = CreateRoundRectRgn(0, 0, right, bottom, d, d);
        if (hrgn == IntPtr.Zero) return;
        var region = Region.FromHrgn(hrgn);
        DeleteObject(hrgn);
        var old = control.Region;
        control.Region = region;
        old?.Dispose();
    }

    internal static void ApplyRingRegion(Control control, int radius, int inset)
    {
        if (!control.IsHandleCreated || control.Width <= 1 || control.Height <= 1) return;
        var hole = Math.Max(2, inset);
        if (control.Width <= hole * 2 || control.Height <= hole * 2) return;
        var d = FlyoutLayout.RoundRegionDiameter(radius);
        var dIn = FlyoutLayout.RoundRegionDiameter(Math.Max(1, radius - hole));
        var (right, bottom) = FlyoutLayout.RoundRegionExtent(control.Width, control.Height);
        var outer = CreateRoundRectRgn(0, 0, right, bottom, d, d);
        var inner = CreateRoundRectRgn(hole, hole, right - hole, bottom - hole, dIn, dIn);
        var dest = CreateRectRgn(0, 0, 0, 0);
        if (outer == IntPtr.Zero || inner == IntPtr.Zero || dest == IntPtr.Zero)
        {
            if (outer != IntPtr.Zero) DeleteObject(outer);
            if (inner != IntPtr.Zero) DeleteObject(inner);
            if (dest != IntPtr.Zero) DeleteObject(dest);
            return;
        }
        CombineRgn(dest, outer, inner, RgnDiff);
        DeleteObject(outer);
        DeleteObject(inner);
        var region = Region.FromHrgn(dest);
        DeleteObject(dest);
        var old = control.Region;
        control.Region = region;
        old?.Dispose();
    }

    public static FieldFrame Frame(Control inner, int height = 0)
    {
        if (inner is TextBox box)
            box.BorderStyle = BorderStyle.None;
        inner.Margin = Padding.Empty;
        var multiline = inner is TextBox { Multiline: true };
        inner.Dock = multiline ? DockStyle.Fill : DockStyle.None;
        var panel = new FieldFrame
        {
            BackColor = ColorOf(Tone.Field),
            Padding = Padding.Empty,
            Height = height > 0 ? height : FormTone.FieldHeight,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 2, 0, 8),
        };
        panel.Controls.Add(inner);
        return panel;
    }

    public static void Apply(Form form)
    {
        var pal = Tone;
        form.Font = UiFont();
        form.BackColor = ColorOf(pal.Window);
        form.ForeColor = ColorOf(pal.Text);
        ApplyTree(form, pal, form.DeviceDpi);
        ApplyTitleBar(form);
        form.Invalidate(true);
    }

    static bool SameRgb(Color color, FormTone.Rgb rgb) => color.ToArgb() == ColorOf(rgb).ToArgb();

    static bool IsThemeSecondary(Color color) =>
        color.ToArgb() == Color.DimGray.ToArgb()
        || SameRgb(color, FormTone.Light.Secondary)
        || SameRgb(color, FormTone.Dark.Secondary);

    static bool IsThemeText(Color color) =>
        color.ToArgb() == SystemColors.ControlText.ToArgb()
        || color.ToArgb() == Color.Black.ToArgb()
        || SameRgb(color, FormTone.Light.Text)
        || SameRgb(color, FormTone.Dark.Text);

    public static void StyleGrid(DataGridView grid)
    {
        var pal = Tone;
        grid.Font = UiFont(9f);
        grid.BorderStyle = BorderStyle.None;
        grid.BackgroundColor = ColorOf(pal.Window);
        grid.GridColor = ColorOf(pal.Hairline);
        grid.EnableHeadersVisualStyles = false;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            Font = UiFont(9f, FontStyle.Bold),
            BackColor = ColorOf(pal.Header),
            ForeColor = ColorOf(pal.Secondary),
            SelectionBackColor = ColorOf(pal.Header),
            SelectionForeColor = ColorOf(pal.Secondary),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0),
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            Font = UiFont(9f),
            BackColor = ColorOf(pal.Window),
            ForeColor = ColorOf(pal.Text),
            SelectionBackColor = ColorOf(pal.Selection),
            SelectionForeColor = ColorOf(pal.Text),
            Padding = new Padding(8, 0, 8, 0),
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = ColorOf(pal.Field),
            ForeColor = ColorOf(pal.Text),
            SelectionBackColor = ColorOf(pal.Selection),
            SelectionForeColor = ColorOf(pal.Text),
        };
        foreach (DataGridViewColumn col in grid.Columns)
        {
            col.DefaultCellStyle.SelectionBackColor = ColorOf(pal.Selection);
            col.DefaultCellStyle.SelectionForeColor = ColorOf(pal.Text);
        }
        grid.CellFormatting -= PaintSelection;
        grid.CellFormatting += PaintSelection;
        grid.CellPainting -= PaintSelectedCell;
        grid.RowHeadersVisible = false;
        grid.HandleCreated -= ThemeGridScroll;
        grid.HandleCreated += ThemeGridScroll;
        grid.ControlAdded -= ThemeGridScrollChild;
        grid.ControlAdded += ThemeGridScrollChild;
        grid.Layout -= ThemeGridScroll;
        grid.Layout += ThemeGridScroll;
        if (grid.IsHandleCreated) ThemeGridScroll(grid, EventArgs.Empty);
        DarkScrollCover.Attach(grid);
    }

    static void ThemeGridScrollChild(object? sender, ControlEventArgs e)
    {
        if (e.Control is VScrollBar or HScrollBar)
            NativeTheme.DarkScroll(e.Control);
    }

    static void PaintSelectedCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (sender is not DataGridView grid || e.Graphics is null || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
        if (!grid.Rows[e.RowIndex].Selected) return;
        using var br = new SolidBrush(ColorOf(Tone.Selection));
        e.Graphics.FillRectangle(br, e.CellBounds);
        e.Paint(e.CellBounds, DataGridViewPaintParts.ContentForeground | DataGridViewPaintParts.Border | DataGridViewPaintParts.ErrorIcon);
        e.Handled = true;
    }

    static void PaintSelection(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not DataGridView grid || e.CellStyle is null || e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
        if (!grid.Rows[e.RowIndex].Selected) return;
        e.CellStyle.SelectionBackColor = ColorOf(Tone.Selection);
        e.CellStyle.SelectionForeColor = ColorOf(Tone.Text);
        e.CellStyle.BackColor = ColorOf(Tone.Selection);
        e.CellStyle.ForeColor = ColorOf(Tone.Text);
        if (e.Value is null or DBNull) e.Value = "";
    }

    static void ThemeTextScroll(object? sender, EventArgs e)
    {
        if (sender is TextBox box && box.IsHandleCreated)
            NativeTheme.Strip(box.Handle);
    }

    static void ThemeGridScroll(object? sender, EventArgs e)
    {
        if (sender is not DataGridView grid || !grid.IsHandleCreated) return;
        NativeTheme.DarkTree(grid.Handle);
        foreach (Control child in grid.Controls)
        {
            if (child is VScrollBar or HScrollBar)
                NativeTheme.DarkScroll(child);
        }
    }

    static void ThemeSpin(object? sender, EventArgs e)
    {
        if (sender is NumericUpDown spin && spin.IsHandleCreated)
            NativeTheme.Spin(spin.Handle);
    }

    public static Color HeaderFill() => ColorOf(Tone.Header);
    public static Color SecondaryText() => ColorOf(Tone.Secondary);
    public static Color SelectionFill() => ColorOf(Tone.Selection);
    public static Color GoodText() => ColorOf(Tone.Good);

    static void StyleButton(Button btn, UiButtonKind kind)
    {
        var pal = Tone;
        btn.FlatStyle = FlatStyle.Flat;
        btn.UseVisualStyleBackColor = false;
        btn.FlatAppearance.BorderSize = 0;
        switch (kind)
        {
            case UiButtonKind.Primary:
                btn.BackColor = ColorOf(pal.Accent);
                btn.ForeColor = ColorOf(pal.OnAccent);
                btn.FlatAppearance.BorderColor = ColorOf(pal.Accent);
                btn.FlatAppearance.MouseOverBackColor = ColorOf(pal.AccentHover);
                btn.FlatAppearance.MouseDownBackColor = ColorOf(pal.AccentHover);
                break;
            case UiButtonKind.Danger:
                btn.BackColor = ColorOf(pal.Window);
                btn.ForeColor = ColorOf(pal.Danger);
                btn.FlatAppearance.BorderColor = ColorOf(pal.Danger);
                btn.FlatAppearance.MouseOverBackColor = ColorOf(pal.DangerFill);
                btn.FlatAppearance.MouseDownBackColor = ColorOf(pal.DangerFill);
                break;
            default:
                btn.BackColor = ColorOf(pal.Button);
                btn.ForeColor = ColorOf(pal.Text);
                btn.FlatAppearance.BorderColor = ColorOf(pal.Stroke);
                btn.FlatAppearance.MouseOverBackColor = ColorOf(pal.ButtonHover);
                btn.FlatAppearance.MouseDownBackColor = ColorOf(pal.ButtonHover);
                break;
        }
    }

    static void StyleInput(Control control, FormTone.Palette pal, int dpi)
    {
        var field = ColorOf(pal.Field);
        var text = ColorOf(pal.Text);
        var h = FieldPx(dpi);
        var font = control.FindForm()?.Font;
        if (font is not null) control.Font = font;
        switch (control)
        {
            case TextBox box:
                var framed = box.Parent is FieldFrame;
                box.BorderStyle = BorderStyle.None;
                box.BackColor = field;
                box.ForeColor = text;
                box.HandleCreated -= ThemeTextScroll;
                box.HandleCreated += ThemeTextScroll;
                if (box.IsHandleCreated) ThemeTextScroll(box, EventArgs.Empty);
                if (!box.Multiline)
                {
                    if (framed)
                    {
                        box.MinimumSize = Size.Empty;
                        box.MaximumSize = Size.Empty;
                        box.Dock = DockStyle.None;
                        if (box.Parent is FieldFrame frame)
                            frame.Relayout();
                    }
                    else
                    {
                        box.MinimumSize = new Size(box.MinimumSize.Width, h);
                        box.Height = h;
                        CenterEdit(box);
                    }
                }
                break;
            case ComboBox combo:
                combo.FlatStyle = FlatStyle.Flat;
                combo.BackColor = field;
                combo.ForeColor = text;
                combo.IntegralHeight = false;
                if (combo.Parent is FieldFrame host)
                {
                    combo.Dock = DockStyle.None;
                    combo.MaximumSize = Size.Empty;
                    host.Relayout();
                }
                else
                {
                    combo.MinimumSize = new Size(combo.MinimumSize.Width, h);
                    combo.MaximumSize = new Size(0, h);
                    combo.Height = h;
                    FitComboItem(combo);
                }
                if (combo.IsHandleCreated) NativeTheme.StripCombo(combo.Handle);
                break;
            case NumericUpDown spin:
                spin.BorderStyle = BorderStyle.None;
                spin.BackColor = field;
                spin.ForeColor = text;
                spin.TextAlign = HorizontalAlignment.Center;
                spin.HandleCreated -= ThemeSpin;
                spin.HandleCreated += ThemeSpin;
                if (spin.IsHandleCreated) ThemeSpin(spin, EventArgs.Empty);
                if (spin.Parent is FieldFrame spinHost)
                {
                    spinHost.Width = UiLayout.ScalePx(FormTone.FieldWidthShort, dpi);
                    spinHost.Height = h;
                    spinHost.Relayout();
                }
                else
                {
                    spin.MinimumSize = new Size(spin.MinimumSize.Width, h);
                    spin.Height = h;
                }
                break;
            case FlatDatePicker picker:
                picker.ForeColor = text;
                picker.BackColor = field;
                picker.CalendarForeColor = text;
                picker.CalendarMonthBackground = field;
                picker.CalendarTitleBackColor = ColorOf(pal.Header);
                picker.CalendarTitleForeColor = text;
                picker.CalendarTrailingForeColor = ColorOf(pal.Secondary);
                picker.ApplyTheme();
                if (picker.Parent is FieldFrame dateHost)
                    dateHost.Relayout();
                else
                {
                    picker.MinimumSize = new Size(picker.MinimumSize.Width, h);
                    picker.Height = h;
                }
                break;
            case FlatUnitField unit:
                unit.ApplyTheme();
                if (unit.Parent is FieldFrame unitHost)
                {
                    unitHost.Width = UiLayout.ScalePx(FormTone.FieldWidthDuration, dpi);
                    unitHost.Height = h;
                    unitHost.Relayout();
                }
                else
                {
                    unit.MinimumSize = new Size(unit.MinimumSize.Width, h);
                    unit.Height = h;
                }
                break;
        }
    }

    static void FitComboItem(ComboBox combo)
    {
        var inner = Math.Max(16, combo.Height - 6);
        if (combo.ItemHeight != inner)
            combo.ItemHeight = inner;
    }

    const int EmSetRect = 0x00B3;

    internal static void CenterEdit(TextBox box, int padX = 6)
    {
        void ApplyRect(object? sender, EventArgs e)
        {
            if (!box.IsHandleCreated || box.IsDisposed || box.Multiline || box.ClientSize.Height <= 1) return;
            var fontH = TextRenderer.MeasureText("Ag", box.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height;
            var top = Math.Max(0, (box.ClientSize.Height - fontH) / 2);
            var rc = new RECT
            {
                Left = padX,
                Top = top,
                Right = Math.Max(padX + 4, box.ClientSize.Width - padX),
                Bottom = box.ClientSize.Height - top,
            };
            _ = SendMessage(box.Handle, EmSetRect, IntPtr.Zero, ref rc);
        }
        box.HandleCreated -= ApplyRect;
        box.HandleCreated += ApplyRect;
        box.Resize -= ApplyRect;
        box.Resize += ApplyRect;
        box.FontChanged -= ApplyRect;
        box.FontChanged += ApplyRect;
        if (box.IsHandleCreated) ApplyRect(box, EventArgs.Empty);
    }

    static void StyleTabs(TabControl tabs, FormTone.Palette pal, int dpi)
    {
        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.BackColor = ColorOf(pal.Window);
        foreach (TabPage page in tabs.TabPages)
        {
            page.UseVisualStyleBackColor = false;
            page.BackColor = ColorOf(pal.Window);
            page.ForeColor = ColorOf(pal.Text);
        }
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.Padding = new Point(16, 7);
        tabs.ItemSize = new Size(UiLayout.ScalePx(FormTone.TabItemWidth, dpi), UiLayout.ScalePx(FormTone.TabItemHeight, dpi));
        tabs.DrawItem -= DrawTab;
        tabs.DrawItem += DrawTab;
    }

    static void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs) return;
        var pal = Tone;
        var selected = (e.State & DrawItemState.Selected) != 0;
        var bounds = e.Bounds;
        using (var bg = new SolidBrush(selected ? ColorOf(pal.Window) : ColorOf(pal.Header)))
            e.Graphics.FillRectangle(bg, bounds);
        if (selected)
        {
            using var accent = new SolidBrush(ColorOf(pal.Accent));
            e.Graphics.FillRectangle(accent, bounds.Left + 8, bounds.Bottom - 2, Math.Max(8, bounds.Width - 16), 2);
        }
        var text = e.Index >= 0 && e.Index < tabs.TabPages.Count ? tabs.TabPages[e.Index].Text : "";
        TextRenderer.DrawText(
            e.Graphics,
            text,
            tabs.Font,
            bounds,
            ColorOf(pal.Text),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    static void ApplyTree(Control parent, FormTone.Palette pal, int dpi)
    {
        var window = ColorOf(pal.Window);
        var text = ColorOf(pal.Text);
        var secondary = ColorOf(pal.Secondary);
        switch (parent)
        {
            case Button button:
                StyleButton(button, button.Tag is UiButtonKind kind ? kind : UiButtonKind.Secondary);
                SizeToText(button, dpi);
                return;
            case DataGridView grid:
                StyleGrid(grid);
                return;
            case UsageChartBox plot:
                plot.BackColor = window;
                return;
            case TabControl tabs:
                StyleTabs(tabs, pal, dpi);
                break;
            case TextBox or ComboBox or NumericUpDown or DateTimePicker or FlatDatePicker or FlatUnitField:
                StyleInput(parent, pal, dpi);
                return;
            case CheckBox box:
                box.ForeColor = text;
                box.BackColor = window;
                box.AutoSize = true;
                if (box is FlatCheck)
                {
                    box.FlatStyle = FlatStyle.Flat;
                    box.FlatAppearance.BorderSize = 0;
                    box.FlatAppearance.CheckedBackColor = window;
                    box.FlatAppearance.MouseOverBackColor = window;
                    box.FlatAppearance.MouseDownBackColor = window;
                    return;
                }
                box.FlatStyle = FlatStyle.Standard;
                if (box.IsHandleCreated) NativeTheme.DarkExplorer(box.Handle);
                return;
            case LinkLabel link:
                link.LinkColor = ColorOf(pal.Accent);
                link.ActiveLinkColor = ColorOf(pal.AccentHover);
                link.VisitedLinkColor = ColorOf(pal.Accent);
                link.BackColor = Color.Transparent;
                return;
            case Label label:
                if (IsThemeSecondary(label.ForeColor))
                    label.ForeColor = secondary;
                else if (IsThemeText(label.ForeColor))
                    label.ForeColor = text;
                label.BackColor = Color.Transparent;
                break;
            case FieldFrame frame:
                frame.BackColor = ColorOf(pal.Field);
                if (frame.Controls.Count == 1 && frame.Controls[0] is not TextBox { Multiline: true })
                {
                    var h = FieldPx(dpi);
                    frame.Height = h;
                    frame.MinimumSize = new Size(0, h);
                    frame.MaximumSize = new Size(0, h);
                    frame.Relayout();
                }
                break;
            case AppearanceSegment segment:
                segment.BackColor = window;
                segment.Invalidate();
                return;
            case FieldRowPanel row:
                row.BackColor = window;
                row.ForeColor = text;
                row.Caption.ForeColor = secondary;
                row.Caption.Font = UiFont(9f);
                row.Fit(dpi);
                break;
            case SplitContainer split:
                split.BackColor = ColorOf(pal.Hairline);
                split.Panel1.BackColor = window;
                split.Panel2.BackColor = window;
                break;
            case Panel or TableLayoutPanel or FlowLayoutPanel or TabPage or UserControl:
                parent.BackColor = window;
                parent.ForeColor = text;
                break;
        }

        foreach (Control child in parent.Controls)
            ApplyTree(child, pal, dpi);
    }

    internal static GraphicsPath RoundRect(Rectangle bounds, int radius)
        => RoundRect(new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), radius);

    internal static GraphicsPath RoundRect(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(2f, Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // GDI+ DrawRectangle with a 1px pen rasterizes the bottom and right edges one pixel heavy.
    // Fill must stay on device pixels; AntiAlias (set for the chevron) smears a 1px rect across two rows.
    internal static void DrawHairline(Graphics g, Color color, int width, int height)
    {
        if (width < 2 || height < 2) return;
        var smooth = g.SmoothingMode;
        var offset = g.PixelOffsetMode;
        var unit = g.PageUnit;
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.None;
        g.PageUnit = GraphicsUnit.Pixel;
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, 0, 0, width, 1);
        g.FillRectangle(brush, 0, height - 1, width, 1);
        if (height > 2)
        {
            g.FillRectangle(brush, 0, 1, 1, height - 2);
            g.FillRectangle(brush, width - 1, 1, 1, height - 2);
        }
        g.PageUnit = unit;
        g.PixelOffsetMode = offset;
        g.SmoothingMode = smooth;
    }

    internal static void DrawRoundBorder(Graphics g, Color stroke, Color outside, int width, int height, int radius)
    {
        if (width < 2 || height < 2) return;
        var smooth = g.SmoothingMode;
        var offset = g.PixelOffsetMode;
        var unit = g.PageUnit;
        Region? previous = null;
        g.PageUnit = GraphicsUnit.Pixel;
        g.PixelOffsetMode = PixelOffsetMode.None;
        var bounds = new RectangleF(0.5f, 0.5f, Math.Max(1f, width - 1f), Math.Max(1f, height - 1f));
        using var path = RoundRect(bounds, Math.Max(1, radius));
        try
        {
            previous = g.Clip;
            using var outsideRegion = new Region(new Rectangle(0, 0, width, height));
            outsideRegion.Exclude(path);
            g.SmoothingMode = SmoothingMode.None;
            g.SetClip(outsideRegion, CombineMode.Replace);
            using var fill = new SolidBrush(outside);
            g.FillRectangle(fill, 0, 0, width, height);
        }
        finally
        {
            if (previous is not null)
            {
                g.Clip = previous;
                previous.Dispose();
            }
            else g.ResetClip();
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var pen = new Pen(stroke, 1f);
        g.DrawPath(pen, path);
        g.PageUnit = unit;
        g.PixelOffsetMode = offset;
        g.SmoothingMode = smooth;
    }

    static void ApplyTitleBar(Form form)
    {
        void Apply()
        {
            var dark = UseLightTheme() ? 0 : 1;
            _ = DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        }
        if (form.IsHandleCreated) Apply();
        else form.HandleCreated += (_, _) => Apply();
    }

    const int DwmwaUseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref RECT lParam);

    const int RgnDiff = 4;

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

    [DllImport("gdi32.dll")]
    static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int nCombineMode);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

}

