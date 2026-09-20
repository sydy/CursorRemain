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
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 8, 4),
            Padding = new Padding(FormTone.ButtonPadX, 3, FormTone.ButtonPadX, 3),
            MinimumSize = new Size(0, FormTone.ButtonMinHeight),
            Tag = kind,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        StyleButton(btn, kind);
        return btn;
    }

    public static Label Heading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = UiFont(11f, FontStyle.Bold),
        Margin = new Padding(0, 12, 0, 6),
    };

    public static Button MenuButton(string text, params (string Title, EventHandler Handler)[] items)
    {
        var btn = Button(text);
        var menu = new ContextMenuStrip();
        foreach (var item in items)
            menu.Items.Add(item.Title, null, item.Handler);
        btn.Click += (_, _) => menu.Show(btn, new Point(0, btn.Height));
        return btn;
    }

    public static HintBlock Hint(string summary, string detail) => new(summary, detail);

    public static void Apply(Form form)
    {
        var pal = Tone;
        form.Font = UiFont();
        form.BackColor = ColorOf(pal.Window);
        form.ForeColor = ColorOf(pal.Text);
        ApplyTree(form, pal, form.DeviceDpi);
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
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = ColorOf(pal.Window),
            ForeColor = ColorOf(pal.Text),
            SelectionBackColor = ColorOf(pal.Selection),
            SelectionForeColor = ColorOf(pal.Text),
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = ColorOf(pal.Field),
            ForeColor = ColorOf(pal.Text),
            SelectionBackColor = ColorOf(pal.Selection),
            SelectionForeColor = ColorOf(pal.Text),
        };
        grid.RowHeadersVisible = false;
    }

    public static Color HeaderFill() => ColorOf(Tone.Header);
    public static Color SecondaryText() => ColorOf(Tone.Secondary);

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
                break;
            case ComboBox combo:
                combo.FlatStyle = FlatStyle.Flat;
                combo.BackColor = field;
                combo.ForeColor = text;
                combo.IntegralHeight = false;
                combo.Height = Math.Max(combo.Height, h);
                break;
            case NumericUpDown spin:
                spin.BorderStyle = BorderStyle.FixedSingle;
                spin.BackColor = field;
                spin.ForeColor = text;
                spin.MinimumSize = new Size(spin.MinimumSize.Width, h);
                break;
            case DateTimePicker picker:
                picker.CalendarForeColor = text;
                picker.CalendarMonthBackground = field;
                picker.CalendarTitleBackColor = ColorOf(pal.Header);
                picker.CalendarTitleForeColor = text;
                picker.CalendarTrailingForeColor = ColorOf(pal.Secondary);
                picker.MinimumSize = new Size(picker.MinimumSize.Width, h);
                break;
        }
    }

    static void StyleTabs(TabControl tabs, FormTone.Palette pal)
    {
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.Padding = new Point(14, 5);
        tabs.BackColor = ColorOf(pal.Window);
        foreach (TabPage page in tabs.TabPages)
        {
            page.UseVisualStyleBackColor = false;
            page.BackColor = ColorOf(pal.Window);
            page.ForeColor = ColorOf(pal.Text);
        }
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
                return;
            case DataGridView grid:
                StyleGrid(grid);
                return;
            case UsageChartBox plot:
                plot.BackColor = window;
                return;
            case TabControl tabs:
                StyleTabs(tabs, pal);
                break;
            case TextBox or ComboBox or NumericUpDown or DateTimePicker:
                StyleInput(parent, pal, dpi);
                return;
            case CheckBox box:
                box.ForeColor = text;
                box.BackColor = window;
                box.FlatStyle = FlatStyle.Standard;
                return;
            case LinkLabel:
                return;
            case Label label:
                if (label.ForeColor == Color.DimGray || label.ForeColor.ToArgb() == secondary.ToArgb())
                    label.ForeColor = secondary;
                else if (label.ForeColor == SystemColors.ControlText || label.ForeColor == Color.Black)
                    label.ForeColor = text;
                label.BackColor = Color.Transparent;
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

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);
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
        Dock = DockStyle.Fill;
        Margin = new Padding(0, 0, 0, 8);
        _summary = new Label
        {
            Text = summary,
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 2),
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
            Margin = new Padding(0, 0, 0, 0),
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        _toggle.LinkClicked += (_, _) =>
        {
            _open = !_open;
            _detail.Visible = _open;
            _toggle.Text = _open ? "收起" : "了解更多";
        };
        Controls.Add(_summary);
        Controls.Add(_detail);
        Controls.Add(_toggle);
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
    public KpiStrip()
    {
        AutoSize = true;
        WrapContents = true;
        Margin = new Padding(0, 4, 0, 0);
    }

    public void Bind(params (string Label, string Value)[] items)
    {
        SuspendLayout();
        Controls.Clear();
        foreach (var (label, value) in items)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var box = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 1,
                Margin = new Padding(0, 0, 20, 8),
            };
            box.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(0),
            });
            box.Controls.Add(new Label
            {
                Text = value,
                AutoSize = true,
                Font = UiChrome.UiFont(11f, FontStyle.Bold),
                Margin = new Padding(0, 2, 0, 0),
            });
            Controls.Add(box);
        }
        ResumeLayout();
    }
}
