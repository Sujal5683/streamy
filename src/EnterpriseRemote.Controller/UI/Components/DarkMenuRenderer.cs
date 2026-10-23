using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using EnterpriseRemote.Controller.UI.Theme;

namespace EnterpriseRemote.Controller.UI.Components;

public class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkMenuColorTable())
    {
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var g = e.Graphics;
        ThemeTokens.EnableHighQualityGraphics(g);

        if (e.Item.Selected || e.Item.Pressed)
        {
            var bounds = new Rectangle(4, 2, e.Item.Width - 8, e.Item.Height - 4);
            using var path = ThemeTokens.CreateRoundedPath(bounds, 4);
            using var brush = new SolidBrush(Color.FromArgb(45, 59, 86));
            g.FillPath(brush, path);

            using var pen = new Pen(ThemeTokens.BorderInteractive, 1f);
            g.DrawPath(pen, path);
        }
        else
        {
            base.OnRenderMenuItemBackground(e);
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var g = e.Graphics;
        int y = e.Item.Height / 2;
        using var pen = new Pen(ThemeTokens.BorderSubtle, 1f);
        g.DrawLine(pen, 12, y, e.Item.Width - 12, y);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var g = e.Graphics;
        ThemeTokens.EnableHighQualityGraphics(g);
        var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        using var pen = new Pen(ThemeTokens.BorderInteractive, 1f);
        g.DrawRectangle(pen, bounds);
    }

    private sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => ThemeTokens.BackgroundHeader;
        public override Color MenuBorder => ThemeTokens.BorderInteractive;
        public override Color MenuItemSelected => Color.FromArgb(45, 59, 86);
        public override Color MenuItemBorder => ThemeTokens.BorderFocus;
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(45, 59, 86);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(45, 59, 86);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(55, 70, 100);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(55, 70, 100);
        public override Color ImageMarginGradientBegin => ThemeTokens.BackgroundHeader;
        public override Color ImageMarginGradientMiddle => ThemeTokens.BackgroundHeader;
        public override Color ImageMarginGradientEnd => ThemeTokens.BackgroundHeader;
        public override Color SeparatorDark => ThemeTokens.BorderSubtle;
        public override Color SeparatorLight => Color.Transparent;
        public override Color CheckBackground => ThemeTokens.AccentPrimary;
        public override Color CheckSelectedBackground => ThemeTokens.AccentPrimaryHover;
        public override Color CheckPressedBackground => ThemeTokens.AccentPrimaryDark;
    }
}
