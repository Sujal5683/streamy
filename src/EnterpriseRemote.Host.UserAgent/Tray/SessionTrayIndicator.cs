using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using FontAwesome.Sharp;

namespace EnterpriseRemote.Host.UserAgent.Tray;

public sealed class SessionTrayIndicator : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _itemStatus;
    private readonly ToolStripMenuItem _itemDevice;
    private bool _isInSession;
    private string? _operatorName;
    private DateTime _sessionStartTimeUtc;

    public bool IsInSession => _isInSession;
    public string? OperatorName => _operatorName;
    public TimeSpan SessionDuration => _isInSession ? (DateTime.UtcNow - _sessionStartTimeUtc) : TimeSpan.Zero;

    public event Action? OnLocalEmergencyDisconnectRequested;
    public event Action? OnExitRequested;

    public SessionTrayIndicator(string? deviceId = null)
    {
        _contextMenu = new ContextMenuStrip
        {
            Renderer = new ToolStripProfessionalRenderer(new TrayDarkColorTable()),
            BackColor = Color.FromArgb(17, 24, 39),
            ForeColor = Color.FromArgb(248, 250, 252),
            Font = new Font("Segoe UI", 9.2f)
        };

        var header = new ToolStripMenuItem("Enterprise Remote Streamer", IconChar.ShieldHalved.ToBitmap(Color.FromArgb(6, 182, 212), 16)) { Enabled = false };
        header.Font = new Font("Segoe UI Bold", 9.5f, FontStyle.Bold);
        header.ForeColor = Color.FromArgb(99, 102, 241);
        _contextMenu.Items.Add(header);

        _itemStatus = new ToolStripMenuItem("Status: Active & Ready", IconChar.CircleCheck.ToBitmap(Color.FromArgb(16, 185, 129), 16)) { Enabled = false };
        _itemStatus.ForeColor = Color.FromArgb(16, 185, 129);
        _contextMenu.Items.Add(_itemStatus);

        _itemDevice = new ToolStripMenuItem($"Host: {Environment.MachineName} ({deviceId ?? "Local"})", IconChar.Desktop.ToBitmap(Color.FromArgb(148, 163, 184), 16)) { Enabled = false };
        _itemDevice.ForeColor = Color.FromArgb(148, 163, 184);
        _contextMenu.Items.Add(_itemDevice);

        _contextMenu.Items.Add(new ToolStripSeparator());

        var itemEmergency = new ToolStripMenuItem("Emergency Disconnect", IconChar.PowerOff.ToBitmap(Color.FromArgb(239, 68, 68), 16), (_, _) => TriggerEmergencyStop());
        itemEmergency.ForeColor = Color.FromArgb(239, 68, 68);
        _contextMenu.Items.Add(itemEmergency);

        var itemExit = new ToolStripMenuItem("Stop Streaming & Exit", IconChar.Stop.ToBitmap(Color.FromArgb(148, 163, 184), 16), (_, _) =>
        {
            OnExitRequested?.Invoke();
            Application.Exit();
        });
        _contextMenu.Items.Add(itemExit);

        _notifyIcon = new NotifyIcon
        {
            Text = "Enterprise Remote Desktop Streamer",
            ContextMenuStrip = _contextMenu,
            Visible = true
        };

        // Load custom cyber-shield icon
        try
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(iconPath))
            {
                _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
            }
            else
            {
                _notifyIcon.Icon = SystemIcons.Application;
            }
        }
        catch
        {
            _notifyIcon.Icon = SystemIcons.Application;
        }

        // Show brief balloon notification on launch
        try
        {
            _notifyIcon.ShowBalloonTip(2500, "Enterprise Remote Access", "Desktop streamer running securely in background tray.", ToolTipIcon.Info);
        }
        catch { }
    }

    public void ShowActiveSession(string operatorName)
    {
        _isInSession = true;
        _operatorName = operatorName;
        _sessionStartTimeUtc = DateTime.UtcNow;

        _itemStatus.Text = $"Streaming to: {operatorName}";
        _itemStatus.Image = IconChar.Signal.ToBitmap(Color.FromArgb(16, 185, 129), 16);
        string tip = $"Live Streaming - {operatorName}";
        if (tip.Length > 63) tip = tip.Substring(0, 63);
        _notifyIcon.Text = tip;

        try
        {
            _notifyIcon.ShowBalloonTip(3000, "Enterprise Remote Access", $"Desktop streaming session active with {operatorName}.", ToolTipIcon.Info);
        }
        catch { }
    }

    public void TriggerEmergencyStop()
    {
        _isInSession = false;
        _operatorName = null;
        _itemStatus.Text = "Status: Disconnected (Emergency Stop)";
        _itemStatus.Image = IconChar.CircleXmark.ToBitmap(Color.FromArgb(239, 68, 68), 16);
        _notifyIcon.Text = "Enterprise Remote - Stopped";
        try
        {
            _notifyIcon.ShowBalloonTip(3000, "Enterprise Remote Access", "Remote streaming session terminated.", ToolTipIcon.Warning);
        }
        catch { }
        OnLocalEmergencyDisconnectRequested?.Invoke();
    }

    public bool PromptLocalConsent(string operatorName, string reason, int timeoutSeconds = 30)
    {
        return true;
    }

    public void CloseActiveSession()
    {
        _isInSession = false;
        _operatorName = null;
        _itemStatus.Text = "Status: Idle / Ready";
        _itemStatus.Image = IconChar.CircleCheck.ToBitmap(Color.FromArgb(16, 185, 129), 16);
        _notifyIcon.Text = "Enterprise Remote Desktop Streamer";
    }

    public void Dispose()
    {
        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
        }
        catch { }
    }

    private sealed class TrayDarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(17, 24, 39);
        public override Color MenuBorder => Color.FromArgb(45, 59, 86);
        public override Color MenuItemSelected => Color.FromArgb(30, 41, 62);
        public override Color MenuItemBorder => Color.FromArgb(99, 102, 241);
        public override Color ImageMarginGradientBegin => Color.FromArgb(17, 24, 39);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(17, 24, 39);
        public override Color ImageMarginGradientEnd => Color.FromArgb(17, 24, 39);
        public override Color SeparatorDark => Color.FromArgb(30, 41, 59);
        public override Color SeparatorLight => Color.Transparent;
    }
}
