using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EnterpriseRemote.Controller.UI.Theme;
using FontAwesome.Sharp;

namespace EnterpriseRemote.Controller.UI.Components;

public class CyberCard : Panel
{
    private string _title = "";
    private IconChar _headerIcon = IconChar.None;
    private Color _headerIconColor = ThemeTokens.AccentCyan;
    private int _borderRadius = ThemeTokens.RadiusMedium;
    private bool _showBorder = true;
    private Color _customBorderColor = Color.Empty;
    private int _headerHeight = 36;
    private bool _hasHeader = false;

    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            _hasHeader = !string.IsNullOrEmpty(_title) || _headerIcon != IconChar.None;
            Invalidate();
        }
    }

    public IconChar HeaderIcon
    {
        get => _headerIcon;
        set
        {
            _headerIcon = value;
            _hasHeader = !string.IsNullOrEmpty(_title) || _headerIcon != IconChar.None;
            Invalidate();
        }
    }

    public Color HeaderIconColor
    {
        get => _headerIconColor;
        set { _headerIconColor = value; Invalidate(); }
    }

    public int BorderRadius
    {
        get => _borderRadius;
        set { _borderRadius = value; Invalidate(); }
    }

    public bool ShowBorder
    {
        get => _showBorder;
        set { _showBorder = value; Invalidate(); }
    }

    public Color CustomBorderColor
    {
        get => _customBorderColor;
        set { _customBorderColor = value; Invalidate(); }
    }

    public int HeaderHeight
    {
        get => _headerHeight;
        set { _headerHeight = value; Invalidate(); }
    }

    public CyberCard()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);  // No SupportsTransparentBackColor

        // Explicitly opaque — we paint our own background from scratch
        BackColor = ThemeTokens.BackgroundCard;
        Padding = new Padding(12);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ThemeTokens.EnableHighQualityGraphics(g);

        var bounds = ClientRectangle;
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        // Erase entire client area with the parent background first to avoid ghosting
        using (var eraseBrush = new SolidBrush(GetParentBackColor()))
        {
            g.FillRectangle(eraseBrush, bounds);
        }

        var drawBounds = new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1);

        // Fill card background
        using (var path = ThemeTokens.CreateRoundedPath(drawBounds, _borderRadius))
        {
            using (var brush = new LinearGradientBrush(
                drawBounds,
                ThemeTokens.BackgroundCard,
                Color.FromArgb(18, 24, 40),
                LinearGradientMode.Vertical))
            {
                g.FillPath(brush, path);
            }

            // Draw header bar background if title or icon is configured
            if (_hasHeader)
            {
                var headerRect = new Rectangle(0, 0, bounds.Width - 1, _headerHeight);
                using (var headerBrush = new SolidBrush(Color.FromArgb(20, 27, 46)))
                {
                    // Clip to card rounded path
                    var oldClip = g.Clip;
                    g.SetClip(path);
                    g.FillRectangle(headerBrush, headerRect);

                    // Header separator line
                    using var sepPen = new Pen(ThemeTokens.BorderSubtle, 1f);
                    g.DrawLine(sepPen, 0, _headerHeight, bounds.Width, _headerHeight);
                    g.Clip = oldClip;
                }

                int iconX = 14;
                int iconY = (_headerHeight - 16) / 2;
                int textX = iconX;

                if (_headerIcon != IconChar.None)
                {
                    using var bmp = _headerIcon.ToBitmap(_headerIconColor, 16);
                    g.DrawImage(bmp, iconX, iconY, 16, 16);
                    textX += 22;
                }

                if (!string.IsNullOrEmpty(_title))
                {
                    var textRect = new Rectangle(textX, 0, bounds.Width - textX - 10, _headerHeight);
                    TextRenderer.DrawText(g, _title, ThemeTokens.FontCaptionBold, textRect,
                        ThemeTokens.TextSecondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                }
            }

            // Draw card border
            if (_showBorder)
            {
                Color borderClr = _customBorderColor != Color.Empty ? _customBorderColor : ThemeTokens.BorderCard;
                using var pen = new Pen(borderClr, 1f);
                g.DrawPath(pen, path);

                // Subtle inner top highlight
                using var highlightPen = new Pen(Color.FromArgb(25, 255, 255, 255), 1f);
                g.DrawLine(highlightPen, drawBounds.X + _borderRadius, drawBounds.Y + 1, drawBounds.Right - _borderRadius, drawBounds.Y + 1);
            }
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
}
