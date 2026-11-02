using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using EnterpriseRemote.Controller.UI.Components;
using EnterpriseRemote.Controller.UI.Theme;
using EnterpriseRemote.Controller.UI.Utils;
using FontAwesome.Sharp;

namespace EnterpriseRemote.Controller.UI;

public sealed class EnrollRemoteDeviceForm : Form
{
    private readonly string _serverUrl;
    private readonly string _operatorId;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // UI Controls
    private CyberCard _cardStep1 = null!;
    private CyberCard _cardStep2 = null!;
    private CyberCard _cardStep3 = null!;
    private CyberCard _cardStep4 = null!;
    private CyberTextBox _txtServerUrl = null!;
    private CyberButton _btnStartTunnel = null!;
    private CyberTextBox _txtInvitationCode = null!;
    private CyberTextBox _txtCommand = null!;
    private CyberButton _btnGenerate = null!;
    private CyberButton _btnCopyCode = null!;
    private CyberButton _btnCopyCommand = null!;
    private CyberButton _btnExportPackage = null!;
    private CyberButton _btnClose = null!;
    private CyberBadge _badgeStatus = null!;
    private CyberBadge _badgeLanIp = null!;
    private CyberScrollPanel _pnlContent = null!;

    public string? GeneratedInvitationCode { get; private set; }

    public EnrollRemoteDeviceForm(string serverUrl, string? operatorId)
    {
        _serverUrl = serverUrl;
        _operatorId = operatorId ?? "USR-ADMIN-01";
        InitializeComponent();
        DetectAndDisplayLanIp();
    }

    private void InitializeComponent()
    {
        Text = "Connect Remote PC — Secure Host Enrollment Wizard";
        Size = new Size(880, 840);
        MinimumSize = new Size(760, 580);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        BackColor = ThemeTokens.BackgroundApp;
        ForeColor = ThemeTokens.TextPrimary;
        Font = ThemeTokens.FontBody;

        // Custom Application Icon
        try
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(iconPath)) this.Icon = new System.Drawing.Icon(iconPath);
        }
        catch { }

        // =========================================================================
        // TOP HEADER BANNER
        // =========================================================================
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 78,
            BackColor = ThemeTokens.BackgroundHeader,
            Padding = new Padding(24, 16, 24, 16)
        };
        pnlHeader.Paint += (s, e) =>
        {
            using var pen = new Pen(ThemeTokens.BorderSubtle, 1f);
            e.Graphics.DrawLine(pen, 0, pnlHeader.Height - 1, pnlHeader.Width, pnlHeader.Height - 1);
        };

        var picShield = new PictureBox
        {
            Image = IconChar.ShieldHalved.ToBitmap(ThemeTokens.AccentCyan, 36),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Size = new Size(42, 42),
            Location = new Point(24, 18),
            BackColor = Color.Transparent
        };
        pnlHeader.Controls.Add(picShield);

        var lblTitle = new Label
        {
            Text = "Remote PC Enrollment & Deployment Wizard",
            Font = ThemeTokens.FontDisplayLarge,
            ForeColor = ThemeTokens.TextPrimary,
            UseMnemonic = false,
            AutoSize = true,
            Location = new Point(78, 14)
        };
        pnlHeader.Controls.Add(lblTitle);

        var lblSubtitle = new Label
        {
            Text = "Generate a cryptographically signed token and export a 1-click deployment package for your remote Windows computer.",
            Font = ThemeTokens.FontCaption,
            ForeColor = ThemeTokens.TextSecondary,
            UseMnemonic = false,
            Location = new Point(80, 44),
            Size = new Size(740, 24)
        };
        pnlHeader.Controls.Add(lblSubtitle);
        pnlHeader.Resize += (_, _) =>
        {
            lblSubtitle.Width = Math.Max(200, pnlHeader.Width - 100);
        };
        Controls.Add(pnlHeader);

        // =========================================================================
        // BOTTOM FOOTER ACTION BAR
        // =========================================================================
        var pnlFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 64,
            BackColor = ThemeTokens.BackgroundHeader,
            Padding = new Padding(24, 14, 24, 14)
        };
        pnlFooter.Paint += (s, e) =>
        {
            using var pen = new Pen(ThemeTokens.BorderSubtle, 1f);
            e.Graphics.DrawLine(pen, 0, 0, pnlFooter.Width, 0);
        };

        _badgeStatus = new CyberBadge
        {
            Text = "Ready. Generate an invitation code to link your remote host.",
            Status = CyberBadgeStatus.Neutral,
            IconChar = IconChar.CircleInfo,
            Location = new Point(24, 16),
            Size = new Size(540, 32),
            BackColor = ThemeTokens.BackgroundHeader
        };
        pnlFooter.Controls.Add(_badgeStatus);

        _btnClose = new CyberButton
        {
            Text = "Done",
            Variant = CyberButtonVariant.Secondary,
            IconChar = IconChar.Check,
            Size = new Size(116, 38)
        };
        _btnClose.Click += (_, _) => Close();
        pnlFooter.Controls.Add(_btnClose);

        void LayoutFooter()
        {
            int btnY = (pnlFooter.Height - _btnClose.Height) / 2;
            _btnClose.Location = new Point(pnlFooter.Width - _btnClose.Width - 24, btnY);
            int badgeY = (pnlFooter.Height - _badgeStatus.Height) / 2;
            _badgeStatus.Location = new Point(24, badgeY);
            _badgeStatus.Width = Math.Max(200, _btnClose.Left - _badgeStatus.Left - 16);
        }

        pnlFooter.Resize += (_, _) => LayoutFooter();
        Controls.Add(pnlFooter);

        // =========================================================================
        // MAIN SCROLLABLE CONTENT CONTAINER
        // =========================================================================
        _pnlContent = new CyberScrollPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(24, 18, 24, 18),
            BackColor = ThemeTokens.BackgroundApp
        };
        Controls.Add(_pnlContent);
        _pnlContent.SendToBack();
        pnlHeader.BringToFront();
        pnlFooter.BringToFront();

        int curY = 16;
        int cardWidth = 780;

        // -------------------------------------------------------------------------
        // STEP 1 CARD: Network Gateway Address
        // -------------------------------------------------------------------------
        _cardStep1 = new CyberCard
        {
            Title = "STEP 1: NETWORK GATEWAY ADDRESS",
            HeaderIcon = IconChar.NetworkWired,
            HeaderIconColor = ThemeTokens.AccentCyan,
            Location = new Point(16, curY),
            Size = new Size(cardWidth, 168)
        };

        _badgeLanIp = new CyberBadge
        {
            Text = "Detected LAN IP: Resolving network interfaces...",
            Status = CyberBadgeStatus.Cyan,
            IconChar = IconChar.Wifi,
            Location = new Point(18, 48),
            Size = new Size(cardWidth - 36, 28)
        };
        _cardStep1.Controls.Add(_badgeLanIp);

        _txtServerUrl = new CyberTextBox
        {
            Text = _serverUrl,
            LeadingIcon = IconChar.Globe,
            PlaceholderText = "http://192.168.1.X:5293 or https://...",
            Location = new Point(18, 86),
            Size = new Size(cardWidth - 228, 36)
        };
        _cardStep1.Controls.Add(_txtServerUrl);

        _btnStartTunnel = new CyberButton
        {
            Text = "Internet Tunnel",
            Variant = CyberButtonVariant.Secondary,
            IconChar = IconChar.Globe,
            CustomIconColor = ThemeTokens.AccentCyan,
            Location = new Point(cardWidth - 200, 86),
            Size = new Size(182, 36),
            Font = ThemeTokens.FontCaption
        };
        _btnStartTunnel.Click += (_, _) => LaunchPublicTunnel();
        _cardStep1.Controls.Add(_btnStartTunnel);

        var lblTip1 = new Label
        {
            Text = "Pro Tip: On same Wi-Fi, use the detected LAN IP. For connecting across different networks/Internet, click 'Internet Tunnel'!",
            ForeColor = ThemeTokens.TextMuted,
            Font = ThemeTokens.FontCaption,
            Location = new Point(20, 130),
            Size = new Size(cardWidth - 40, 26),
            BackColor = Color.Transparent
        };
        _cardStep1.Controls.Add(lblTip1);

        void LayoutCard1()
        {
            int w = _cardStep1.Width;
            _badgeLanIp.Width = w - 36;
            _btnStartTunnel.Location = new Point(w - _btnStartTunnel.Width - 18, 86);
            _txtServerUrl.Location = new Point(18, 86);
            _txtServerUrl.Width = Math.Max(120, _btnStartTunnel.Left - _txtServerUrl.Left - 10);
            lblTip1.Width = w - 40;
        }
        _cardStep1.Resize += (_, _) => LayoutCard1();

        _pnlContent.Controls.Add(_cardStep1);
        curY += _cardStep1.Height + 14;

        // -------------------------------------------------------------------------
        // STEP 2 CARD: Generate Enrollment Token
        // -------------------------------------------------------------------------
        _cardStep2 = new CyberCard
        {
            Title = "STEP 2: SINGLE-USE ENROLLMENT TOKEN",
            HeaderIcon = IconChar.Key,
            HeaderIconColor = ThemeTokens.AccentPrimary,
            Location = new Point(16, curY),
            Size = new Size(cardWidth, 154)
        };

        _btnGenerate = new CyberButton
        {
            Text = "Generate Invitation Code",
            Variant = CyberButtonVariant.Primary,
            IconChar = IconChar.WandMagicSparkles,
            Location = new Point(18, 48),
            Size = new Size(240, 38)
        };
        _btnGenerate.Click += async (_, _) => await GenerateInvitationAsync();
        _cardStep2.Controls.Add(_btnGenerate);

        _txtInvitationCode = new CyberTextBox
        {
            Text = "Click 'Generate Invitation Code' above to generate token",
            LeadingIcon = IconChar.Fingerprint,
            ReadOnly = true,
            Location = new Point(18, 96),
            Size = new Size(cardWidth - 180, 36)
        };
        _cardStep2.Controls.Add(_txtInvitationCode);

        _btnCopyCode = new CyberButton
        {
            Text = "Copy Token",
            Variant = CyberButtonVariant.Secondary,
            IconChar = IconChar.Copy,
            Location = new Point(cardWidth - 152, 96),
            Size = new Size(134, 36),
            Enabled = false
        };
        _btnCopyCode.Click += (_, _) => CopyToClipboard(_txtInvitationCode.Text, "Enrollment invitation code copied!");
        _cardStep2.Controls.Add(_btnCopyCode);

        void LayoutCard2()
        {
            int rightPad = 18;
            _btnCopyCode.Location = new Point(_cardStep2.Width - _btnCopyCode.Width - rightPad, 96);
            _txtInvitationCode.Location = new Point(18, 96);
            _txtInvitationCode.Width = Math.Max(120, _btnCopyCode.Left - _txtInvitationCode.Left - 10);
        }
        _cardStep2.Resize += (_, _) => LayoutCard2();

        _pnlContent.Controls.Add(_cardStep2);
        curY += _cardStep2.Height + 14;

        // -------------------------------------------------------------------------
        // STEP 3 CARD: 1-Click Setup Package for Remote PC (RECOMMENDED)
        // -------------------------------------------------------------------------
        _cardStep3 = new CyberCard
        {
            Title = "STEP 3: 1-CLICK DEPLOYMENT PACKAGE FOR OTHER PC (RECOMMENDED)",
            HeaderIcon = IconChar.BoxArchive,
            HeaderIconColor = ThemeTokens.AccentSuccess,
            Location = new Point(16, curY),
            Size = new Size(cardWidth, 194)
        };

        var lblExportDesc = new Label
        {
            Text = "Exports a portable setup folder with all binaries, pre-configured 1-click startup scripts, and your token. Simply copy it to your other PC via USB or network folder and double click '1_CLICK_ENROLL_REMOTE_PC.bat' to enroll and stream automatically!",
            ForeColor = ThemeTokens.TextSecondary,
            Font = ThemeTokens.FontBody,
            Location = new Point(18, 46),
            Size = new Size(cardWidth - 36, 62),
            BackColor = Color.Transparent
        };
        _cardStep3.Controls.Add(lblExportDesc);

        _btnExportPackage = new CyberButton
        {
            Text = "Export 1-Click Setup Folder for Remote PC",
            Variant = CyberButtonVariant.Success,
            IconChar = IconChar.BoxArchive,
            Location = new Point(18, 120),
            Size = new Size(420, 44),
            Font = ThemeTokens.FontBodyBold,
            Enabled = false
        };
        _btnExportPackage.Click += (_, _) => ExportRemotePcPackage();
        _cardStep3.Controls.Add(_btnExportPackage);

        _cardStep3.Resize += (_, _) =>
        {
            lblExportDesc.Width = _cardStep3.Width - 36;
        };

        _pnlContent.Controls.Add(_cardStep3);
        curY += _cardStep3.Height + 14;

        // -------------------------------------------------------------------------
        // STEP 4 CARD: Command-Line Setup (Advanced)
        // -------------------------------------------------------------------------
        _cardStep4 = new CyberCard
        {
            Title = "STEP 4: MANUAL COMMAND-LINE SETUP (ADVANCED / SCRIPTED)",
            HeaderIcon = IconChar.Terminal,
            HeaderIconColor = ThemeTokens.TextSecondary,
            Location = new Point(16, curY),
            Size = new Size(cardWidth, 154)
        };

        _txtCommand = new CyberTextBox
        {
            Text = "EnterpriseRemote.Host.Service.exe --enroll <INVITATION_CODE> --server <SERVER_URL>",
            LeadingIcon = IconChar.Code,
            ReadOnly = true,
            Location = new Point(18, 46),
            Size = new Size(cardWidth - 190, 36)
        };
        _cardStep4.Controls.Add(_txtCommand);

        _btnCopyCommand = new CyberButton
        {
            Text = "Copy Command",
            Variant = CyberButtonVariant.Secondary,
            IconChar = IconChar.Copy,
            Location = new Point(cardWidth - 162, 46),
            Size = new Size(144, 36),
            Enabled = false
        };
        _btnCopyCommand.Click += (_, _) => CopyToClipboard(_txtCommand.Text, "CLI command copied to clipboard!");
        _cardStep4.Controls.Add(_btnCopyCommand);

        var lblCmdNote = new Label
        {
            Text = "Run the command above in an elevated Administrator prompt on the target machine if configuring manually.",
            ForeColor = ThemeTokens.TextMuted,
            Font = ThemeTokens.FontCaption,
            Location = new Point(20, 94),
            Size = new Size(cardWidth - 36, 28),
            BackColor = Color.Transparent
        };
        _cardStep4.Controls.Add(lblCmdNote);

        void LayoutCard4()
        {
            int rightPad = 18;
            _btnCopyCommand.Location = new Point(_cardStep4.Width - _btnCopyCommand.Width - rightPad, 46);
            _txtCommand.Location = new Point(18, 46);
            _txtCommand.Width = Math.Max(120, _btnCopyCommand.Left - _txtCommand.Left - 10);
            lblCmdNote.Width = _cardStep4.Width - 36;
        }
        _cardStep4.Resize += (_, _) => LayoutCard4();

        _pnlContent.Controls.Add(_cardStep4);
        curY += _cardStep4.Height + 24;

        _pnlContent.AutoScrollMinSize = new Size(0, curY);
        _pnlContent.Resize += (_, _) => LayoutContentCards();

        Load += (_, _) =>
        {
            _pnlContent.AutoScrollPosition = new Point(0, 0);
            LayoutContentCards();
            LayoutFooter();
            LayoutCard1();
            LayoutCard2();
            LayoutCard4();
            ActiveControl = _btnClose;
        };

        Shown += (_, _) =>
        {
            _pnlContent.AutoScrollPosition = new Point(0, 0);
            LayoutContentCards();
            LayoutFooter();
            LayoutCard1();
            LayoutCard2();
            LayoutCard4();
            _btnClose.Focus();
        };
    }

    private void LayoutContentCards()
    {
        if (_pnlContent == null || _cardStep1 == null) return;
        int targetWidth = Math.Max(660, _pnlContent.ClientSize.Width - 32);
        _cardStep1.Width = targetWidth;
        _cardStep2.Width = targetWidth;
        _cardStep3.Width = targetWidth;
        _cardStep4.Width = targetWidth;

        _cardStep1.Left = 16;
        _cardStep2.Left = 16;
        _cardStep3.Left = 16;
        _cardStep4.Left = 16;
    }

    private void DetectAndDisplayLanIp()
    {
        try
        {
            string? tunnelUrl = TryReadPublicTunnelUrl();

            // 1. If user passed a non-local server URL into this dialog, respect it
            if (!string.IsNullOrWhiteSpace(_serverUrl) &&
                !_serverUrl.Contains("127.0.0.1") &&
                !_serverUrl.Contains("localhost"))
            {
                _txtServerUrl.Text = _serverUrl;
                _badgeLanIp.Text = $"Using Gateway Server: {_serverUrl}";
                _badgeLanIp.Status = CyberBadgeStatus.Success;
                return;
            }

            // 2. If a public tunnel URL file exists, use it
            if (!string.IsNullOrEmpty(tunnelUrl))
            {
                _badgeLanIp.Text = $"Active Internet Tunnel: {tunnelUrl} (Connect from ANY network)";
                _badgeLanIp.Status = CyberBadgeStatus.Success;
                _txtServerUrl.Text = tunnelUrl;
                return;
            }

            // 3. Use NetworkDiscoveryHelper for reliable Wi-Fi/LAN adapter detection
            int port = 5293;
            if (Uri.TryCreate(_serverUrl, UriKind.Absolute, out var uri) && uri.Port > 0)
            {
                port = uri.Port;
            }

            var adapters = NetworkDiscoveryHelper.GetActiveLanAdapters();
            // Prefer Wi-Fi with gateway, then Ethernet with gateway, then anything
            LanAdapterInfo? bestNullable = adapters.Cast<LanAdapterInfo?>()
                .FirstOrDefault(a => a!.Value.IsWireless && a!.Value.HasGateway)
                ?? adapters.Cast<LanAdapterInfo?>().FirstOrDefault(a => a!.Value.HasGateway)
                ?? adapters.Cast<LanAdapterInfo?>().FirstOrDefault();
            var best = bestNullable ?? default;

            if (bestNullable.HasValue && best.IpAddress != null && best.IpAddress != "127.0.0.1")
            {
                string adapterLabel = best.IsWireless ? "Wi-Fi" : "Ethernet";
                _badgeLanIp.Text = $"Detected {adapterLabel} IP: {best.IpAddress}  ({best.Name})  — For same network. For Internet, click 'Internet Tunnel'";
                _badgeLanIp.Status = CyberBadgeStatus.Cyan;
                _txtServerUrl.Text = $"http://{best.IpAddress}:{port}";
            }
            else
            {
                _badgeLanIp.Text = "Could not detect LAN IP. Enter gateway address manually or click 'Internet Tunnel'.";
                _badgeLanIp.Status = CyberBadgeStatus.Warning;
            }
        }
        catch
        {
            _badgeLanIp.Text = "Could not automatically resolve LAN IP. Enter IP address manually.";
            _badgeLanIp.Status = CyberBadgeStatus.Warning;
        }
    }

    private static bool IsTunnelReachable(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var resp = http.GetAsync($"{url.TrimEnd('/')}/health").GetAwaiter().GetResult();
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static void TryCleanStaleTunnelFiles()
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string rootDir = baseDir;
            for (int i = 0; i < 6; i++)
            {
                string candidate = Path.Combine(rootDir, "public_tunnel_url.txt");
                if (File.Exists(candidate)) File.Delete(candidate);
                string? parent = Directory.GetParent(rootDir)?.FullName;
                if (parent == null) break;
                rootDir = parent;
            }
            string localCandidate = Path.Combine(baseDir, "public_tunnel_url.txt");
            if (File.Exists(localCandidate)) File.Delete(localCandidate);
        }
        catch { }
    }

    private void LaunchPublicTunnel()
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string rootDir = baseDir;
            for (int i = 0; i < 6; i++)
            {
                if (Directory.Exists(Path.Combine(rootDir, "scripts")) ||
                    Directory.Exists(Path.Combine(rootDir, "EnterpriseRemote_Apps"))) break;
                string? parent = Directory.GetParent(rootDir)?.FullName;
                if (parent == null) break;
                rootDir = parent;
            }

            string batScript = Path.Combine(rootDir, "EnterpriseRemote_Apps", "1_MAIN_PC_CONTROLLER", "START_PUBLIC_INTERNET_TUNNEL.bat");
            if (!File.Exists(batScript))
            {
                batScript = Path.Combine(rootDir, "scripts", "Start_Cloudflare_Tunnel.bat");
            }

            if (File.Exists(batScript))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{batScript}\"\"",
                    UseShellExecute = true
                });

                CyberToast.Show(this, "Internet Tunnel Starting", "Cloudflare edge tunnel launched in background. Check the tunnel console for your https://*.trycloudflare.com URL!", ToastType.Info, 5000);
            }
            else
            {
                CyberDialog.Show(this, "Could not find START_PUBLIC_INTERNET_TUNNEL.bat launcher.", "Tunnel Error", CyberDialogType.Warning);
            }
        }
        catch (Exception ex)
        {
            CyberDialog.Show(this, $"Failed to launch tunnel: {ex.Message}", "Tunnel Error", CyberDialogType.Error);
        }
    }

    private static string? TryReadPublicTunnelUrl()
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string rootDir = baseDir;
            for (int i = 0; i < 6; i++)
            {
                string candidate = Path.Combine(rootDir, "public_tunnel_url.txt");
                if (File.Exists(candidate))
                {
                    string url = File.ReadAllText(candidate).Trim();
                    if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return url;
                }
                string? parent = Directory.GetParent(rootDir)?.FullName;
                if (parent == null) break;
                rootDir = parent;
            }

            string localCandidate = Path.Combine(baseDir, "public_tunnel_url.txt");
            if (File.Exists(localCandidate))
            {
                string url = File.ReadAllText(localCandidate).Trim();
                if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return url;
            }
        }
        catch { }
        return null;
    }

    private static string GetLocalLanIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint)
            {
                return endPoint.Address.ToString();
            }
        }
        catch { }

        foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
            {
                return ip.ToString();
            }
        }
        return "127.0.0.1";
    }

    private async Task GenerateInvitationAsync()
    {
        _btnGenerate.Enabled = false;
        _badgeStatus.Text = "Contacting Control Plane to generate secure enrollment invitation...";
        _badgeStatus.Status = CyberBadgeStatus.Warning;

        try
        {
            string targetServer = _txtServerUrl.Text.TrimEnd('/');
            string localServer = _serverUrl.TrimEnd('/');

            var endpointsToTry = new List<string>();
            // 1. Loopback on local host (always fastest and guaranteed to reach local Control Plane without cloudflare hairpin)
            endpointsToTry.Add("http://127.0.0.1:5293");
            if (!string.IsNullOrEmpty(localServer) && !endpointsToTry.Contains(localServer, StringComparer.OrdinalIgnoreCase))
            {
                endpointsToTry.Add(localServer);
            }
            if (!string.IsNullOrEmpty(targetServer) && !endpointsToTry.Contains(targetServer, StringComparer.OrdinalIgnoreCase))
            {
                endpointsToTry.Add(targetServer);
            }

            var body = new
            {
                AdminUserId = string.IsNullOrEmpty(_operatorId) ? "USR-ADMIN-01" : _operatorId,
                GroupId = "GRP-CORP-DESKTOPS",
                TtlMinutes = 120,
                AttendedRequired = false
            };

            CreateInvitationResponse? invResp = null;
            string lastError = "";

            for (int attempt = 0; attempt < 2; attempt++)
            {
                foreach (var ep in endpointsToTry)
                {
                    try
                    {
                        using var http = new HttpClient { BaseAddress = new Uri(ep), Timeout = TimeSpan.FromSeconds(4) };
                        var resp = await http.PostAsJsonAsync("/api/v1/invitations/create", body);
                        if (resp.IsSuccessStatusCode)
                        {
                            invResp = await resp.Content.ReadFromJsonAsync<CreateInvitationResponse>(JsonOpts);
                            if (invResp != null && !string.IsNullOrEmpty(invResp.InvitationCode))
                            {
                                break;
                            }
                        }
                        else
                        {
                            lastError = await resp.Content.ReadAsStringAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                    }
                }

                if (invResp != null) break;

                if (attempt == 0)
                {
                    _badgeStatus.Text = "Control Plane not responding. Attempting to start Control Plane service...";
                    if (TryAutoStartControlPlane())
                    {
                        await Task.Delay(2000);
                    }
                }
            }

            if (invResp == null || string.IsNullOrEmpty(invResp.InvitationCode))
            {
                _badgeStatus.Text = $"Connection Error: Control Plane is offline ({lastError}). Please run 'Launch_Remote_Access_App.bat'.";
                _badgeStatus.Status = CyberBadgeStatus.Danger;
                _btnGenerate.Enabled = true;
                return;
            }

            GeneratedInvitationCode = invResp.InvitationCode;
            _txtInvitationCode.Text = invResp.InvitationCode;
            _txtCommand.Text = $"EnterpriseRemote.Host.Service.exe --enroll {invResp.InvitationCode} --server {targetServer}";

            _btnCopyCode.Enabled = true;
            _btnCopyCommand.Enabled = true;
            _btnExportPackage.Enabled = true;
            _btnGenerate.Enabled = true;

            _badgeStatus.Text = $"Invitation token generated! Valid until: {invResp.ExpiresAtUtc.ToLocalTime():g}";
            _badgeStatus.Status = CyberBadgeStatus.Success;

            CyberToast.Show(this, "Invitation Generated", $"Single-use token valid until {invResp.ExpiresAtUtc.ToLocalTime():t}", ToastType.Success);
        }
        catch (Exception ex)
        {
            _badgeStatus.Text = $"Connection Error: {ex.Message}";
            _badgeStatus.Status = CyberBadgeStatus.Danger;
            _btnGenerate.Enabled = true;
        }
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

    private void CopyToClipboard(string text, string successMessage)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        SafeClipboard.TrySetText(text);
        _badgeStatus.Text = successMessage;
        _badgeStatus.Status = CyberBadgeStatus.Success;
        CyberToast.Show(this, "Copied", successMessage, ToastType.Success);
    }

    private void ExportRemotePcPackage()
    {
        if (string.IsNullOrEmpty(GeneratedInvitationCode)) return;

        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string rootDir = baseDir;
            for (int i = 0; i < 6; i++)
            {
                if (Directory.Exists(Path.Combine(rootDir, "EnterpriseRemote_Apps")) ||
                    Directory.Exists(Path.Combine(rootDir, "src")) ||
                    Directory.Exists(Path.Combine(rootDir, "dist"))) break;
                string? parent = Directory.GetParent(rootDir)?.FullName;
                if (parent == null) break;
                rootDir = parent;
            }

            string exportDir = Path.Combine(rootDir, "EnterpriseRemote_Apps", "2_REMOTE_PC_STREAMER");
            string exportBinDir = Path.Combine(exportDir, "Bin");
            Directory.CreateDirectory(exportBinDir);

            string targetServer = _txtServerUrl.Text.TrimEnd('/');

            // Copy binary dependencies into Bin/
            string[] sourceDirs = new[]
            {
                Path.Combine(rootDir, "src", "EnterpriseRemote.Host.Service", "bin", "Release", "net8.0-windows"),
                Path.Combine(rootDir, "src", "EnterpriseRemote.Host.UserAgent", "bin", "Release", "net8.0-windows"),
                Path.Combine(rootDir, "src", "EnterpriseRemote.Common", "bin", "Release", "net8.0"),
                Path.Combine(rootDir, "dist", "HostService"),
                Path.Combine(rootDir, "dist", "UserAgent"),
                Path.Combine(rootDir, "RemotePC_Package")
            };

            foreach (var sDir in sourceDirs)
            {
                if (Directory.Exists(sDir))
                {
                    foreach (var file in Directory.GetFiles(sDir))
                    {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext is ".exe" or ".dll" or ".json" or ".pdb" or ".ico")
                        {
                            string dest = Path.Combine(exportBinDir, Path.GetFileName(file));
                            try { File.Copy(file, dest, overwrite: true); } catch { }
                        }
                    }
                }
            }

            // 1. Silent VBS Launcher (0 console windows)
            string vbsContent = @"' ================================================================================
' Enterprise Remote Access - Remote PC Silent Streamer Launcher
' ================================================================================
' Double-click this .vbs file to start remote streaming silently in the System Tray.
' ZERO terminal windows or command prompts will be opened.

Set WshShell = CreateObject(""WScript.Shell"")
Set fso      = CreateObject(""Scripting.FileSystemObject"")
scriptDir    = fso.GetParentFolderName(WScript.ScriptFullName)
binDir       = scriptDir & ""\Bin""
appData      = WshShell.ExpandEnvironmentStrings(""%ProgramData%"") & ""\EnterpriseRemote""

hasIdentity = False
If fso.FileExists(binDir & ""\device_identity.json"") Then hasIdentity = True
If fso.FileExists(scriptDir & ""\device_identity.json"") Then hasIdentity = True
If fso.FileExists(appData & ""\device_identity.json"") Then hasIdentity = True

If Not hasIdentity Then
    WshShell.Run """" & scriptDir & ""\1_CLICK_ENROLL_REMOTE_PC.bat"""", 1, True
Else
    WshShell.CurrentDirectory = binDir
    WshShell.Run """" & binDir & ""\EnterpriseRemote.Host.Service.exe"" --run"", 0, False
    WScript.Sleep 800
    WshShell.Run """" & binDir & ""\EnterpriseRemote.Host.UserAgent.exe"""", 0, False
End If
";
            File.WriteAllText(Path.Combine(exportDir, "START_REMOTE_STREAMING.vbs"), vbsContent);

            // 2. Batch Launcher (exits immediately)
            string batLauncher = @"@echo off
title Enterprise Remote Access — Remote PC Streamer
setlocal

cd /d ""%~dp0""

:: Enrollment check: look for device identity in known locations
set HAS_IDENTITY=0
if exist ""Bin\device_identity.json""                              set HAS_IDENTITY=1
if exist ""device_identity.json""                                  set HAS_IDENTITY=1
if exist ""%ProgramData%\EnterpriseRemote\device_identity.json""  set HAS_IDENTITY=1

if ""%HAS_IDENTITY%""==""0"" (
    echo [!] This PC has not been enrolled yet.
    echo [*] Launching the 1-Click Enrollment wizard...
    call ""1_CLICK_ENROLL_REMOTE_PC.bat""
    exit /b 0
)

cd /d ""%~dp0Bin""
start """" /B EnterpriseRemote.Host.Service.exe --run
timeout /T 1 /NOBREAK >nul 2>&1
start """" /B EnterpriseRemote.Host.UserAgent.exe

exit /b 0
";
            File.WriteAllText(Path.Combine(exportDir, "START_REMOTE_STREAMING.bat"), batLauncher);

            // 3. 1-Click Enrollment (pre-configured with server and invitation code)
            string enrollBat = $@"@echo off
title Enterprise Remote Access — 1-Click Device Enrollment
setlocal
cd /d ""%~dp0""

echo.
echo  ================================================================
echo    ENTERPRISE REMOTE ACCESS - 1-CLICK DEVICE ENROLLMENT
echo  ================================================================
echo.
echo   Enrolling this Remote PC with your Controller PC:
echo     Server URL:       {targetServer}
echo     Invitation Code:  {GeneratedInvitationCode}
echo.
echo  [*] Contacting Control Plane and configuring cryptographic keys...
echo.

cd /d ""%~dp0Bin""
if not exist ""EnterpriseRemote.Host.Service.exe"" cd /d ""%~dp0""

EnterpriseRemote.Host.Service.exe --enroll ""{GeneratedInvitationCode}"" --server ""{targetServer}""
set ENROLL_RESULT=%ERRORLEVEL%
cd /d ""%~dp0""

if %ENROLL_RESULT% neq 0 (
    echo.
    echo  [ERROR] Enrollment failed (exit code: %ENROLL_RESULT%).
    echo.
    echo  Troubleshooting checklist:
    echo    1. Ensure START_MAIN_PC is running on the Main PC ({targetServer}).
    echo    2. Verify both PCs are connected to the same Wi-Fi, LAN, or Mobile Hotspot.
    echo    3. Verify port 5293 is allowed in Windows Firewall on the Main PC.
    echo.
    pause
    exit /b %ENROLL_RESULT%
)

echo.
echo  [SUCCESS] Device enrolled and paired successfully!
echo  [*] Starting Enterprise Remote background desktop streaming...
echo.

timeout /T 2 /NOBREAK >nul 2>&1
call START_REMOTE_STREAMING.bat
exit /b 0
";
            File.WriteAllText(Path.Combine(exportDir, "1_CLICK_ENROLL_REMOTE_PC.bat"), enrollBat);

            // 4. Stop Script
            string stopBat = @"@echo off
title Enterprise Remote Access — Stop Streaming
setlocal

taskkill /F /IM EnterpriseRemote.Host.UserAgent.exe >nul 2>&1
taskkill /F /IM EnterpriseRemote.Host.Service.exe   >nul 2>&1

echo.
echo   [OK] Remote desktop streaming stopped cleanly.
echo.
timeout /T 3 >nul
exit /b 0
";
            File.WriteAllText(Path.Combine(exportDir, "STOP_REMOTE_STREAMING.bat"), stopBat);

            // 5. Polished Readme
            string readme = $@"================================================================================
  ENTERPRISE REMOTE ACCESS — REMOTE PC (DESKTOP STREAMER)
================================================================================

  This folder is pre-configured for your second laptop / remote computer!

  PRE-CONFIGURED SETTINGS:
  ------------------------
  Server URL:      {targetServer}
  Invitation Code: {GeneratedInvitationCode}

  QUICK START (2 SIMPLE STEPS):
  -----------------------------

  STEP 1: Copy this entire folder to your other laptop
          (Use a USB flash drive, local network shared folder, or cloud drive)

  STEP 2: Double-click '1_CLICK_ENROLL_REMOTE_PC.bat'
          That's it! Because this package was exported from your Controller,
          your server URL and cryptographic token are already pre-filled.
          The device will pair in ~3 seconds and start streaming immediately.

  DAILY USE (ONCE ENROLLED):
  --------------------------
  - Double-click 'START_REMOTE_STREAMING.vbs'
    Runs completely silently in the Windows System Tray (0 terminal windows!)
  - To stop streaming anytime: Double-click 'STOP_REMOTE_STREAMING.bat'
================================================================================";
            File.WriteAllText(Path.Combine(exportDir, "README_REMOTE_PC.txt"), readme);

            // Also synchronize RemotePC_Package if it exists in the workspace root
            string altPackageDir = Path.Combine(rootDir, "RemotePC_Package");
            if (Directory.Exists(altPackageDir))
            {
                try
                {
                    File.WriteAllText(Path.Combine(altPackageDir, "1-Click_Enroll_And_Start.bat"), enrollBat);
                    File.WriteAllText(Path.Combine(altPackageDir, "1_CLICK_ENROLL_REMOTE_PC.bat"), enrollBat);
                    File.WriteAllText(Path.Combine(altPackageDir, "Start_Remote_Streaming.vbs"), vbsContent);
                    File.WriteAllText(Path.Combine(altPackageDir, "Start_Remote_Streaming.bat"), batLauncher);
                    File.WriteAllText(Path.Combine(altPackageDir, "Stop_Remote_Streaming.bat"), stopBat);
                }
                catch { }
            }

            _badgeStatus.Text = $"Ready-to-copy package: {exportDir}";
            _badgeStatus.Status = CyberBadgeStatus.Success;

            Process.Start(new ProcessStartInfo
            {
                FileName = exportDir,
                UseShellExecute = true
            });

            CyberDialog.Show(
                this,
                $"Ready-to-copy Remote PC package created successfully!\n\nLocation:\n{exportDir}\n\n1. Copy this entire folder to your other laptop.\n2. Double-click '1_CLICK_ENROLL_REMOTE_PC.bat' (token is already pre-configured!).",
                "Deployment Package Ready",
                CyberDialogType.Success);
        }
        catch (Exception ex)
        {
            _badgeStatus.Text = $"Export error: {ex.Message}";
            _badgeStatus.Status = CyberBadgeStatus.Danger;
            CyberDialog.Show(this, $"Failed to export package:\n{ex.Message}", "Export Error", CyberDialogType.Error);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _http.Dispose();
        }
        base.Dispose(disposing);
    }

    private record CreateInvitationResponse(string InvitationId, string InvitationCode, DateTime ExpiresAtUtc, string GroupId);
}
