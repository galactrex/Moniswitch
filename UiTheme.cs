using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Moniswitch;

internal enum ButtonTone
{
    Neutral,
    Accent,
    Ghost,
    Danger
}

internal static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(15, 16, 14);
    public static readonly Color Surface = Color.FromArgb(25, 27, 23);
    public static readonly Color SurfaceRaised = Color.FromArgb(35, 38, 32);
    public static readonly Color SurfaceHover = Color.FromArgb(43, 46, 39);
    public static readonly Color Border = Color.FromArgb(57, 61, 52);
    public static readonly Color BorderStrong = Color.FromArgb(79, 84, 72);
    public static readonly Color Text = Color.FromArgb(240, 240, 233);
    public static readonly Color Muted = Color.FromArgb(157, 162, 147);
    public static readonly Color Faint = Color.FromArgb(103, 108, 97);
    public static readonly Color Accent = Color.FromArgb(255, 111, 67);
    public static readonly Color AccentHover = Color.FromArgb(255, 130, 91);
    public static readonly Color AccentPressed = Color.FromArgb(224, 83, 43);
    public static readonly Color Success = Color.FromArgb(106, 207, 151);
    public static readonly Color Danger = Color.FromArgb(239, 105, 105);

    public static Font Font(float size = 9.5f, FontStyle style = FontStyle.Regular) =>
        CreateFont("Segoe UI Variable Text", "Segoe UI", size, style);

    public static Font DisplayFont(float size = 10, FontStyle style = FontStyle.Regular) =>
        CreateFont("Bahnschrift SemiCondensed", "Bahnschrift", size, style);

    public static Font MonoFont(float size = 9, FontStyle style = FontStyle.Regular) =>
        CreateFont("Cascadia Mono", "Consolas", size, style);

    public static Button Button(string text, ButtonTone tone = ButtonTone.Neutral)
    {
        var (background, hover, pressed, border, foreground) = tone switch
        {
            ButtonTone.Accent => (Accent, AccentHover, AccentPressed, Accent, Background),
            ButtonTone.Ghost => (Surface, SurfaceHover, SurfaceRaised, Border, Text),
            ButtonTone.Danger => (Surface, Color.FromArgb(59, 35, 32), Color.FromArgb(45, 27, 25), Danger, Danger),
            _ => (SurfaceRaised, SurfaceHover, Surface, BorderStrong, Text)
        };

        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Height = 40,
            FlatStyle = FlatStyle.Flat,
            BackColor = background,
            ForeColor = foreground,
            Font = DisplayFont(9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Padding = new Padding(12, 0, 12, 0),
            UseVisualStyleBackColor = false,
            TabStop = true
        };
        button.FlatAppearance.BorderColor = border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = hover;
        button.FlatAppearance.MouseDownBackColor = pressed;
        return button;
    }

    public static RouteComboBox ComboBox() => new();

    public static Label Label(
        string text,
        float size = 9.5f,
        bool bold = false,
        Color? color = null,
        bool display = false)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = display
                ? DisplayFont(size, bold ? FontStyle.Bold : FontStyle.Regular)
                : Font(size, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = color ?? Text,
            BackColor = Color.Transparent,
            UseMnemonic = false
        };
    }

    public static Label ControlLabel(string text)
    {
        var label = Label(text.ToUpperInvariant(), 8, bold: true, color: Muted, display: true);
        label.Padding = new Padding(0, 1, 0, 0);
        return label;
    }

    public static Label SignalLabel(string text, Color? color = null)
    {
        var label = Label(text, 8.5f, color: color ?? Muted);
        label.Font = MonoFont(8.5f, FontStyle.Bold);
        return label;
    }

    public static SurfacePanel SurfacePanel(Color? background = null) => new()
    {
        BackColor = background ?? Surface,
        BorderColor = Border
    };

    public static void StyleMenu(ContextMenuStrip menu)
    {
        menu.BackColor = SurfaceRaised;
        menu.ForeColor = Text;
        menu.Font = Font(9.25f);
        menu.ShowImageMargin = false;
        menu.RenderMode = ToolStripRenderMode.System;
        menu.Padding = new Padding(2);
    }

    public static void UseDarkTitleBar(Form form)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            return;
        }

        var enabled = 1;
        _ = DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
        _ = DwmSetWindowAttribute(form.Handle, 19, ref enabled, sizeof(int));
    }

    private static Font CreateFont(
        string preferred,
        string fallback,
        float size,
        FontStyle style)
    {
        try
        {
            return new Font(preferred, size, style, GraphicsUnit.Point);
        }
        catch
        {
            return new Font(fallback, size, style, GraphicsUnit.Point);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int valueSize);
}

internal sealed class SurfacePanel : Panel
{
    public SurfacePanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Margin = Padding.Empty;
    }

    public Color BorderColor { get; set; } = UiTheme.Border;

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        using var pen = new Pen(BorderColor);
        eventArgs.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
    }
}

internal sealed class RouteComboBox : ComboBox
{
    public RouteComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        BackColor = UiTheme.SurfaceRaised;
        ForeColor = UiTheme.Text;
        Font = UiTheme.Font(9.25f);
        Height = 38;
        ItemHeight = 30;
        IntegralHeight = false;
        DropDownHeight = 244;
        Cursor = Cursors.Hand;
    }

    protected override void OnDrawItem(DrawItemEventArgs eventArgs)
    {
        if (eventArgs.Index < 0)
        {
            return;
        }

        var selected = (eventArgs.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected ? UiTheme.Accent : UiTheme.SurfaceRaised);
        using var foreground = new SolidBrush(selected ? UiTheme.Background : UiTheme.Text);
        eventArgs.Graphics.FillRectangle(background, eventArgs.Bounds);
        eventArgs.Graphics.DrawString(
            GetItemText(Items[eventArgs.Index]),
            Font,
            foreground,
            new RectangleF(eventArgs.Bounds.X + 9, eventArgs.Bounds.Y + 6, eventArgs.Bounds.Width - 14, eventArgs.Bounds.Height - 8));
        eventArgs.DrawFocusRectangle();
    }
}

internal sealed class BrandMark : Control
{
    public BrandMark()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        Size = new Size(42, 42);
        MinimumSize = new Size(24, 24);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var scale = Math.Min(Width, Height) / 64f;
        graphics.TranslateTransform((Width - 64 * scale) / 2f, (Height - 64 * scale) / 2f);
        graphics.ScaleTransform(scale, scale);

        using var framePen = new Pen(UiTheme.Text, 3.5f)
        {
            LineJoin = LineJoin.Round
        };
        using var signalPen = new Pen(UiTheme.Accent, 4.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        graphics.DrawRectangle(framePen, 6, 10, 14, 38);
        graphics.DrawRectangle(framePen, 25, 10, 14, 38);
        graphics.DrawRectangle(framePen, 44, 10, 14, 38);
        graphics.DrawLines(signalPen,
        [
            new PointF(11, 29),
            new PointF(24, 29),
            new PointF(31, 21),
            new PointF(39, 37),
            new PointF(47, 29),
            new PointF(55, 29)
        ]);
        graphics.DrawLines(signalPen,
        [
            new PointF(50, 23),
            new PointF(56, 29),
            new PointF(50, 35)
        ]);
        graphics.ResetTransform();
    }
}
