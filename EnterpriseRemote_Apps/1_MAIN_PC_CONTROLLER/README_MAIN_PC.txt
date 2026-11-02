================================================================================
  ENTERPRISE REMOTE ACCESS — MAIN PC (OPERATOR CONSOLE)
================================================================================

  QUICK START
  ───────────
  ⭐  Double-click:  START_MAIN_PC.vbs          ← RECOMMENDED (zero windows!)
  OR  Double-click:  START_MAIN_PC.bat           ← Alternative
  🌐  Double-click:  START_PUBLIC_INTERNET_TUNNEL.bat ← Connect From Anywhere (Cloudflare edge tunnel)

  What starts automatically:
    • Control Plane API  →  runs silently at http://0.0.0.0:5293
    • Operator Controller GUI  →  appears on your screen with custom dark cyber theme

================================================================================

  FOLDER CONTENTS
  ───────────────
  START_MAIN_PC.vbs                 ← 1-click silent launcher (preferred)
  START_MAIN_PC.bat                 ← Batch launcher (exits immediately)
  START_TAILSCALE_MODE.bat          ← 1-click Tailscale low-latency WAN mode (WireGuard UDP)
  START_TAILSCALE_MODE.vbs          ← Silent Tailscale mode launcher
  START_PUBLIC_INTERNET_TUNNEL.bat  ← 1-click Cloudflare tunnel for Internet access
  start_tailscale_tunnel.ps1        ← Tailscale status checker & IP display helper
  ADMIN_ACCOUNTS.txt                ← Pre-seeded admin directory & login reference
  start_tunnel.ps1                  ← Edge tunnel PowerShell automation engine
  README_MAIN_PC.txt                ← This file
  Bin\                              ← Binaries (don't touch, don't move)
    EnterpriseRemote.ControlPlane.exe   ← API backend & WebSocket streaming relay
    EnterpriseRemote.Controller.exe     ← Operator GUI
    cloudflared.exe                     ← Cloudflare edge tunnel binary
    appsettings.json                    ← Server configuration
    app.ico / app_logo.png              ← High-res cyber-shield brand assets

================================================================================

  OPERATOR CONTROLLER FEATURES
  ─────────────────────────────
  • Admin Login: Click "[Admin Accounts ▾]" in Card 1 to log in with 1 click.
  • Connect Anywhere: Double-click START_PUBLIC_INTERNET_TUNNEL.bat to get a
    public HTTPS URL to stream across mobile data, coffee shop Wi-Fi, or globally.
  • Display Scaling Modes: Click the "Scale Mode" toggle in the viewer header:
      - Fit Screen: Preserves aspect ratio with letterboxes.
      - Stretch (Fill): Stretches remote desktop to 100% of your window.
      - 1:1 Native: Pixel-perfect native resolution with direct coordinate mapping.
  • Unblocked Exam & Protected Windows: Automatically captures Safe Exam Browser,
    lock-down browsers, and windows protected with WDA_EXCLUDEFROMCAPTURE without
    black screens or blocking.
  • Unattended 24/7 Access: Auto-assigned PERM-XXXXXXXXXXXX keys connect without
    requiring human acceptance on the remote laptop.

================================================================================

  IMPORTANT NOTES
  ───────────────
  • The Control Plane starts automatically when you run START_MAIN_PC.vbs.
  • Default port: 5293. If you change it, update appsettings.json in Bin\.
  • Your LAN IP is shown automatically in the "Connect Other PC" dialog.
  • To stop everything: close the Controller window. The Control Plane will
    stop automatically when the Controller exits (or you can use Task Manager).

================================================================================

  FIREWALL (if Remote PC can't connect over LAN)
  ──────────────────────────────────────────────
  Run this ONCE in an Admin Command Prompt on this PC:

    netsh advfirewall firewall add rule name="EnterpriseRemote ControlPlane" ^
      protocol=TCP dir=in localport=5293 action=allow

================================================================================
