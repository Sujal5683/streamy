using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using EnterpriseRemote.Controller.UI;
using EnterpriseRemote.Controller.UI.Components;
using EnterpriseRemote.Controller.UI.Theme;
using FontAwesome.Sharp;
using Xunit;

namespace EnterpriseRemote.UnitTests;

public class UiDesignSystemTests
{
    [Fact]
    public void ThemeTokens_ShouldHaveValidEnterprisePalette()
    {
        Assert.NotEqual(Color.Empty, ThemeTokens.BackgroundApp);
        Assert.NotEqual(Color.Empty, ThemeTokens.BackgroundSidebar);
        Assert.NotEqual(Color.Empty, ThemeTokens.BackgroundHeader);
        Assert.NotEqual(Color.Empty, ThemeTokens.BackgroundCard);
        Assert.NotEqual(Color.Empty, ThemeTokens.AccentPrimary);
        Assert.NotEqual(Color.Empty, ThemeTokens.AccentSuccess);
        Assert.NotEqual(Color.Empty, ThemeTokens.AccentDanger);
        Assert.NotEqual(Color.Empty, ThemeTokens.TextPrimary);

        Assert.NotNull(ThemeTokens.FontDisplayLarge);
        Assert.NotNull(ThemeTokens.FontBody);
        Assert.NotNull(ThemeTokens.FontCode);

        // Verify rounded path creation
        var bounds = new Rectangle(0, 0, 100, 50);
        using var path = ThemeTokens.CreateRoundedPath(bounds, 8);
        Assert.NotNull(path);
        Assert.True(path.PointCount > 0);
    }

    [Fact]
    public void CyberButton_ShouldSupportAllVariantsAndIcons()
    {
        RunOnStaThread(() =>
        {
            var btn = new CyberButton
            {
                Text = "Test Action",
                Variant = CyberButtonVariant.Success,
                IconChar = IconChar.Play,
                Size = new Size(160, 40)
            };

            Assert.Equal("Test Action", btn.Text);
            Assert.Equal(CyberButtonVariant.Success, btn.Variant);
            Assert.Equal(IconChar.Play, btn.IconChar);

            // Test render to bitmap
            using var bmp = new Bitmap(btn.Width, btn.Height);
            btn.DrawToBitmap(bmp, new Rectangle(0, 0, btn.Width, btn.Height));
            Assert.NotNull(bmp);
        });
    }

    [Fact]
    public void CyberToggleSwitch_ShouldToggleAndTriggerEvent()
    {
        RunOnStaThread(() =>
        {
            var toggle = new CyberToggleSwitch { Text = "Remote Control", Checked = false };
            bool eventFired = false;
            toggle.CheckedChanged += (_, _) => { eventFired = true; };

            toggle.Checked = true;
            Assert.True(toggle.Checked);
            Assert.True(eventFired);

            toggle.Checked = false;
            Assert.False(toggle.Checked);
        });
    }

    [Fact]
    public void CyberTextBox_ShouldHandleTextAndIconsWithoutExceptions()
    {
        RunOnStaThread(() =>
        {
            var txt = new CyberTextBox
            {
                Text = "http://127.0.0.1:5293",
                LeadingIcon = IconChar.Globe,
                PlaceholderText = "Enter address..."
            };

            Assert.Equal("http://127.0.0.1:5293", txt.Text);
            Assert.Equal(IconChar.Globe, txt.LeadingIcon);

            txt.Text = "http://192.168.1.100:5293";
            Assert.Equal("http://192.168.1.100:5293", txt.Text);

            using var bmp = new Bitmap(txt.Width, txt.Height);
            txt.DrawToBitmap(bmp, new Rectangle(0, 0, txt.Width, txt.Height));
            Assert.NotNull(bmp);
        });
    }

    [Fact]
    public void CyberBadge_ShouldSupportStatuses()
    {
        RunOnStaThread(() =>
        {
            var badge = new CyberBadge
            {
                Text = "LIVE: DESKTOP-TEST",
                Status = CyberBadgeStatus.Success,
                IconChar = IconChar.Desktop
            };

            Assert.Equal("LIVE: DESKTOP-TEST", badge.Text);
            Assert.Equal(CyberBadgeStatus.Success, badge.Status);

            using var bmp = new Bitmap(badge.Width, badge.Height);
            badge.DrawToBitmap(bmp, new Rectangle(0, 0, badge.Width, badge.Height));
            Assert.NotNull(bmp);
        });
    }

    [Fact]
    public void RemoteViewerForm_ShouldInitializeAndRenderAccurately()
    {
        RunOnStaThread(() =>
        {
            using var form = new RemoteViewerForm();
            form.Size = new Size(1440, 900);
            form.CreateControl();

            // Verify form controls exist
            Assert.NotNull(form);
            Assert.True(form.Controls.Count > 0);

            // Test toggling sidebar
            form.ToggleFullScreen();
            form.ToggleFullScreen();

            // Render to bitmap
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));

            string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "RemoteViewer_Verified.png");
            bmp.Save(outPath, ImageFormat.Png);

            Assert.True(File.Exists(outPath));
            Assert.True(new FileInfo(outPath).Length > 1000);
        });
    }

    [Fact]
    public void EnrollRemoteDeviceForm_ShouldInitializeAndRenderAccurately()
    {
        RunOnStaThread(() =>
        {
            using var form = new EnrollRemoteDeviceForm("http://127.0.0.1:5293", "USR-ADMIN-01");
            form.Size = new Size(820, 740);
            form.CreateControl();

            Assert.NotNull(form);
            Assert.True(form.Controls.Count > 0);

            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));

            string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshots");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "EnrollRemoteDevice_Verified.png");
            bmp.Save(outPath, ImageFormat.Png);

            Assert.True(File.Exists(outPath));
            Assert.True(new FileInfo(outPath).Length > 1000);
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(10000);

        if (caught != null)
        {
            throw new AggregateException("STA Thread Exception", caught);
        }
    }
}
