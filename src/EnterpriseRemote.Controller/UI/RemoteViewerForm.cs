using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace EnterpriseRemote.Controller.UI;

/// <summary>
/// Enterprise Windows Remote Access — Unified Native Controller Shell.
/// Powered by Microsoft Edge WebView2 (Direct3D Hardware-Accelerated Chromium).
/// 
/// ARCHITECTURE NOTE FOR ALL DEVELOPERS & AI AGENTS:
/// ------------------------------------------------
/// This native desktop shell embeds and renders the single source of truth UI
/// from the Control Plane (wwwroot/index.html, app.js, app.css).
/// It provides:
///   1. 100% exact UI parity with the modern Cyber UI (two-tier topbar, status chips, segmented pills).
///   2. GPU-accelerated Direct3D 60 FPS viewport rendering.
///   3. Native Windows F11 borderless fullscreen and Esc restore.
///   4. System tray minimize and quick actions.
///   5. Automated background ControlPlane launcher and local loopback binding.
/// 
/// DO NOT CREATE OR MODIFY LEGACY GDI+ WINFORMS CONTROLS.
/// All UI layout, styling, and interactions live in wwwroot/.
/// </summary>
public sealed class RemoteViewerForm : Form
{
    private readonly WebView2 _webView;
    private NotifyIcon? _trayIcon;
    private bool _isFullScreen = false;
    private FormBorderStyle _prevBorderStyle;
    private FormWindowState _prevWindowState;
    private Rectangle _prevBounds;
    private const string TargetServerUrl = "http://127.0.0.1:5293";

    public RemoteViewerForm()
    {
        Text = "Enterprise Windows Remote Access — Secure Operator Console";
        Size = new Size(1440, 900);
        MinimumSize = new Size(1024, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(11, 15, 25); // #0B0F19 dark cyber theme (prevents white startup flash)
        ForeColor = Color.FromArgb(226, 232, 240);
        KeyPreview = true;

        // Try load high-res brand icon
        try
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(iconPath))
            {
                Icon = new Icon(iconPath);
            }
            else
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
        }
        catch { }

        // Initialize WebView2 docked to fill entire window
        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(11, 15, 25)
        };
        Controls.Add(_webView);

        InitializeSystemTray();
        HookKeyboardShortcuts();

        Shown += async (_, _) => await InitializeDesktopHostAsync();
        FormClosing += (_, _) =>
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        };
    }

    private void InitializeSystemTray()
    {
        try
        {
            var contextMenu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(241, 245, 249),
                ShowImageMargin = false
            };

            var itemOpen = new ToolStripMenuItem("Open Enterprise Remote", null, (_, _) => RestoreWindow())
            {
                Font = new Font(Font, FontStyle.Bold)
            };
            contextMenu.Items.Add(itemOpen);

            contextMenu.Items.Add(new ToolStripMenuItem("Toggle Full Screen (F11)", null, (_, _) => ToggleFullScreen()));
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(new ToolStripMenuItem("Exit Application", null, (_, _) =>
            {
                if (_trayIcon != null) _trayIcon.Visible = false;
                Application.Exit();
            }));

            _trayIcon = new NotifyIcon
            {
                Text = "Enterprise Remote Controller",
                Icon = Icon ?? SystemIcons.Application,
                ContextMenuStrip = contextMenu,
                Visible = true
            };

            _trayIcon.DoubleClick += (_, _) => RestoreWindow();
        }
        catch { }
    }

    private void RestoreWindow()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        BringToFront();
        Activate();
    }

    private void HookKeyboardShortcuts()
    {
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.F11)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape && _isFullScreen)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
        };
    }

    public static string NormalizeServerUrl(string input)
        => EnterpriseRemote.Common.Protocol.UrlUtils.NormalizeServerUrl(input);

    public void ToggleFullScreen()
    {
        if (!_isFullScreen)
        {
            _prevBorderStyle = FormBorderStyle;
            _prevWindowState = WindowState;
            _prevBounds = Bounds;

            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            Bounds = Screen.FromControl(this).Bounds;
            _isFullScreen = true;
        }
        else
        {
            FormBorderStyle = _prevBorderStyle;
            WindowState = _prevWindowState;
            Bounds = _prevBounds;
            _isFullScreen = false;
        }
    }

    private async Task InitializeDesktopHostAsync()
    {
        try
        {
            // 1. Ensure the local ControlPlane backend server is running silently
            await EnsureControlPlaneRunningAsync();

            // 2. Configure dedicated local user data folder for WebView2 storage & cache
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EnterpriseRemote",
                "WebView2Data");
            Directory.CreateDirectory(userDataFolder);

            var envOptions = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--disable-background-timer-throttling --allow-insecure-localhost"
            };

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                options: envOptions);

            await _webView.EnsureCoreWebView2Async(env);

            // Configure browser settings for native enterprise client
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

            // Render instant cyber splash screen to guarantee 0ms dark theme without white flash
            _webView.NavigateToString(@"<!DOCTYPE html>
<html>
<head>
  <meta charset='utf-8'>
  <style>
    * { margin:0; padding:0; box-sizing:border-box; }
    body { background:#07090e; color:#00e5ff; font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif; display:flex; flex-direction:column; align-items:center; justify-content:center; height:100vh; overflow:hidden; user-select:none; }
    .loader { width:46px; height:46px; border:3px solid rgba(0,229,255,0.15); border-top-color:#00e5ff; border-radius:50%; animation:spin 0.75s cubic-bezier(0.4, 0, 0.2, 1) infinite; margin-bottom:20px; box-shadow:0 0 15px rgba(0,229,255,0.25); }
    @keyframes spin { to { transform: rotate(360deg); } }
    .title { font-size:15px; font-weight:700; letter-spacing:1.5px; color:#f8fafc; margin-bottom:6px; }
    .sub { font-size:12px; color:#64748b; font-family:Consolas,monospace; }
  </style>
</head>
<body>
  <div class='loader'></div>
  <div class='title'>ENTERPRISE REMOTE ACCESS</div>
  <div class='sub'>Connecting Secure Direct3D Session Engine...</div>
</body>
</html>");

            // Handle messages bridged from JS
            _webView.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                try
                {
                    string rawJson = args.TryGetWebMessageAsString();
                    if (string.IsNullOrWhiteSpace(rawJson)) return;

                    using var doc = JsonDocument.Parse(rawJson);
                    if (doc.RootElement.TryGetProperty("type", out var typeEl))
                    {
                        string msgType = typeEl.GetString() ?? "";
                        if (msgType.Equals("toggle-fullscreen", StringComparison.OrdinalIgnoreCase))
                        {
                            BeginInvoke(ToggleFullScreen);
                        }
                    }
                }
                catch { }
            };

            // Catch navigation issues & auto-retry if port 5293 takes a moment to bind
            int retryCount = 0;
            _webView.NavigationCompleted += async (_, e) =>
            {
                if (!e.IsSuccess && retryCount < 10)
                {
                    retryCount++;
                    await Task.Delay(600);
                    _webView.CoreWebView2?.Navigate(TargetServerUrl);
                }
            };

            // Navigate to the ControlPlane web controller
            _webView.CoreWebView2.Navigate(TargetServerUrl);
        }
        catch (Exception ex)
        {
            ShowWebView2FallbackDialog(ex);
        }
    }

    private static async Task EnsureControlPlaneRunningAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1000) };
        try
        {
            var res = await http.GetAsync(TargetServerUrl);
            if (res.IsSuccessStatusCode) return; // ControlPlane already alive and listening!
        }
        catch { }

        // Attempt to launch ControlPlane silently
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidatePaths =
        [
            Path.Combine(baseDir, "EnterpriseRemote.ControlPlane.exe"),
            Path.Combine(baseDir, "..", "EnterpriseRemote.ControlPlane.exe"),
            Path.Combine(baseDir, "..", "..", "1_MAIN_PC_CONTROLLER", "Bin", "EnterpriseRemote.ControlPlane.exe"),
            Path.Combine(baseDir, "..", "..", "..", "EnterpriseRemote.ControlPlane", "bin", "Release", "net8.0", "EnterpriseRemote.ControlPlane.exe")
        ];

        string? foundPath = null;
        foreach (var p in candidatePaths)
        {
            string full = Path.GetFullPath(p);
            if (File.Exists(full))
            {
                foundPath = full;
                break;
            }
        }

        if (foundPath != null)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = foundPath,
                    WorkingDirectory = Path.GetDirectoryName(foundPath)!,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);

                // Wait up to 5 seconds for server to bind port
                for (int i = 0; i < 25; i++)
                {
                    await Task.Delay(200);
                    try
                    {
                        var res = await http.GetAsync(TargetServerUrl);
                        if (res.IsSuccessStatusCode) break;
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    private void ShowWebView2FallbackDialog(Exception ex)
    {
        var result = MessageBox.Show(
            $"The Enterprise Remote Controller requires the Microsoft Edge WebView2 Runtime to display the Direct3D hardware-accelerated interface.\n\n" +
            $"Error details: {ex.Message}\n\n" +
            "Would you like to open the Web Controller directly in your default browser now?",
            "Enterprise Remote Controller — Runtime Notice",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);

        if (result == DialogResult.Yes)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = TargetServerUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
