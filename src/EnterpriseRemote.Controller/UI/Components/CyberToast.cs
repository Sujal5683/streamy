using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EnterpriseRemote.Controller.UI.Theme;
using FontAwesome.Sharp;

namespace EnterpriseRemote.Controller.UI.Components;

public enum ToastType
{
    Info,
    Success,
    Warning,
    Danger
}

public class CyberToast : Panel
{
    private readonly Form _parentForm;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToastType _type;
    private readonly string _title;
    private readonly string _message;
    private readonly IconChar _icon;
    private int _durationMs = 3500;
    private int _elapsedMs = 0;
    private readonly Action? _clickAction;

    public static void Show(Form parent, string title, string message, ToastType type = ToastType.Info, int durationMs = 3500, Action? onClick = null)
    {
        if (parent.IsDisposed) return;
        if (parent.InvokeRequired)
        {
            parent.BeginInvoke(new Action(() => Show(parent, title, message, type, durationMs, onClick)));
            return;
        }

        var toast = new CyberToast(parent, title, message, type, durationMs, onClick);
        parent.Controls.Add(toast);
        toast.BringToFront();
        toast.PositionToast();
    }

    private CyberToast(Form parent, string title, string message, ToastType type, int durationMs, Action? onClick)
    {
        _parentForm = parent;
        _title = title;
        _message = message;
        _type = type;
        _durationMs = durationMs;
        _clickAction = onClick;

        _icon = type switch
        {
            ToastType.Success => IconChar.CircleCheck,
            ToastType.Warning => IconChar.TriangleExclamation,
            ToastType.Danger => IconChar.CircleXmark,
            _ => IconChar.CircleInfo
        };

        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint |
                 ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
        Size = new Size(360, 68);
        Cursor = onClick != null ? Cursors.Hand : Cursors.Default;

        _timer = new System.Windows.Forms.Timer { Interval = 40 };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        parent.Resize += (_, _) => PositionToast();
    }

    private void PositionToast()
    {
        if (_parentForm.IsDisposed) return;
        int x = _parentForm.ClientSize.Width - Width - 24;
        int y = _parentForm.ClientSize.Height - Height - 48;
        Location = new Point(Math.Max(10, x), Math.Max(10, y));
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _elapsedMs += _timer.Interval;
        if (_elapsedMs >= _durationMs)
        {
            Dismiss();
        }
        else
        {
            Invalidate();
        }
    }

    private void Dismiss()
    {
        _timer.Stop();
        _timer.Dispose();
        if (_parentForm.Controls.Contains(this))
        {
            _parentForm.Controls.Remove(this);
        }
        Dispose();
    }

    protected override void OnClick(EventArgs e)
    {
        _clickAction?.Invoke();
        Dismiss();
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        ThemeTokens.EnableHighQualityGraphics(g);

        var bounds = ClientRectangle;
        if (bounds.Width <= 1 || bounds.Height <= 1) return;

        var drawBounds = new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1);
        int radius = ThemeTokens.RadiusMedium;

        Color accentClr = _type switch
        {
            ToastType.Success => ThemeTokens.AccentSuccess,
            ToastType.Warning => ThemeTokens.AccentWarning,
            ToastType.Danger => ThemeTokens.AccentDanger,
            _ => ThemeTokens.AccentPrimary
        };

        // Draw Shadow and Card Background
        using (var path = ThemeTokens.CreateRoundedPath(drawBounds, radius))
        {
            using (var brush = new LinearGradientBrush(drawBounds, Color.FromArgb(245, 24, 32, 54), Color.FromArgb(245, 17, 24, 39), LinearGradientMode.Vertical))
            {
                g.FillPath(brush, path);
            }

            using (var pen = new Pen(ThemeTokens.BorderInteractive, 1f))
            {
                g.DrawPath(pen, path);
            }

            // Left accent bar
            using var accentBrush = new SolidBrush(accentClr);
            g.FillRectangle(accentBrush, 0, radius, 4, drawBounds.Height - (radius * 2));
        }

        // Draw Icon
        int iconSize = 22;
        int iconX = 16;
        int iconY = (bounds.Height - iconSize) / 2;
        using (var iconBmp = _icon.ToBitmap(accentClr, iconSize))
        {
            g.DrawImage(iconBmp, iconX, iconY, iconSize, iconSize);
        }

        // Draw Title
        int textX = iconX + iconSize + 12;
        var titleRect = new Rectangle(textX, 12, bounds.Width - textX - 28, 20);
        TextRenderer.DrawText(g, _title, ThemeTokens.FontBodyBold, titleRect, ThemeTokens.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);

        // Draw Message
        var msgRect = new Rectangle(textX, 32, bounds.Width - textX - 28, 26);
        TextRenderer.DrawText(g, _message, ThemeTokens.FontCaption, msgRect, ThemeTokens.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

        // Draw Close 'x'
        int closeX = bounds.Width - 22;
        int closeY = 12;
        using var closeIcon = IconChar.Xmark.ToBitmap(ThemeTokens.TextMuted, 12);
        g.DrawImage(closeIcon, closeX, closeY, 12, 12);

        // Draw Auto-dismiss Progress Bar along bottom edge
        float progress = 1.0f - Math.Clamp((float)_elapsedMs / _durationMs, 0f, 1f);
        int barWidth = (int)((bounds.Width - 8) * progress);
        if (barWidth > 0)
        {
            using var barBrush = new SolidBrush(Color.FromArgb(140, accentClr.R, accentClr.G, accentClr.B));
            g.FillRectangle(barBrush, 4, bounds.Height - 3, barWidth, 2);
        }
    }
}
