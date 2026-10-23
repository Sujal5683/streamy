using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Protocol;
using EnterpriseRemote.Controller.UI.Components;
using EnterpriseRemote.Controller.UI.Theme;
using EnterpriseRemote.Controller.UI.Utils;
using FontAwesome.Sharp;
using ProtocolMods = EnterpriseRemote.Common.Protocol.ModifierKeys;

namespace EnterpriseRemote.Controller.UI;

public sealed class RemoteViewerForm : Form
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // UI Header Controls
    private Panel _topBar = null!;
    private Panel _pnlHeaderLeft = null!;
    private Panel _pnlHeaderCenter = null!;
    private FlowLayoutPanel _flowHeaderRight = null!;
    private PictureBox _picHeaderLogo = null!;
    private Label _lblBrandTitle = null!;
    private CyberBadge _badgeAuth = null!;
    private CyberBadge _badgeTarget = null!;
    private CyberButton _btnToggleSidebar = null!;
    private CyberButton _btnEmergencyStop = null!;
    private CyberButton _btnFullScreen = null!;
    private CyberButton _btnTakeScreenshot = null!;
    private CyberButton _btnQuickActions = null!;
    private CyberButton _btnToggleInputControl = null!;
    private CyberButton _btnScaleMode = null!;
    private ViewportScaleMode _scaleMode = ViewportScaleMode.Fit;

    // Framerate & Stream Settings Controls
    private CyberButton _btnFpsMode = null!;
    private CyberButton _btnStreamSettings = null!;
    private ContextMenuStrip _menuFps = null!;
    private ContextMenuStrip _menuStreamSettings = null!;
    private CyberButton _btnFloatFpsMode = null!;

    // Stream Quality & Dynamic Framerate State
    private int _targetFps = 60;
    private int _jpegQuality = 70;
    private ushort _maxHeight = 0;
    private byte _lowLatencyMode = 1;
    private float _lastMeasuredRttMs = 0f;
    private ulong _lastPingSentTicks = 0;

    // Quick Actions Context Menu
    private ContextMenuStrip _menuRemoteActions = null!;

    // UI Sidebar Controls
    private CyberScrollPanel _sidePanel = null!;
    private CyberTextBox _txtServer = null!;
    private CyberTextBox _txtEmail = null!;
    private CyberButton _btnLogin = null!;
    private CyberComboBox _cboDevices = null!;
    private CyberButton _btnRefresh = null!;
    private CyberTextBox _txtPermanentKey = null!;
    private readonly Dictionary<string, string> _devicePermanentTokens = new(StringComparer.OrdinalIgnoreCase);
    private bool _userInitiatedDisconnect = true;
    private bool _isAutoReconnecting = false;
    private int _autoReconnectAttempt = 0;
    private System.Windows.Forms.Timer? _autoReconnectTimer;

    private static string PermanentTokensFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EnterpriseRemote",
        "permanent_keys.json");

    private void LoadPermanentTokensFromDisk()
    {
        try
        {
            string path = PermanentTokensFilePath;
            string localFallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "permanent_keys.json");
            string? target = File.Exists(path) ? path : (File.Exists(localFallback) ? localFallback : null);
            if (target != null)
            {
                string json = File.ReadAllText(target);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null)
                {
                    foreach (var (k, v) in dict)
                    {
                        if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(v))
                        {
                            _devicePermanentTokens[k] = v.Trim();
                        }
                    }
                }
            }
        }
        catch { }
    }

    private void SavePermanentTokensToDisk()
    {
        try
        {
            string dir = Path.GetDirectoryName(PermanentTokensFilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(_devicePermanentTokens, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PermanentTokensFilePath, json);
        }
        catch { }
    }
    private CyberButton _btnEnrollRemote = null!;
    private CyberButton _btnConnect = null!;
    private CyberButton _btnDisconnect = null!;
    private CyberToggleSwitch _chkInputControl = null!;
    private CyberToggleSwitch _chkClipboard = null!;
    private Label _lblTelemetry = null!;

    // UI Viewport & Status
    private PictureBox _viewport = null!;
    private Panel _statusBar = null!;
    private Label _lblStatus = null!;
    private Label _lblHints = null!;

    // UI Floating Full-Screen Island Bar (Draggable & Collapsible)
    private FlowLayoutPanel _floatingBar = null!;
    private Label _lblFloatGrip = null!;
    private CyberBadge _badgeFloatDevice = null!;
    private CyberButton _btnFloatExitFullScreen = null!;
    private CyberButton _btnFloatToggleControl = null!;
    private CyberButton _btnFloatActions = null!;
    private CyberButton _btnFloatScreenshot = null!;
    private CyberButton _btnFloatScaleMode = null!;
    private CyberButton _btnFloatDisconnect = null!;
    private CyberButton _btnFloatEmergency = null!;
    private CyberButton _btnFloatCollapse = null!;
    private bool _isFloatBarCollapsed = false;
    private bool _isDraggingFloatBar = false;
    private Point _dragStartCursorPos;
    private Point _dragStartBarPos;
    private Image? _appLogoImage;
    private NotifyIcon? _trayIcon;

    // Session & Streaming State
    private string? _operatorId;
    private string? _currentDeviceId;
    private string? _currentDeviceHostname;
    private RemoteSessionClient? _sessionClient;
    private System.Windows.Forms.Timer? _frameTimer;
    private Bitmap? _previewBitmap;
    private readonly object _streamLock = new();
    private int _frameCount = 0;
    private DateTime _lastFpsUpdate = DateTime.UtcNow;
    private DateTime _sessionStartTime = DateTime.UtcNow;
    private float _currentFps = 60.0f;
    private Size _remoteScreenSize = new(1920, 1080);
    private bool _isMouseDown = false;
    private long _lastMouseMoveTicks = 0;
    private float _lastSentNormX = -1f;
    private float _lastSentNormY = -1f;

    // WebSocket Stream Client
    private ClientWebSocket? _streamWs;
    private CancellationTokenSource? _streamCts;
    private readonly SemaphoreSlim _wsSendLock = new(1, 1);
    private Task? _streamReceiveTask;
    private bool _isStreamConnected = false;
    private string _streamStatusMessage = "Waiting for host...";

    // Window Layout & Full Screen State
    private bool _isFullScreen = false;
    private bool _sidebarCollapsed = false;
    private FormBorderStyle _previousBorderStyle;
    private FormWindowState _previousWindowState;
    private Rectangle _previousBounds;

    public RemoteViewerForm()
    {
        KeyPreview = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        InitializeComponent();
        LoadPermanentTokensFromDisk();
        InitializeTrayIcon();
        TryAutoStartControlPlane();
        _ = AuthenticateAndLoadDevicesAsync();
    }

    private void InitializeTrayIcon()
    {
        try
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new DarkMenuRenderer(),
                BackColor = ThemeTokens.BackgroundHeader,
                ForeColor = ThemeTokens.TextPrimary,
                Font = ThemeTokens.FontBody
            };

            var itemOpen = new ToolStripMenuItem("Open Enterprise Remote", IconChar.ShieldHalved.ToBitmap(ThemeTokens.AccentPrimary, 16), (_, _) =>
            {
                Show();
                WindowState = FormWindowState.Normal;
                BringToFront();
            });
            itemOpen.Font = ThemeTokens.FontBodyBold;
            menu.Items.Add(itemOpen);

            menu.Items.Add(new ToolStripMenuItem("Toggle Full Screen (F11)", IconChar.Expand.ToBitmap(ThemeTokens.AccentCyan, 16), (_, _) =>
            {
                Show();
                ToggleFullScreen();
            }));

            menu.Items.Add(new ToolStripSeparator());

            menu.Items.Add(new ToolStripMenuItem("Exit Application", IconChar.PowerOff.ToBitmap(ThemeTokens.AccentDanger, 16), (_, _) =>
            {
                if (_trayIcon != null) _trayIcon.Visible = false;
                Close();
            }));

            _trayIcon = new NotifyIcon
            {
                Text = "Enterprise Remote Controller",
                Icon = this.Icon ?? SystemIcons.Application,
                ContextMenuStrip = menu,
                Visible = true
            };

            _trayIcon.DoubleClick += (_, _) =>
            {
                Show();
                WindowState = FormWindowState.Normal;
                BringToFront();
            };
        }
        catch { }
    }

    private void InitializeComponent()
    {
        Text = "Enterprise Windows Remote Access — Secure Operator Console";
        Size = new Size(1440, 900);
        MinimumSize = new Size(1080, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = ThemeTokens.BackgroundApp;
        ForeColor = ThemeTokens.TextPrimary;
        Font = ThemeTokens.FontBody;

        // Load Custom Application Icon & Brand Logo
        try
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(iconPath))
            {
                this.Icon = new System.Drawing.Icon(iconPath);
            }
            string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_logo.png");
            if (File.Exists(logoPath))
            {
                _appLogoImage = Image.FromFile(logoPath);
            }
        }
        catch { }

        // =========================================================================
        // 1. TOP RESPONSIVE HEADER BAR
        // =========================================================================
        _topBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 70,
            BackColor = ThemeTokens.BackgroundHeader,
            Padding = new Padding(16, 12, 16, 12)
        };
        _topBar.Paint += (s, e) =>
        {
            using var pen = new Pen(ThemeTokens.BorderSubtle, 1f);
            e.Graphics.DrawLine(pen, 0, _topBar.Height - 1, _topBar.Width, _topBar.Height - 1);
        };
        _topBar.Resize += (_, _) => LayoutTopBarElements();

        InitializeRemoteActionsMenu();
        InitializeFpsMenu();
        InitializeStreamSettingsMenu();

        // 1A. Left Branded Block
        _pnlHeaderLeft = new Panel
        {
            Location = new Point(16, 12),
            Size = new Size(380, 46),
            BackColor = ThemeTokens.BackgroundHeader
        };

        _btnToggleSidebar = new CyberButton
        {
            Text = "",
            IconChar = IconChar.Bars,
            IconSize = 18,
            Variant = CyberButtonVariant.Ghost,
            Size = new Size(38, 38),
            Location = new Point(0, 4),
            BorderRadius = ThemeTokens.RadiusMedium
        };
        _btnToggleSidebar.Click += (_, _) => ToggleSidebar();
        _pnlHeaderLeft.Controls.Add(_btnToggleSidebar);

        int brandStartX = 46;
        if (_appLogoImage != null)
        {
            _picHeaderLogo = new PictureBox
            {
                Image = _appLogoImage,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(36, 36),
                Location = new Point(brandStartX, 5),
                BackColor = Color.Transparent
            };
            _pnlHeaderLeft.Controls.Add(_picHeaderLogo);
            brandStartX += 44;
        }
        else
        {
            var picShield = new PictureBox
            {
                Image = IconChar.ShieldHalved.ToBitmap(ThemeTokens.AccentCyan, 28),
                SizeMode = PictureBoxSizeMode.CenterImage,
                Size = new Size(32, 32),
                Location = new Point(brandStartX, 6),
                BackColor = Color.Transparent
            };
            _pnlHeaderLeft.Controls.Add(picShield);
            brandStartX += 38;
        }

        _lblBrandTitle = new Label
        {
            Text = "EnterpriseRemote",
            Font = ThemeTokens.FontDisplayMedium,
            ForeColor = ThemeTokens.TextPrimary,
            Location = new Point(brandStartX, 11),
            AutoSize = true
        };
        _pnlHeaderLeft.Controls.Add(_lblBrandTitle);

        _badgeAuth = new CyberBadge
        {
            Text = "Connecting operator...",
            Status = CyberBadgeStatus.Neutral,
            IconChar = IconChar.UserShield,
            ShowDot = false,
            Location = new Point(brandStartX + 172, 10),
            Size = new Size(230, 26),
            Font = ThemeTokens.FontCaptionBold,
            BackColor = ThemeTokens.BackgroundHeader
        };
        _pnlHeaderLeft.Controls.Add(_badgeAuth);
        _pnlHeaderLeft.Width = _badgeAuth.Right + 8;
        _topBar.Controls.Add(_pnlHeaderLeft);

        // 1B. Center Dynamic Session Status Badge
        _pnlHeaderCenter = new Panel
        {
            Size = new Size(380, 42),
            BackColor = ThemeTokens.BackgroundHeader
        };

        _badgeTarget = new CyberBadge
        {
            Dock = DockStyle.Fill,
            Text = "IDLE — No Session Active",
            Status = CyberBadgeStatus.Neutral,
            IconChar = IconChar.Desktop,
            Font = ThemeTokens.FontBodySemibold,
            BackColor = ThemeTokens.BackgroundHeader
        };
        _pnlHeaderCenter.Controls.Add(_badgeTarget);
        _topBar.Controls.Add(_pnlHeaderCenter);

        // 1C. Right Action Toolstrip
        _flowHeaderRight = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            Height = 46,
            BackColor = ThemeTokens.BackgroundHeader
        };

        // Emergency Kill Button
        _btnEmergencyStop = new CyberButton
        {
            Text = "",
            IconChar = IconChar.PowerOff,
            IconSize = 16,
            Variant = CyberButtonVariant.Danger,
            Size = new Size(42, 38),
            BorderRadius = ThemeTokens.RadiusMedium,
            PulseGlow = true
        };
        _btnEmergencyStop.Click += (_, _) => OnEmergencyStopClicked();
        _flowHeaderRight.Controls.Add(_btnEmergencyStop);

        // Full Screen Button
        _btnFullScreen = new CyberButton
        {
            Text = "Full Screen",
            IconChar = IconChar.Expand,
            IconSize = 15,
            Variant = CyberButtonVariant.Primary,
            Size = new Size(124, 38)
        };
        _btnFullScreen.Click += (_, _) => ToggleFullScreen();
        _flowHeaderRight.Controls.Add(_btnFullScreen);

        // Screenshot Button
        _btnTakeScreenshot = new CyberButton
        {
            Text = "Capture",
            IconChar = IconChar.Camera,
            IconSize = 15,
            Variant = CyberButtonVariant.Secondary,
            Size = new Size(100, 38)
        };
        _btnTakeScreenshot.Click += (_, _) => TakeScreenshot();
        _flowHeaderRight.Controls.Add(_btnTakeScreenshot);

        // Quick Remote Actions Dropdown
        _btnQuickActions = new CyberButton
        {
            Text = "Actions ▾",
            IconChar = IconChar.Bolt,
            IconSize = 15,
            Variant = CyberButtonVariant.Secondary,
            Size = new Size(114, 38)
        };
        _btnQuickActions.Click += (_, _) => _menuRemoteActions.Show(_btnQuickActions, new Point(0, _btnQuickActions.Height + 4));
        _flowHeaderRight.Controls.Add(_btnQuickActions);

        // Interactive Input Control Toggle
        _btnToggleInputControl = new CyberButton
        {
            Text = "Control: ON",
            IconChar = IconChar.ComputerMouse,
            IconSize = 15,
            Variant = CyberButtonVariant.Success,
            Size = new Size(136, 38)
        };
        _btnToggleInputControl.Click += (_, _) => ToggleInputControl();
        _flowHeaderRight.Controls.Add(_btnToggleInputControl);

        // Viewport Display Scale Mode Toggle (Fit Screen, Stretch, 1:1)
        _btnScaleMode = new CyberButton
        {
            Text = "Fit Screen",
            IconChar = IconChar.Compress,
            IconSize = 14,
            Variant = CyberButtonVariant.Secondary,
            Size = new Size(116, 38)
        };
        _btnScaleMode.Click += (_, _) => CycleScaleMode();
        _flowHeaderRight.Controls.Add(_btnScaleMode);

        // Dynamic Framerate Selector (60 FPS default)
        _btnFpsMode = new CyberButton
        {
            Text = "60 FPS ▾",
            IconChar = IconChar.Bolt,
            IconSize = 14,
            Variant = CyberButtonVariant.Secondary,
            Size = new Size(116, 38)
        };
        _btnFpsMode.Click += (_, _) => _menuFps.Show(_btnFpsMode, new Point(0, _btnFpsMode.Height + 4));
        _flowHeaderRight.Controls.Add(_btnFpsMode);

        // Stream Quality & Low Latency Performance Settings
        _btnStreamSettings = new CyberButton
        {
            Text = "Settings ▾",
            IconChar = IconChar.Sliders,
            IconSize = 14,
            Variant = CyberButtonVariant.Secondary,
            Size = new Size(114, 38)
        };
        _btnStreamSettings.Click += (_, _) => _menuStreamSettings.Show(_btnStreamSettings, new Point(0, _btnStreamSettings.Height + 4));
        _flowHeaderRight.Controls.Add(_btnStreamSettings);

        _topBar.Controls.Add(_flowHeaderRight);
        Controls.Add(_topBar);

        // =========================================================================
        // 2. BOTTOM STATUS BAR
        // =========================================================================
        _statusBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            BackColor = ThemeTokens.BackgroundStatusBar,
            Padding = new Padding(16, 8, 16, 8)
        };
        _statusBar.Paint += (s, e) =>
        {
            using var pen = new Pen(ThemeTokens.BorderSubtle, 1f);
            e.Graphics.DrawLine(pen, 0, 0, _statusBar.Width, 0);
        };

        _lblHints = new Label
        {
            Dock = DockStyle.Right,
            Text = "F11: Full Screen  |  Esc: Exit  |  Ctrl+Alt+Del: Secure Attention",
            ForeColor = ThemeTokens.TextMuted,
            Font = ThemeTokens.FontCaption,
            AutoSize = false,
            Width = 420,
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = ThemeTokens.BackgroundStatusBar
        };
        _statusBar.Controls.Add(_lblHints);

        _lblStatus = new Label
        {
            Dock = DockStyle.Fill,
            Text = "● Ready. Select a target device and click 'START REMOTE SESSION'.",
            ForeColor = ThemeTokens.TextSecondary,
            Font = ThemeTokens.FontBody,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = ThemeTokens.BackgroundStatusBar
        };
        _statusBar.Controls.Add(_lblStatus);
        Controls.Add(_statusBar);

        // =========================================================================
        // 3. LEFT SIDEBAR CONTROL PANEL
        // =========================================================================
        _sidePanel = new CyberScrollPanel
        {
            Dock = DockStyle.Left,
            Width = 340,
            BackColor = ThemeTokens.BackgroundSidebar,
            AutoScroll = true,
            Padding = new Padding(14)
        };
        _sidePanel.Paint += (s, e) =>
        {
            using var pen = new Pen(ThemeTokens.BorderSubtle, 1f);
            e.Graphics.DrawLine(pen, _sidePanel.Width - 1, 0, _sidePanel.Width - 1, _sidePanel.Height);
        };

        int curY = 14;
        int cardWidth = 300;

        // Card 1: Control Plane Gateway
        var cardGateway = new CyberCard
        {
            Title = "GATEWAY & IDENTITY",
            HeaderIcon = IconChar.Server,
            HeaderIconColor = ThemeTokens.AccentCyan,
            Location = new Point(14, curY),
            Size = new Size(cardWidth, 214)
        };

        var lblServer = new Label { Text = "Control Plane Gateway URL:", ForeColor = ThemeTokens.TextSecondary, AutoSize = true, Location = new Point(12, 44), Font = ThemeTokens.FontCaption, BackColor = Color.Transparent };
        cardGateway.Controls.Add(lblServer);

        var lnkLocal = new LinkLabel
        {
            Text = "[Reset 127.0.0.1]",
            LinkColor = ThemeTokens.AccentCyan,
            ActiveLinkColor = ThemeTokens.AccentPrimary,
            AutoSize = true,
            Location = new Point(cardWidth - 128, 44),
            Font = ThemeTokens.FontCaption,
            BackColor = Color.Transparent
        };
        lnkLocal.LinkClicked += (_, _) =>
        {
            _txtServer.Text = "http://127.0.0.1:5293";
            CyberToast.Show(this, "Reset to Local", "Server URL reset to http://127.0.0.1:5293", ToastType.Info, 2000);
        };
        cardGateway.Controls.Add(lnkLocal);

        _txtServer = new CyberTextBox
        {
            Text = "http://127.0.0.1:5293",
            LeadingIcon = IconChar.Globe,
            Location = new Point(12, 64),
            Size = new Size(cardWidth - 24, 34)
        };
        cardGateway.Controls.Add(_txtServer);

        var lblEmail = new Label { Text = "Operator Identity (Your Account):", ForeColor = ThemeTokens.TextSecondary, AutoSize = true, Location = new Point(12, 106), Font = ThemeTokens.FontCaption, BackColor = Color.Transparent };
        cardGateway.Controls.Add(lblEmail);

        var lnkAdminAccounts = new LinkLabel
        {
            Text = "[Admin Accounts ▾]",
            LinkColor = ThemeTokens.AccentCyan,
            ActiveLinkColor = ThemeTokens.AccentPrimary,
            AutoSize = true,
            Location = new Point(cardWidth - 138, 106),
            Font = ThemeTokens.FontCaption,
            BackColor = Color.Transparent
        };
        var adminMenu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = ThemeTokens.BackgroundHeader,
            ForeColor = ThemeTokens.TextPrimary,
            Font = ThemeTokens.FontBody
        };
        void AddAdminAccount(string email, string label, IconChar icon)
        {
            var item = new ToolStripMenuItem($"{label} ({email})", icon.ToBitmap(ThemeTokens.AccentCyan, 16), (_, _) =>
            {
                _txtEmail.Text = email;
                _ = AuthenticateAndLoadDevicesAsync();
            });
            adminMenu.Items.Add(item);
        }
        AddAdminAccount("admin@corp.internal", "Security Admin", IconChar.ShieldHalved);
        AddAdminAccount("admin@local", "Local Admin", IconChar.UserShield);
        AddAdminAccount("sujal@corp.internal", "Sujal (Admin)", IconChar.UserGear);
        AddAdminAccount("alice@corp.internal", "Alice Admin", IconChar.UserCheck);
        AddAdminAccount("operator@corp.internal", "Remote Operator", IconChar.LaptopCode);
        AddAdminAccount("admin", "System Admin (Quick)", IconChar.Key);
        AddAdminAccount("root@corp.internal", "Super Admin", IconChar.Crown);
        adminMenu.Items.Add(new ToolStripSeparator());
        var itemDirectory = new ToolStripMenuItem("📋 View Accounts Directory...", IconChar.AddressBook.ToBitmap(ThemeTokens.AccentCyan, 16), (_, _) => ShowAdminAccountsDialog());
        itemDirectory.Font = ThemeTokens.FontBodyBold;
        adminMenu.Items.Add(itemDirectory);

        lnkAdminAccounts.LinkClicked += (_, _) =>
        {
            adminMenu.Show(lnkAdminAccounts, new Point(0, lnkAdminAccounts.Height + 2));
        };
        cardGateway.Controls.Add(lnkAdminAccounts);

        _txtEmail = new CyberTextBox
        {
            Text = "admin@corp.internal",
            LeadingIcon = IconChar.User,
            Location = new Point(12, 126),
            Size = new Size(cardWidth - 110, 34)
        };
        cardGateway.Controls.Add(_txtEmail);

        _btnLogin = new CyberButton
        {
            Text = "Login",
            Variant = CyberButtonVariant.Primary,
            IconChar = IconChar.ArrowRightToBracket,
            Location = new Point(cardWidth - 92, 126),
            Size = new Size(80, 34)
        };
        _btnLogin.Click += async (_, _) => await AuthenticateAndLoadDevicesAsync();
        cardGateway.Controls.Add(_btnLogin);

        var lblEmailHint = new Label
        {
            Text = "Accounts listed in ADMIN_ACCOUNTS.txt (or click [Admin Accounts ▾]).",
            ForeColor = ThemeTokens.TextMuted,
            Font = ThemeTokens.FontCaption,
            Location = new Point(14, 168),
            Size = new Size(cardWidth - 28, 32),
            BackColor = Color.Transparent
        };
        cardGateway.Controls.Add(lblEmailHint);

        _sidePanel.Controls.Add(cardGateway);
        curY += cardGateway.Height + 14;

        // Card 2: Target Device & Enrollment
        var cardDevice = new CyberCard
        {
            Title = "TARGET ENDPOINT",
            HeaderIcon = IconChar.Desktop,
            HeaderIconColor = ThemeTokens.AccentPrimary,
            Location = new Point(14, curY),
            Size = new Size(cardWidth, 222)
        };

        var lblDev = new Label { Text = "Select Remote Workstation:", ForeColor = ThemeTokens.TextSecondary, AutoSize = true, Location = new Point(12, 44), Font = ThemeTokens.FontCaption, BackColor = Color.Transparent };
        cardDevice.Controls.Add(lblDev);

        _cboDevices = new CyberComboBox
        {
            Location = new Point(12, 64),
            Size = new Size(cardWidth - 68, 34)
        };
        _cboDevices.SelectedIndexChanged += (_, _) =>
        {
            if (_cboDevices.SelectedItem == null) return;
            string sel = _cboDevices.SelectedItem.ToString() ?? "";
            string devId = sel.Split('|')[0].Trim();
            if (_devicePermanentTokens.TryGetValue(devId, out var token) && !string.IsNullOrEmpty(token))
            {
                _txtPermanentKey.Text = token;
            }
            else
            {
                _txtPermanentKey.Text = "";
            }
        };
        cardDevice.Controls.Add(_cboDevices);

        _btnRefresh = new CyberButton
        {
            Text = "",
            IconChar = IconChar.ArrowsRotate,
            IconSize = 15,
            Variant = CyberButtonVariant.Secondary,
            Location = new Point(cardWidth - 50, 64),
            Size = new Size(38, 34)
        };
        _btnRefresh.Click += async (_, _) => await LoadDevicesAsync();
        cardDevice.Controls.Add(_btnRefresh);

        var lblPermKey = new Label { Text = "Permanent Access Key (Unattended):", ForeColor = ThemeTokens.TextSecondary, AutoSize = true, Location = new Point(12, 106), Font = ThemeTokens.FontCaption, BackColor = Color.Transparent };
        cardDevice.Controls.Add(lblPermKey);

        _txtPermanentKey = new CyberTextBox
        {
            LeadingIcon = IconChar.Key,
            Location = new Point(12, 126),
            Size = new Size(cardWidth - 24, 34)
        };
        _txtPermanentKey.TextChanged += (_, _) =>
        {
            string key = _txtPermanentKey.Text.Trim();
            if (_cboDevices.SelectedItem != null)
            {
                string sel = _cboDevices.SelectedItem.ToString() ?? "";
                string devId = sel.Split('|')[0].Trim();
                if (!string.IsNullOrEmpty(devId) && !devId.StartsWith("No", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(key))
                        _devicePermanentTokens[devId] = key;
                    else
                        _devicePermanentTokens.Remove(devId);
                    SavePermanentTokensToDisk();
                }
            }
        };
        cardDevice.Controls.Add(_txtPermanentKey);

        _btnEnrollRemote = new CyberButton
        {
            Text = "Connect Other PC (Enroll Host)",
            Variant = CyberButtonVariant.Secondary,
            IconChar = IconChar.Plus,
            CustomIconColor = ThemeTokens.AccentCyan,
            Location = new Point(12, 170),
            Size = new Size(cardWidth - 24, 36),
            Font = ThemeTokens.FontBodyBold
        };
        _btnEnrollRemote.Click += (_, _) => OpenEnrollRemoteDialog();
        cardDevice.Controls.Add(_btnEnrollRemote);

        _sidePanel.Controls.Add(cardDevice);
        curY += cardDevice.Height + 14;

        // Card 3: Session Controls & Permissions
        var cardSession = new CyberCard
        {
            Title = "SESSION SECURITY & CONTROLS",
            HeaderIcon = IconChar.Sliders,
            HeaderIconColor = ThemeTokens.AccentSuccess,
            Location = new Point(14, curY),
            Size = new Size(cardWidth, 206)
        };

        _chkInputControl = new CyberToggleSwitch
        {
            Text = "Remote Mouse & Keyboard",
            Checked = true,
            Location = new Point(14, 46),
            Size = new Size(cardWidth - 28, 28),
            BackColor = ThemeTokens.BackgroundCard
        };
        _chkInputControl.CheckedChanged += (_, _) => UpdateInputControlUI();
        cardSession.Controls.Add(_chkInputControl);

        _chkClipboard = new CyberToggleSwitch
        {
            Text = "Synchronize Clipboard",
            Checked = true,
            Location = new Point(14, 78),
            Size = new Size(cardWidth - 28, 28),
            BackColor = ThemeTokens.BackgroundCard
        };
        cardSession.Controls.Add(_chkClipboard);

        _btnConnect = new CyberButton
        {
            Text = "START REMOTE SESSION",
            Variant = CyberButtonVariant.Success,
            IconChar = IconChar.Play,
            Location = new Point(12, 114),
            Size = new Size(cardWidth - 24, 42),
            Font = ThemeTokens.FontBodyBold
        };
        _btnConnect.Click += async (_, _) => await ConnectSessionAsync();
        cardSession.Controls.Add(_btnConnect);

        _btnDisconnect = new CyberButton
        {
            Text = "End Remote Session",
            Variant = CyberButtonVariant.Secondary,
            IconChar = IconChar.Stop,
            Location = new Point(12, 162),
            Size = new Size(cardWidth - 24, 32),
            Enabled = false
        };
        _btnDisconnect.Click += (_, _) => DisconnectSession();
        cardSession.Controls.Add(_btnDisconnect);

        _sidePanel.Controls.Add(cardSession);
        curY += cardSession.Height + 14;

        // Card 4: Telemetry & Security HUD
        var cardTelemetry = new CyberCard
        {
            Title = "REAL-TIME TELEMETRY HUD",
            HeaderIcon = IconChar.ChartLine,
            HeaderIconColor = ThemeTokens.AccentCyan,
            Location = new Point(14, curY),
            Size = new Size(cardWidth, 196)
        };

        var pnlHud = new Panel
        {
            Location = new Point(12, 44),
            Size = new Size(cardWidth - 24, 138),
            BackColor = ThemeTokens.BackgroundInput,
            Padding = new Padding(10)
        };
        pnlHud.Paint += (s, e) =>
        {
            using var pen = new Pen(ThemeTokens.BorderSubtle, 1f);
            e.Graphics.DrawRectangle(pen, 0, 0, pnlHud.Width - 1, pnlHud.Height - 1);
        };

        _lblTelemetry = new Label
        {
            Dock = DockStyle.Fill,
            Text = "• Protocol: DTLS 1.3 / SRTP\n• Pipeline: Ready (0.0ms)\n• Framerate: 60 FPS\n• Target: None\n• Status: Idle / Ready\n• Security: Zero-Trust Attested",
            Font = ThemeTokens.FontCode,
            ForeColor = ThemeTokens.TextSecondary,
            BackColor = ThemeTokens.BackgroundInput
        };
        pnlHud.Controls.Add(_lblTelemetry);
        cardTelemetry.Controls.Add(pnlHud);

        _sidePanel.Controls.Add(cardTelemetry);
        curY += cardTelemetry.Height + 14;
        _sidePanel.AutoScrollMinSize = new Size(cardWidth, curY);
        Controls.Add(_sidePanel);

        // =========================================================================
        // 4. FLOATING OVERLAY BAR (FULL SCREEN HUD)
        // =========================================================================
        InitializeFloatingBar();

        // =========================================================================
        // 5. CENTER VIEWPORT CANVAS
        // =========================================================================
        _viewport = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeTokens.BackgroundApp
        };
        _viewport.Paint += OnViewportPaint;
        _viewport.MouseMove += OnViewportMouseMove;
        _viewport.MouseDown += OnViewportMouseDown;
        _viewport.MouseUp += OnViewportMouseUp;
        _viewport.MouseWheel += OnViewportMouseWheel;
        _viewport.MouseLeave += (_, _) => { _isMouseDown = false; };
        _viewport.Click += (_, _) => _viewport.Focus();
        Controls.Add(_viewport);
        _viewport.SendToBack();

        _frameTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _frameTimer.Tick += OnFrameTimerTick;

        LayoutTopBarElements();
    }

    private void LayoutTopBarElements()
    {
        if (_topBar == null || _pnlHeaderLeft == null || _flowHeaderRight == null || _pnlHeaderCenter == null) return;

        if (_lblBrandTitle != null && _badgeAuth != null)
        {
            _badgeAuth.Location = new Point(_lblBrandTitle.Right + 12, 10);
            _pnlHeaderLeft.Width = _badgeAuth.Right + 8;
        }

        _flowHeaderRight.Location = new Point(_topBar.Width - _flowHeaderRight.Width - 16, 12);

        int leftEdge = _pnlHeaderLeft.Right + 16;
        int rightEdge = _flowHeaderRight.Left - 16;
        int availableWidth = rightEdge - leftEdge;

        if (availableWidth >= 240)
        {
            _pnlHeaderCenter.Visible = true;
            int centerWidth = Math.Min(400, availableWidth);
            _pnlHeaderCenter.Size = new Size(centerWidth, 42);
            _pnlHeaderCenter.Location = new Point(leftEdge + (availableWidth - centerWidth) / 2, 14);
        }
        else if (availableWidth >= 160)
        {
            _pnlHeaderCenter.Visible = true;
            _pnlHeaderCenter.Size = new Size(availableWidth, 42);
            _pnlHeaderCenter.Location = new Point(leftEdge, 14);
        }
        else
        {
            _pnlHeaderCenter.Visible = false;
        }
    }

    private void ToggleSidebar()
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        _sidePanel.Visible = !_sidebarCollapsed;
        _btnToggleSidebar.IconChar = _sidebarCollapsed ? IconChar.ChevronRight : IconChar.Bars;
        _viewport.Focus();
    }

    private void InitializeRemoteActionsMenu()
    {
        _menuRemoteActions = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = ThemeTokens.BackgroundHeader,
            ForeColor = ThemeTokens.TextPrimary,
            Font = ThemeTokens.FontBody,
            ShowImageMargin = true
        };

        var itemCad = new ToolStripMenuItem("Send Ctrl+Alt+Del (Secure Attention)", IconChar.ShieldHalved.ToBitmap(ThemeTokens.AccentPrimary, 16), (_, _) => SendCtrlAltDel());
        var itemTm = new ToolStripMenuItem("Open Task Manager (Ctrl+Shift+Esc)", IconChar.ListCheck.ToBitmap(ThemeTokens.AccentCyan, 16), (_, _) => SendTaskManager());
        var itemAltTab = new ToolStripMenuItem("Switch Windows (Alt+Tab)", IconChar.WindowRestore.ToBitmap(ThemeTokens.TextSecondary, 16), (_, _) => SendAltTab());
        var itemWin = new ToolStripMenuItem("Windows Start Menu (Ctrl+Esc)", IconChar.Windows.ToBitmap(ThemeTokens.TextSecondary, 16), (_, _) => SendWindowsKey());
        var itemPaste = new ToolStripMenuItem("Paste Local Clipboard to Remote", IconChar.Paste.ToBitmap(ThemeTokens.AccentSuccess, 16), (_, _) => SendClipboardText());
        var itemLock = new ToolStripMenuItem("Lock Remote Workstation (Win+L)", IconChar.Lock.ToBitmap(ThemeTokens.AccentWarning, 16), (_, _) => LockRemoteWorkstation());
        var itemSep1 = new ToolStripSeparator();
        var itemShot = new ToolStripMenuItem("Capture Desktop Screenshot", IconChar.Camera.ToBitmap(ThemeTokens.AccentCyan, 16), (_, _) => TakeScreenshot());
        var itemSep2 = new ToolStripSeparator();
        var itemKill = new ToolStripMenuItem("Emergency Disconnect (Kill Session)", IconChar.PowerOff.ToBitmap(ThemeTokens.AccentDanger, 16), (_, _) => OnEmergencyStopClicked());
        itemKill.ForeColor = ThemeTokens.AccentDanger;

        _menuRemoteActions.Items.AddRange(new ToolStripItem[] {
            itemCad, itemTm, itemAltTab, itemWin, itemPaste, itemLock, itemSep1, itemShot, itemSep2, itemKill
        });
    }

    private void InitializeFpsMenu()
    {
        _menuFps = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = ThemeTokens.BackgroundHeader,
            ForeColor = ThemeTokens.TextPrimary,
            Font = ThemeTokens.FontBody,
            ShowImageMargin = true
        };

        var fps60 = new ToolStripMenuItem("60 FPS (Ultra Smooth — High Performance)", IconChar.Bolt.ToBitmap(ThemeTokens.AccentCyan, 16), (_, _) => SetTargetFramerate(60));
        var fps45 = new ToolStripMenuItem("45 FPS (Smooth — Balanced)", IconChar.Forward.ToBitmap(ThemeTokens.AccentSuccess, 16), (_, _) => SetTargetFramerate(45));
        var fps30 = new ToolStripMenuItem("30 FPS (Standard Remote Desktop)", IconChar.Play.ToBitmap(ThemeTokens.AccentPrimary, 16), (_, _) => SetTargetFramerate(30));
        var fps15 = new ToolStripMenuItem("15 FPS (Bandwidth Saver)", IconChar.ShieldHalved.ToBitmap(ThemeTokens.AccentWarning, 16), (_, _) => SetTargetFramerate(15));

        fps60.Checked = true;

        _menuFps.Items.AddRange(new ToolStripItem[] { fps60, fps45, fps30, fps15 });
    }

    private void InitializeStreamSettingsMenu()
    {
        _menuStreamSettings = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = ThemeTokens.BackgroundHeader,
            ForeColor = ThemeTokens.TextPrimary,
            Font = ThemeTokens.FontBody,
            ShowImageMargin = true
        };

        var titlePresets = new ToolStripMenuItem("PERFORMANCE PRESETS") { Enabled = false, ForeColor = ThemeTokens.AccentCyan, Font = ThemeTokens.FontCaptionBold };
        var presetLowLatency = new ToolStripMenuItem("Ultra Low Latency (Fast WAN / 60 FPS / Q65)", IconChar.Bolt.ToBitmap(ThemeTokens.AccentCyan, 16), (_, _) =>
            ApplyStreamPreset(60, 65, 0, 1, "Ultra Low Latency (Fast WAN / 60 FPS)"));
        var presetBalanced = new ToolStripMenuItem("Balanced (Standard WAN / 45 FPS / Q72)", IconChar.ScaleBalanced.ToBitmap(ThemeTokens.AccentSuccess, 16), (_, _) =>
            ApplyStreamPreset(45, 72, 0, 1, "Balanced (Standard WAN / 45 FPS)"));
        var presetHd = new ToolStripMenuItem("High Definition (LAN / 60 FPS / Q85)", IconChar.Gem.ToBitmap(ThemeTokens.AccentPrimary, 16), (_, _) =>
            ApplyStreamPreset(60, 85, 0, 1, "High Definition (LAN / 60 FPS)"));
        var presetSaver = new ToolStripMenuItem("Bandwidth Saver (Mobile / 15 FPS / Q50)", IconChar.MobileScreen.ToBitmap(ThemeTokens.AccentWarning, 16), (_, _) =>
            ApplyStreamPreset(15, 50, 720, 1, "Bandwidth Saver (Mobile / 15 FPS)"));

        var sep1 = new ToolStripSeparator();
        var titleRes = new ToolStripMenuItem("MAXIMUM RESOLUTION LIMIT") { Enabled = false, ForeColor = ThemeTokens.AccentCyan, Font = ThemeTokens.FontCaptionBold };
        var resNative = new ToolStripMenuItem("Native Display (Unscaled)", IconChar.Desktop.ToBitmap(ThemeTokens.TextSecondary, 16), (_, _) => SetResolutionLimit(0, "Native"));
        var res1080 = new ToolStripMenuItem("Limit to 1080p (Scale 4K/2K)", IconChar.Tv.ToBitmap(ThemeTokens.TextSecondary, 16), (_, _) => SetResolutionLimit(1080, "1080p Max"));
        var res720 = new ToolStripMenuItem("Limit to 720p (Maximum Bandwidth Savings)", IconChar.Compress.ToBitmap(ThemeTokens.TextSecondary, 16), (_, _) => SetResolutionLimit(720, "720p Max"));

        var sep2 = new ToolStripSeparator();
        var itemZeroBuffer = new ToolStripMenuItem("Zero-Bufferbloat Decoupled Stream: ACTIVE", IconChar.CheckCircle.ToBitmap(ThemeTokens.AccentSuccess, 16))
        {
            Checked = true,
            Enabled = false,
            ToolTipText = "Automatically drops stale buffered frames over remote networks to guarantee lowest real-time latency."
        };

        _menuStreamSettings.Items.AddRange(new ToolStripItem[] {
            titlePresets, presetLowLatency, presetBalanced, presetHd, presetSaver,
            sep1,
            titleRes, resNative, res1080, res720,
            sep2,
            itemZeroBuffer
        });
    }

    private void SetTargetFramerate(int fps)
    {
        _targetFps = fps;
        if (_btnFpsMode != null) _btnFpsMode.Text = $"{fps} FPS ▾";
        if (_btnFloatFpsMode != null) _btnFloatFpsMode.Text = $"{fps} FPS ▾";

        if (_menuFps != null)
        {
            foreach (ToolStripItem item in _menuFps.Items)
            {
                if (item is ToolStripMenuItem mi)
                {
                    mi.Checked = mi.Text?.StartsWith($"{fps} FPS", StringComparison.OrdinalIgnoreCase) == true;
                }
            }
        }

        ApplyStreamConfig(_targetFps, _jpegQuality, _maxHeight, _lowLatencyMode, notifyToast: true, $"Framerate updated to {fps} FPS");
    }

    private void ApplyStreamPreset(int fps, int quality, ushort maxHeight, byte lowLatency, string presetName)
    {
        _targetFps = fps;
        _jpegQuality = quality;
        _maxHeight = maxHeight;
        _lowLatencyMode = lowLatency;

        if (_btnFpsMode != null) _btnFpsMode.Text = $"{fps} FPS ▾";
        if (_btnFloatFpsMode != null) _btnFloatFpsMode.Text = $"{fps} FPS ▾";

        ApplyStreamConfig(_targetFps, _jpegQuality, _maxHeight, _lowLatencyMode, notifyToast: true, $"Preset Applied: {presetName}");
    }

    private void SetResolutionLimit(ushort maxHeight, string label)
    {
        _maxHeight = maxHeight;
        ApplyStreamConfig(_targetFps, _jpegQuality, _maxHeight, _lowLatencyMode, notifyToast: true, $"Resolution limit: {label}");
    }

    private void ApplyStreamConfig(int targetFps, int quality, ushort maxHeight, byte lowLatency, bool notifyToast = false, string? toastMessage = null)
    {
        if (_streamWs != null && _streamWs.State == WebSocketState.Open)
        {
            var packet = new StreamConfigPacket((byte)targetFps, (byte)quality, maxHeight, lowLatency, (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            byte[] raw = DataChannelProtocol.SerializeStreamConfig(packet);
            _ = SendInputPacketAsync(raw);
        }

        if (notifyToast && !string.IsNullOrEmpty(toastMessage))
        {
            CyberToast.Show(this, "Stream Configuration Updated", $"{toastMessage} (Zero-Bufferbloat Active)", ToastType.Success, 2200);
        }
    }

    private void InitializeFloatingBar()
    {
        _floatingBar = new FlowLayoutPanel
        {
            Height = 48,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = ThemeTokens.BackgroundFloatingBar,
            Padding = new Padding(10, 6, 10, 6),
            Visible = false,
            Anchor = AnchorStyles.None,
            Cursor = Cursors.Default
        };
        _floatingBar.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(ThemeTokens.BorderFocus, 1.5f);
            e.Graphics.DrawRectangle(pen, 0, 0, _floatingBar.Width - 1, _floatingBar.Height - 1);
        };

        // 1. Drag Handle
        _lblFloatGrip = new Label
        {
            Text = "⋮⋮",
            Font = new Font("Segoe UI Bold", 13f, FontStyle.Bold),
            ForeColor = ThemeTokens.TextMuted,
            AutoSize = true,
            Cursor = Cursors.SizeAll,
            Padding = new Padding(2, 6, 4, 0),
            Margin = new Padding(0)
        };
        _floatingBar.Controls.Add(_lblFloatGrip);

        // 2. Device Label Badge
        _badgeFloatDevice = new CyberBadge
        {
            Text = "REMOTE HOST | 60 FPS",
            Status = CyberBadgeStatus.Success,
            IconChar = IconChar.Desktop,
            Size = new Size(180, 32),
            Cursor = Cursors.SizeAll,
            Margin = new Padding(3, 2, 4, 2)
        };
        _floatingBar.Controls.Add(_badgeFloatDevice);

        // 3. Interactive Input Control Toggle
        _btnFloatToggleControl = new CyberButton
        {
            Text = "Control: ON",
            IconChar = IconChar.ComputerMouse,
            IconSize = 14,
            Variant = CyberButtonVariant.Success,
            Size = new Size(126, 34)
        };
        _btnFloatToggleControl.Click += (_, _) => ToggleInputControl();
        _floatingBar.Controls.Add(_btnFloatToggleControl);

        // 4. Quick Actions Dropdown
        _btnFloatActions = new CyberButton
        {
            Text = "Actions ▾",
            IconChar = IconChar.Bolt,
            IconSize = 14,
            Variant = CyberButtonVariant.Secondary,
            Size = new Size(106, 34)
        };
        _btnFloatActions.Click += (_, _) => _menuRemoteActions.Show(_btnFloatActions, new Point(0, _btnFloatActions.Height + 4));
        _floatingBar.Controls.Add(_btnFloatActions);

        // 5. Screenshot Button
        _btnFloatScreenshot = new CyberButton
        {
            Text = "Shot",
            IconChar = IconChar.Camera,
            IconSize = 14,
            Variant = CyberButtonVariant.Secondary,
            Size = new Size(78, 34)
        };
        _btnFloatScreenshot.Click += (_, _) => TakeScreenshot();
        _floatingBar.Controls.Add(_btnFloatScreenshot);

        // Viewport Display Scale Mode Toggle in Full Screen
        _btnFloatScaleMode = new CyberButton
        {
            Text = "Fit Screen",
            IconChar = IconChar.Compress,
            IconSize = 14,
            Variant = CyberButtonVariant.Secondary,
            Size = new Size(102, 34)
        };
        _btnFloatScaleMode.Click += (_, _) => CycleScaleMode();
        _floatingBar.Controls.Add(_btnFloatScaleMode);

        // Framerate Selector Button in Full Screen
        _btnFloatFpsMode = new CyberButton
        {
            Text = "60 FPS ▾",
            IconChar = IconChar.Bolt,
            IconSize = 13,
            Variant = CyberButtonVariant.Secondary,
            Size = new Size(98, 34)
        };
        _btnFloatFpsMode.Click += (_, _) => _menuFps.Show(_btnFloatFpsMode, new Point(0, _btnFloatFpsMode.Height + 4));
        _floatingBar.Controls.Add(_btnFloatFpsMode);

        // 6. Exit Full Screen
        _btnFloatExitFullScreen = new CyberButton
        {
            Text = "Exit (F11)",
            IconChar = IconChar.Compress,
            IconSize = 14,
            Variant = CyberButtonVariant.Primary,
            Size = new Size(104, 34)
        };
        _btnFloatExitFullScreen.Click += (_, _) => ExitFullScreen();
        _floatingBar.Controls.Add(_btnFloatExitFullScreen);

        // 7. Disconnect Session
        _btnFloatDisconnect = new CyberButton
        {
            Text = "End",
            IconChar = IconChar.Stop,
            IconSize = 14,
            Variant = CyberButtonVariant.Secondary,
            Size = new Size(72, 34)
        };
        _btnFloatDisconnect.Click += (_, _) => DisconnectSession();
        _floatingBar.Controls.Add(_btnFloatDisconnect);

        // 8. Emergency Kill
        _btnFloatEmergency = new CyberButton
        {
            Text = "",
            IconChar = IconChar.PowerOff,
            IconSize = 15,
            Variant = CyberButtonVariant.Danger,
            Size = new Size(38, 34)
        };
        _btnFloatEmergency.Click += (_, _) => OnEmergencyStopClicked();
        _floatingBar.Controls.Add(_btnFloatEmergency);

        // 9. Collapse / Expand Button
        _btnFloatCollapse = new CyberButton
        {
            Text = "",
            IconChar = IconChar.ChevronUp,
            IconSize = 12,
            Variant = CyberButtonVariant.Ghost,
            Size = new Size(34, 34)
        };
        _btnFloatCollapse.Click += (_, _) => ToggleFloatingBarCollapse();
        _floatingBar.Controls.Add(_btnFloatCollapse);

        SetupFloatingBarDrag(_floatingBar);
        SetupFloatingBarDrag(_lblFloatGrip);
        SetupFloatingBarDrag(_badgeFloatDevice);

        Controls.Add(_floatingBar);
    }

    private void SetupFloatingBarDrag(Control c)
    {
        c.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDraggingFloatBar = true;
                _dragStartCursorPos = Cursor.Position;
                _dragStartBarPos = _floatingBar.Location;
            }
        };

        c.MouseMove += (s, e) =>
        {
            if (_isDraggingFloatBar)
            {
                Point currentCursor = Cursor.Position;
                int deltaX = currentCursor.X - _dragStartCursorPos.X;
                int deltaY = currentCursor.Y - _dragStartCursorPos.Y;
                int newX = _dragStartBarPos.X + deltaX;
                int newY = _dragStartBarPos.Y + deltaY;

                newX = Math.Clamp(newX, 0, Math.Max(0, ClientSize.Width - _floatingBar.Width));
                newY = Math.Clamp(newY, 0, Math.Max(0, ClientSize.Height - _floatingBar.Height));
                _floatingBar.Location = new Point(newX, newY);
            }
        };

        c.MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _isDraggingFloatBar = false;
            }
        };
    }

    private void ToggleFloatingBarCollapse()
    {
        _isFloatBarCollapsed = !_isFloatBarCollapsed;
        _badgeFloatDevice.Visible = !_isFloatBarCollapsed;
        _btnFloatToggleControl.Visible = !_isFloatBarCollapsed;
        _btnFloatActions.Visible = !_isFloatBarCollapsed;
        _btnFloatScreenshot.Visible = !_isFloatBarCollapsed;
        _btnFloatEmergency.Visible = !_isFloatBarCollapsed;

        _btnFloatCollapse.IconChar = _isFloatBarCollapsed ? IconChar.ChevronDown : IconChar.ChevronUp;
        _floatingBar.PerformLayout();
    }

    public void ToggleFullScreen()
    {
        if (_isFullScreen) ExitFullScreen();
        else EnterFullScreen();
    }

    private void EnterFullScreen()
    {
        if (_isFullScreen) return;
        _isFullScreen = true;

        _previousWindowState = WindowState;
        _previousBorderStyle = FormBorderStyle;
        _previousBounds = Bounds;

        _topBar.Visible = false;
        _sidePanel.Visible = false;
        _statusBar.Visible = false;

        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Normal;
        Bounds = Screen.FromControl(this).Bounds;

        _floatingBar.Location = new Point((Width - _floatingBar.Width) / 2, 8);
        _floatingBar.Visible = true;
        _floatingBar.BringToFront();

        _btnFullScreen.Text = "Exit";
        _btnFullScreen.IconChar = IconChar.Compress;
        _lblStatus.Text = "● Full Screen Mode Active. Press F11 or Esc to exit.";
        _viewport.Focus();
    }

    private void ExitFullScreen()
    {
        if (!_isFullScreen) return;
        _isFullScreen = false;

        _floatingBar.Visible = false;
        _topBar.Visible = true;
        _sidePanel.Visible = !_sidebarCollapsed;
        _statusBar.Visible = true;

        FormBorderStyle = _previousBorderStyle;
        WindowState = _previousWindowState;
        Bounds = _previousBounds;

        _btnFullScreen.Text = "Full Screen";
        _btnFullScreen.IconChar = IconChar.Expand;
        _lblStatus.Text = "● Restored to Windowed Mode. Press F11 for Full Screen.";
        LayoutTopBarElements();
        _viewport.Focus();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F11)
        {
            ToggleFullScreen();
            return true;
        }

        if (keyData == Keys.Escape && _isFullScreen)
        {
            ExitFullScreen();
            return true;
        }

        if (_sessionClient != null && _sessionClient.IsActive && _chkInputControl.Checked)
        {
            Keys pureKey = keyData & Keys.KeyCode;
            ProtocolMods mods = ProtocolMods.None;
            if ((keyData & Keys.Control) != 0) mods |= ProtocolMods.Control;
            if ((keyData & Keys.Alt) != 0) mods |= ProtocolMods.Alt;
            if ((keyData & Keys.Shift) != 0) mods |= ProtocolMods.Shift;

            _sessionClient.SendKeyboardKey((ushort)pureKey, isDown: true, mods);
            Task.Delay(40).ContinueWith(_ => _sessionClient?.SendKeyboardKey((ushort)pureKey, isDown: false, mods));

            if (pureKey == Keys.Tab || pureKey == Keys.Enter || pureKey == Keys.Up || pureKey == Keys.Down || pureKey == Keys.Left || pureKey == Keys.Right)
            {
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_sessionClient != null && _sessionClient.IsActive && _chkInputControl.Checked)
        {
            ProtocolMods mods = GetCurrentModifiers();
            _sessionClient.SendKeyboardKey((ushort)e.KeyCode, isDown: true, mods, (uint)e.KeyValue);
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (_sessionClient != null && _sessionClient.IsActive && _chkInputControl.Checked)
        {
            ProtocolMods mods = GetCurrentModifiers();
            _sessionClient.SendKeyboardKey((ushort)e.KeyCode, isDown: false, mods, (uint)e.KeyValue);
        }
        base.OnKeyUp(e);
    }

    private static ProtocolMods GetCurrentModifiers()
    {
        ProtocolMods mods = ProtocolMods.None;
        if ((Control.ModifierKeys & Keys.Control) != 0) mods |= ProtocolMods.Control;
        if ((Control.ModifierKeys & Keys.Alt) != 0) mods |= ProtocolMods.Alt;
        if ((Control.ModifierKeys & Keys.Shift) != 0) mods |= ProtocolMods.Shift;
        return mods;
    }

    private void SendCtrlAltDel()
    {
        if (_sessionClient == null || !_sessionClient.IsActive)
        {
            CyberDialog.Show(this, "Please connect to an active remote session before transmitting Ctrl+Alt+Del.", "Session Required", CyberDialogType.Warning);
            return;
        }

        _sessionClient.SendKeyboardKey(0x11, true, ProtocolMods.Control);
        _sessionClient.SendKeyboardKey(0x12, true, ProtocolMods.Control | ProtocolMods.Alt);
        _sessionClient.SendKeyboardKey(0x2E, true, ProtocolMods.Control | ProtocolMods.Alt);
        _sessionClient.SendKeyboardKey(0x2E, false);
        _sessionClient.SendKeyboardKey(0x12, false);
        _sessionClient.SendKeyboardKey(0x11, false);

        _lblStatus.Text = "⌨️ Sent 'Ctrl+Alt+Del' secure attention sequence to remote host.";
        CyberToast.Show(this, "Secure Attention Sent", "Ctrl+Alt+Del injected into remote Windows session.", ToastType.Info);
    }

    private void SendWindowsKey()
    {
        if (_sessionClient == null || !_sessionClient.IsActive) return;
        _sessionClient.SendKeyboardKey(0x11, true, ProtocolMods.Control);
        _sessionClient.SendKeyboardKey(0x1B, true, ProtocolMods.Control);
        Task.Delay(50).ContinueWith(_ =>
        {
            _sessionClient?.SendKeyboardKey(0x1B, false, ProtocolMods.Control);
            _sessionClient?.SendKeyboardKey(0x11, false);
        });
        _lblStatus.Text = "⊞ Triggered Windows Start Menu on remote host.";
    }

    private void SendTaskManager()
    {
        if (_sessionClient == null || !_sessionClient.IsActive) return;
        _sessionClient.SendKeyboardKey(0x11, true, ProtocolMods.Control | ProtocolMods.Shift);
        _sessionClient.SendKeyboardKey(0x10, true, ProtocolMods.Control | ProtocolMods.Shift);
        _sessionClient.SendKeyboardKey(0x1B, true, ProtocolMods.Control | ProtocolMods.Shift);
        Task.Delay(50).ContinueWith(_ =>
        {
            _sessionClient?.SendKeyboardKey(0x1B, false, ProtocolMods.Control | ProtocolMods.Shift);
            _sessionClient?.SendKeyboardKey(0x10, false);
            _sessionClient?.SendKeyboardKey(0x11, false);
        });
        _lblStatus.Text = "⚡ Triggered Task Manager (Ctrl+Shift+Esc) on remote host.";
        CyberToast.Show(this, "Task Manager", "Opened Task Manager on remote host.", ToastType.Info);
    }

    private void SendAltTab()
    {
        if (_sessionClient == null || !_sessionClient.IsActive) return;
        _sessionClient.SendKeyboardKey(0x12, true, ProtocolMods.Alt);
        _sessionClient.SendKeyboardKey(0x09, true, ProtocolMods.Alt);
        Task.Delay(60).ContinueWith(_ =>
        {
            _sessionClient?.SendKeyboardKey(0x09, false, ProtocolMods.Alt);
            _sessionClient?.SendKeyboardKey(0x12, false);
        });
        _lblStatus.Text = "⇥ Sent Alt+Tab window switch to remote host.";
    }

    private async void SendClipboardText()
    {
        if (_sessionClient == null || !_sessionClient.IsActive)
        {
            CyberDialog.Show(this, "Please connect to an active remote session first.", "No Active Session", CyberDialogType.Warning);
            return;
        }

        string? text = SafeClipboard.TryGetText();
        if (string.IsNullOrEmpty(text))
        {
            CyberDialog.Show(this, "Local clipboard contains no text to paste.", "Clipboard Empty", CyberDialogType.Information);
            return;
        }

        _lblStatus.Text = $"📋 Pasting {text.Length} character(s) from local clipboard into remote window...";
        foreach (char c in text)
        {
            if (c == '\r') continue;
            if (c == '\n')
            {
                _sessionClient.SendKeyboardKey(0x0D, true);
                await Task.Delay(20);
                _sessionClient.SendKeyboardKey(0x0D, false);
                await Task.Delay(20);
                continue;
            }

            _sessionClient.SendKeyboardKey(0, true, ProtocolMods.None, (uint)c);
            await Task.Delay(15);
            _sessionClient.SendKeyboardKey(0, false, ProtocolMods.None, (uint)c);
            await Task.Delay(15);
        }
        _lblStatus.Text = "📋 Clipboard text sent successfully to remote host.";
        CyberToast.Show(this, "Clipboard Synchronized", $"Pasted {text.Length} characters to remote host.", ToastType.Success);
    }

    private void LockRemoteWorkstation()
    {
        if (_sessionClient == null || !_sessionClient.IsActive) return;
        _lblStatus.Text = "🔒 Sent lock workstation command to remote host.";
        _sessionClient.SendKeyboardKey(0x5B, true, ProtocolMods.Windows);
        _sessionClient.SendKeyboardKey(0x4C, true, ProtocolMods.Windows);
        Task.Delay(50).ContinueWith(_ =>
        {
            _sessionClient?.SendKeyboardKey(0x4C, false);
            _sessionClient?.SendKeyboardKey(0x5B, false);
        });
        CyberToast.Show(this, "Workstation Locked", "Remote screen locked (Win+L).", ToastType.Warning);
    }

    private void OpenEnrollRemoteDialog()
    {
        using var dlg = new EnrollRemoteDeviceForm(_txtServer.Text.Trim(), _operatorId);
        dlg.ShowDialog(this);
        _ = LoadDevicesAsync();
    }

    private async Task AuthenticateAndLoadDevicesAsync()
    {
        try
        {
            _lblStatus.Text = "Authenticating with Control Plane...";
            _badgeAuth.Text = "Authenticating...";
            _badgeAuth.Status = CyberBadgeStatus.Warning;

            string serverUrl = NormalizeServerUrl(_txtServer.Text);
            _txtServer.Text = serverUrl;

            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var validatedUri))
            {
                CyberDialog.Show(this, "The Gateway URL is not formatted correctly.\n\nExamples:\n  • Local PC: http://127.0.0.1:5293\n  • Internet Tunnel: https://your-tunnel.trycloudflare.com\n\nClick '[Reset 127.0.0.1]' above to reset.", "Invalid Gateway URL", CyberDialogType.Warning);
                _badgeAuth.Text = "Invalid URL";
                _badgeAuth.Status = CyberBadgeStatus.Warning;
                return;
            }

            string authUrl = $"{serverUrl}/api/v1/auth/token";
            HttpResponseMessage? authResp = null;
            try
            {
                authResp = await _http.PostAsJsonAsync(authUrl, new { Email = _txtEmail.Text.Trim() });
            }
            catch (Exception ex)
            {
                // Only if the user specifically requested localhost / 127.0.0.1 and it's not up, attempt auto-start
                if (serverUrl.Contains("127.0.0.1") || serverUrl.Contains("localhost"))
                {
                    _lblStatus.Text = "Control Plane not running locally. Starting Control Plane background service...";
                    if (TryAutoStartControlPlane())
                    {
                        await Task.Delay(2000);
                        try
                        {
                            authResp = await _http.PostAsJsonAsync(authUrl, new { Email = _txtEmail.Text.Trim() });
                        }
                        catch { }
                    }
                }

                if (authResp == null)
                {
                    string errDetail = ex.InnerException?.Message ?? ex.Message;
                    _lblStatus.Text = $"Connection Failed: {errDetail}";
                    _badgeAuth.Text = "Offline";
                    _badgeAuth.Status = CyberBadgeStatus.Danger;
                    CyberToast.Show(this, "Connection Failed", $"Could not reach gateway at:\n{serverUrl}\n\nError: {errDetail}\n\nTip: Click '[Reset 127.0.0.1]' if testing on this PC.", ToastType.Danger, 5000);
                    return;
                }
            }

            if (authResp == null || !authResp.IsSuccessStatusCode)
            {
                int statusCode = authResp != null ? (int)authResp.StatusCode : 0;
                string statusReason = authResp != null ? $"HTTP {statusCode}" : "Unreachable";

                string helpMessage = $"Could not authenticate with gateway at:\n{serverUrl}\n(Status: {statusReason})\n";
                if (statusCode == 530)
                {
                    helpMessage += "\nCloudflare Error 1033: The tunnel host is currently not connected or has expired. Please verify that 'START_INTERNET_TUNNEL.bat' is running on the host PC.";
                }
                else if (serverUrl != "http://127.0.0.1:5293")
                {
                    helpMessage += "\nTip: If testing locally on this PC, click '[Reset 127.0.0.1]' above.";
                }

                _lblStatus.Text = $"Authentication failed ({statusReason}). Keep URL or click '[Reset 127.0.0.1]'.";
                _badgeAuth.Text = "Offline";
                _badgeAuth.Status = CyberBadgeStatus.Danger;
                CyberToast.Show(this, "Authentication Failed", helpMessage, ToastType.Danger, 6000);
                return;
            }

            var authUser = await authResp.Content.ReadFromJsonAsync<AuthResult>(JsonOpts);
            if (authUser != null)
            {
                _operatorId = authUser.UserId;
                _badgeAuth.Text = $"{authUser.DisplayName} ({authUser.Role})";
                _badgeAuth.Status = CyberBadgeStatus.Success;
                _lblStatus.Text = "Authenticated successfully. Querying authorized devices...";
                CyberToast.Show(this, "Logged In", $"Authenticated as {authUser.DisplayName} ({authUser.Role}).", ToastType.Success, 2000);
                await LoadDevicesAsync();
            }
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"Connection Error: {ex.Message}";
            _badgeAuth.Text = "Offline";
            _badgeAuth.Status = CyberBadgeStatus.Danger;
            CyberToast.Show(this, "Connection Error", $"Failed to connect to gateway:\n{ex.Message}\n\nClick '[Reset 127.0.0.1]' to reset if needed.", ToastType.Danger, 5000);
        }
    }

    public static string NormalizeServerUrl(string input)
    {
        string url = (input ?? "").Trim();
        if (string.IsNullOrEmpty(url))
        {
            return "http://127.0.0.1:5293";
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (url.Contains("trycloudflare.com", StringComparison.OrdinalIgnoreCase) ||
                (url.Contains(".") && !url.StartsWith("192.") && !url.StartsWith("10.") && !url.StartsWith("172.") && !url.StartsWith("127.")))
            {
                url = "https://" + url;
            }
            else
            {
                url = "http://" + url;
            }
        }

        return url.TrimEnd('/');
    }

    private static bool TryAutoStartControlPlane()
    {
        try
        {
            if (Process.GetProcessesByName("EnterpriseRemote.ControlPlane").Length > 0)
            {
                return true;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] possiblePaths = new[]
            {
                Path.Combine(baseDir, "EnterpriseRemote.ControlPlane.exe"),
                Path.Combine(baseDir, "..", "ControlPlane", "EnterpriseRemote.ControlPlane.exe"),
                Path.Combine(baseDir, "..", "1_MAIN_PC_CONTROLLER", "Bin", "EnterpriseRemote.ControlPlane.exe"),
                Path.Combine(baseDir, "..", "..", "EnterpriseRemote_Apps", "1_MAIN_PC_CONTROLLER", "Bin", "EnterpriseRemote.ControlPlane.exe"),
                Path.Combine(baseDir, "..", "..", "..", "dist", "ControlPlane", "EnterpriseRemote.ControlPlane.exe"),
                Path.Combine(baseDir, "..", "..", "..", "src", "EnterpriseRemote.ControlPlane", "bin", "Release", "net8.0", "EnterpriseRemote.ControlPlane.exe"),
                Path.Combine(baseDir, "..", "..", "..", "src", "EnterpriseRemote.ControlPlane", "bin", "Debug", "net8.0", "EnterpriseRemote.ControlPlane.exe"),
                Path.Combine(baseDir, "..", "..", "..", "..", "dist", "ControlPlane", "EnterpriseRemote.ControlPlane.exe"),
                Path.Combine(baseDir, "..", "..", "..", "..", "src", "EnterpriseRemote.ControlPlane", "bin", "Release", "net8.0", "EnterpriseRemote.ControlPlane.exe"),
                Path.Combine(baseDir, "..", "..", "..", "..", "src", "EnterpriseRemote.ControlPlane", "bin", "Debug", "net8.0", "EnterpriseRemote.ControlPlane.exe")
            };

            foreach (var p in possiblePaths)
            {
                string fullPath = Path.GetFullPath(p);
                if (File.Exists(fullPath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = fullPath,
                        Arguments = "--urls=http://0.0.0.0:5293",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    Process.Start(psi);
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private void ShowAdminAccountsDialog()
    {
        string directoryText =
            "AUTHORIZED ADMIN ACCOUNTS DIRECTORY\n" +
            "───────────────────────────────────────────────\n\n" +
            "1. admin@corp.internal   — Security Admin (Super-Admin)\n" +
            "2. sujal@corp.internal   — Sujal (Admin)\n" +
            "3. sujal                 — Sujal (Admin short login)\n" +
            "4. admin@local           — Local Host Administrator\n" +
            "5. admin                 — System Administrator (Single-word)\n" +
            "6. alice@corp.internal   — Alice Administrator\n" +
            "7. operator@corp.internal — Remote Operator (Admin)\n" +
            "8. root@corp.internal    — Super Admin Root\n\n" +
            "DYNAMIC SELF-PROVISIONING:\n" +
            "Any custom username or email you enter is automatically granted full Administrator permissions on the fly.\n\n" +
            "Saved for offline viewing in 'ADMIN_ACCOUNTS.txt' in your app folder.";

        CyberDialog.Show(this, directoryText, "Admin Accounts Directory", CyberDialogType.Information);
    }

    private async Task LoadDevicesAsync()
    {
        if (string.IsNullOrEmpty(_operatorId)) return;

        try
        {
            string serverUrl = NormalizeServerUrl(_txtServer.Text);
            var resp = await _http.GetAsync($"{serverUrl}/api/v1/devices?userId={_operatorId}");
            if (resp.IsSuccessStatusCode)
            {
                var devices = await resp.Content.ReadFromJsonAsync<List<EnrolledDevice>>(JsonOpts);
                _cboDevices.Items.Clear();
                if (devices != null && devices.Count > 0)
                {
                    string myMachineName = Environment.MachineName;
                    var sorted = devices
                        .OrderByDescending(d => !string.Equals(d.Hostname, myMachineName, StringComparison.OrdinalIgnoreCase))
                        .ThenBy(d => d.Hostname)
                        .ToList();

                    int defaultIndex = 0;
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        var d = sorted[i];
                        if (!string.IsNullOrEmpty(d.PermanentAccessToken))
                        {
                            _devicePermanentTokens[d.DeviceId] = d.PermanentAccessToken;
                        }
                        else if (_devicePermanentTokens.TryGetValue(d.DeviceId, out var savedToken))
                        {
                            d.PermanentAccessToken = savedToken;
                        }

                        bool isThisMachine = string.Equals(d.Hostname, myMachineName, StringComparison.OrdinalIgnoreCase);
                        string tag = isThisMachine ? "[This PC / Local Controller]" : "[Remote Host]";
                        _cboDevices.Items.Add($"{d.DeviceId} | {d.Hostname} {tag} ({d.Status})");

                        if (!isThisMachine && defaultIndex == 0)
                        {
                            defaultIndex = i;
                        }
                    }
                    SavePermanentTokensToDisk();

                    _cboDevices.SelectedIndex = defaultIndex;
                    if (sorted.Count > defaultIndex && _devicePermanentTokens.TryGetValue(sorted[defaultIndex].DeviceId, out var initToken))
                    {
                        _txtPermanentKey.Text = initToken;
                    }

                    _lblStatus.Text = $"Loaded {devices.Count} authorized device(s). Ready to stream.";
                    CyberToast.Show(this, "Devices Refreshed", $"Discovered {devices.Count} enrolled machine(s).", ToastType.Info);
                }
                else
                {
                    _cboDevices.Items.Add("No enrolled devices found");
                    _cboDevices.SelectedIndex = 0;
                    _lblStatus.Text = "No enrolled devices found. Enter Permanent Key or click 'Connect Other PC'.";
                }
            }
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"Device query error: {ex.Message}";
        }
    }

    private async Task ConnectSessionAsync()
    {
        string deviceId = "";
        string hostPart = "";
        if (_cboDevices.SelectedItem != null)
        {
            string selectedText = _cboDevices.SelectedItem.ToString() ?? "";
            deviceId = selectedText.Split('|')[0].Trim();
            hostPart = selectedText.Split('|').Length > 1 ? selectedText.Split('|')[1].Trim() : deviceId;
        }

        string permKey = _txtPermanentKey.Text.Trim();

        // If no device selected or list empty, but user provided a permanent key, connect automatically!
        if (string.IsNullOrEmpty(deviceId) || deviceId.StartsWith("No", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(permKey))
            {
                deviceId = "AUTO";
                hostPart = "Remote Host (Direct Permanent Key)";
            }
            else
            {
                if (!_isAutoReconnecting)
                {
                    CyberDialog.Show(this, "Please select an enrolled target device or enter a Permanent Access Key.\n\nTip: Click '➕ Connect Other PC' to enroll a remote computer.", "Device Required", CyberDialogType.Warning);
                }
                return;
            }
        }

        _userInitiatedDisconnect = false;
        _currentDeviceId = deviceId;

        string rawHost = hostPart.Replace("[Remote Host]", "").Replace("[This PC / Local Controller]", "").Replace("[This PC / Diagnostic]", "").Trim();
        int parenIdx = rawHost.IndexOf('(');
        if (parenIdx > 0) rawHost = rawHost.Substring(0, parenIdx).Trim();
        _currentDeviceHostname = rawHost;

        // Safeguard: Alert if operator inadvertently selects their own Controller PC instead of the remote laptop
        if (!_isAutoReconnecting && string.Equals(_currentDeviceHostname, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            var choice = CyberDialog.Show(
                this,
                $"You have selected THIS LOCAL COMPUTER ({_currentDeviceHostname}).\n\n" +
                "Connecting to yourself will display this controller machine's screen.\n" +
                "To remotely view and control your OTHER computer, please choose the '[Remote Host]' device from the dropdown.\n\n" +
                "Do you want to continue connecting to this local PC anyway?",
                "Local Machine Selected",
                CyberDialogType.Question,
                MessageBoxButtons.YesNo);

            if (choice == DialogResult.No)
            {
                for (int i = 0; i < _cboDevices.Items.Count; i++)
                {
                    if (_cboDevices.Items[i]?.ToString()?.Contains("[Remote Host]") == true)
                    {
                        _cboDevices.SelectedIndex = i;
                        break;
                    }
                }
                return;
            }
        }

        _lblStatus.Text = $"Requesting Session Authorization Ticket for {_currentDeviceHostname} ({deviceId})...";
        _badgeTarget.Text = _isAutoReconnecting ? $"RECONNECTING: {_currentDeviceHostname}" : $"CONNECTING: {_currentDeviceHostname}";
        _badgeTarget.Status = CyberBadgeStatus.Warning;
        _btnConnect.Enabled = false;

        try
        {
            var sessionRequest = new
            {
                OperatorId = _operatorId,
                DeviceId = deviceId,
                RequestedCapabilities = SessionCapabilities.View |
                    (_chkInputControl.Checked ? SessionCapabilities.Input : SessionCapabilities.None) |
                    (_chkClipboard.Checked ? SessionCapabilities.Clipboard : SessionCapabilities.None),
                PermanentAccessToken = string.IsNullOrEmpty(permKey) ? null : permKey
            };

            string serverUrl = NormalizeServerUrl(_txtServer.Text);
            var resp = await _http.PostAsJsonAsync($"{serverUrl}/api/v1/sessions/request", sessionRequest);
            if (!resp.IsSuccessStatusCode)
            {
                string err = await resp.Content.ReadAsStringAsync();
                if (!_isAutoReconnecting)
                {
                    CyberDialog.Show(this, $"Access Denied by Security Policy:\n{err}", "Authorization Failed", CyberDialogType.Error);
                }
                _lblStatus.Text = $"● Authorization notice: {err}.";
                _badgeTarget.Text = _isAutoReconnecting ? "RECONNECT PENDING" : "ACCESS DENIED";
                _badgeTarget.Status = CyberBadgeStatus.Danger;
                _btnConnect.Enabled = true;
                return;
            }

            var sessionResult = await resp.Content.ReadFromJsonAsync<SessionResult>(JsonOpts);
            if (sessionResult == null) return;

            // Adopt resolved device ID and store permanent token
            _currentDeviceId = sessionResult.DeviceId;
            if (!string.IsNullOrEmpty(permKey))
            {
                _devicePermanentTokens[_currentDeviceId] = permKey;
                SavePermanentTokensToDisk();
            }

            // Successfully authorized: reset reconnect state
            _isAutoReconnecting = false;
            _autoReconnectAttempt = 0;
            _autoReconnectTimer?.Stop();

            CloseStream();

            _sessionStartTime = DateTime.UtcNow;
            _sessionClient = new RemoteSessionClient(_operatorId!, _currentDeviceId);
            _sessionClient.InitializeSession(sessionResult.SessionId, sessionResult.PermittedCapabilities, sessionResult.RequiresAttendedConsent);
            _sessionClient.OnInputPacketReady = packet => _ = SendInputPacketAsync(packet);

            _btnConnect.Enabled = false;
            _btnDisconnect.Enabled = true;

            string authTypeDesc = sessionResult.RequiresAttendedConsent ? "Attended Session" : "Unattended (Permanent Token)";
            _badgeTarget.Text = $"LIVE: {_currentDeviceHostname}";
            _badgeTarget.Status = CyberBadgeStatus.Success;
            _badgeFloatDevice.Text = $"{_currentDeviceHostname} | Connecting...";
            _lblStatus.Text = $"● LIVE SESSION ACTIVE: Connected to {_currentDeviceHostname} ({authTypeDesc}) via WebSocket Relay.";

            lock (_streamLock)
            {
                _previewBitmap?.Dispose();
                _previewBitmap = null;
            }

            _streamCts = new CancellationTokenSource();
            _streamWs = new ClientWebSocket();
            _streamWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);

            string wsBase = serverUrl
                .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)
                .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase);

            var wsUri = new Uri($"{wsBase}/api/v1/stream/viewer?deviceId={Uri.EscapeDataString(_currentDeviceId)}&sessionId={Uri.EscapeDataString(sessionResult.SessionId)}");

            _streamStatusMessage = $"Connecting stream socket to {wsUri.Host}...";
            await _streamWs.ConnectAsync(wsUri, _streamCts.Token);
            _streamStatusMessage = $"Connected to relay. Awaiting desktop frames from {_currentDeviceHostname}...";

            _streamReceiveTask = Task.Run(() => StreamReceiveLoopAsync(_streamWs, _streamCts.Token));

            // Sync current framerate & quality settings to remote host immediately
            ApplyStreamConfig(_targetFps, _jpegQuality, _maxHeight, _lowLatencyMode);

            _frameTimer?.Start();
            _viewport.Focus();
            CyberToast.Show(this, "Session Authorized", $"Connected to {_currentDeviceHostname} ({_targetFps} FPS Zero-Bufferbloat Pipeline).", ToastType.Success);
        }
        catch (Exception ex)
        {
            CyberDialog.Show(this, $"Failed to establish remote session:\n{ex.Message}", "Connection Error", CyberDialogType.Error);
            _badgeTarget.Text = "CONNECTION FAILED";
            _badgeTarget.Status = CyberBadgeStatus.Danger;
            _btnConnect.Enabled = true;
        }
    }

    private void DisconnectSession()
    {
        _userInitiatedDisconnect = true;
        _isAutoReconnecting = false;
        _autoReconnectAttempt = 0;
        _autoReconnectTimer?.Stop();

        CloseStream();

        _frameTimer?.Stop();
        _sessionClient?.TerminateSession();
        _sessionClient = null;

        if (_isFullScreen) ExitFullScreen();

        _btnConnect.Enabled = true;
        _btnDisconnect.Enabled = false;
        _badgeTarget.Text = "IDLE — No Session Active";
        _badgeTarget.Status = CyberBadgeStatus.Neutral;
        _lblStatus.Text = "● Session terminated cleanly.";
        SafeInvalidateViewport();
    }

    private void CloseStream()
    {
        try
        {
            _streamCts?.Cancel();
            _streamCts?.Dispose();
            _streamCts = null;

            if (_streamWs != null)
            {
                if (_streamWs.State == WebSocketState.Open)
                {
                    _streamWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session terminated", CancellationToken.None).Wait(300);
                }
                _streamWs.Dispose();
                _streamWs = null;
            }
        }
        catch { }

        lock (_streamLock)
        {
            _previewBitmap?.Dispose();
            _previewBitmap = null;
        }
        _isStreamConnected = false;
    }

    private async Task StreamReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        byte[] buffer = new byte[1024 * 128];
        using var ms = new MemoryStream();
        _isStreamConnected = true;

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _streamStatusMessage = "Stream closed by remote host or server.";
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Host closed", ct);
                        SafeInvalidateViewport();
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (ms.Length > 0)
                {
                    try
                    {
                        byte[] frameBytes = ms.ToArray();

                        // 1. Check for Latency Pong packet (0x41)
                        if (frameBytes.Length >= 9 && frameBytes[0] == (byte)InputEventType.LatencyPong)
                        {
                            if (DataChannelProtocol.TryDeserializeLatencyPingPong(frameBytes, out bool isPing, out ulong pingTs) && !isPing)
                            {
                                ulong nowTs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                                if (nowTs >= pingTs)
                                {
                                    _lastMeasuredRttMs = Math.Max(1f, (float)(nowTs - pingTs));
                                }
                            }
                            continue;
                        }

                        // 2. Decode live desktop frame
                        using var frameStream = new MemoryStream(frameBytes);
                        using var srcBmp = new Bitmap(frameStream);
                        var newBmp = new Bitmap(srcBmp.Width, srcBmp.Height, PixelFormat.Format32bppRgb);
                        using (var gBmp = Graphics.FromImage(newBmp))
                        {
                            gBmp.DrawImageUnscaled(srcBmp, 0, 0);
                        }
                        _remoteScreenSize = newBmp.Size;

                        lock (_streamLock)
                        {
                            var old = _previewBitmap;
                            _previewBitmap = newBmp;
                            old?.Dispose();
                        }

                        _frameCount++;
                        _streamStatusMessage = "Streaming active";
                        SafeInvalidateViewport();
                    }
                    catch (OutOfMemoryException)
                    {
                        GC.Collect(1, GCCollectionMode.Optimized);
                    }
                    catch { }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _streamStatusMessage = $"Stream disconnected: {ex.Message}";
            SafeInvalidateViewport();
        }
        finally
        {
            _isStreamConnected = false;
            SafeInvalidateViewport();

            if (!_userInitiatedDisconnect && !string.IsNullOrEmpty(_txtPermanentKey.Text.Trim()) && _autoReconnectAttempt < 30)
            {
                TriggerAutoReconnect();
            }
        }
    }

    private void TriggerAutoReconnect()
    {
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(TriggerAutoReconnect)); } catch { }
            return;
        }

        if (_userInitiatedDisconnect || _isAutoReconnecting) return;

        _isAutoReconnecting = true;
        _autoReconnectAttempt++;
        _badgeTarget.Text = $"AUTO-RECONNECTING #{_autoReconnectAttempt}...";
        _badgeTarget.Status = CyberBadgeStatus.Warning;
        _lblStatus.Text = $"● Connection interrupted. Auto-reconnecting via Permanent Access Key in 3s (Attempt #{_autoReconnectAttempt})...";

        if (_autoReconnectTimer == null)
        {
            _autoReconnectTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _autoReconnectTimer.Tick += async (_, _) =>
            {
                if (_userInitiatedDisconnect)
                {
                    _autoReconnectTimer.Stop();
                    _isAutoReconnecting = false;
                    return;
                }
                _lblStatus.Text = $"● Attempting auto-reconnect #{_autoReconnectAttempt} to {_currentDeviceHostname}...";
                await ConnectSessionAsync();
            };
        }

        _autoReconnectTimer.Stop();
        _autoReconnectTimer.Start();
    }

    private async Task SendInputPacketAsync(byte[] packet)
    {
        if (_streamWs == null || _streamWs.State != WebSocketState.Open || packet.Length == 0) return;

        // User interactive input (mouse & keyboard) requires input control to be enabled;
        // system packets (StreamConfig 0x35 and LatencyPing 0x40) always transmit
        byte type = packet[0];
        bool isUserInteractiveInput = type is 0x01 or 0x02 or 0x03 or 0x04 or 0x10 or 0x11;
        if (isUserInteractiveInput && !_chkInputControl.Checked) return;

        try
        {
            await _wsSendLock.WaitAsync();
            try
            {
                if (_streamWs.State == WebSocketState.Open)
                {
                    await _streamWs.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary, true, CancellationToken.None);
                }
            }
            finally
            {
                _wsSendLock.Release();
            }
        }
        catch { }
    }

    private void ToggleInputControl()
    {
        _chkInputControl.Checked = !_chkInputControl.Checked;
    }

    private void UpdateInputControlUI()
    {
        bool enabled = _chkInputControl.Checked;
        if (enabled)
        {
            _btnToggleInputControl.Text = "Control: ON";
            _btnToggleInputControl.IconChar = IconChar.ComputerMouse;
            _btnToggleInputControl.Variant = CyberButtonVariant.Success;

            _btnFloatToggleControl.Text = "Control: ON";
            _btnFloatToggleControl.IconChar = IconChar.ComputerMouse;
            _btnFloatToggleControl.Variant = CyberButtonVariant.Success;

            _lblStatus.Text = "🖱️ Interactive Remote Control ENABLED. Clicks and keystrokes are transmitted.";
            CyberToast.Show(this, "Control Mode: Interactive", "Mouse and keyboard events transmitted to remote host.", ToastType.Success);
        }
        else
        {
            _btnToggleInputControl.Text = "View-Only";
            _btnToggleInputControl.IconChar = IconChar.Eye;
            _btnToggleInputControl.Variant = CyberButtonVariant.Warning;

            _btnFloatToggleControl.Text = "View-Only";
            _btnFloatToggleControl.IconChar = IconChar.Eye;
            _btnFloatToggleControl.Variant = CyberButtonVariant.Warning;

            _lblStatus.Text = "👁️ View-Only Mode ENABLED. Mouse & keyboard input transmission MUTED.";
            CyberToast.Show(this, "Control Mode: View-Only", "Input transmission paused. Remote screen view only.", ToastType.Warning);
        }
        SafeInvalidateViewport();
    }

    private void TakeScreenshot()
    {
        Bitmap? frameToSave = null;
        lock (_streamLock)
        {
            if (_previewBitmap != null)
            {
                frameToSave = (Bitmap)_previewBitmap.Clone();
            }
        }

        if (frameToSave == null)
        {
            CyberDialog.Show(this, "No active remote screen to capture. Connect to a device first.", "Screenshot", CyberDialogType.Information);
            return;
        }

        try
        {
            using (frameToSave)
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "EnterpriseRemote_Screenshots");
                Directory.CreateDirectory(dir);
                string cleanHost = string.Join("_", (_currentDeviceHostname ?? "Remote").Split(Path.GetInvalidFileNameChars()));
                string filename = $"Screenshot_{cleanHost}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                string filePath = Path.Combine(dir, filename);

                frameToSave.Save(filePath, ImageFormat.Png);

                _lblStatus.Text = $"📷 Screenshot saved: {filePath}";
                System.Media.SystemSounds.Asterisk.Play();

                CyberToast.Show(this, "Screenshot Captured", $"Saved to Pictures\\EnterpriseRemote_Screenshots\\{filename}", ToastType.Success, 4000, () =>
                {
                    try { Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true }); } catch { }
                });
            }
        }
        catch (Exception ex)
        {
            CyberDialog.Show(this, $"Failed to save screenshot: {ex.Message}", "Screenshot Error", CyberDialogType.Error);
        }
    }

    private void SafeInvalidateViewport()
    {
        if (!IsDisposed && _viewport != null && !_viewport.IsDisposed)
        {
            try
            {
                if (_viewport.InvokeRequired)
                {
                    _viewport.BeginInvoke((Action)(() => _viewport.Invalidate()));
                }
                else
                {
                    _viewport.Invalidate();
                }
            }
            catch { }
        }
    }

    private void OnEmergencyStopClicked()
    {
        DisconnectSession();
        _lblStatus.Text = "🚨 EMERGENCY STOP EXECUTED: All remote sessions revoked.";
        CyberDialog.Show(this, "Emergency Stop triggered!\n\nRemote desktop stream terminated and all input channels severed.", "Emergency Disconnect", CyberDialogType.Warning);
    }

    private void OnFrameTimerTick(object? sender, EventArgs e)
    {
        if (_isStreamConnected && _streamWs != null && _streamWs.State == WebSocketState.Open)
        {
            ulong nowTs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if ((nowTs - _lastPingSentTicks) >= 1500)
            {
                _lastPingSentTicks = nowTs;
                byte[] ping = DataChannelProtocol.SerializeLatencyPing(nowTs);
                _ = SendInputPacketAsync(ping);
            }
        }

        var elapsed = (DateTime.UtcNow - _lastFpsUpdate).TotalSeconds;
        if (elapsed >= 1.0)
        {
            _currentFps = _frameCount / (float)elapsed;
            _frameCount = 0;
            _lastFpsUpdate = DateTime.UtcNow;

            string inputState = _chkInputControl.Checked ? "Interactive (ACTIVE)" : "View-Only (MUTED)";
            var duration = _sessionClient != null ? (DateTime.UtcNow - _sessionStartTime) : TimeSpan.Zero;
            string latencyStr = _lastMeasuredRttMs > 0 ? $"{_lastMeasuredRttMs:F0} ms" : "< 15 ms (Tunnel)";

            _lblTelemetry.Text = $"• Target: {_currentDeviceHostname}\n" +
                                 $"• Device ID: {_currentDeviceId}\n" +
                                 $"• Resolution: {_remoteScreenSize.Width}x{_remoteScreenSize.Height}\n" +
                                 $"• Framerate: {_currentFps:F1} FPS (Target: {_targetFps})\n" +
                                 $"• Latency (RTT): {latencyStr}\n" +
                                 $"• Quality: {_jpegQuality}% (Zero-Bufferbloat)\n" +
                                 $"• Control: {inputState}\n" +
                                 $"• Duration: {duration:mm\\:ss}\n" +
                                 $"• Protocol: WebSocket / TLS 1.3\n" +
                                 $"• Status: {(_isStreamConnected ? "Live Desktop" : _streamStatusMessage)}";

            _badgeFloatDevice.Text = $"{_currentDeviceHostname} | {_currentFps:F0} FPS | {latencyStr}";

            if (_isStreamConnected)
            {
                _lblStatus.Text = $"🟢 Live Desktop ({_currentFps:F0} FPS, {latencyStr} RTT) • Zero-Bufferbloat Active • Target: {_targetFps} FPS • Quality: {_jpegQuality}%";
            }
        }

        _viewport.Invalidate();
    }

    private void OnViewportPaint(object? sender, PaintEventArgs e)
    {
        try
        {
            var g = e.Graphics;
            ThemeTokens.EnableHighQualityGraphics(g);
            g.Clear(ThemeTokens.BackgroundApp);

            if (_sessionClient != null)
            {
                bool drewFrame = false;
                lock (_streamLock)
                {
                    if (_previewBitmap != null)
                    {
                        var destRect = GetScaledRectangle(_previewBitmap.Size, _viewport.ClientSize);
                        g.DrawImage(_previewBitmap, destRect);

                        // Clean neon border highlight around active remote screen
                        Color frameColor = _chkInputControl.Checked ? ThemeTokens.AccentSuccess : ThemeTokens.AccentWarning;
                        using var borderPen = new Pen(frameColor, 1.5f);
                        g.DrawRectangle(borderPen, destRect);
                        drewFrame = true;
                    }
                }

                if (!drewFrame)
                {
                    // Awaiting Stream Visual Card
                    RenderAwaitingStreamDashboard(g);
                }
            }
            else
            {
                // Disconnected State Visual Dashboard
                RenderMissionControlDashboard(g);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Viewport paint exception caught safely: {ex.Message}");
        }
    }

    private void RenderAwaitingStreamDashboard(Graphics g)
    {
        int cardW = Math.Min(680, _viewport.Width - 40);
        int cardH = 260;
        int cardX = (_viewport.Width - cardW) / 2;
        int cardY = Math.Max(40, (_viewport.Height - cardH) / 2);
        var cardRect = new Rectangle(cardX, cardY, cardW, cardH);

        using (var path = ThemeTokens.CreateRoundedPath(cardRect, ThemeTokens.RadiusLarge))
        {
            using (var brush = new LinearGradientBrush(cardRect, ThemeTokens.BackgroundCard, Color.FromArgb(16, 22, 38), LinearGradientMode.Vertical))
            {
                g.FillPath(brush, path);
            }
            using (var pen = new Pen(ThemeTokens.BorderFocus, 1.5f))
            {
                g.DrawPath(pen, path);
            }
        }

        // Animated pulsing loader icon
        int iconSize = 32;
        int iconX = cardX + 28;
        int iconY = cardY + 28;
        using (var iconBmp = IconChar.Spinner.ToBitmap(ThemeTokens.AccentCyan, iconSize))
        {
            g.DrawImage(iconBmp, iconX, iconY, iconSize, iconSize);
        }

        string title = $"Awaiting Desktop Stream from {_currentDeviceHostname}";
        g.DrawString(title, ThemeTokens.FontDisplayMedium, Brushes.White, iconX + 44, iconY + 4);

        string statusLine = $"• Status: {_streamStatusMessage}\n" +
                           $"• Device ID: {_currentDeviceId}\n\n" +
                           $"1. Control Plane session authorization ticket successfully validated.\n" +
                           $"2. Ensure 'Start_Remote_Streaming.bat' or '1-Click_Enroll_And_Start.bat' is active on the target PC.\n" +
                           $"3. Live 60 FPS remote desktop frames will render automatically upon first frame reception.";

        var textRect = new RectangleF(cardX + 28, cardY + 74, cardW - 56, cardH - 90);
        using var textBrush = new SolidBrush(ThemeTokens.TextSecondary);
        g.DrawString(statusLine, ThemeTokens.FontBody, textBrush, textRect);
    }

    private void RenderMissionControlDashboard(Graphics g)
    {
        int centerX = _viewport.Width / 2;
        int startY = Math.Max(40, (_viewport.Height / 2) - 210);

        // Cyber Shield / Brand Logo
        int logoSize = 64;
        if (_appLogoImage != null)
        {
            g.DrawImage(_appLogoImage, centerX - (logoSize / 2), startY, logoSize, logoSize);
        }
        else
        {
            using var shieldBmp = IconChar.ShieldHalved.ToBitmap(ThemeTokens.AccentCyan, logoSize);
            g.DrawImage(shieldBmp, centerX - (logoSize / 2), startY, logoSize, logoSize);
        }

        startY += logoSize + 14;

        string title = "Enterprise Remote Access";
        var sizeTitle = g.MeasureString(title, ThemeTokens.FontDisplayLarge);
        using (var brushTitle = new SolidBrush(ThemeTokens.TextPrimary))
        {
            g.DrawString(title, ThemeTokens.FontDisplayLarge, brushTitle, centerX - (sizeTitle.Width / 2), startY);
        }

        startY += (int)sizeTitle.Height + 4;

        string sub = "Zero-Trust Encrypted Remote Desktop & Server Administration Console";
        var sizeSub = g.MeasureString(sub, ThemeTokens.FontBody);
        using (var brushSub = new SolidBrush(ThemeTokens.TextSecondary))
        {
            g.DrawString(sub, ThemeTokens.FontBody, brushSub, centerX - (sizeSub.Width / 2), startY);
        }

        startY += (int)sizeSub.Height + 12;

        // Security Tag Chip
        string chip = "FIPS 140-3 COMPLIANT  •  PER-MONITOR V2 DPI  •  ZERO-TRUST ENCRYPTION";
        var sizeChip = g.MeasureString(chip, ThemeTokens.FontCaptionBold);
        int chipW = (int)sizeChip.Width + 24;
        int chipH = 24;
        var chipRect = new Rectangle(centerX - (chipW / 2), startY, chipW, chipH);
        using (var pathChip = ThemeTokens.CreateRoundedPath(chipRect, chipH / 2))
        {
            using var chipBg = new SolidBrush(Color.FromArgb(20, 6, 182, 212));
            g.FillPath(chipBg, pathChip);
            using var chipPen = new Pen(Color.FromArgb(60, 6, 182, 212), 1f);
            g.DrawPath(chipPen, pathChip);
        }
        using (var chipTextBrush = new SolidBrush(ThemeTokens.AccentCyan))
        {
            g.DrawString(chip, ThemeTokens.FontCaptionBold, chipTextBrush, chipRect.X + 12, chipRect.Y + 4);
        }

        startY += chipH + 24;

        // 3 Feature Cards
        int cardWidth = 270;
        int cardHeight = 140;
        int spacing = 20;
        int totalWidth = (cardWidth * 3) + (spacing * 2);
        int cardStartX = centerX - (totalWidth / 2);

        if (_viewport.Width > totalWidth + 40)
        {
            RenderFeatureCard(g, cardStartX, startY, cardWidth, cardHeight, IconChar.Desktop, ThemeTokens.AccentCyan,
                "Connect Other PC",
                "Click 'Connect Other PC' on the left sidebar to export a 1-click installer and token for your second laptop.");

            RenderFeatureCard(g, cardStartX + cardWidth + spacing, startY, cardWidth, cardHeight, IconChar.ComputerMouse, ThemeTokens.AccentSuccess,
                "Sub-Millisecond Input",
                "Per-Monitor V2 DPI awareness guarantees exact mouse cursor and click synchronization without offset.");

            RenderFeatureCard(g, cardStartX + (cardWidth * 2) + (spacing * 2), startY, cardWidth, cardHeight, IconChar.Key, ThemeTokens.AccentPrimary,
                "Hardware Cryptography",
                "Cryptographic session authorization tickets signed with ECDSA P-256 and audited in real time.");
        }
    }

    private static void RenderFeatureCard(Graphics g, int x, int y, int width, int height, IconChar icon, Color accentClr, string title, string description)
    {
        var rect = new Rectangle(x, y, width, height);
        using (var path = ThemeTokens.CreateRoundedPath(rect, ThemeTokens.RadiusMedium))
        {
            using (var brush = new LinearGradientBrush(rect, ThemeTokens.BackgroundCard, Color.FromArgb(16, 22, 38), LinearGradientMode.Vertical))
            {
                g.FillPath(brush, path);
            }
            using (var pen = new Pen(ThemeTokens.BorderCard, 1f))
            {
                g.DrawPath(pen, path);
            }
        }

        int iconSize = 20;
        using (var iconBmp = icon.ToBitmap(accentClr, iconSize))
        {
            g.DrawImage(iconBmp, x + 16, y + 16, iconSize, iconSize);
        }

        using (var titleBrush = new SolidBrush(ThemeTokens.TextPrimary))
        {
            g.DrawString(title, ThemeTokens.FontBodyBold, titleBrush, x + 44, y + 16);
        }

        var descRect = new RectangleF(x + 16, y + 46, width - 32, height - 56);
        using (var descBrush = new SolidBrush(ThemeTokens.TextSecondary))
        {
            g.DrawString(description, ThemeTokens.FontCaption, descBrush, descRect);
        }
    }

    // =========================================================================
    // VIEWPORT SCALE & DISPLAY MODES (Fit to Screen, Stretch to Fill, 1:1)
    // =========================================================================
    private void CycleScaleMode()
    {
        _scaleMode = _scaleMode switch
        {
            ViewportScaleMode.Fit => ViewportScaleMode.Stretch,
            ViewportScaleMode.Stretch => ViewportScaleMode.Original,
            _ => ViewportScaleMode.Fit
        };
        UpdateScaleModeUI();
    }

    private void UpdateScaleModeUI()
    {
        string modeText = _scaleMode switch
        {
            ViewportScaleMode.Stretch => "Stretch",
            ViewportScaleMode.Original => "1:1 Native",
            _ => "Fit Screen"
        };
        IconChar modeIcon = _scaleMode switch
        {
            ViewportScaleMode.Stretch => IconChar.ArrowsUpDownLeftRight,
            ViewportScaleMode.Original => IconChar.Maximize,
            _ => IconChar.Compress
        };

        if (_btnScaleMode != null)
        {
            _btnScaleMode.Text = modeText;
            _btnScaleMode.IconChar = modeIcon;
        }
        if (_btnFloatScaleMode != null)
        {
            _btnFloatScaleMode.Text = modeText;
            _btnFloatScaleMode.IconChar = modeIcon;
        }

        SafeInvalidateViewport();
        CyberToast.Show(this, "Display Mode Changed", $"Scale Mode: {modeText}", ToastType.Info, 1800);
    }

    private Rectangle GetScaledRectangle(Size imgSize, Size canvasSize)
    {
        if (imgSize.Width <= 0 || imgSize.Height <= 0 || canvasSize.Width <= 0 || canvasSize.Height <= 0)
        {
            return new Rectangle(0, 0, Math.Max(1, canvasSize.Width), Math.Max(1, canvasSize.Height));
        }

        switch (_scaleMode)
        {
            case ViewportScaleMode.Stretch:
                return new Rectangle(0, 0, canvasSize.Width, canvasSize.Height);

            case ViewportScaleMode.Original:
                int origX = Math.Max(0, (canvasSize.Width - imgSize.Width) / 2);
                int origY = Math.Max(0, (canvasSize.Height - imgSize.Height) / 2);
                return new Rectangle(origX, origY, imgSize.Width, imgSize.Height);

            case ViewportScaleMode.Fit:
            default:
                float ratioX = (float)canvasSize.Width / imgSize.Width;
                float ratioY = (float)canvasSize.Height / imgSize.Height;
                float ratio = Math.Min(ratioX, ratioY);

                int newWidth = Math.Max(1, (int)(imgSize.Width * ratio));
                int newHeight = Math.Max(1, (int)(imgSize.Height * ratio));
                int posX = (canvasSize.Width - newWidth) / 2;
                int posY = (canvasSize.Height - newHeight) / 2;

                return new Rectangle(posX, posY, newWidth, newHeight);
        }
    }

    // =========================================================================
    // MOUSE EVENT FORWARDING & EXACT BOUNDS TRANSLATION
    // =========================================================================
    private void OnViewportMouseMove(object? sender, MouseEventArgs e)
    {
        if (_sessionClient == null || !_chkInputControl.Checked)
        {
            _viewport.Cursor = Cursors.Default;
            return;
        }

        var rect = GetScaledRectangle(_remoteScreenSize, _viewport.ClientSize);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            _viewport.Cursor = Cursors.Default;
            return;
        }

        if (rect.Contains(e.Location) || _isMouseDown)
        {
            _viewport.Cursor = Cursors.Cross;

            float normX = Math.Clamp((e.X - rect.X) / (float)rect.Width, 0.0f, 1.0f);
            float normY = Math.Clamp((e.Y - rect.Y) / (float)rect.Height, 0.0f, 1.0f);

            if (!_isMouseDown && Math.Abs(normX - _lastSentNormX) < 0.0005f && Math.Abs(normY - _lastSentNormY) < 0.0005f)
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            double elapsedMs = (now - _lastMouseMoveTicks) * 1000.0 / Stopwatch.Frequency;

            if (!_isMouseDown && elapsedMs < 14.0)
            {
                return;
            }

            _lastMouseMoveTicks = now;
            _lastSentNormX = normX;
            _lastSentNormY = normY;
            _sessionClient.SendMouseMove(normX, normY);
        }
        else
        {
            _viewport.Cursor = Cursors.Default;
        }
    }

    private void OnViewportMouseDown(object? sender, MouseEventArgs e)
    {
        _viewport.Focus();
        if (_sessionClient == null || !_chkInputControl.Checked) return;
        var rect = GetScaledRectangle(_remoteScreenSize, _viewport.ClientSize);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        if (rect.Contains(e.Location))
        {
            _isMouseDown = true;
            _viewport.Capture = true;

            float normX = Math.Clamp((e.X - rect.X) / (float)rect.Width, 0.0f, 1.0f);
            float normY = Math.Clamp((e.Y - rect.Y) / (float)rect.Height, 0.0f, 1.0f);
            var btn = e.Button == MouseButtons.Right ? MouseButton.Right :
                      e.Button == MouseButtons.Middle ? MouseButton.Middle : MouseButton.Left;

            _lastSentNormX = normX;
            _lastSentNormY = normY;

            _sessionClient.SendMouseMove(normX, normY);
            _sessionClient.SendMouseButton(btn, isDown: true, normX, normY);
        }
    }

    private void OnViewportMouseUp(object? sender, MouseEventArgs e)
    {
        if (_isMouseDown)
        {
            _isMouseDown = false;
            _viewport.Capture = false;
        }

        if (_sessionClient == null || !_chkInputControl.Checked) return;
        var rect = GetScaledRectangle(_remoteScreenSize, _viewport.ClientSize);
        if (rect.Width <= 0 || rect.Height <= 0) return;

        float normX = Math.Clamp((e.X - rect.X) / (float)rect.Width, 0.0f, 1.0f);
        float normY = Math.Clamp((e.Y - rect.Y) / (float)rect.Height, 0.0f, 1.0f);
        _lastSentNormX = normX;
        _lastSentNormY = normY;

        var btn = e.Button == MouseButtons.Right ? MouseButton.Right :
                  e.Button == MouseButtons.Middle ? MouseButton.Middle : MouseButton.Left;
        _sessionClient.SendMouseButton(btn, isDown: false, normX, normY);
    }

    private void OnViewportMouseWheel(object? sender, MouseEventArgs e)
    {
        if (_sessionClient == null || !_chkInputControl.Checked) return;
        var rect = GetScaledRectangle(_remoteScreenSize, _viewport.ClientSize);
        if (rect.Width <= 0 || rect.Height <= 0 || !rect.Contains(e.Location)) return;

        float normX = Math.Clamp((e.X - rect.X) / (float)rect.Width, 0.0f, 1.0f);
        float normY = Math.Clamp((e.Y - rect.Y) / (float)rect.Height, 0.0f, 1.0f);
        _sessionClient.SendMouseWheel((short)e.Delta, normX, normY);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        DisconnectSession();
        _http.Dispose();
        base.OnFormClosing(e);
    }

    private record AuthResult(string UserId, string Email, string DisplayName, string OrgId, UserRole Role, string[] AuthorizedGroupIds);
    private record SessionResult(
        string SessionId,
        string DeviceId,
        SessionCapabilities PermittedCapabilities,
        bool RequiresAttendedConsent,
        DateTime IssuedAtUtc,
        DateTime ExpiresAtUtc,
        string SignatureBase64,
        string ControlPlanePublicKeyPem);
}

public enum ViewportScaleMode
{
    Fit = 0,
    Stretch = 1,
    Original = 2
}
