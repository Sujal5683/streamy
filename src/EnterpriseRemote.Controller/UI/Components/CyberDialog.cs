using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EnterpriseRemote.Controller.UI.Theme;
using FontAwesome.Sharp;

namespace EnterpriseRemote.Controller.UI.Components;

public enum CyberDialogType
{
    Information,
    Success,
    Warning,
    Error,
    Question
}

public class CyberDialog : Form
{
    public static DialogResult Show(IWin32Window? owner, string message, string title, CyberDialogType type = CyberDialogType.Information, MessageBoxButtons buttons = MessageBoxButtons.OK)
    {
        using var dlg = new CyberDialog(title, message, type, buttons);
        if (owner != null)
        {
            return dlg.ShowDialog(owner);
        }
        return dlg.ShowDialog();
    }

    private CyberDialog(string title, string message, CyberDialogType type, MessageBoxButtons buttons)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        BackColor = ThemeTokens.BackgroundApp;
        ForeColor = ThemeTokens.TextPrimary;
        Font = ThemeTokens.FontBody;
        KeyPreview = true;

        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);

        // Header and Icon Configuration
        var iconChar = type switch
        {
            CyberDialogType.Success => IconChar.CircleCheck,
            CyberDialogType.Warning => IconChar.TriangleExclamation,
            CyberDialogType.Error => IconChar.ShieldHalved,
            CyberDialogType.Question => IconChar.CircleQuestion,
            _ => IconChar.CircleInfo
        };

        var accentColor = type switch
        {
            CyberDialogType.Success => ThemeTokens.AccentSuccess,
            CyberDialogType.Warning => ThemeTokens.AccentWarning,
            CyberDialogType.Error => ThemeTokens.AccentDanger,
            CyberDialogType.Question => ThemeTokens.AccentCyan,
            _ => ThemeTokens.AccentPrimary
        };

        // Layout Components
        int pad = 24;

        // Top Banner Icon + Title
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = ThemeTokens.BackgroundHeader,
            Padding = new Padding(pad, 14, pad, 14)
        };
        pnlHeader.Paint += (s, e) =>
        {
            using var pen = new Pen(ThemeTokens.BorderSubtle, 1f);
            e.Graphics.DrawLine(pen, 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
        };

        var picIcon = new PictureBox
        {
            Image = iconChar.ToBitmap(accentColor, 28),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Size = new Size(32, 32),
            Location = new Point(pad, 14),
            BackColor = Color.Transparent
        };
        pnlHeader.Controls.Add(picIcon);

        var lblTitle = new Label
        {
            Text = title,
            Font = ThemeTokens.FontDisplayMedium,
            ForeColor = ThemeTokens.TextPrimary,
            Location = new Point(pad + 42, 16),
            UseMnemonic = false,
            AutoSize = true
        };
        pnlHeader.Controls.Add(lblTitle);
        Controls.Add(pnlHeader);

        // Bottom Action Bar
        var pnlButtons = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 64,
            BackColor = ThemeTokens.BackgroundHeader,
            Padding = new Padding(pad, 12, pad, 12)
        };
        pnlButtons.Paint += (s, e) =>
        {
            using var pen = new Pen(ThemeTokens.BorderSubtle, 1f);
            e.Graphics.DrawLine(pen, 0, 0, pnlButtons.Width, 0);
        };

        CyberButton btnPrimary;
        CyberButton? btnSecondary = null;

        if (buttons == MessageBoxButtons.YesNo)
        {
            btnPrimary = new CyberButton
            {
                Text = "Yes, Proceed",
                Variant = CyberButtonVariant.Primary,
                Size = new Size(130, 38),
                DialogResult = DialogResult.Yes
            };
            btnPrimary.Click += (_, _) => { DialogResult = DialogResult.Yes; Close(); };

            btnSecondary = new CyberButton
            {
                Text = "No, Cancel",
                Variant = CyberButtonVariant.Secondary,
                Size = new Size(116, 38),
                DialogResult = DialogResult.No
            };
            btnSecondary.Click += (_, _) => { DialogResult = DialogResult.No; Close(); };

            pnlButtons.Controls.Add(btnPrimary);
            pnlButtons.Controls.Add(btnSecondary);

            AcceptButton = btnPrimary;
            CancelButton = btnSecondary;
        }
        else if (buttons == MessageBoxButtons.OKCancel)
        {
            btnPrimary = new CyberButton
            {
                Text = "OK",
                Variant = CyberButtonVariant.Primary,
                Size = new Size(110, 38),
                DialogResult = DialogResult.OK
            };
            btnPrimary.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };

            btnSecondary = new CyberButton
            {
                Text = "Cancel",
                Variant = CyberButtonVariant.Secondary,
                Size = new Size(110, 38),
                DialogResult = DialogResult.Cancel
            };
            btnSecondary.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

            pnlButtons.Controls.Add(btnPrimary);
            pnlButtons.Controls.Add(btnSecondary);

            AcceptButton = btnPrimary;
            CancelButton = btnSecondary;
        }
        else
        {
            btnPrimary = new CyberButton
            {
                Text = "Understood",
                Variant = type == CyberDialogType.Error ? CyberButtonVariant.Danger : CyberButtonVariant.Primary,
                Size = new Size(126, 38),
                DialogResult = DialogResult.OK
            };
            btnPrimary.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
            pnlButtons.Controls.Add(btnPrimary);

            AcceptButton = btnPrimary;
            CancelButton = btnPrimary;
        }

        void LayoutButtons()
        {
            int btnY = (pnlButtons.Height - btnPrimary.Height) / 2;
            btnPrimary.Location = new Point(pnlButtons.Width - pad - btnPrimary.Width, btnY);
            if (btnSecondary != null)
            {
                btnSecondary.Location = new Point(btnPrimary.Left - 12 - btnSecondary.Width, btnY);
            }
        }

        pnlButtons.Resize += (_, _) => LayoutButtons();
        Controls.Add(pnlButtons);

        // Center Message Content
        int dialogWidth = 580;
        int textWidth = dialogWidth - (pad * 2);

        Size measuredSize = TextRenderer.MeasureText(
            message,
            ThemeTokens.FontBody,
            new Size(textWidth, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);

        int requiredH = 60 + measuredSize.Height + 64 + 48;
        Size = new Size(dialogWidth, Math.Clamp(requiredH, 230, 600));

        var pnlContent = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(pad, 18, pad, 18),
            BackColor = ThemeTokens.BackgroundApp,
            AutoScroll = requiredH > 600
        };

        var lblMsg = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            BackColor = ThemeTokens.BackgroundApp,
            ForeColor = ThemeTokens.TextSecondary,
            Font = ThemeTokens.FontBody,
            Text = message,
            UseMnemonic = false,
            TextAlign = ContentAlignment.TopLeft
        };
        pnlContent.Controls.Add(lblMsg);

        Controls.Add(pnlContent);
        pnlContent.SendToBack();
        pnlHeader.BringToFront();
        pnlButtons.BringToFront();

        LayoutButtons();

        Shown += (_, _) =>
        {
            LayoutButtons();
            if (AcceptButton is Control c) c.Focus();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        ThemeTokens.EnableHighQualityGraphics(g);

        // Draw crisp modern border around dialog
        using var pen = new Pen(ThemeTokens.BorderInteractive, 1.5f);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
