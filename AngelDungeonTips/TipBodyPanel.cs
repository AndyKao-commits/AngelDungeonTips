using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace AngelDungeonTips;

/// <summary>
/// Tip title + body. TextOpacity only affects glyphs (with outline for readability);
/// panel fill stays solid so background opacity can be controlled by the form separately.
/// </summary>
public sealed class TipBodyPanel : Panel
{
    private string title = "";
    private string body = "";
    private int textOpacityPercent = 100;
    private int scrollY;
    private int contentHeight;

    public TipBodyPanel()
    {
        DoubleBuffered = true;
        // Solid panel fill — form Opacity controls how much game shows through the whole window.
        BackColor = Color.FromArgb(34, 38, 48);
        TabStop = true;
        Resize += (_, _) => Invalidate();
    }

    public void SetContent(string titleText, string bodyText)
    {
        title = titleText ?? "";
        body = bodyText ?? "";
        scrollY = 0;
        Invalidate();
    }

    public int TextOpacityPercent
    {
        get => textOpacityPercent;
        set
        {
            textOpacityPercent = Math.Clamp(value, 15, 100);
            Invalidate();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int max = Math.Max(0, contentHeight - Height + 8);
        scrollY = Math.Clamp(scrollY - Math.Sign(e.Delta) * 32, 0, max);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Clear fill (independent of text alpha)
        using (var bg = new SolidBrush(BackColor))
            g.FillRectangle(bg, ClientRectangle);

        int alpha = (int)(255 * (textOpacityPercent / 100.0));
        var titleColor = Color.FromArgb(alpha, 255, 210, 120);
        var bodyColor = Color.FromArgb(alpha, 250, 250, 250);
        // Outline stays stronger so text remains readable when text alpha is mid-range
        int outlineA = Math.Min(255, alpha + 40);
        var outlineColor = Color.FromArgb(outlineA, 0, 0, 0);

        using var titleFont = new Font("Microsoft JhengHei UI", 11f, FontStyle.Bold);
        using var bodyFont = new Font("Microsoft JhengHei UI", 10.5f);

        float x = 6;
        float y = 6 - scrollY;
        float wrap = Math.Max(40, Width - 16);

        var titleSize = g.MeasureString(title, titleFont, (int)wrap);
        DrawOutlinedString(g, title, titleFont, titleColor, outlineColor, new RectangleF(x, y, wrap, titleSize.Height + 2));
        y += titleSize.Height + 10;

        var bodySize = g.MeasureString(body, bodyFont, (int)wrap);
        DrawOutlinedString(g, body, bodyFont, bodyColor, outlineColor, new RectangleF(x, y, wrap, bodySize.Height + 4));
        y += bodySize.Height + 8;

        contentHeight = (int)Math.Ceiling(y + scrollY);
    }

    private static void DrawOutlinedString(Graphics g, string text, Font font, Color fill, Color outline, RectangleF layout)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var path = new GraphicsPath();
        // EmSize in GraphicsUnit.Pixel for path
        float em = font.SizeInPoints * g.DpiY / 72f;
        path.AddString(text, font.FontFamily, (int)font.Style, em, layout,
            new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near });
        using (var pen = new Pen(outline, 3f) { LineJoin = LineJoin.Round })
            g.DrawPath(pen, path);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
    }
}
