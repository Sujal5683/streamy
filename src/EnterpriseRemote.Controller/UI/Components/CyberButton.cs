using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EnterpriseRemote.Controller.UI.Theme;
using FontAwesome.Sharp;

namespace EnterpriseRemote.Controller.UI.Components;

public enum CyberButtonVariant
{
    Primary,
    Secondary,
    Success,
    Warning,
    Danger,
    Ghost,
    Outline
}

public class CyberButton : Button
{
    private CyberButtonVariant _variant = CyberButtonVariant.Primary;
    private IconChar _iconChar = IconChar.None;
    private int _iconSize = 16;
    private Color? _customIconColor;
    private int _borderRadius = ThemeTokens.RadiusMedium;
    private bool _isHovered = false;
    private bool _isPressed = false;
    private bool _pulseGlow = false;

    public CyberButtonVariant Variant
    {
        get => _variant;
        set { _variant = value; Invalidate(); }
    }

    public IconChar IconChar
    {
        get => _iconChar;
        set { _iconChar = value; Invalidate(); }
    }

    public int IconSize
    {
        get => _iconSize;
        set { _iconSize = value; Invalidate(); }
    }

    public Color? CustomIconColor
    {
        get => _customIconColor;
        set { _customIconColor = value; Invalidate(); }
    }

    public int BorderRadius
    {
        get => _borderRadius;
        set { _borderRadius = value; Invalidate(); }
    }

    public bool PulseGlow
    {
        get => _pulseGlow;
        set { _pulseGlow = value; Invalidate(); }
    }

    public CyberButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);  // No SupportsTransparentBackColor

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseDownBackColor = Color.Transparent;
        FlatAppearance.MouseOverBackColor = Color.Transparent;
        // Use an explicit opaque default — we always paint the full background ourselves
        BackColor = ThemeTokens.BackgroundCard;
        Cursor = Cursors.Hand;
        Font = ThemeTokens.FontBodySemibold;
        ForeColor = ThemeTokens.TextPrimary;
        Size = new Size(130, 36);
        Margin = new Padding(3);
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
        _isPressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _isPressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _isPressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        ThemeTokens.EnableHighQualityGraphics(g);

        var bounds = ClientRectangle;
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        // Slight inset for crisp border rendering
        var drawBounds = new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1);
        int radius = _borderRadius == ThemeTokens.RadiusPill ? drawBounds.Height / 2 : _borderRadius;

        GetColors(out var bgTop, out var bgBottom, out var borderColor, out var textColor, out var iconColor);

        // Erase entire control rectangle with parent background so rounded corners blend seamlessly without color artifacts
        using (var clearBrush = new SolidBrush(GetParentBackColor()))
        {
            g.FillRectangle(clearBrush, ClientRectangle);
        }

        // Draw background gradient
        using (var path = ThemeTokens.CreateRoundedPath(drawBounds, radius))
        {
            if (bgTop == Color.Transparent && bgBottom == Color.Transparent)
            {
                // Ghost/transparent: fill with parent color to erase residue, then skip solid fill
                using var parentBrush = new SolidBrush(GetParentBackColor());
                g.FillPath(parentBrush, path);
            }
            else if (bgTop == bgBottom)
            {
                using var brush = new SolidBrush(bgTop);
                g.FillPath(brush, path);
            }
            else
            {
                using var brush = new LinearGradientBrush(drawBounds, bgTop, bgBottom, LinearGradientMode.Vertical);
                g.FillPath(brush, path);
            }

            // Draw border
            if (borderColor != Color.Transparent)
            {
                using var pen = new Pen(borderColor, _isHovered ? 1.5f : 1f);
                g.DrawPath(pen, path);
            }

            // Optional subtle top inner highlight for 3D depth
            if (Enabled && _variant != CyberButtonVariant.Ghost && _variant != CyberButtonVariant.Outline && drawBounds.Height > 10)
            {
                using var highlightPen = new Pen(Color.FromArgb(30, 255, 255, 255), 1f);
                g.DrawLine(highlightPen, drawBounds.X + radius, drawBounds.Y + 1, drawBounds.Right - radius, drawBounds.Y + 1);
            }
        }

        // Render Icon and Text
        int contentWidth = 0;
        int iconOffset = 0;
        int spacing = 8;
        Size textSize = Size.Empty;

        if (!string.IsNullOrEmpty(Text))
        {
            textSize = TextRenderer.MeasureText(Text, Font);
            contentWidth += textSize.Width;
        }

        if (_iconChar != IconChar.None)
        {
            contentWidth += _iconSize;
            if (!string.IsNullOrEmpty(Text))
                contentWidth += spacing;
        }

        int startX = (bounds.Width - contentWidth) / 2;
        if (startX < 6) startX = 6;

        if (_iconChar != IconChar.None)
        {
            int iconY = (bounds.Height - _iconSize) / 2;
            if (_isPressed) iconY += 1;

            using var iconBmp = _iconChar.ToBitmap(iconColor, _iconSize);
            g.DrawImage(iconBmp, startX, iconY, _iconSize, _iconSize);
            iconOffset = _iconSize + spacing;
        }

        if (!string.IsNullOrEmpty(Text))
        {
            int textX = startX + iconOffset;
            int textY = (bounds.Height - textSize.Height) / 2;
            if (_isPressed) textY += 1;

            var textRect = new Rectangle(textX, textY, bounds.Width - textX - 4, textSize.Height);
            TextRenderer.DrawText(g, Text, Font, textRect, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>
    /// Returns the effective background color of the parent container for proper blending.
    /// </summary>
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

    private void GetColors(out Color bgTop, out Color bgBottom, out Color borderColor, out Color textColor, out Color iconColor)
    {
        if (!Enabled)
        {
            bgTop = Color.FromArgb(20, 28, 45);
            bgBottom = Color.FromArgb(16, 22, 36);
            borderColor = Color.FromArgb(35, 45, 65);
            textColor = ThemeTokens.TextDisabled;
            iconColor = ThemeTokens.TextDisabled;
            return;
        }

        textColor = ThemeTokens.TextPrimary;

        switch (_variant)
        {
            case CyberButtonVariant.Primary:
                if (_isPressed)
                {
                    bgTop = Color.FromArgb(67, 56, 202);
                    bgBottom = Color.FromArgb(55, 48, 163);
                    borderColor = ThemeTokens.AccentPrimary;
                }
                else if (_isHovered)
                {
                    bgTop = Color.FromArgb(129, 140, 248);
                    bgBottom = Color.FromArgb(99, 102, 241);
                    borderColor = Color.FromArgb(165, 180, 252);
                }
                else
                {
                    bgTop = Color.FromArgb(99, 102, 241);
                    bgBottom = Color.FromArgb(79, 70, 229);
                    borderColor = Color.FromArgb(129, 140, 248);
                }
                iconColor = _customIconColor ?? Color.White;
                break;

            case CyberButtonVariant.Secondary:
                if (_isPressed)
                {
                    bgTop = Color.FromArgb(30, 41, 65);
                    bgBottom = Color.FromArgb(24, 32, 54);
                    borderColor = ThemeTokens.BorderInteractive;
                }
                else if (_isHovered)
                {
                    bgTop = Color.FromArgb(45, 59, 86);
                    bgBottom = Color.FromArgb(35, 47, 72);
                    borderColor = ThemeTokens.BorderFocus;
                }
                else
                {
                    bgTop = Color.FromArgb(30, 41, 62);
                    bgBottom = Color.FromArgb(22, 30, 48);
                    borderColor = ThemeTokens.BorderCard;
                }
                textColor = _isHovered ? ThemeTokens.TextPrimary : ThemeTokens.TextSecondary;
                iconColor = _customIconColor ?? (_isHovered ? ThemeTokens.TextPrimary : ThemeTokens.AccentCyan);
                break;

            case CyberButtonVariant.Success:
                if (_isPressed)
                {
                    bgTop = Color.FromArgb(4, 120, 87);
                    bgBottom = Color.FromArgb(6, 95, 70);
                    borderColor = ThemeTokens.AccentSuccess;
                }
                else if (_isHovered)
                {
                    bgTop = Color.FromArgb(52, 211, 153);
                    bgBottom = Color.FromArgb(16, 185, 129);
                    borderColor = Color.FromArgb(110, 231, 183);
                }
                else
                {
                    bgTop = Color.FromArgb(16, 185, 129);
                    bgBottom = Color.FromArgb(5, 150, 105);
                    borderColor = Color.FromArgb(52, 211, 153);
                }
                iconColor = _customIconColor ?? Color.White;
                break;

            case CyberButtonVariant.Warning:
                if (_isPressed)
                {
                    bgTop = Color.FromArgb(180, 83, 9);
                    bgBottom = Color.FromArgb(146, 64, 14);
                    borderColor = ThemeTokens.AccentWarning;
                }
                else if (_isHovered)
                {
                    bgTop = Color.FromArgb(251, 191, 36);
                    bgBottom = Color.FromArgb(245, 158, 11);
                    borderColor = Color.FromArgb(252, 211, 77);
                }
                else
                {
                    bgTop = Color.FromArgb(245, 158, 11);
                    bgBottom = Color.FromArgb(217, 119, 6);
                    borderColor = Color.FromArgb(251, 191, 36);
                }
                iconColor = _customIconColor ?? Color.White;
                break;

            case CyberButtonVariant.Danger:
                if (_isPressed)
                {
                    bgTop = Color.FromArgb(185, 28, 28);
                    bgBottom = Color.FromArgb(153, 27, 27);
                    borderColor = ThemeTokens.AccentDanger;
                }
                else if (_isHovered)
                {
                    bgTop = Color.FromArgb(248, 113, 113);
                    bgBottom = Color.FromArgb(239, 68, 68);
                    borderColor = Color.FromArgb(252, 165, 165);
                }
                else
                {
                    bgTop = Color.FromArgb(239, 68, 68);
                    bgBottom = Color.FromArgb(220, 38, 38);
                    borderColor = Color.FromArgb(248, 113, 113);
                }
                iconColor = _customIconColor ?? Color.White;
                break;

            case CyberButtonVariant.Ghost:
                if (_isPressed)
                {
                    bgTop = Color.FromArgb(40, 50, 75);
                    bgBottom = Color.FromArgb(40, 50, 75);
                    borderColor = Color.Transparent;
                }
                else if (_isHovered)
                {
                    bgTop = Color.FromArgb(30, 41, 62);
                    bgBottom = Color.FromArgb(30, 41, 62);
                    borderColor = Color.Transparent;
                }
                else
                {
                    bgTop = Color.Transparent;
                    bgBottom = Color.Transparent;
                    borderColor = Color.Transparent;
                }
                textColor = _isHovered ? ThemeTokens.TextPrimary : ThemeTokens.TextSecondary;
                iconColor = _customIconColor ?? textColor;
                break;

            case CyberButtonVariant.Outline:
            default:
                if (_isPressed)
                {
                    bgTop = Color.FromArgb(30, 41, 65);
                    bgBottom = Color.FromArgb(24, 32, 54);
                    borderColor = ThemeTokens.BorderFocus;
                }
                else if (_isHovered)
                {
                    bgTop = Color.FromArgb(25, 35, 55);
                    bgBottom = Color.FromArgb(20, 28, 45);
                    borderColor = ThemeTokens.BorderHover;
                }
                else
                {
                    bgTop = Color.Transparent;
                    bgBottom = Color.Transparent;
                    borderColor = ThemeTokens.BorderCard;
                }
                textColor = _isHovered ? ThemeTokens.TextPrimary : ThemeTokens.TextSecondary;
                iconColor = _customIconColor ?? textColor;
                break;
        }
    }
}
