using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EnterpriseRemote.Controller.UI.Theme;

namespace EnterpriseRemote.Controller.UI.Components;

public class CyberToggleSwitch : Control
{
    private bool _checked = false;
    private Color _activeColor = ThemeTokens.AccentSuccess;
    private Color _inactiveColor = Color.FromArgb(40, 50, 75);
    private int _switchWidth = 42;
    private int _switchHeight = 22;
    private bool _isHovered = false;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked != value)
            {
                _checked = value;
                CheckedChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }
    }

    public Color ActiveColor
    {
        get => _activeColor;
        set { _activeColor = value; Invalidate(); }
    }

    public Color InactiveColor
    {
        get => _inactiveColor;
        set { _inactiveColor = value; Invalidate(); }
    }

    public CyberToggleSwitch()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint |
                 ControlStyles.Selectable, true);

        // Do NOT use SupportsTransparentBackColor — paint bg explicitly
        BackColor = ThemeTokens.BackgroundCard;  // default; parent may override
        ForeColor = ThemeTokens.TextPrimary;
        Font = ThemeTokens.FontBody;
        Cursor = Cursors.Hand;
        Size = new Size(220, 28);
    }

    protected override void OnClick(EventArgs e)
    {
        Checked = !Checked;
        Focus();
        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ThemeTokens.EnableHighQualityGraphics(g);

        var bounds = ClientRectangle;
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        // Fill entire client rect with opaque parent color to prevent bleed
        using (var bgBrush = new SolidBrush(GetParentBackColor()))
        {
            g.FillRectangle(bgBrush, bounds);
        }

        int switchY = (bounds.Height - _switchHeight) / 2;
        var switchRect = new Rectangle(0, switchY, _switchWidth, _switchHeight);

        Color pillBg = _checked ? _activeColor : _inactiveColor;
        Color knobColor = Color.White;

        // Draw Switch Track Pill
        int radius = _switchHeight / 2;
        using (var path = ThemeTokens.CreateRoundedPath(switchRect, radius))
        {
            using (var brush = new SolidBrush(pillBg))
            {
                g.FillPath(brush, path);
            }

            // Outline highlight
            Color borderClr = _isHovered ? Color.FromArgb(160, 255, 255, 255) : Color.FromArgb(40, 255, 255, 255);
            using var pen = new Pen(borderClr, 1f);
            g.DrawPath(pen, path);
        }

        // Draw Switch Knob
        int knobDiameter = _switchHeight - 4;
        int knobX = _checked ? (_switchWidth - knobDiameter - 2) : 2;
        int knobY = switchY + 2;

        var knobRect = new Rectangle(knobX, knobY, knobDiameter, knobDiameter);
        using (var knobBrush = new SolidBrush(knobColor))
        {
            g.FillEllipse(knobBrush, knobRect);
        }

        // Draw Focus Ring if control has focus
        if (Focused)
        {
            using var focusPen = new Pen(ThemeTokens.BorderFocus, 1.5f);
            var focusRect = new Rectangle(switchRect.X - 2, switchRect.Y - 2, switchRect.Width + 4, switchRect.Height + 4);
            using var focusPath = ThemeTokens.CreateRoundedPath(focusRect, radius + 2);
            g.DrawPath(focusPen, focusPath);
        }

        // Draw Label Text
        if (!string.IsNullOrEmpty(Text))
        {
            int textX = _switchWidth + 10;
            var textRect = new Rectangle(textX, 0, Math.Max(10, bounds.Width - textX - 2), bounds.Height);
            Color textClr = Enabled
                ? (_isHovered ? ThemeTokens.TextPrimary : ThemeTokens.TextSecondary)
                : ThemeTokens.TextDisabled;
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
}
