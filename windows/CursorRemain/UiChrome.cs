using System.Drawing.Drawing2D;
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
    public static bool AppsUseLightTheme()
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

    public static FormTone.Palette Tone => FormTone.For(AppsUseLightTheme());

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
        btn.Click += (_, _) => menu.Show(btn, new Point(0, btn.Height));
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
    }

    public static void StyleGrid(DataGridView grid)
    {
        var pal = Tone;
        grid.BorderStyle = BorderStyle.None;
        grid.BackgroundColor = ColorOf(pal.Window);
        grid.GridColor = ColorOf(pal.Hairline);
        grid.EnableHeadersVisualStyles = false;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = ColorOf(pal.Header),
            ForeColor = ColorOf(pal.Secondary),
            SelectionBackColor = ColorOf(pal.Header),
            SelectionForeColor = ColorOf(pal.Secondary),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0),
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
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
                if (picker.Parent is FieldFrame dateHost)
                    dateHost.Relayout();
                else
                {
                    picker.MinimumSize = new Size(picker.MinimumSize.Width, h);
                    picker.Height = h;
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
            case TextBox or ComboBox or NumericUpDown or DateTimePicker or FlatDatePicker:
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
                if (label.ForeColor == Color.DimGray || label.ForeColor.ToArgb() == secondary.ToArgb())
                    label.ForeColor = secondary;
                else if (label.ForeColor == SystemColors.ControlText || label.ForeColor == Color.Black)
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

    static void ApplyTitleBar(Form form)
    {
        void Apply()
        {
            var dark = AppsUseLightTheme() ? 0 : 1;
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

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

}

static class NativeTheme
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string? pszSubIdList);

    [DllImport("uxtheme.dll", EntryPoint = "#133")]
    static extern int AllowDarkModeForWindow(IntPtr hwnd, bool allow);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#104")]
    static extern void RefreshImmersiveColorPolicyState();

    const int WmThemeChanged = 0x031A;

    [DllImport("user32.dll")]
    static extern bool GetComboBoxInfo(IntPtr hwndCombo, ref ComboBoxInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string? className, string? window);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    const int WmSetRedraw = 0x000B;

    public static void SetRedraw(Control control, bool enabled)
    {
        if (!control.IsHandleCreated) return;
        SendMessage(control.Handle, WmSetRedraw, (IntPtr)(enabled ? 1 : 0), IntPtr.Zero);
        if (enabled)
        {
            control.Invalidate(true);
            control.Update();
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static void Strip(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        _ = SetWindowTheme(hwnd, "", "");
        const int GwlStyle = -16;
        const int GwlExStyle = -20;
        const int WsBorder = 0x00800000;
        const int WsExClientEdge = 0x00000200;
        const int WsExStaticEdge = 0x00020000;
        const int WsExWindowEdge = 0x00000100;
        const uint SwpNosize = 0x0001;
        const uint SwpNomove = 0x0002;
        const uint SwpNozorder = 0x0004;
        const uint SwpFramechanged = 0x0020;
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlStyle, (IntPtr)(style & ~WsBorder));
        var ex = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, (IntPtr)(ex & ~WsExClientEdge & ~WsExStaticEdge & ~WsExWindowEdge));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNosize | SwpNomove | SwpNozorder | SwpFramechanged);
    }

    public static void PreferAppDarkMode()
    {
        try
        {
            SetPreferredAppMode(UiChrome.AppsUseLightTheme() ? 0 : 2);
            RefreshImmersiveColorPolicyState();
        }
        catch
        {
            // older Windows builds omit these ordinals
        }
    }

    public static void StripCombo(IntPtr hwnd)
    {
        Strip(hwnd);
        var info = new ComboBoxInfo { cbSize = Marshal.SizeOf<ComboBoxInfo>() };
        if (!GetComboBoxInfo(hwnd, ref info)) return;
        if (info.hwndItem != IntPtr.Zero) Strip(info.hwndItem);
        if (info.hwndList != IntPtr.Zero) Strip(info.hwndList);
    }

    public static void DarkExplorer(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try { AllowDarkModeForWindow(hwnd, !UiChrome.AppsUseLightTheme()); }
        catch { }
        _ = SetWindowTheme(hwnd, UiChrome.AppsUseLightTheme() ? "Explorer" : "DarkMode_Explorer", null);
        SendMessage(hwnd, WmThemeChanged, IntPtr.Zero, IntPtr.Zero);
    }

    public static void DarkScroll(Control scroll)
    {
        if (scroll is null || !scroll.IsHandleCreated) return;
        DarkExplorer(scroll.Handle);
        var child = IntPtr.Zero;
        while ((child = FindWindowEx(scroll.Handle, child, "ScrollBar", null)) != IntPtr.Zero)
            DarkExplorer(child);
    }

    public static void DarkTree(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        DarkExplorer(hwnd);
        var child = IntPtr.Zero;
        while ((child = FindWindowEx(hwnd, child, null, null)) != IntPtr.Zero)
            DarkTree(child);
    }

    public static void Spin(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        Strip(hwnd);
        var child = IntPtr.Zero;
        while ((child = FindWindowEx(hwnd, child, null, null)) != IntPtr.Zero)
        {
            Strip(child);
            var cls = ClassName(child);
            if (cls.Contains("updown", StringComparison.OrdinalIgnoreCase))
                ShowWindow(child, 0);
        }
    }

    static string ClassName(IntPtr hwnd)
    {
        var buf = new char[64];
        var n = GetClassName(hwnd, buf, buf.Length);
        return n > 0 ? new string(buf, 0, n) : "";
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);

    public static void ComboList(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var info = new ComboBoxInfo { cbSize = Marshal.SizeOf<ComboBoxInfo>() };
        if (!GetComboBoxInfo(hwnd, ref info) || info.hwndList == IntPtr.Zero) return;
        DarkExplorer(info.hwndList);
    }

    public static Rectangle ComboButton(IntPtr hwnd, int fallbackLeft, int height, int width)
    {
        var info = new ComboBoxInfo { cbSize = Marshal.SizeOf<ComboBoxInfo>() };
        if (hwnd != IntPtr.Zero && GetComboBoxInfo(hwnd, ref info))
        {
            var left = Math.Max(0, Math.Min(info.rcButton.Left, fallbackLeft));
            return Rectangle.FromLTRB(left, 1, Math.Max(left + 1, width - 1), Math.Max(2, height - 1));
        }
        return Rectangle.FromLTRB(fallbackLeft, 1, width - 1, Math.Max(2, height - 1));
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ComboBoxInfo
    {
        public int cbSize;
        public Rect rcItem;
        public Rect rcButton;
        public int stateButton;
        public IntPtr hwndCombo;
        public IntPtr hwndItem;
        public IntPtr hwndList;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

sealed class FieldFrame : Panel
{
    public FieldFrame()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        UpdateStyles();
    }

    public void Relayout() => LayoutInner();

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        if (Parent is FieldRowPanel { HasCaption: true, FieldWidth: > 0 } row)
        {
            var dpi = DeviceDpi > 0 ? DeviceDpi : 96;
            width = Math.Min(width, row.ScaledFieldWidth(dpi));
        }
        base.SetBoundsCore(x, y, width, height, specified);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (Controls.Count == 1 && Controls[0] is TextBox { Multiline: true })
            return base.GetPreferredSize(proposedSize);
        var dpi = DeviceDpi > 0 ? DeviceDpi : 96;
        var h = UiChrome.FieldPx(dpi);
        var w = proposedSize.Width > 0 ? proposedSize.Width : Math.Max(Width, 80);
        if (Parent is FieldRowPanel { HasCaption: true, FieldWidth: > 0 } row)
            w = Math.Min(w, row.ScaledFieldWidth(dpi));
        return new Size(Math.Max(8, w), h);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        LayoutInner();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutInner();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        LayoutInner();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var window = Parent?.BackColor ?? UiChrome.ColorOf(UiChrome.Tone.Window);
        e.Graphics.Clear(window);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var radius = UiLayout.ScalePx(FormTone.ButtonRadius, DeviceDpi > 0 ? DeviceDpi : 96);
        var bounds = new RectangleF(0.5f, 0.5f, Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f));
        using var path = UiChrome.RoundRect(bounds, radius);
        using (var br = new SolidBrush(UiChrome.ColorOf(UiChrome.Tone.Field)))
            g.FillPath(br, path);
        using var pen = new Pen(UiChrome.ColorOf(UiChrome.Tone.Field));
        g.DrawPath(pen, path);
    }

    void LayoutInner()
    {
        if (Controls.Count != 1) return;
        var inner = Controls[0];
        if (inner is NumericUpDown spin)
        {
            inner.Dock = DockStyle.None;
            inner.Margin = Padding.Empty;
            var inset = 1;
            var w = Math.Max(8, Width - inset * 2);
            var h = Math.Max(8, Math.Min(spin.PreferredSize.Height, Height - inset * 2));
            inner.Bounds = new Rectangle(inset, Math.Max(inset, (Height - h) / 2), w, h);
            return;
        }
        if (inner is TextBox { Multiline: true } or ComboBox or FlatDatePicker)
        {
            inner.Dock = DockStyle.None;
            inner.Margin = Padding.Empty;
            var inset = inner is ComboBox ? 3 : 1;
            inner.Bounds = new Rectangle(inset, inset, Math.Max(8, Width - inset * 2), Math.Max(8, Height - inset * 2));
            if (inner is ComboBox combo)
            {
                var innerH = Math.Max(16, inner.Height - 6);
                if (combo.ItemHeight != innerH)
                    combo.ItemHeight = innerH;
            }
            return;
        }
        inner.Dock = DockStyle.None;
        inner.Margin = Padding.Empty;
        var padX = 6;
        var line = Math.Max(inner.Font.Height + 2, inner.PreferredSize.Height);
        inner.Size = new Size(Math.Max(8, ClientSize.Width - padX * 2), line);
        inner.Location = new Point(padX, Math.Max(0, (ClientSize.Height - inner.Height) / 2));
    }
}

sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkMenuColors()) { }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var pal = UiChrome.Tone;
        var fill = e.Item.Selected ? UiChrome.ColorOf(pal.Selection) : UiChrome.ColorOf(pal.Field);
        using var br = new SolidBrush(fill);
        e.Graphics.FillRectangle(br, new Rectangle(Point.Empty, e.Item.Size));
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(UiChrome.ColorOf(UiChrome.Tone.Hairline));
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 8, y, Math.Max(8, e.Item.Width - 8), y);
    }

    sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color MenuBorder => UiChrome.ColorOf(UiChrome.Tone.Stroke);
        public override Color MenuItemBorder => UiChrome.ColorOf(UiChrome.Tone.Selection);
        public override Color MenuItemSelected => UiChrome.ColorOf(UiChrome.Tone.Selection);
        public override Color MenuItemSelectedGradientBegin => UiChrome.ColorOf(UiChrome.Tone.Selection);
        public override Color MenuItemSelectedGradientEnd => UiChrome.ColorOf(UiChrome.Tone.Selection);
        public override Color ToolStripDropDownBackground => UiChrome.ColorOf(UiChrome.Tone.Field);
        public override Color ImageMarginGradientBegin => UiChrome.ColorOf(UiChrome.Tone.Field);
        public override Color ImageMarginGradientMiddle => UiChrome.ColorOf(UiChrome.Tone.Field);
        public override Color ImageMarginGradientEnd => UiChrome.ColorOf(UiChrome.Tone.Field);
        public override Color SeparatorDark => UiChrome.ColorOf(UiChrome.Tone.Hairline);
        public override Color SeparatorLight => UiChrome.ColorOf(UiChrome.Tone.Hairline);
    }
}

sealed class FieldRowPanel : Panel
{
    public Label Caption { get; }
    public Control Field { get; }
    public int FieldWidth { get; }
    public bool HasCaption => Caption is not null && !string.IsNullOrEmpty(Caption.Text);

    public int ScaledFieldWidth(int dpi) =>
        FieldWidth > 0 ? UiLayout.ScalePx(FieldWidth, dpi) : Math.Max(8, Width);

    public FieldRowPanel(string label, Control field, int fieldWidth = FormTone.FieldMaxWidth)
    {
        AutoSize = false;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.None;
        Margin = new Padding(0, 4, 0, 6);
        Tag = UiChrome.FieldRowTag;
        FieldWidth = fieldWidth;
        Caption = new Label
        {
            Text = label,
            AutoSize = false,
            Dock = DockStyle.None,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Visible = label.Length > 0,
        };
        Field = field;
        field.Dock = DockStyle.None;
        field.Margin = Padding.Empty;
        Controls.Add(field);
        Controls.Add(Caption);
        Height = FormTone.FieldHeight + (label.Length > 0 ? 24 : 8);
        MinimumSize = new Size(0, Height);
    }

    public void Fit(int dpi)
    {
        var h = MeasureHeight(dpi, Width);
        MinimumSize = new Size(0, h);
        Height = h;
        PerformLayout();
    }

    int MeasureHeight(int dpi, int width)
    {
        var fieldH = Math.Max(UiChrome.FieldPx(dpi), Field?.GetPreferredSize(new Size(Math.Max(80, width), 0)).Height ?? 0);
        if (!HasCaption) return fieldH;
        return CaptionHeight(dpi) + UiLayout.ScalePx(4, dpi) + fieldH;
    }

    int CaptionHeight(int dpi) => UiLayout.ScalePx(18, dpi);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var w = proposedSize.Width > 1 ? proposedSize.Width : Math.Max(Width, 80);
        var dpi = DeviceDpi > 0 ? DeviceDpi : 96;
        return new Size(w, MeasureHeight(dpi, w));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (Field is null || Caption is null) return;
        var dpi = DeviceDpi > 0 ? DeviceDpi : 96;
        var want = MeasureHeight(dpi, Width);
        if (Math.Abs(Height - want) > 1)
        {
            MinimumSize = new Size(0, want);
            Height = want;
        }
        var capH = HasCaption ? CaptionHeight(dpi) : 0;
        var gap = HasCaption ? UiLayout.ScalePx(4, dpi) : 0;
        var maxW = ScaledFieldWidth(dpi);
        var fieldW = HasCaption ? Math.Min(Math.Max(8, Width), maxW) : Math.Max(8, Width);
        Field.Dock = DockStyle.None;
        Field.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        Field.MaximumSize = HasCaption && FieldWidth > 0 ? new Size(maxW, 0) : Size.Empty;
        if (HasCaption)
            Caption.SetBounds(0, 0, Math.Max(fieldW, Width), capH);
        else
            Caption.SetBounds(0, 0, 0, 0);
        var fieldH = Math.Max(8, Height - capH - gap);
        Field.SetBounds(0, capH + gap, fieldW, fieldH);
        base.OnLayout(levent);
    }
}

sealed class FlatTabHost : Panel
{
    readonly List<string> _titles = [];
    readonly List<Panel> _pages = [];
    readonly Panel _body = new() { Dock = DockStyle.Fill };
    int _selected;
    int _hover = -1;

    public FlatTabHost()
    {
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        Padding = new Padding(SettingsLayout.NavWidth, 0, 0, 0);
        Controls.Add(_body);
    }

    public IReadOnlyList<Panel> Pages => _pages;
    public int TabCount => _pages.Count;
    public event EventHandler? SelectedIndexChanged;

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value < 0 || value >= _pages.Count || value == _selected) return;
            _selected = value;
            ShowSelected();
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void AddPage(string title, Panel page)
    {
        page.Dock = DockStyle.Fill;
        _titles.Add(title);
        _pages.Add(page);
        _body.Controls.Add(page);
        page.Visible = _pages.Count == 1;
        Invalidate();
    }

    public void PreparePages()
    {
        var bounds = _body.ClientRectangle;
        if (bounds.Width < 8 || bounds.Height < 8) return;
        foreach (var page in _pages)
            page.Bounds = bounds;
    }

    public void FitNav(int dpi)
    {
        var w = UiLayout.ScalePx(SettingsLayout.NavWidth, dpi);
        Padding = new Padding(w, 0, 0, 0);
        _body.Dock = DockStyle.Fill;
        Invalidate();
    }

    int NavSpan => Math.Max(1, Padding.Left);

    int ItemHeight
    {
        get
        {
            var dpi = DeviceDpi > 0 ? DeviceDpi : 96;
            return UiLayout.ScalePx(FormTone.TabItemHeight, dpi);
        }
    }

    Rectangle TabSlot(int index)
    {
        var h = ItemHeight;
        return new Rectangle(0, index * h, NavSpan, h);
    }

    int TabAt(Point pt)
    {
        if (pt.X >= NavSpan) return -1;
        for (var i = 0; i < _pages.Count; i++)
        {
            if (TabSlot(i).Contains(pt)) return i;
        }
        return -1;
    }

    void ShowSelected()
    {
        NativeTheme.SetRedraw(this, false);
        try
        {
            for (var i = 0; i < _pages.Count; i++)
            {
                var on = i == _selected;
                if (_pages[i].Visible != on)
                    _pages[i].Visible = on;
                if (on)
                    _pages[i].BringToFront();
            }
        }
        finally
        {
            NativeTheme.SetRedraw(this, true);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var idx = TabAt(e.Location);
        if (idx >= 0)
            SelectedIndex = idx;
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var idx = TabAt(e.Location);
        if (idx == _hover) return;
        _hover = idx;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover < 0) return;
        _hover = -1;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(UiChrome.ColorOf(UiChrome.Tone.Window));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var pal = UiChrome.Tone;
        var window = UiChrome.ColorOf(pal.Window);
        var text = UiChrome.ColorOf(pal.Text);
        var secondary = UiChrome.ColorOf(pal.Secondary);
        var accent = UiChrome.ColorOf(pal.Accent);
        var hair = UiChrome.ColorOf(pal.Hairline);
        var header = UiChrome.ColorOf(pal.Header);
        var g = e.Graphics;
        var navW = NavSpan;
        g.Clear(window);
        using (var navBg = new SolidBrush(header))
            g.FillRectangle(navBg, 0, 0, navW, Height);
        using (var line = new SolidBrush(hair))
            g.FillRectangle(line, Math.Max(0, navW - 1), 0, 1, Height);
        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        var dpi = DeviceDpi > 0 ? DeviceDpi : 96;
        var pad = UiLayout.ScalePx(14, dpi);
        var mark = UiLayout.ScalePx(3, dpi);
        var iconSlot = UiLayout.ScalePx(16, dpi);
        var iconGap = UiLayout.ScalePx(8, dpi);
        using var iconFont = UiChrome.IconFont(11f);
        for (var i = 0; i < _pages.Count; i++)
        {
            var bounds = TabSlot(i);
            var selected = i == _selected;
            var color = selected ? text : secondary;
            if (selected)
            {
                using var fill = new SolidBrush(window);
                g.FillRectangle(fill, bounds);
                using var bar = new SolidBrush(accent);
                g.FillRectangle(bar, bounds.X, bounds.Y + 6, mark, Math.Max(8, bounds.Height - 12));
            }
            else if (i == _hover)
            {
                using var hover = new SolidBrush(window);
                g.FillRectangle(hover, bounds);
            }
            var x = bounds.X + pad;
            var glyph = SettingsLayout.TabGlyph(_titles[i]);
            if (iconFont is not null && glyph.Length > 0)
            {
                var iconBox = new Rectangle(x, bounds.Y, iconSlot, bounds.Height);
                TextRenderer.DrawText(g, glyph, iconFont, iconBox, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                x += iconSlot + iconGap;
            }
            var label = new Rectangle(x, bounds.Y, Math.Max(8, bounds.Right - x - 8), bounds.Height);
            TextRenderer.DrawText(g, _titles[i], Font, label, color, flags);
        }
    }

}

sealed class FlatCombo : ComboBox
{
    const int WmPaint = 0x000F;
    const int WmWindowPosChanging = 0x0046;
    const int SwpNosize = 0x0001;

    public FlatCombo()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        IntegralHeight = false;
        MaxDropDownItems = 12;
    }

    int DesiredHeight()
    {
        if (Parent is FieldFrame frame && frame.ClientSize.Height > 1)
            return frame.ClientSize.Height;
        return UiChrome.FieldPx(DeviceDpi > 0 ? DeviceDpi : 96);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeTheme.StripCombo(Handle);
        BackColor = UiChrome.ColorOf(UiChrome.Tone.Field);
        ForeColor = UiChrome.ColorOf(UiChrome.Tone.Text);
        ItemHeight = Math.Max(16, DesiredHeight() - 6);
        Height = DesiredHeight();
    }

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        height = DesiredHeight();
        base.SetBoundsCore(x, y, width, height, specified);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        var inner = Math.Max(16, Height - 6);
        if (ItemHeight != inner)
            ItemHeight = inner;
    }

    protected override void OnDropDown(EventArgs e)
    {
        FitDropDownWidth();
        NativeTheme.ComboList(Handle);
        base.OnDropDown(e);
    }

    public void FitDropDownWidth()
    {
        var pad = UiLayout.ScalePx(36, DeviceDpi);
        var max = Math.Max(Width, UiLayout.ScalePx(280, DeviceDpi));
        var w = Width;
        foreach (var item in Items)
        {
            var text = item?.ToString() ?? "";
            w = Math.Max(w, TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width + pad);
        }
        DropDownWidth = Math.Min(max, Math.Max(Width, w));
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Graphics is null) return;
        var pal = UiChrome.Tone;
        var edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
        var selected = !edit && (e.State & DrawItemState.Selected) != 0;
        var bg = selected ? UiChrome.ColorOf(pal.Selection) : UiChrome.ColorOf(pal.Field);
        var fg = UiChrome.ColorOf(pal.Text);
        using var br = new SolidBrush(bg);
        e.Graphics.FillRectangle(br, e.Bounds);
        var text = Items[e.Index]?.ToString() ?? "";
        var pad = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(4, e.Bounds.Width - 10), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, text, Font, pad, fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmWindowPosChanging && m.LParam != IntPtr.Zero)
        {
            var pos = Marshal.PtrToStructure<WindowPos>(m.LParam);
            var want = DesiredHeight();
            if ((pos.Flags & SwpNosize) == 0 && pos.Cy > 0 && pos.Cy != want)
            {
                pos.Cy = want;
                Marshal.StructureToPtr(pos, m.LParam, false);
            }
        }
        const int WmNcPaint = 0x0085;
        if (m.Msg == WmNcPaint)
        {
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
        if (m.Msg == WmPaint) PaintChrome();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WindowPos
    {
        public IntPtr Hwnd;
        public IntPtr HwndInsertAfter;
        public int X, Y, Cx, Cy, Flags;
    }

    void PaintChrome()
    {
        if (!IsHandleCreated || Width <= 1 || Height <= 1) return;
        using var g = Graphics.FromHwnd(Handle);
        var pal = UiChrome.Tone;
        var field = UiChrome.ColorOf(pal.Field);
        var stroke = UiChrome.ColorOf(pal.Stroke);
        var text = UiChrome.ColorOf(pal.Secondary);
        using (var cover = new SolidBrush(field))
        {
            g.FillRectangle(cover, 0, 0, Width, 3);
            g.FillRectangle(cover, 0, Height - 3, Width, 3);
            g.FillRectangle(cover, 0, 0, 3, Height);
            g.FillRectangle(cover, Width - 3, 0, 3, Height);
        }
        if (Parent is not FieldFrame)
        {
            using var pen = new Pen(stroke);
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        var fallbackLeft = Width - Math.Max(20, UiLayout.ScalePx(20, DeviceDpi));
        var arrow = NativeTheme.ComboButton(Handle, fallbackLeft, Height, Width);
        using (var br = new SolidBrush(field))
            g.FillRectangle(br, arrow);
        DrawChevron(g, arrow, text);
    }

    static void DrawChevron(Graphics g, Rectangle area, Color color)
    {
        var cx = area.X + area.Width / 2;
        var cy = area.Y + area.Height / 2;
        using var pen = new Pen(color, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawLines(pen, new[] { new Point(cx - 4, cy - 1), new Point(cx, cy + 3), new Point(cx + 4, cy - 1) });
    }
}

sealed class FlatSpin : NumericUpDown
{
    const int WmPaint = 0x000F;
    readonly System.Windows.Forms.Timer _repeat = new() { Interval = 360 };
    int _press;
    int _hover;

    public FlatSpin()
    {
        BorderStyle = BorderStyle.None;
        DecimalPlaces = 0;
        TextAlign = HorizontalAlignment.Center;
        _repeat.Tick += (_, _) =>
        {
            if (_press == 0) { _repeat.Stop(); return; }
            Step(_press);
            _repeat.Interval = 70;
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _repeat.Dispose();
        base.Dispose(disposing);
    }

    const int WmLButtonDown = 0x0201;
    const int WmLButtonUp = 0x0202;
    const int WmLButtonDblClk = 0x0203;

    Control? NativeButtons => Controls.Cast<Control>().FirstOrDefault(c => c.GetType().Name == "UpDownButtons");
    TextBox? Edit => Controls.OfType<TextBox>().FirstOrDefault();
    int SpinWidth() => Math.Max(18, UiLayout.ScalePx(18, DeviceDpi > 0 ? DeviceDpi : 96));
    Rectangle SpinBox() => new(Math.Max(0, Width - SpinWidth()), 0, SpinWidth() + 1, Height);
    Rectangle UpBox()
    {
        var box = SpinBox();
        return new Rectangle(box.X, box.Y, box.Width, box.Height / 2);
    }
    Rectangle DownBox()
    {
        var box = SpinBox();
        return new Rectangle(box.X, box.Y + box.Height / 2, box.Width, box.Height - box.Height / 2);
    }

    void HideNativeButtons()
    {
        var buttons = NativeButtons;
        if (buttons is null) return;
        buttons.Visible = false;
        buttons.Enabled = false;
        buttons.SetBounds(-32000, -32000, 0, 0);
    }

    void FitEdit()
    {
        HideNativeButtons();
        var edit = Edit;
        if (edit is null) return;
        edit.BorderStyle = BorderStyle.None;
        edit.Dock = DockStyle.None;
        edit.Margin = Padding.Empty;
        edit.TextAlign = HorizontalAlignment.Center;
        var dpi = DeviceDpi > 0 ? DeviceDpi : 96;
        var spinW = SpinWidth();
        var padX = UiLayout.ScalePx(6, dpi);
        var fontH = Math.Max(edit.Font.Height + 2, TextRenderer.MeasureText("8", edit.Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Height);
        var h = Math.Min(Math.Max(8, fontH), Math.Max(8, Height - 2));
        var editW = Math.Max(8, Width - spinW - padX - 2);
        edit.MaximumSize = Size.Empty;
        edit.Bounds = new Rectangle(padX, Math.Max(0, (Height - h) / 2), editW, h);
        if (edit.IsHandleCreated) NativeTheme.Strip(edit.Handle);
        UiChrome.CenterEdit(edit, 0);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeTheme.Spin(Handle);
        BackColor = UiChrome.ColorOf(UiChrome.Tone.Field);
        ForeColor = UiChrome.ColorOf(UiChrome.Tone.Text);
        if (Edit is { } edit)
        {
            edit.BackColor = BackColor;
            edit.ForeColor = ForeColor;
        }
        FitEdit();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        FitEdit();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        FitEdit();
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        FitEdit();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var next = UpBox().Contains(e.Location) ? 1 : DownBox().Contains(e.Location) ? -1 : 0;
        if (next != _hover)
        {
            _hover = next;
            Invalidate();
        }
        Cursor = next != 0 ? Cursors.Hand : Cursors.IBeam;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = 0;
        EndRepeat();
        Cursor = Cursors.Default;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (UpBox().Contains(e.Location)) { BeginRepeat(1); return; }
            if (DownBox().Contains(e.Location)) { BeginRepeat(-1); return; }
            if (Edit is { } edit)
            {
                edit.Focus();
                edit.SelectAll();
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        EndRepeat();
        base.OnMouseUp(e);
    }

    void BeginRepeat(int dir)
    {
        Focus();
        Step(dir);
        _press = dir;
        _repeat.Interval = 360;
        _repeat.Start();
        Invalidate();
    }

    void EndRepeat()
    {
        _press = 0;
        _repeat.Stop();
        Invalidate();
    }

    void Step(int dir)
    {
        if (dir > 0) UpButton();
        else DownButton();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg is WmLButtonDown or WmLButtonDblClk)
        {
            var raw = m.LParam.ToInt64();
            var pt = new Point((short)(raw & 0xFFFF), (short)((raw >> 16) & 0xFFFF));
            if (UpBox().Contains(pt)) { BeginRepeat(1); return; }
            if (DownBox().Contains(pt)) { BeginRepeat(-1); return; }
        }
        if (m.Msg == WmLButtonUp && _press != 0)
        {
            EndRepeat();
            return;
        }
        try { base.WndProc(ref m); }
        catch (InvalidOperationException) { }
        if (m.Msg == WmPaint) PaintChrome();
    }

    void PaintChrome()
    {
        if (!IsHandleCreated || Width <= 1 || Height <= 1) return;
        using var g = Graphics.FromHwnd(Handle);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var pal = UiChrome.Tone;
        var field = UiChrome.ColorOf(pal.Field);
        var stroke = UiChrome.ColorOf(pal.Stroke);
        var text = UiChrome.ColorOf(pal.Secondary);
        var hover = UiChrome.ColorOf(pal.ButtonHover);
        var framed = Parent is FieldFrame;
        if (!framed)
        {
            using var pen = new Pen(stroke);
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        var arrow = SpinBox();
        using (var br = new SolidBrush(field))
        {
            g.FillRectangle(br, arrow);
            g.FillRectangle(br, arrow.X, 0, arrow.Width, 3);
            g.FillRectangle(br, arrow.X, Math.Max(0, Height - 3), arrow.Width, 3);
        }
        var up = UpBox();
        var down = DownBox();
        if (_hover == 1 || _press == 1)
        {
            using var br = new SolidBrush(hover);
            g.FillRectangle(br, up);
        }
        if (_hover == -1 || _press == -1)
        {
            using var br = new SolidBrush(hover);
            g.FillRectangle(br, down);
        }
        var dpi = DeviceDpi > 0 ? DeviceDpi : 96;
        DrawChevron(g, up, text, up: true, dpi);
        DrawChevron(g, down, text, up: false, dpi);
    }

    static void DrawChevron(Graphics g, Rectangle area, Color color, bool up, int dpi)
    {
        var s = Math.Max(3, UiLayout.ScalePx(3, dpi));
        var cx = area.X + area.Width / 2;
        var cy = area.Y + area.Height / 2;
        using var pen = new Pen(color, Math.Max(1.2f, dpi / 96f * 1.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (up)
            g.DrawLines(pen, new[] { new Point(cx - s, cy + s - 1), new Point(cx, cy - s + 1), new Point(cx + s, cy + s - 1) });
        else
            g.DrawLines(pen, new[] { new Point(cx - s, cy - s + 1), new Point(cx, cy + s - 1), new Point(cx + s, cy - s + 1) });
    }
}

sealed class FlatButton : Button
{
    public FlatButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        UpdateStyles();
    }

    protected override void OnPaintBackground(PaintEventArgs pevent) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var pal = UiChrome.Tone;
        var window = Parent?.BackColor ?? UiChrome.ColorOf(pal.Window);
        using (var clear = new SolidBrush(window))
            g.FillRectangle(clear, ClientRectangle);

        var hover = Enabled && ClientRectangle.Contains(PointToClient(MousePosition));
        var down = hover && (MouseButtons & MouseButtons.Left) != 0;
        var fill = !Enabled
            ? window
            : down ? FlatAppearance.MouseDownBackColor
            : hover ? FlatAppearance.MouseOverBackColor
            : BackColor;
        var stroke = Enabled ? FlatAppearance.BorderColor : UiChrome.ColorOf(pal.Hairline);
        var text = Enabled ? ForeColor : UiChrome.ColorOf(pal.Secondary);
        var radius = UiLayout.ScalePx(FormTone.ButtonRadius, DeviceDpi);
        var bounds = new RectangleF(0.5f, 0.5f, Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f));
        using var path = UiChrome.RoundRect(bounds, radius);
        using (var br = new SolidBrush(fill))
            g.FillPath(br, path);
        using (var pen = new Pen(stroke, 1f))
            g.DrawPath(pen, path);
        TextRenderer.DrawText(
            g,
            Text ?? "",
            Font,
            ClientRectangle,
            text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }
}

sealed class FlatCheck : CheckBox
{
    public FlatCheck()
    {
        AutoSize = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        UpdateStyles();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var box = Math.Max(14, UiLayout.ScalePx(14, DeviceDpi));
        var text = TextRenderer.MeasureText(Text, Font);
        return new Size(box + 8 + text.Width, Math.Max(FormTone.FieldHeight - 2, text.Height + 2));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var window = BackColor;
        using (var clear = new SolidBrush(window))
            g.FillRectangle(clear, ClientRectangle);
        var pal = UiChrome.Tone;
        var box = Math.Max(14, UiLayout.ScalePx(14, DeviceDpi));
        var y = Math.Max(0, (Height - box) / 2);
        var rect = new Rectangle(1, y, box, box);
        var accent = UiChrome.ColorOf(pal.Accent);
        var stroke = UiChrome.ColorOf(pal.Stroke);
        var field = UiChrome.ColorOf(pal.Field);
        using var path = UiChrome.RoundRect(rect, 3);
        using (var br = new SolidBrush(Checked ? accent : field))
            g.FillPath(br, path);
        using (var pen = new Pen(Checked ? accent : stroke))
            g.DrawPath(pen, path);
        if (Checked)
        {
            using var mark = new Pen(UiChrome.ColorOf(pal.OnAccent), 1.6f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            g.DrawLines(mark, new[]
            {
                new Point(rect.X + 3, rect.Y + box / 2),
                new Point(rect.X + box / 2 - 1, rect.Bottom - 4),
                new Point(rect.Right - 3, rect.Y + 3),
            });
        }
        var textRect = new Rectangle(box + 8, 0, Math.Max(4, Width - box - 8), Height);
        TextRenderer.DrawText(
            g,
            Text,
            Font,
            textRect,
            Enabled ? ForeColor : UiChrome.ColorOf(pal.Secondary),
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
    }
}

sealed class FlatDatePicker : Control
{
    DateTime _value = DateTime.Now;
    DateTime _min = new(2000, 1, 1);
    DateTime _max = new(2100, 1, 1);
    bool _checked = true;
    bool _hover;
    Form? _popup;

    public event EventHandler? ValueChanged;

    public FlatDatePicker()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        UpdateStyles();
        Cursor = Cursors.Hand;
        CustomFormat = "yyyy-MM-dd";
        Height = FormTone.FieldHeight;
        TabStop = true;
    }

    public string CustomFormat { get; set; }
    public bool ShowCheckBox { get; set; }
    public bool ShowUpDown { get; set; }
    public Color CalendarForeColor { get; set; }
    public Color CalendarMonthBackground { get; set; }
    public Color CalendarTitleBackColor { get; set; }
    public Color CalendarTitleForeColor { get; set; }
    public Color CalendarTrailingForeColor { get; set; }

    public DateTime MinDate
    {
        get => _min;
        set { _min = value; Value = _value; }
    }

    public DateTime MaxDate
    {
        get => _max;
        set { _max = value; Value = _value; }
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public DateTime Value
    {
        get => _value;
        set
        {
            var next = Clamp(value);
            if (next == _value) return;
            _value = next;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public override string Text => ShowCheckBox && !Checked ? "" : _value.ToString(CustomFormat);

    bool NeedsTime()
    {
        var format = CustomFormat ?? "";
        return format.Contains("HH", StringComparison.Ordinal) || format.Contains("hh", StringComparison.Ordinal)
            || format.Contains("mm", StringComparison.Ordinal);
    }

    DateTime Clamp(DateTime value)
    {
        if (value < _min) return _min;
        if (value > _max) return _max;
        return value;
    }

    int ButtonWidth() => Math.Max(22, UiLayout.ScalePx(28, DeviceDpi > 0 ? DeviceDpi : 96));

    Rectangle ButtonBox()
    {
        var w = ButtonWidth();
        return new Rectangle(Math.Max(0, Width - w), 0, w, Height);
    }

    Rectangle CheckBox()
    {
        var size = Math.Max(12, UiLayout.ScalePx(14, DeviceDpi > 0 ? DeviceDpi : 96));
        return new Rectangle(8, Math.Max(0, (Height - size) / 2), size, size);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var fill = Parent is FieldFrame
            ? UiChrome.ColorOf(UiChrome.Tone.Field)
            : Parent?.BackColor ?? UiChrome.ColorOf(UiChrome.Tone.Window);
        e.Graphics.Clear(fill);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var pal = UiChrome.Tone;
        var field = UiChrome.ColorOf(pal.Field);
        var text = UiChrome.ColorOf(pal.Text);
        var secondary = UiChrome.ColorOf(pal.Secondary);
        var stroke = UiChrome.ColorOf(pal.Stroke);
        var accent = UiChrome.ColorOf(pal.Accent);
        var button = _hover ? UiChrome.ColorOf(pal.ButtonHover) : UiChrome.ColorOf(pal.Button);
        var bounds = ClientRectangle;
        if (Parent is not FieldFrame)
        {
            var radius = UiLayout.ScalePx(FormTone.ButtonRadius, DeviceDpi);
            using var path = UiChrome.RoundRect(new RectangleF(0.5f, 0.5f, Math.Max(1f, Width - 1f), Math.Max(1f, Height - 1f)), radius);
            using (var br = new SolidBrush(field))
                g.FillPath(br, path);
            using (var pen = new Pen(stroke))
                g.DrawPath(pen, path);
        }
        else
        {
            using var br = new SolidBrush(field);
            g.FillRectangle(br, bounds);
        }

        var x = 8;
        if (ShowCheckBox)
        {
            var box = CheckBox();
            using var path = UiChrome.RoundRect(box, 3);
            using (var br = new SolidBrush(Checked ? accent : field))
                g.FillPath(br, path);
            using (var pen = new Pen(Checked ? accent : stroke))
                g.DrawPath(pen, path);
            if (Checked)
            {
                using var mark = new Pen(UiChrome.ColorOf(pal.OnAccent), 1.6f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };
                g.DrawLines(mark, new[]
                {
                    new Point(box.X + 3, box.Y + box.Height / 2),
                    new Point(box.X + box.Width / 2 - 1, box.Bottom - 4),
                    new Point(box.Right - 3, box.Y + 3),
                });
            }
            x = box.Right + 8;
        }

        var btn = ButtonBox();
        using (var br = new SolidBrush(button))
            g.FillRectangle(br, btn);
        using (var split = new Pen(stroke))
            g.DrawLine(split, btn.X, 4, btn.X, Height - 4);
        var cx = btn.X + btn.Width / 2;
        var cy = btn.Y + btn.Height / 2;
        using (var chevron = new Pen(Enabled ? text : secondary, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLines(chevron, new[] { new Point(cx - 5, cy - 1), new Point(cx, cy + 4), new Point(cx + 5, cy - 1) });

        var faded = !Enabled || (ShowCheckBox && !Checked);
        var label = ShowCheckBox && !Checked ? "不限" : _value.ToString(CustomFormat);
        var textRect = new Rectangle(x, 0, Math.Max(8, btn.X - x - 6), Height);
        TextRenderer.DrawText(
            g,
            label,
            Font,
            textRect,
            faded ? secondary : text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!Enabled) return;
        Focus();
        if (ShowCheckBox && CheckBox().Contains(e.Location))
        {
            Checked = !Checked;
            return;
        }
        if (ShowCheckBox && !Checked)
            Checked = true;
        ShowCalendar();
        base.OnMouseDown(e);
    }

    void ShowCalendar()
    {
        if (_popup is { IsDisposed: false })
        {
            _popup.Close();
            return;
        }
        var stamp = Clamp(_value);
        var view = new FlatMonthView
        {
            Selected = stamp.Date,
            MinDate = _min,
            MaxDate = _max,
            ShowTime = NeedsTime(),
            Hour = stamp.Hour,
            Minute = stamp.Minute,
        };
        var popup = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            MinimizeBox = false,
            MaximizeBox = false,
            ControlBox = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = Padding.Empty,
            Text = "",
            BackColor = UiChrome.ColorOf(UiChrome.Tone.Window),
        };
        view.DatePicked += date =>
        {
            Value = date;
            if (!popup.IsDisposed) popup.Close();
        };
        popup.Controls.Add(view);
        var armed = false;
        popup.Shown += (_, _) => armed = true;
        popup.Deactivate += (_, _) =>
        {
            if (!armed || popup.IsDisposed) return;
            popup.BeginInvoke(() => { if (!popup.IsDisposed) popup.Close(); });
        };
        popup.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_popup, popup)) _popup = null;
        };
        popup.Paint += (_, e) =>
        {
            using var pen = new Pen(UiChrome.ColorOf(UiChrome.Tone.Stroke));
            e.Graphics.DrawRectangle(pen, 0, 0, popup.ClientSize.Width - 1, popup.ClientSize.Height - 1);
        };
        var origin = PointToScreen(new Point(0, Height + 2));
        var work = Screen.FromControl(this).WorkingArea;
        popup.Location = origin;
        _popup = popup;
        var owner = FindForm();
        if (owner is not null) popup.Show(owner);
        else popup.Show();
        var size = popup.Size;
        var x = Math.Min(origin.X, work.Right - size.Width);
        var y = origin.Y + size.Height > work.Bottom ? origin.Y - Height - size.Height - 2 : origin.Y;
        popup.Location = new Point(Math.Max(work.Left, x), Math.Max(work.Top, y));
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_popup is { IsDisposed: false })
            _popup.Close();
        base.OnHandleDestroyed(e);
    }
}

sealed class FlatMonthView : Control
{
    static readonly string[] Weekdays = ["一", "二", "三", "四", "五", "六", "日"];

    readonly FlatSpin _hours = new() { Minimum = 0, Maximum = 23, DecimalPlaces = 0 };
    readonly FlatSpin _minutes = new() { Minimum = 0, Maximum = 59, DecimalPlaces = 0 };
    readonly FieldFrame _hourFrame;
    readonly FieldFrame _minFrame;
    readonly Label _hourLbl = new() { Text = "时", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    readonly Label _minLbl = new() { Text = "分", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    readonly Button _ok = UiChrome.Button("确定", UiButtonKind.Primary);
    DateTime _month;
    DateTime _selected;
    DateTime _min = new(2000, 1, 1);
    DateTime _max = new(2100, 1, 1);
    int _hover = -1;
    bool _showTime;

    public event Action<DateTime>? DatePicked;

    public FlatMonthView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        UpdateStyles();
        _selected = DateTime.Today;
        _month = new DateTime(_selected.Year, _selected.Month, 1);
        _hourFrame = UiChrome.Frame(_hours);
        _minFrame = UiChrome.Frame(_minutes);
        foreach (var frame in new[] { _hourFrame, _minFrame })
        {
            frame.Dock = DockStyle.None;
            frame.Margin = Padding.Empty;
        }
        _ok.Margin = Padding.Empty;
        _ok.Click += (_, _) => Pick(Stamp());
        Controls.AddRange([_hourFrame, _hourLbl, _minFrame, _minLbl, _ok]);
        foreach (var child in TimeParts())
            child.Visible = false;
    }

    Control[] TimeParts() => [_hourFrame, _hourLbl, _minFrame, _minLbl, _ok];

    public bool ShowTime
    {
        get => _showTime;
        set
        {
            _showTime = value;
            foreach (var child in TimeParts())
                child.Visible = value;
            Fit();
        }
    }

    public int Hour
    {
        get => (int)_hours.Value;
        set => _hours.Value = Math.Clamp(value, 0, 23);
    }

    public int Minute
    {
        get => (int)_minutes.Value;
        set => _minutes.Value = Math.Clamp(value, 0, 59);
    }

    public DateTime MinDate { get => _min; set { _min = value.Date; Invalidate(); } }
    public DateTime MaxDate { get => _max; set { _max = value.Date; Invalidate(); } }

    public DateTime Selected
    {
        get => _selected;
        set
        {
            _selected = value.Date;
            _month = new DateTime(_selected.Year, _selected.Month, 1);
            Invalidate();
        }
    }

    int Cell() => UiLayout.ScalePx(32, DeviceDpi > 0 ? DeviceDpi : 96);
    int Pad() => UiLayout.ScalePx(12, DeviceDpi > 0 ? DeviceDpi : 96);
    int HeaderH() => UiLayout.ScalePx(36, DeviceDpi > 0 ? DeviceDpi : 96);
    int WeekH() => UiLayout.ScalePx(24, DeviceDpi > 0 ? DeviceDpi : 96);
    int FootH() => UiLayout.ScalePx(36, DeviceDpi > 0 ? DeviceDpi : 96);
    int TimeH() => _showTime ? UiChrome.FieldPx(DeviceDpi > 0 ? DeviceDpi : 96) + Pad() / 2 : 0;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Fit();
    }

    DateTime Stamp() => _selected.Date.AddHours(Hour).AddMinutes(Minute);

    void Pick(DateTime value) => DatePicked?.Invoke(value);

    void Fit()
    {
        var dpi = DeviceDpi > 0 ? DeviceDpi : 96;
        StyleTime(dpi);
        var cell = Cell();
        var pad = Pad();
        Width = Math.Max(pad * 2 + cell * 7, TimeRowWidth(dpi));
        Height = pad + HeaderH() + WeekH() + cell * 6 + FootH() + TimeH();
        LayoutTime(dpi);
    }

    void StyleTime(int dpi)
    {
        var pal = UiChrome.Tone;
        var secondary = UiChrome.ColorOf(pal.Secondary);
        var window = UiChrome.ColorOf(pal.Window);
        var h = UiChrome.FieldPx(dpi);
        var w = UiLayout.ScalePx(FormTone.FieldWidthShort, dpi);
        foreach (var frame in new[] { _hourFrame, _minFrame })
        {
            frame.Size = new Size(w, h);
            frame.Relayout();
        }
        var labelW = UiLayout.ScalePx(20, dpi);
        foreach (var label in new[] { _hourLbl, _minLbl })
        {
            label.ForeColor = secondary;
            label.BackColor = window;
            label.Font = Font;
            label.Size = new Size(labelW, h);
        }
        UiChrome.SizeToText(_ok, dpi);
        if (_ok.Height != h)
            _ok.Height = h;
    }

    int TimeRowWidth(int dpi)
    {
        if (!_showTime) return 0;
        var pad = Pad();
        var gap = UiLayout.ScalePx(8, dpi);
        return pad + _hourFrame.Width + gap + _hourLbl.Width + gap
            + _minFrame.Width + gap + _minLbl.Width + gap + _ok.Width + pad;
    }

    void LayoutTime(int dpi)
    {
        if (!_showTime) return;
        var pad = Pad();
        var gap = UiLayout.ScalePx(8, dpi);
        var h = UiChrome.FieldPx(dpi);
        var y = Height - TimeH() + Math.Max(0, (TimeH() - h) / 2);
        var x = pad;
        _hourFrame.SetBounds(x, y, _hourFrame.Width, h);
        x = _hourFrame.Right + gap;
        _hourLbl.SetBounds(x, y, _hourLbl.Width, h);
        x = _hourLbl.Right + gap;
        _minFrame.SetBounds(x, y, _minFrame.Width, h);
        x = _minFrame.Right + gap;
        _minLbl.SetBounds(x, y, _minLbl.Width, h);
        _ok.SetBounds(Width - pad - _ok.Width, y + Math.Max(0, (h - _ok.Height) / 2), _ok.Width, _ok.Height);
    }

    Rectangle NavBox(bool next)
    {
        var pad = Pad();
        var size = UiLayout.ScalePx(24, DeviceDpi > 0 ? DeviceDpi : 96);
        var y = pad + (HeaderH() - size) / 2;
        return next
            ? new Rectangle(Width - pad - size, y, size, size)
            : new Rectangle(pad, y, size, size);
    }

    Rectangle DayBox(int index)
    {
        var cell = Cell();
        var pad = Pad();
        var top = pad + HeaderH() + WeekH();
        return new Rectangle(pad + index % 7 * cell, top + index / 7 * cell, cell, cell);
    }

    Rectangle TodayBox()
    {
        var pad = Pad();
        return new Rectangle(pad, Height - FootH() - TimeH(), Width - pad * 2, FootH() - pad / 2);
    }

    DateTime FirstGridDay()
    {
        var first = _month;
        var offset = ((int)first.DayOfWeek + 6) % 7;
        return first.AddDays(-offset);
    }

    DateTime DayAt(int index) => FirstGridDay().AddDays(index);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var pal = UiChrome.Tone;
        var window = UiChrome.ColorOf(pal.Window);
        var text = UiChrome.ColorOf(pal.Text);
        var secondary = UiChrome.ColorOf(pal.Secondary);
        var accent = UiChrome.ColorOf(pal.Accent);
        var onAccent = UiChrome.ColorOf(pal.OnAccent);
        var hover = UiChrome.ColorOf(pal.ButtonHover);
        using (var bg = new SolidBrush(window))
            g.FillRectangle(bg, ClientRectangle);

        var pad = Pad();
        var title = $"{_month.Year}年{_month.Month}月";
        TextRenderer.DrawText(g, title, UiChrome.UiFont(10f, FontStyle.Bold), new Rectangle(0, pad, Width, HeaderH()), text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        DrawArrow(g, NavBox(false), text, left: true);
        DrawArrow(g, NavBox(true), text, left: false);

        var weekTop = pad + HeaderH();
        var cell = Cell();
        for (var i = 0; i < 7; i++)
        {
            var box = new Rectangle(pad + i * cell, weekTop, cell, WeekH());
            TextRenderer.DrawText(g, Weekdays[i], Font, box, secondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        var today = DateTime.Today;
        for (var i = 0; i < 42; i++)
        {
            var day = DayAt(i);
            var box = DayBox(i);
            var muted = day.Month != _month.Month;
            var disabled = day < _min || day > _max;
            var on = day == _selected;
            var isToday = day == today;
            if (i == _hover && !disabled)
            {
                using var br = new SolidBrush(hover);
                g.FillEllipse(br, Inset(box, 4));
            }
            if (on)
            {
                using var br = new SolidBrush(accent);
                g.FillEllipse(br, Inset(box, 4));
            }
            else if (isToday)
            {
                using var pen = new Pen(accent);
                g.DrawEllipse(pen, Inset(box, 4));
            }
            var color = disabled ? UiChrome.ColorOf(pal.Hairline) : on ? onAccent : muted ? secondary : text;
            TextRenderer.DrawText(g, day.Day.ToString(), Font, box, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        var foot = TodayBox();
        var todayOn = _hover == 42;
        TextRenderer.DrawText(g, "今天  " + today.ToString("M月d日"), UiChrome.UiFont(9f), foot, todayOn ? accent : secondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }

    static Rectangle Inset(Rectangle box, int pad) =>
        new(box.X + pad, box.Y + pad, Math.Max(4, box.Width - pad * 2), Math.Max(4, box.Height - pad * 2));

    static void DrawArrow(Graphics g, Rectangle box, Color color, bool left)
    {
        var cx = box.X + box.Width / 2;
        var cy = box.Y + box.Height / 2;
        using var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (left)
            g.DrawLines(pen, new[] { new Point(cx + 3, cy - 5), new Point(cx - 3, cy), new Point(cx + 3, cy + 5) });
        else
            g.DrawLines(pen, new[] { new Point(cx - 3, cy - 5), new Point(cx + 3, cy), new Point(cx - 3, cy + 5) });
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var next = Hit(e.Location);
        if (next == _hover) return;
        _hover = next;
        Invalidate();
        Cursor = next >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Cursor = Cursors.Default;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (NavBox(false).Contains(e.Location))
        {
            Shift(-1);
            return;
        }
        if (NavBox(true).Contains(e.Location))
        {
            Shift(1);
            return;
        }
        if (TodayBox().Contains(e.Location))
        {
            var today = DateTime.Today;
            if (today < _min || today > _max) return;
            if (_showTime)
            {
                Selected = today;
                return;
            }
            Pick(today);
            return;
        }
        var hit = Hit(e.Location);
        if (hit is >= 0 and < 42)
        {
            var day = DayAt(hit);
            if (day < _min || day > _max) return;
            if (_showTime)
            {
                Selected = day;
                return;
            }
            Pick(day);
        }
    }

    void Shift(int months)
    {
        var next = _month.AddMonths(months);
        if (next > _max || next.AddMonths(1).AddDays(-1) < _min) return;
        _month = new DateTime(next.Year, next.Month, 1);
        Invalidate();
    }

    int Hit(Point pt)
    {
        if (TodayBox().Contains(pt)) return 42;
        for (var i = 0; i < 42; i++)
            if (DayBox(i).Contains(pt)) return i;
        return -1;
    }
}

sealed class HintBlock : TableLayoutPanel
{
    readonly Label _summary;
    readonly Label _detail;
    readonly LinkLabel _toggle;
    bool _open;

    public HintBlock(string summary, string detail)
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 1;
        Dock = DockStyle.Top;
        Margin = new Padding(0, 0, 0, 8);
        _summary = new Label
        {
            Text = summary,
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 2, 8, 0),
        };
        _detail = new Label
        {
            Text = detail,
            AutoSize = true,
            ForeColor = Color.DimGray,
            Visible = false,
            Margin = new Padding(0, 0, 0, 2),
        };
        _toggle = new LinkLabel
        {
            Text = "了解更多",
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 0),
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        _toggle.LinkClicked += (_, _) =>
        {
            _open = !_open;
            _detail.Visible = _open;
            _toggle.Text = _open ? "收起" : "了解更多";
        };
        var line = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        line.Controls.Add(_summary);
        line.Controls.Add(_toggle);
        Controls.Add(line);
        Controls.Add(_detail);
    }

    public void SetInnerWidth(int width)
    {
        var inner = Math.Max(160, width);
        _summary.MaximumSize = new Size(inner, 0);
        _detail.MaximumSize = new Size(inner, 0);
    }
}

sealed class KpiStrip : FlowLayoutPanel
{
    public float ValuePt { get; set; } = 12f;
    public int ItemGap { get; set; } = 28;

    public KpiStrip()
    {
        AutoSize = true;
        WrapContents = true;
        Margin = new Padding(0, 4, 0, 0);
    }

    public void Bind(params (string Label, string Value)[] items)
    {
        var wanted = items.Where(i => !string.IsNullOrWhiteSpace(i.Value)).ToArray();
        while (Controls.Count > wanted.Length)
            Controls.RemoveAt(Controls.Count - 1);
        for (var i = 0; i < wanted.Length; i++)
        {
            if (i < Controls.Count && Controls[i] is TableLayoutPanel box && box.Controls.Count >= 2)
            {
                box.Controls[0].Text = wanted[i].Label;
                box.Controls[1].Text = wanted[i].Value;
                box.Controls[1].Font = UiChrome.UiFont(ValuePt, FontStyle.Bold);
                continue;
            }
            var fresh = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 1,
                Margin = new Padding(0, 0, ItemGap, 8),
            };
            fresh.Controls.Add(new Label
            {
                Text = wanted[i].Label,
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(0),
            });
            fresh.Controls.Add(new Label
            {
                Text = wanted[i].Value,
                AutoSize = true,
                Font = UiChrome.UiFont(ValuePt, FontStyle.Bold),
                Margin = new Padding(0, 1, 0, 0),
            });
            Controls.Add(fresh);
        }
    }

    int _spreadSlot;

    public void Compact()
    {
        if (_spreadSlot == 0 && Controls.Count > 0 && Controls[0].MinimumSize.IsEmpty) return;
        _spreadSlot = 0;
        foreach (Control box in Controls)
            box.MinimumSize = Size.Empty;
    }

    public void SpreadTo(int width)
    {
        if (Controls.Count == 0 || width < 200) return;
        var slot = Math.Min(200, Math.Max(96, width / Math.Max(4, Controls.Count)));
        if (slot == _spreadSlot) return;
        _spreadSlot = slot;
        foreach (Control box in Controls)
        {
            box.Margin = new Padding(0, 0, ItemGap, 8);
            box.MinimumSize = new Size(slot, 0);
        }
    }
}

/// <summary>
/// Covers the stock WinForms scrollbar, which stays light even after
/// DarkMode_Explorer, and paints a muted rail that matches the window.
/// </summary>
sealed class DarkScrollCover : Control
{
    readonly DataGridView _grid;
    readonly bool _vertical;
    bool _drag;

    DarkScrollCover(DataGridView grid, bool vertical)
    {
        _grid = grid;
        _vertical = vertical;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }

    public static void Attach(DataGridView grid)
    {
        if (grid.Controls.OfType<DarkScrollCover>().Any()) return;
        var v = new DarkScrollCover(grid, true);
        var h = new DarkScrollCover(grid, false);
        grid.Controls.Add(v);
        grid.Controls.Add(h);
        void Sync(object? _, EventArgs e)
        {
            v.Sync();
            h.Sync();
        }
        grid.RowsAdded += Sync;
        grid.RowsRemoved += Sync;
        grid.Resize += Sync;
        grid.Scroll += Sync;
        grid.Layout += Sync;
        grid.ClientSizeChanged += Sync;
        Sync(null, EventArgs.Empty);
    }

    public void Sync()
    {
        var bar = Bar();
        if (bar is null || !bar.Visible)
        {
            Visible = false;
            return;
        }
        Bounds = bar.Bounds;
        Visible = true;
        BringToFront();
        Invalidate();
    }

    ScrollBar? Bar()
    {
        foreach (Control child in _grid.Controls)
        {
            if (_vertical && child is VScrollBar vs) return vs;
            if (!_vertical && child is HScrollBar hs) return hs;
        }
        return null;
    }

    Rectangle ThumbRect()
    {
        var bar = Bar();
        if (bar is null || Width <= 0 || Height <= 0) return Rectangle.Empty;
        var span = Math.Max(1, bar.Maximum - bar.Minimum + 1);
        var large = Math.Max(1, bar.LargeChange);
        if (_vertical)
        {
            var thumbH = Math.Max(UiLayout.ScalePx(24, DeviceDpi), (int)(Height * (large / (float)span)));
            var travel = Math.Max(0, Height - thumbH);
            var maxVal = Math.Max(bar.Minimum, bar.Maximum - large + 1);
            var t = maxVal <= bar.Minimum ? 0 : (bar.Value - bar.Minimum) / (float)(maxVal - bar.Minimum);
            return new Rectangle(2, (int)(t * travel), Math.Max(4, Width - 4), thumbH);
        }
        var thumbW = Math.Max(UiLayout.ScalePx(24, DeviceDpi), (int)(Width * (large / (float)span)));
        var travelX = Math.Max(0, Width - thumbW);
        var maxX = Math.Max(bar.Minimum, bar.Maximum - large + 1);
        var tx = maxX <= bar.Minimum ? 0 : (bar.Value - bar.Minimum) / (float)(maxX - bar.Minimum);
        return new Rectangle((int)(tx * travelX), 2, thumbW, Math.Max(4, Height - 4));
    }

    void ScrollTo(int pos)
    {
        var bar = Bar();
        if (bar is null) return;
        var large = Math.Max(1, bar.LargeChange);
        var maxVal = Math.Max(bar.Minimum, bar.Maximum - large + 1);
        var travel = _vertical ? Math.Max(1, Height - ThumbRect().Height) : Math.Max(1, Width - ThumbRect().Width);
        var value = bar.Minimum + (int)Math.Round((pos / (float)travel) * (maxVal - bar.Minimum));
        bar.Value = Math.Clamp(value, bar.Minimum, maxVal);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var pal = UiChrome.Tone;
        e.Graphics.Clear(UiChrome.ColorOf(pal.Window));
        var thumb = ThumbRect();
        if (thumb.Width <= 0 || thumb.Height <= 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var br = new SolidBrush(UiChrome.ColorOf(_drag ? pal.ButtonHover : pal.Stroke));
        using var path = UiChrome.RoundRect(thumb, UiLayout.ScalePx(3, DeviceDpi));
        e.Graphics.FillPath(br, path);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _drag = true;
        Capture = true;
        ScrollTo(_vertical ? e.Y : e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag) ScrollTo(_vertical ? e.Y : e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _drag = false;
        Capture = false;
        Invalidate();
    }
}
