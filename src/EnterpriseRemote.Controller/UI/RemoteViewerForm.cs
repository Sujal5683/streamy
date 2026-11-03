using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
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
    private static Process? _controlPlaneProcess;
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
            TerminateAllBackgroundProcesses();
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
                TerminateAllBackgroundProcesses();
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
            else if (e.KeyCode == Keys.F5 || (e.Control && e.KeyCode == Keys.R))
            {
                _webView.CoreWebView2?.Reload();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F12)
            {
                _webView.CoreWebView2?.OpenDevToolsWindow();
                e.Handled = true;
            }
        };
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyImmersiveDarkMode(Handle);
    }

    private static void ApplyImmersiveDarkMode(IntPtr handle)
    {
        try
        {
            int trueValue = 1;
            if (DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueValue, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref trueValue, sizeof(int));
            }

            // Dark cyber theme caption color (#0B0F19 in BGR format: 0x00190F0B) and cyan text (#00E5FF in BGR: 0x00FFE500)
            int darkBg = 0x00190F0B;
            DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref darkBg, sizeof(int));
            int cyanText = 0x00FFE500;
            DwmSetWindowAttribute(handle, DWMWA_TEXT_COLOR, ref cyanText, sizeof(int));
        }
        catch { }
    }

    public static string NormalizeServerUrl(string input)
        => EnterpriseRemote.Common.Protocol.UrlUtils.NormalizeServerUrl(input);

    public void SetFullScreen(bool fullscreen)
    {
        if (fullscreen == _isFullScreen) return;

        if (fullscreen)
        {
            _prevBorderStyle = FormBorderStyle;
            _prevWindowState = WindowState;
            _prevBounds = Bounds;

            // Completely eliminate the OS title bar, minimize/maximize/close icons, and window borders
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            Bounds = Screen.FromControl(this).Bounds;
            TopMost = true; // Ensure native window floats above Windows taskbar and system elements
            _isFullScreen = true;
        }
        else
        {
            TopMost = false;
            FormBorderStyle = _prevBorderStyle != FormBorderStyle.None ? _prevBorderStyle : FormBorderStyle.Sizable;
            WindowState = _prevWindowState;
            Bounds = _prevBounds.Width > 0 ? _prevBounds : new Rectangle(100, 100, 1440, 900);
            _isFullScreen = false;
        }
    }

    public void ToggleFullScreen()
    {
        SetFullScreen(!_isFullScreen);
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

            // Seamlessly synchronize native Windows fullscreen with WebView2 HTML5 fullscreen
            _webView.CoreWebView2.ContainsFullScreenElementChanged += (_, _) =>
            {
                bool isHtmlFullscreen = _webView.CoreWebView2?.ContainsFullScreenElement ?? false;
                BeginInvoke(() => SetFullScreen(isHtmlFullscreen));
            };

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
                            if (doc.RootElement.TryGetProperty("fullscreen", out var fsEl))
                            {
                                bool targetFs = fsEl.GetBoolean();
                                BeginInvoke(() => SetFullScreen(targetFs));
                            }
                            else
                            {
                                BeginInvoke(ToggleFullScreen);
                            }
                        }
                        else if (msgType.Equals("shutdown-all", StringComparison.OrdinalIgnoreCase))
                        {
                            BeginInvoke(() =>
                            {
                                if (_trayIcon != null) _trayIcon.Visible = false;
                                TerminateAllBackgroundProcesses();
                                Application.Exit();
                            });
                        }
                        else if (msgType.Equals("reload-ui", StringComparison.OrdinalIgnoreCase))
                        {
                            BeginInvoke(() => _webView.CoreWebView2?.Reload());
                        }
                    }
                }
                catch { }
            };

            // Catch navigation issues & auto-retry if port 5293 takes a moment to bind
            int retryCount = 0;
            string freshUrl = $"{TargetServerUrl}/?v={DateTime.UtcNow.Ticks}";
            _webView.NavigationCompleted += async (_, e) =>
            {
                if (!e.IsSuccess && retryCount < 10)
                {
                    retryCount++;
                    await Task.Delay(600);
                    _webView.CoreWebView2?.Navigate(freshUrl);
                }
            };

            // Clear browsing cache so UI edits in wwwroot are always 100% instantly live
            try
            {
                await _webView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                    CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage);
            }
            catch { }

            // Navigate to the ControlPlane web controller with dynamic cache-buster
            _webView.CoreWebView2.Navigate(freshUrl);
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
                _controlPlaneProcess = Process.Start(psi);

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

    public static void TerminateAllBackgroundProcesses()
    {
        try
        {
            if (_controlPlaneProcess != null && !_controlPlaneProcess.HasExited)
            {
                try { _controlPlaneProcess.Kill(true); } catch { }
                _controlPlaneProcess = null;
            }
        }
        catch { }

        try
        {
            foreach (var procName in new[] { "EnterpriseRemote.ControlPlane", "cloudflared" })
            {
                foreach (var p in Process.GetProcessesByName(procName))
                {
                    try
                    {
                        p.Kill(true);
                    }
                    catch { }
                }
            }
        }
        catch { }
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
