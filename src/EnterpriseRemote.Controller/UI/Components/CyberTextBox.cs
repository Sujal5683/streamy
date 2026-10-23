using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EnterpriseRemote.Controller.UI.Theme;
using FontAwesome.Sharp;

namespace EnterpriseRemote.Controller.UI.Components;

public class CyberTextBox : Control
{
    private readonly TextBox _innerBox;
    private IconChar _leadingIcon = IconChar.None;
    private int _iconSize = 15;
    private Color _iconColor = ThemeTokens.TextMuted;
    private string _placeholderText = "";
    private bool _isFocused = false;
    private int _borderRadius = ThemeTokens.RadiusMedium;

    public TextBox InnerTextBox => _innerBox;

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => _innerBox.Text ?? string.Empty;
        set
        {
            _innerBox.Text = value ?? string.Empty;
            Invalidate();
        }
    }

    public string PlaceholderText
    {
        get => _placeholderText;
        set
        {
            _placeholderText = value;
            Invalidate();
        }
    }

    public IconChar LeadingIcon
    {
        get => _leadingIcon;
        set
        {
            _leadingIcon = value;
            AdjustLayout();
            Invalidate();
        }
    }

    public Color IconColor
    {
        get => _iconColor;
        set
        {
            _iconColor = value;
            Invalidate();
        }
    }

    public bool ReadOnly
    {
        get => _innerBox.ReadOnly;
        set
        {
            _innerBox.ReadOnly = value;
            _innerBox.BackColor = value ? Color.FromArgb(10, 14, 25) : ThemeTokens.BackgroundInput;
            Invalidate();
        }
    }

    public new Font Font
    {
        get => _innerBox.Font;
        set
        {
            base.Font = value;
            _innerBox.Font = value;
            AdjustLayout();
        }
    }

    public int BorderRadius
    {
        get => _borderRadius;
        set { _borderRadius = value; Invalidate(); }
    }

    public CyberTextBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);

        // Use explicit opaque background — we paint behind ourselves
        BackColor = ThemeTokens.BackgroundInput;

        _innerBox = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = ThemeTokens.BackgroundInput,
            ForeColor = ThemeTokens.TextPrimary,
            Font = ThemeTokens.FontCode,
            Location = new Point(10, 8),
            Size = new Size(220, 20)
        };

        _innerBox.GotFocus += (_, _) => { _isFocused = true; Invalidate(); };
        _innerBox.LostFocus += (_, _) => { _isFocused = false; Invalidate(); };
        _innerBox.TextChanged += (_, _) => Invalidate();

        Controls.Add(_innerBox);
        Size = new Size(240, 36);
        AdjustLayout();
    }

    private void AdjustLayout()
    {
        if (_innerBox == null) return;

        int left = 10;
        if (_leadingIcon != IconChar.None)
        {
            left += _iconSize + 8;
        }

        int top = (Height - _innerBox.PreferredHeight) / 2;
        int width = Math.Max(20, Width - left - 10);

        _innerBox.Location = new Point(left, top);
        _innerBox.Width = width;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        AdjustLayout();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        _innerBox.Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ThemeTokens.EnableHighQualityGraphics(g);

        var bounds = ClientRectangle;
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        // Fill entire client area with parent background first to ensure rounded corners blend seamlessly
        using (var bgBrush = new SolidBrush(GetParentBackColor()))
        {
            g.FillRectangle(bgBrush, bounds);
        }

        var drawBounds = new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1);

        Color bgColor = ReadOnly ? Color.FromArgb(10, 14, 25) : ThemeTokens.BackgroundInput;
        Color borderColor = _isFocused ? ThemeTokens.BorderFocus : ThemeTokens.BorderSubtle;

        // Keep inner textbox background in sync with drawn background
        _innerBox.BackColor = bgColor;

        using (var path = ThemeTokens.CreateRoundedPath(drawBounds, _borderRadius))
        {
            using (var brush = new SolidBrush(bgColor))
            {
                g.FillPath(brush, path);
            }

            using (var pen = new Pen(borderColor, _isFocused ? 1.5f : 1f))
            {
                g.DrawPath(pen, path);
            }
        }

        // Render Leading Icon
        if (_leadingIcon != IconChar.None)
        {
            int iconY = (Height - _iconSize) / 2;
            Color clr = _isFocused ? ThemeTokens.AccentPrimary : _iconColor;
            using var iconBmp = _leadingIcon.ToBitmap(clr, _iconSize);
            g.DrawImage(iconBmp, 10, iconY, _iconSize, _iconSize);
        }

        // Render Placeholder Text when empty and not focused
        if (string.IsNullOrEmpty(_innerBox.Text) && !string.IsNullOrEmpty(_placeholderText) && !_isFocused)
        {
            int left = _innerBox.Left;
            int top = _innerBox.Top;
            var textRect = new Rectangle(left, top, _innerBox.Width, _innerBox.Height);
            TextRenderer.DrawText(g, _placeholderText, Font, textRect, ThemeTokens.TextMuted,
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
}
