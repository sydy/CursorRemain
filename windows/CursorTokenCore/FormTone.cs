namespace CursorTokenCore;

/// <summary>
/// Shared window chrome tokens. Flyout solids and WinForms skins both read this
/// so settings / report / compare match the tray card.
/// </summary>
public static class FormTone
{
    public const int FieldHeight = 28;
    public const int ButtonMinHeight = 28;
    public const int ButtonMinWidth = 72;
    public const int LabelColumn = 160;
    public const int ButtonRadius = 6;
    public const int ButtonPadX = 12;
    public const int TabItemWidth = 84;
    public const int TabItemHeight = 34;

    public readonly record struct Rgb(int R, int G, int B);

    public readonly record struct Palette(
        Rgb Window,
        Rgb Field,
        Rgb Text,
        Rgb Secondary,
        Rgb Accent,
        Rgb AccentHover,
        Rgb OnAccent,
        Rgb Stroke,
        Rgb Hairline,
        Rgb Header,
        Rgb Selection,
        Rgb Danger,
        Rgb DangerFill,
        Rgb Button,
        Rgb ButtonHover);

    public static Palette Light { get; } = new(
        Window: new(246, 246, 248),
        Field: new(255, 255, 255),
        Text: new(28, 28, 30),
        Secondary: new(110, 110, 115),
        Accent: new(0, 122, 255),
        AccentHover: new(10, 110, 230),
        OnAccent: new(255, 255, 255),
        Stroke: new(210, 210, 214),
        Hairline: new(228, 228, 232),
        Header: new(238, 238, 240),
        Selection: new(220, 236, 255),
        Danger: new(192, 57, 43),
        DangerFill: new(253, 236, 234),
        Button: new(236, 236, 238),
        ButtonHover: new(226, 226, 228));

    public static Palette Dark { get; } = new(
        Window: new(36, 36, 38),
        Field: new(56, 56, 60),
        Text: new(245, 245, 247),
        Secondary: new(152, 152, 157),
        Accent: new(10, 132, 255),
        AccentHover: new(30, 144, 255),
        OnAccent: new(255, 255, 255),
        Stroke: new(72, 72, 76),
        Hairline: new(58, 58, 62),
        Header: new(48, 48, 50),
        Selection: new(20, 60, 100),
        Danger: new(255, 138, 128),
        DangerFill: new(72, 36, 34),
        Button: new(54, 54, 58),
        ButtonHover: new(66, 66, 70));

    public static Palette For(bool light) => light ? Light : Dark;
}
