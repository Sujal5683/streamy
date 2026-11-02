================================================================================
  ENTERPRISE REMOTE ACCESS — REMOTE PC (DESKTOP STREAMER)
================================================================================

  QUICK START & WHAT TO RUN WHEN
  ───────────────────────────────
  ⭐  Double-click:  START_REMOTE_STREAMING.bat       ← RECOMMENDED (auto-prompts Admin for full UIPI control)
  OR  Double-click:  START_REMOTE_STREAMING.vbs       ← Silent system tray launcher
  🛡️  Right-click:   Install_Windows_Service_Admin.bat ← Run as Admin for 24/7 unattended boot-time service

  WHAT TO RUN WHEN & FIREWALL CLARIFICATION:
  • Remote PC (this PC): Does NOT listen on any port. It only connects OUTBOUND to the Main PC.
    You do NOT need to open any firewall ports on this Remote PC!
  • Full Control over Task Manager / Admin apps: Run START_REMOTE_STREAMING.bat (auto-elevates to Admin).
    This allows synthetic mouse clicks to control Task Manager, elevated Command Prompt, Services,
    and applications that install low-level hooks or attempt to trap the mouse cursor.
  • Unattended 24/7 Access: Run Install_Windows_Service_Admin.bat once as Administrator.

  First time on this PC?
    → If using Tailscale WAN: Double-click SETUP_TAILSCALE_REMOTE.bat to verify connection.
    → Double-click 1_CLICK_ENROLL_REMOTE_PC.bat to pair with the Main PC.
    → Enter your Main PC's IP (LAN), Tailscale IP (100.x.x.x:5293), or Cloudflare URL.
    → Enter the Invitation Code from the Main PC Controller.

  Already enrolled? Just double-click START_REMOTE_STREAMING.vbs — that's it!

================================================================================

  FOLDER CONTENTS
  ───────────────
  START_REMOTE_STREAMING.vbs       ← 1-click silent launcher (runs in System Tray ONLY)
  START_REMOTE_STREAMING.bat       ← Batch launcher (exits immediately)
  STOP_REMOTE_STREAMING.bat        ← 1-click to stop all streaming processes
  1_CLICK_ENROLL_REMOTE_PC.bat     ← First-time pairing wizard (LAN IP, Tailscale, or Tunnel URL)
  SETUP_TAILSCALE_REMOTE.bat       ← Confirms Tailscale connectivity on this Remote PC
  Install_Windows_Service_Admin.bat← Installs Session 0 Windows Service for 24/7 access
  Uninstall_Windows_Service_Admin.bat← Removes the background Windows Service cleanly
  README_REMOTE_PC.txt             ← This file
  Bin\                             ← Binaries (don't touch, don't move)
    EnterpriseRemote.Host.Service.exe   ← Streaming engine & hardware input injector
    EnterpriseRemote.Host.UserAgent.exe ← System tray agent with unblocked capture engine

================================================================================

  TRAY ICON & USER EXPERIENCE
  ───────────────────────────
  • The streaming agent runs in your System Tray (bottom-right notification area, click ^).
  • Look for the cyber-shield icon. Right-click it for quick options and status.
  • NO taskbar button will appear — runs quietly in the background without disturbing work.
  • Your laptop's physical keyboard, trackpad, and mouse remain 100% responsive.

================================================================================

  ADVANCED UNBLOCKED STREAMING CAPABILITIES
  ─────────────────────────────────────────
  • Proactive Window Affinity Stripping: Automatically removes WDA_EXCLUDEFROMCAPTURE
    and WDA_MONITOR protection flags on exam browsers (Safe Exam Browser / lock-down apps)
    so the operator can view and control without black screens.
  • Desktop Switch Sync: Automatically detects and attaches to active WinSta0 input
    desktops (UAC secure elevation, LockApp, secure exam desktops).
  • Hardware Input Injection: Sends native Win32 hardware scancodes and mouse inputs
    even inside elevated and privileged windows.

================================================================================

  RUN AS 24/7 WINDOWS SERVICE (UNATTENDED ACCESS)
  ────────────────────────────────────────────────
  If you want this PC to be controllable even when no user is logged in, or right after
  reboot before login:
  1. Enroll this PC first using 1_CLICK_ENROLL_REMOTE_PC.bat.
  2. Right-click "Install_Windows_Service_Admin.bat" and select "Run as Administrator".
  3. The Host Service is registered in Session 0 and starts automatically with Windows!

================================================================================

  TROUBLESHOOTING
  ───────────────
  • "Cannot connect" → Check both PCs are on the same Wi-Fi / LAN, or:
    - FOR BEST SPEED: Use Tailscale (START_TAILSCALE_MODE.bat on Main PC, use 100.x.x.x:5293)
    - FOR ZERO SETUP: Use Cloudflare (START_PUBLIC_INTERNET_TUNNEL.bat on Main PC)
  • Invitation code expired → Codes last 2 hours. Generate a new one in Controller.
  • Windows Firewall blocking → Run on MAIN PC (Admin prompt):
      netsh advfirewall firewall add rule name="EnterpriseRemote" ^
        protocol=TCP dir=in localport=5293 action=allow
  • To re-enroll: delete device_identity.json from Bin\ then re-run 1_CLICK_ENROLL_REMOTE_PC.bat.

================================================================================
