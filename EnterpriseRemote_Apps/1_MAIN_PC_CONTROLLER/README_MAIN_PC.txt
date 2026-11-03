================================================================================
  ENTERPRISE REMOTE ACCESS — MAIN PC (OPERATOR CONSOLE)
================================================================================

  QUICK START & FILE INVENTORY
  ────────────────────────────
  ⭐  Double-click:  START_MAIN_PC.vbs          ← RECOMMENDED (zero-window silent launch)
  OR  Double-click:  START_MAIN_PC.bat           ← Console batch alternative
  🛑  Double-click:  STOP_MAIN_PC.vbs           ← 1-CLICK SILENT STOP (terminates all processes)
  🛑  Double-click:  STOP_MAIN_PC.bat           ← Interactive stop with status confirmation
  🌐  Double-click:  START_PUBLIC_INTERNET_TUNNEL.bat ← Cloudflare edge tunnel (connect anywhere)
  ⚡  Double-click:  START_TAILSCALE_MODE.vbs    ← Tailscale low-latency WAN mode (WireGuard UDP)
  📖  Open file:     USER_GUIDE.md              ← Comprehensive step-by-step user manual

  What starts automatically with START_MAIN_PC.vbs:
    • Control Plane API  → runs silently at http://0.0.0.0:5293
    • Operator Console GUI → appears on your screen with modern dark cyber theme

================================================================================

  FOLDER CONTENTS
  ───────────────
  START_MAIN_PC.vbs                 ← 1-click silent launcher (preferred)
  START_MAIN_PC.bat                 ← Batch launcher
  STOP_MAIN_PC.vbs                  ← 1-click silent stopper (kills GUI, server, tunnels)
  STOP_MAIN_PC.bat                  ← Interactive stopper (displays status and releases port)
  OPEN_WEB_CONTROLLER.bat           ← Launches Web Controller in default browser
  START_TAILSCALE_MODE.bat          ← 1-click Tailscale low-latency WAN mode (WireGuard UDP)
  START_TAILSCALE_MODE.vbs          ← Silent Tailscale mode launcher
  START_PUBLIC_INTERNET_TUNNEL.bat  ← 1-click Cloudflare tunnel for Internet access
  USER_GUIDE.md                     ← Comprehensive user guide with UI controls reference
  ADMIN_ACCOUNTS.txt                ← Pre-seeded admin directory & login reference
  README_MAIN_PC.txt                ← This file
  Bin\                              ← Binaries (don't touch, don't move)
    EnterpriseRemote.ControlPlane.exe   ← API backend & WebSocket streaming relay
    EnterpriseRemote.Controller.exe     ← Operator GUI shell (WebView2 Direct3D)
    cloudflared.exe                     ← Cloudflare edge tunnel binary
    appsettings.json                    ← Server configuration
    app.ico / app_logo.png              ← High-res cyber-shield brand assets
    wwwroot\                            ← Modern cyber web application assets

================================================================================

  HOW TO PROPERLY CLOSE & TERMINATE
  ─────────────────────────────────
  • Disconnect only: Click "Disconnect" on the toolbar or "⏹ End" in fullscreen.
    The viewport wipes to pitch black and the server stays ready for the next machine.
  • Stop everything completely: 
      - Click "🛑 Terminate Background Processes & Exit" in the top-right "⋮" menu or Settings.
      - OR double-click STOP_MAIN_PC.vbs (silent) or STOP_MAIN_PC.bat (console).
      - Closing the Controller window also terminates the background server automatically.

================================================================================

  FIREWALL (if Remote PC can't connect over LAN)
  ──────────────────────────────────────────────
  Run this ONCE in an Admin Command Prompt on this PC:

    netsh advfirewall firewall add rule name="EnterpriseRemote ControlPlane" ^
      protocol=TCP dir=in localport=5293 action=allow

================================================================================
