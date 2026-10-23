using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EnterpriseRemote.Controller.UI.Theme;
using FontAwesome.Sharp;

namespace EnterpriseRemote.Controller.UI.Components;

public class CyberComboBox : ComboBox
{
    private int _borderRadius = ThemeTokens.RadiusMedium;
    private bool _isHovered = false;

    public int BorderRadius
    {
        get => _borderRadius;
        set { _borderRadius = value; Invalidate(); }
    }

    public CyberComboBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);

        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        ItemHeight = 28;
        FlatStyle = FlatStyle.Flat;
        BackColor = ThemeTokens.BackgroundInput;
        ForeColor = ThemeTokens.TextPrimary;
        Font = ThemeTokens.FontBody;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _isHovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _isHovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;

        var g = e.Graphics;
        ThemeTokens.EnableHighQualityGraphics(g);

        bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        Color bg = isSelected ? Color.FromArgb(40, 52, 80) : ThemeTokens.BackgroundCard;
        Color textClr = isSelected ? ThemeTokens.TextPrimary : ThemeTokens.TextSecondary;

        using (var brush = new SolidBrush(bg))
        {
            g.FillRectangle(brush, e.Bounds);
        }

        string itemText = Items[e.Index]?.ToString() ?? "";

        // Check if item contains status indicators or tags
        Color dotColor = ThemeTokens.AccentSuccess;
        if (itemText.Contains("No enrolled", StringComparison.OrdinalIgnoreCase))
            dotColor = ThemeTokens.AccentWarning;
        else if (itemText.Contains("[This PC", StringComparison.OrdinalIgnoreCase))
            dotColor = ThemeTokens.AccentCyan;
        else if (itemText.Contains("Offline", StringComparison.OrdinalIgnoreCase))
            dotColor = ThemeTokens.AccentDanger;

        // Draw dot
        int dotSize = 8;
        int dotX = e.Bounds.X + 10;
        int dotY = e.Bounds.Y + (e.Bounds.Height - dotSize) / 2;

        using (var dotBrush = new SolidBrush(dotColor))
        {
            g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
        }

        // Draw text
        int textX = dotX + dotSize + 8;
        var textRect = new Rectangle(textX, e.Bounds.Y, e.Bounds.Width - textX - 6, e.Bounds.Height);
        TextRenderer.DrawText(g, itemText, Font, textRect, textClr,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // Divider line between items
        using var divPen = new Pen(ThemeTokens.BorderSubtle, 1f);
        g.DrawLine(divPen, e.Bounds.X, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ThemeTokens.EnableHighQualityGraphics(g);

        var bounds = ClientRectangle;
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        // Erase with parent background to eliminate jagged corner artifacts
        using (var bgBrush = new SolidBrush(GetParentBackColor()))
        {
            g.FillRectangle(bgBrush, bounds);
        }

        var drawBounds = new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1);
        Color borderColor = _isHovered || DroppedDown ? ThemeTokens.BorderFocus : ThemeTokens.BorderSubtle;

        // Draw background
        using (var path = ThemeTokens.CreateRoundedPath(drawBounds, _borderRadius))
        {
            using (var brush = new SolidBrush(ThemeTokens.BackgroundInput))
            {
                g.FillPath(brush, path);
            }

            using (var pen = new Pen(borderColor, _isHovered ? 1.5f : 1f))
            {
                g.DrawPath(pen, path);
            }
        }

        // Draw selected text
        string displayText = SelectedItem?.ToString() ?? Text;
        int textLeft = 12;

        if (!string.IsNullOrEmpty(displayText))
        {
            Color dotColor = ThemeTokens.AccentSuccess;
            if (displayText.Contains("No enrolled", StringComparison.OrdinalIgnoreCase))
                dotColor = ThemeTokens.AccentWarning;
            else if (displayText.Contains("[This PC", StringComparison.OrdinalIgnoreCase))
                dotColor = ThemeTokens.AccentCyan;
            else if (displayText.Contains("Offline", StringComparison.OrdinalIgnoreCase))
                dotColor = ThemeTokens.AccentDanger;

            int dotSize = 8;
            int dotY = (bounds.Height - dotSize) / 2;
            using (var dotBrush = new SolidBrush(dotColor))
            {
                g.FillEllipse(dotBrush, textLeft, dotY, dotSize, dotSize);
            }
            textLeft += dotSize + 8;

            int textRight = bounds.Width - 30;
            var textRect = new Rectangle(textLeft, 0, Math.Max(10, textRight - textLeft), bounds.Height);
            TextRenderer.DrawText(g, displayText, Font, textRect, ThemeTokens.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        // Draw chevron down icon
        int iconSize = 12;
        int iconX = bounds.Width - iconSize - 12;
        int iconY = (bounds.Height - iconSize) / 2;
        using var iconBmp = IconChar.ChevronDown.ToBitmap(_isHovered ? ThemeTokens.AccentPrimary : ThemeTokens.TextMuted, iconSize);
        g.DrawImage(iconBmp, iconX, iconY, iconSize, iconSize);
    }

    private Color GetParentBackColor()
    {
        Control? p = Parent;
        while (p != null)
        {
            if (p.BackColor != Color.Transparent)
                return p.BackColor;
            p = p.Parent;
        }
        return ThemeTokens.BackgroundApp;
    }
}
