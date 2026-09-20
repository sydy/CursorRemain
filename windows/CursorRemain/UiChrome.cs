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
        var btn = new Button
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

    public static FieldFrame Frame(Control inner, int height = 0)
    {
        if (inner is TextBox box)
            box.BorderStyle = BorderStyle.None;
        inner.Dock = DockStyle.Fill;
        inner.Margin = Padding.Empty;
        var panel = new FieldFrame
        {
            BackColor = ColorOf(Tone.Stroke),
            Padding = new Padding(1),
            Height = height > 0 ? height : Math.Max(inner.Height, FormTone.FieldHeight) + 2,
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
            NativeTheme.DarkExplorer(box.Handle);
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
        btn.FlatAppearance.BorderSize = 1;
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
                btn.BackColor = ColorOf(pal.DangerFill);
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
        AttachRound(btn, FormTone.ButtonRadius);
    }

    static void StyleInput(Control control, FormTone.Palette pal, int dpi)
    {
        var field = ColorOf(pal.Field);
        var text = ColorOf(pal.Text);
        var h = UiLayout.ScalePx(FormTone.FieldHeight, dpi);
        switch (control)
        {
            case TextBox box:
                box.BorderStyle = BorderStyle.FixedSingle;
                box.BackColor = field;
                box.ForeColor = text;
                if (!box.Multiline)
                    box.MinimumSize = new Size(box.MinimumSize.Width, h);
                box.HandleCreated -= ThemeTextScroll;
                box.HandleCreated += ThemeTextScroll;
                if (box.IsHandleCreated) ThemeTextScroll(box, EventArgs.Empty);
                break;
            case ComboBox combo:
                combo.FlatStyle = FlatStyle.Flat;
                combo.BackColor = field;
                combo.ForeColor = text;
                combo.IntegralHeight = false;
                combo.Height = Math.Max(combo.Height, h);
                if (combo.IsHandleCreated) NativeTheme.Strip(combo.Handle);
                break;
            case NumericUpDown spin:
                spin.BorderStyle = BorderStyle.FixedSingle;
                spin.BackColor = field;
                spin.ForeColor = text;
                spin.MinimumSize = new Size(spin.MinimumSize.Width, h);
                spin.HandleCreated -= ThemeSpin;
                spin.HandleCreated += ThemeSpin;
                if (spin.IsHandleCreated) ThemeSpin(spin, EventArgs.Empty);
                break;
            case DateTimePicker picker:
                picker.CalendarForeColor = text;
                picker.CalendarMonthBackground = field;
                picker.CalendarTitleBackColor = ColorOf(pal.Header);
                picker.CalendarTitleForeColor = text;
                picker.CalendarTrailingForeColor = ColorOf(pal.Secondary);
                picker.MinimumSize = new Size(picker.MinimumSize.Width, h);
                if (picker.IsHandleCreated) NativeTheme.Strip(picker.Handle);
                break;
        }
    }

    static void StyleTabs(TabControl tabs, FormTone.Palette pal, int dpi)
    {
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.ItemSize = new Size(UiLayout.ScalePx(FormTone.TabItemWidth, dpi), UiLayout.ScalePx(FormTone.TabItemHeight, dpi));
        tabs.Padding = new Point(20, 8);
        tabs.BackColor = ColorOf(pal.Window);
        foreach (TabPage page in tabs.TabPages)
        {
            page.UseVisualStyleBackColor = false;
            page.BackColor = ColorOf(pal.Window);
            page.ForeColor = ColorOf(pal.Text);
        }
        if (tabs is FlatTabControl)
            return;
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
            case TextBox or ComboBox or NumericUpDown or DateTimePicker:
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
                frame.BackColor = ColorOf(pal.Stroke);
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

    static void AttachRound(Control control, int radius)
    {
        void ApplyRegion(object? sender, EventArgs e)
        {
            if (control.IsDisposed || control.Width <= 0 || control.Height <= 0) return;
            var d = Math.Max(2, UiLayout.ScalePx(radius, control.DeviceDpi) * 2);
            var hrgn = CreateRoundRectRgn(0, 0, control.Width + 1, control.Height + 1, d, d);
            if (hrgn == IntPtr.Zero) return;
            var region = Region.FromHrgn(hrgn);
            DeleteObject(hrgn);
            var old = control.Region;
            control.Region = region;
            old?.Dispose();
        }
        control.Resize -= ApplyRegion;
        control.Resize += ApplyRegion;
        if (control.IsHandleCreated) ApplyRegion(control, EventArgs.Empty);
        else control.HandleCreated += ApplyRegion;
    }

    internal static GraphicsPath RoundRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
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

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);
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

    public static void Strip(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        _ = SetWindowTheme(hwnd, "", "");
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
            Strip(child);
    }

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

sealed class FieldFrame : Panel;

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

sealed class FlatTabControl : TabControl
{
    int _hover = -1;

    public FlatTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(FormTone.TabItemWidth, FormTone.TabItemHeight);
        Padding = new Point(20, 8);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var idx = -1;
        for (var i = 0; i < TabCount; i++)
        {
            if (GetTabRect(i).Contains(e.Location)) { idx = i; break; }
        }
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

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(UiChrome.ColorOf(UiChrome.Tone.Window));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var pal = UiChrome.Tone;
        var window = UiChrome.ColorOf(pal.Window);
        var text = UiChrome.ColorOf(pal.Text);
        var accent = UiChrome.ColorOf(pal.Accent);
        e.Graphics.Clear(window);
        var headerH = TabCount > 0 ? GetTabRect(0).Bottom : 0;
        using (var line = new SolidBrush(UiChrome.ColorOf(pal.Hairline)))
            e.Graphics.FillRectangle(line, 0, headerH, Width, 1);
        for (var i = 0; i < TabCount; i++)
        {
            var bounds = GetTabRect(i);
            var selected = i == SelectedIndex;
            var chip = new Rectangle(bounds.X + 4, bounds.Y + 6, Math.Max(8, bounds.Width - 8), Math.Max(8, bounds.Height - 10));
            if (selected || i == _hover)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var chipPath = UiChrome.RoundRect(chip, 6);
                using var bg = new SolidBrush(UiChrome.ColorOf(selected ? pal.Button : pal.ButtonHover));
                e.Graphics.FillPath(bg, chipPath);
                if (selected)
                {
                    using var underline = new SolidBrush(accent);
                    e.Graphics.FillRectangle(underline, chip.Left + 10, chip.Bottom - 3, Math.Max(8, chip.Width - 20), 2);
                }
            }
            using var tabFont = selected ? new Font(Font, FontStyle.Bold) : null;
            TextRenderer.DrawText(
                e.Graphics,
                TabPages[i].Text,
                tabFont ?? Font,
                chip,
                selected ? text : UiChrome.ColorOf(pal.Secondary),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }
}

sealed class FlatCombo : ComboBox
{
    const int WmPaint = 0x000F;

    public FlatCombo()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        IntegralHeight = false;
        MaxDropDownItems = 12;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeTheme.Strip(Handle);
        BackColor = UiChrome.ColorOf(UiChrome.Tone.Field);
        ForeColor = UiChrome.ColorOf(UiChrome.Tone.Text);
        ItemHeight = Math.Max(16, UiLayout.ScalePx(FormTone.FieldHeight, DeviceDpi) - 8);
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
        base.WndProc(ref m);
        if (m.Msg == WmPaint) PaintChrome();
    }

    void PaintChrome()
    {
        if (!IsHandleCreated || Width <= 1 || Height <= 1) return;
        using var g = Graphics.FromHwnd(Handle);
        var pal = UiChrome.Tone;
        var field = UiChrome.ColorOf(pal.Field);
        var stroke = UiChrome.ColorOf(pal.Stroke);
        var text = UiChrome.ColorOf(pal.Secondary);
        using (var pen = new Pen(stroke))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
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

    public FlatSpin()
    {
        BorderStyle = BorderStyle.FixedSingle;
        DecimalPlaces = 0;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeTheme.Spin(Handle);
        BackColor = UiChrome.ColorOf(UiChrome.Tone.Field);
        ForeColor = UiChrome.ColorOf(UiChrome.Tone.Text);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
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
        using (var pen = new Pen(stroke))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        var spinW = Math.Max(16, UiLayout.ScalePx(16, DeviceDpi));
        var arrow = new Rectangle(Width - spinW - 1, 1, spinW, Height - 2);
        using (var br = new SolidBrush(field))
            g.FillRectangle(br, arrow);
        var up = new Rectangle(arrow.X, arrow.Y, arrow.Width, arrow.Height / 2);
        var down = new Rectangle(arrow.X, arrow.Y + arrow.Height / 2, arrow.Width, arrow.Height - arrow.Height / 2);
        DrawChevron(g, up, text, up: true);
        DrawChevron(g, down, text, up: false);
    }

    static void DrawChevron(Graphics g, Rectangle area, Color color, bool up)
    {
        var cx = area.X + area.Width / 2;
        var cy = area.Y + area.Height / 2;
        using var pen = new Pen(color, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (up)
            g.DrawLines(pen, new[] { new Point(cx - 3, cy + 1), new Point(cx, cy - 2), new Point(cx + 3, cy + 1) });
        else
            g.DrawLines(pen, new[] { new Point(cx - 3, cy - 1), new Point(cx, cy + 2), new Point(cx + 3, cy - 1) });
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

sealed class FlatDatePicker : DateTimePicker
{
    const int WmPaint = 0x000F;

    public FlatDatePicker()
    {
        Format = DateTimePickerFormat.Custom;
        CustomFormat = "yyyy-MM-dd";
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeTheme.Strip(Handle);
        var pal = UiChrome.Tone;
        CalendarForeColor = UiChrome.ColorOf(pal.Text);
        CalendarMonthBackground = UiChrome.ColorOf(pal.Field);
        CalendarTitleBackColor = UiChrome.ColorOf(pal.Header);
        CalendarTitleForeColor = UiChrome.ColorOf(pal.Text);
        CalendarTrailingForeColor = UiChrome.ColorOf(pal.Secondary);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WmPaint) PaintChrome();
    }

    void PaintChrome()
    {
        if (!IsHandleCreated || Width <= 1 || Height <= 1) return;
        using var g = Graphics.FromHwnd(Handle);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var pal = UiChrome.Tone;
        var field = UiChrome.ColorOf(pal.Field);
        var text = UiChrome.ColorOf(pal.Text);
        var secondary = UiChrome.ColorOf(pal.Secondary);
        var stroke = UiChrome.ColorOf(pal.Stroke);
        var accent = UiChrome.ColorOf(pal.Accent);
        var bounds = ClientRectangle;
        using (var br = new SolidBrush(field))
            g.FillRectangle(br, bounds);
        using (var pen = new Pen(stroke))
            g.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
        var x = 8;
        if (ShowCheckBox)
        {
            var box = new Rectangle(6, Math.Max(2, (bounds.Height - 14) / 2), 14, 14);
            using (var pen = new Pen(Checked ? accent : stroke))
                g.DrawRectangle(pen, box);
            if (Checked)
            {
                using var fill = new SolidBrush(accent);
                g.FillRectangle(fill, box.X + 3, box.Y + 3, 8, 8);
            }
            x = box.Right + 8;
        }
        var faded = !Enabled || (ShowCheckBox && !Checked);
        var textRect = new Rectangle(x, 0, Math.Max(8, bounds.Width - x - 22), bounds.Height);
        TextRenderer.DrawText(
            g,
            Text,
            Font,
            textRect,
            faded ? secondary : text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var cx = bounds.Width - 12;
        var cy = bounds.Height / 2;
        using var chevron = new Pen(secondary, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLines(chevron, new[] { new Point(cx - 4, cy - 1), new Point(cx, cy + 3), new Point(cx + 4, cy - 1) });
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
