using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace EnterpriseRemote.Controller.UI.Theme;

/// <summary>
/// Unified Design Tokens for Enterprise Remote Controller.
/// Provides enterprise cybersecurity palette, typography, radii, and drawing helpers.
/// </summary>
public static class ThemeTokens
{
    // =========================================================================
    // COLOR PALETTE - SURFACES & ELEVATION
    // =========================================================================
    public static readonly Color BackgroundApp = Color.FromArgb(11, 15, 25);         // Deep obsidian #0B0F19
    public static readonly Color BackgroundSidebar = Color.FromArgb(15, 23, 42);     // Deep slate-navy #0F172A
    public static readonly Color BackgroundHeader = Color.FromArgb(17, 24, 39);      // Dark slate #111827
    public static readonly Color BackgroundCard = Color.FromArgb(24, 32, 54);        // Elevated card surface #182036
    public static readonly Color BackgroundCardHover = Color.FromArgb(32, 43, 72);   // Card interactive hover #202B48
    public static readonly Color BackgroundCardActive = Color.FromArgb(38, 52, 88);  // Card pressed state
    public static readonly Color BackgroundInput = Color.FromArgb(13, 19, 33);       // Recessed input field #0D1321
    public static readonly Color BackgroundStatusBar = Color.FromArgb(10, 14, 23);   // Bottom footer #0A0E17
    public static readonly Color BackgroundOverlay = Color.FromArgb(220, 11, 15, 25);
    public static readonly Color BackgroundFloatingBar = Color.FromArgb(245, 17, 24, 39);

    // =========================================================================
    // COLOR PALETTE - BORDERS & STROKES
    // =========================================================================
    public static readonly Color BorderSubtle = Color.FromArgb(30, 41, 59);          // #1E293B
    public static readonly Color BorderCard = Color.FromArgb(45, 59, 86);            // #2D3B56
    public static readonly Color BorderInteractive = Color.FromArgb(64, 82, 117);    // #405275
    public static readonly Color BorderFocus = Color.FromArgb(99, 102, 241);         // Indigo #6366F1
    public static readonly Color BorderHover = Color.FromArgb(129, 140, 248);        // Light indigo #818CF8

    // =========================================================================
    // COLOR PALETTE - ACCENTS & INTENTS
    // =========================================================================
    public static readonly Color AccentPrimary = Color.FromArgb(99, 102, 241);      // Electric Indigo #6366F1
    public static readonly Color AccentPrimaryHover = Color.FromArgb(129, 140, 248); // Indigo Hover #818CF8
    public static readonly Color AccentPrimaryDark = Color.FromArgb(79, 70, 229);   // Deep Indigo #4F46E5

    public static readonly Color AccentCyan = Color.FromArgb(6, 182, 212);           // Cyber Cyan #06B6D4
    public static readonly Color AccentCyanHover = Color.FromArgb(34, 211, 238);
    public static readonly Color AccentCyanDark = Color.FromArgb(8, 145, 178);

    public static readonly Color AccentSuccess = Color.FromArgb(16, 185, 129);       // Emerald Green #10B981
    public static readonly Color AccentSuccessHover = Color.FromArgb(52, 211, 153);
    public static readonly Color AccentSuccessBg = Color.FromArgb(6, 78, 59);

    public static readonly Color AccentWarning = Color.FromArgb(245, 158, 11);       // Amber Gold #F59E0B
    public static readonly Color AccentWarningHover = Color.FromArgb(251, 191, 36);
    public static readonly Color AccentWarningBg = Color.FromArgb(120, 53, 15);

    public static readonly Color AccentDanger = Color.FromArgb(239, 68, 68);         // Crimson Red #EF4444
    public static readonly Color AccentDangerHover = Color.FromArgb(248, 113, 113);
    public static readonly Color AccentDangerBg = Color.FromArgb(127, 29, 29);

    // =========================================================================
    // COLOR PALETTE - TYPOGRAPHY HIERARCHY
    // =========================================================================
    public static readonly Color TextPrimary = Color.FromArgb(248, 250, 252);        // Pure White #F8FAFC
    public static readonly Color TextSecondary = Color.FromArgb(148, 163, 184);      // Slate 400 #94A3B8
    public static readonly Color TextMuted = Color.FromArgb(100, 116, 139);          // Slate 500 #64748B
    public static readonly Color TextDisabled = Color.FromArgb(71, 85, 105);         // Slate 600 #475569
    public static readonly Color TextAccent = Color.FromArgb(129, 140, 248);         // Indigo #818CF8

    // =========================================================================
    // CORNER RADII TOKENS
    // =========================================================================
    public const int RadiusSmall = 5;
    public const int RadiusMedium = 8;
    public const int RadiusLarge = 12;
    public const int RadiusXLarge = 16;
    public const int RadiusPill = 999;

    // =========================================================================
    // TYPOGRAPHY HELPERS
    // =========================================================================
    private static readonly string FontFamilyName = ResolveSystemFontFamily();

    private static string ResolveSystemFontFamily()
    {
        // Segoe UI Variable is available on Windows 11; Segoe UI on Windows 10/earlier
        try
        {
            using var test = new Font("Segoe UI Variable Text", 9f);
            if (test.Name.Equals("Segoe UI Variable Text", StringComparison.OrdinalIgnoreCase))
                return "Segoe UI Variable Text";
        }
        catch { }

        return "Segoe UI";
    }

    public static Font FontDisplayLarge => new(FontFamilyName, 15f, FontStyle.Bold);
    public static Font FontDisplayMedium => new(FontFamilyName, 12.5f, FontStyle.Bold);
    public static Font FontSectionHeader => new(FontFamilyName, 10.5f, FontStyle.Bold);
    public static Font FontBodyBold => new(FontFamilyName, 9.2f, FontStyle.Bold);
    public static Font FontBodySemibold => new(FontFamilyName, 9.2f, FontStyle.Bold);
    public static Font FontBody => new(FontFamilyName, 9.2f, FontStyle.Regular);
    public static Font FontCaption => new(FontFamilyName, 8.2f, FontStyle.Regular);
    public static Font FontCaptionBold => new(FontFamilyName, 8.2f, FontStyle.Bold);
    public static Font FontCode => new("Cascadia Code", 8.8f, FontStyle.Regular);
    public static Font FontCodeBold => new("Cascadia Code", 8.8f, FontStyle.Bold);

    // =========================================================================
    // GRAPHICS & DRAWING HELPERS
    // =========================================================================
    public static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0) return path;

        int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

        // Top-left
        path.AddArc(arc, 180, 90);

        // Top-right
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);

        // Bottom-right
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);

        // Bottom-left
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }

    public static void FillRoundedRectangle(Graphics g, Brush brush, Rectangle bounds, int radius)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = CreateRoundedPath(bounds, radius);
        g.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(Graphics g, Pen pen, Rectangle bounds, int radius)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = CreateRoundedPath(bounds, radius);
        g.DrawPath(pen, path);
    }

    public static void EnableHighQualityGraphics(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
    }
}
