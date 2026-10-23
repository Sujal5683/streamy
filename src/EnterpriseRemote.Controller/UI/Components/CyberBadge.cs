using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EnterpriseRemote.Controller.UI.Theme;
using FontAwesome.Sharp;

namespace EnterpriseRemote.Controller.UI.Components;

public enum CyberBadgeStatus
{
    Neutral,
    Info,
    Success,
    Warning,
    Danger,
    Cyan
}

public class CyberBadge : Control
{
    private CyberBadgeStatus _status = CyberBadgeStatus.Neutral;
    private IconChar _iconChar = IconChar.None;
    private bool _showDot = true;
    private Color? _customBg;
    private Color? _customBorder;
    private Color? _customText;

    public CyberBadgeStatus Status
    {
        get => _status;
        set { _status = value; Invalidate(); }
    }

    public IconChar IconChar
    {
        get => _iconChar;
        set { _iconChar = value; Invalidate(); }
    }

    public bool ShowDot
    {
        get => _showDot;
        set { _showDot = value; Invalidate(); }
    }

    public Color? CustomBackground
    {
        get => _customBg;
        set { _customBg = value; Invalidate(); }
    }

    public Color? CustomBorder
    {
        get => _customBorder;
        set { _customBorder = value; Invalidate(); }
    }

    public Color? CustomText
    {
        get => _customText;
        set { _customText = value; Invalidate(); }
    }

    public CyberBadge()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);

        // Explicit opaque background — we always paint behind ourselves
        BackColor = ThemeTokens.BackgroundHeader;
        Font = ThemeTokens.FontCaptionBold;
        Size = new Size(160, 26);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ThemeTokens.EnableHighQualityGraphics(g);

        var bounds = ClientRectangle;
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        // Fill whole area with parent background first to prevent any bleed
        using (var bgBrush = new SolidBrush(GetParentBackColor()))
        {
            g.FillRectangle(bgBrush, bounds);
        }

        var drawBounds = new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1);
        int radius = drawBounds.Height / 2;

        GetBadgeColors(out var bg, out var border, out var textClr, out var dotClr);

        using (var path = ThemeTokens.CreateRoundedPath(drawBounds, radius))
        {
            using (var brush = new SolidBrush(bg))
            {
                g.FillPath(brush, path);
            }

            using (var pen = new Pen(border, 1f))
            {
                g.DrawPath(pen, path);
            }
        }

        // Draw Dot or Icon + Text
        int currentX = 10;
        int centerY = bounds.Height / 2;

        if (_iconChar != IconChar.None)
        {
            int iconSize = 13;
            int iconY = (bounds.Height - iconSize) / 2;
            using var iconBmp = _iconChar.ToBitmap(textClr, iconSize);
            g.DrawImage(iconBmp, currentX, iconY, iconSize, iconSize);
            currentX += iconSize + 6;
        }
        else if (_showDot)
        {
            int dotSize = 7;
            int dotY = (bounds.Height - dotSize) / 2;
            using var dotBrush = new SolidBrush(dotClr);
            g.FillEllipse(dotBrush, currentX, dotY, dotSize, dotSize);

            // Subtle glow ring around dot
            using var glowPen = new Pen(Color.FromArgb(60, dotClr.R, dotClr.G, dotClr.B), 1.5f);
            g.DrawEllipse(glowPen, currentX - 1, dotY - 1, dotSize + 2, dotSize + 2);

            currentX += dotSize + 6;
        }

        if (!string.IsNullOrEmpty(Text))
        {
            var textRect = new Rectangle(currentX, 0, Math.Max(10, bounds.Width - currentX - 8), bounds.Height);
            TextRenderer.DrawText(g, Text, Font, textRect, textClr,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
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

    private void GetBadgeColors(out Color bg, out Color border, out Color text, out Color dot)
    {
        if (_customBg.HasValue && _customBorder.HasValue && _customText.HasValue)
        {
            bg = _customBg.Value;
            border = _customBorder.Value;
            text = _customText.Value;
            dot = text;
            return;
        }

        switch (_status)
        {
            case CyberBadgeStatus.Success:
                bg = Color.FromArgb(20, 16, 185, 129);
                border = Color.FromArgb(80, 16, 185, 129);
                text = ThemeTokens.AccentSuccess;
                dot = ThemeTokens.AccentSuccess;
                break;

            case CyberBadgeStatus.Warning:
                bg = Color.FromArgb(25, 245, 158, 11);
                border = Color.FromArgb(90, 245, 158, 11);
                text = Color.FromArgb(251, 191, 36);
                dot = ThemeTokens.AccentWarning;
                break;

            case CyberBadgeStatus.Danger:
                bg = Color.FromArgb(25, 239, 68, 68);
                border = Color.FromArgb(90, 239, 68, 68);
                text = Color.FromArgb(248, 113, 113);
                dot = ThemeTokens.AccentDanger;
                break;

            case CyberBadgeStatus.Info:
                bg = Color.FromArgb(25, 99, 102, 241);
                border = Color.FromArgb(90, 99, 102, 241);
                text = ThemeTokens.AccentPrimaryHover;
                dot = ThemeTokens.AccentPrimary;
                break;

            case CyberBadgeStatus.Cyan:
                bg = Color.FromArgb(25, 6, 182, 212);
                border = Color.FromArgb(90, 6, 182, 212);
                text = ThemeTokens.AccentCyan;
                dot = ThemeTokens.AccentCyan;
                break;

            case CyberBadgeStatus.Neutral:
            default:
                bg = Color.FromArgb(20, 30, 48);
                border = ThemeTokens.BorderCard;
                text = ThemeTokens.TextSecondary;
                dot = ThemeTokens.TextMuted;
                break;
        }
    }
}
