================================================================================
  ENTERPRISE REMOTE ACCESS — REMOTE PC (DESKTOP STREAMER)
================================================================================

  QUICK START & WHAT TO RUN WHEN
  ───────────────────────────────
  ⭐  Double-click:  START_REMOTE_STREAMING.bat       ← RECOMMENDED (auto-prompts Admin for full UIPI control)
  OR  Double-click:  START_REMOTE_STREAMING.vbs       ← Silent system tray launcher
  🛑  Double-click:  STOP_REMOTE_STREAMING.vbs        ← 1-CLICK SILENT STOP (terminates all streaming)
  🛑  Double-click:  STOP_REMOTE_STREAMING.bat        ← Interactive stop with status confirmation
  🛡️  Right-click:   Install_Windows_Service_Admin.bat ← Run as Admin for 24/7 unattended boot-time service
  📖  Open file:     USER_GUIDE.md                    ← Comprehensive step-by-step user manual

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

  Already enrolled? Just double-click START_REMOTE_STREAMING.bat — that's it!

================================================================================

  FOLDER CONTENTS
  ───────────────
  START_REMOTE_STREAMING.bat       ← Batch launcher (recommended, elevates to Admin)
  START_REMOTE_STREAMING.vbs       ← 1-click silent launcher (runs in System Tray ONLY)
  STOP_REMOTE_STREAMING.vbs        ← 1-click silent stopper (kills capture agent and service)
  STOP_REMOTE_STREAMING.bat        ← Interactive stopper with confirmation
  1_CLICK_ENROLL_REMOTE_PC.bat     ← First-time pairing wizard (LAN IP, Tailscale, or Tunnel URL)
  RESET_ENROLLMENT.bat             ← Clears pairing identity and allows re-enrolling
  SETUP_TAILSCALE_REMOTE.bat       ← Confirms Tailscale connectivity on this Remote PC
  Install_Windows_Service_Admin.bat← Installs Session 0 Windows Service for 24/7 access
  Uninstall_Windows_Service_Admin.bat← Removes the background Windows Service cleanly
  USER_GUIDE.md                    ← Comprehensive user manual
  README_REMOTE_PC.txt             ← This file
  Bin\                             ← Binaries (don't touch, don't move)
    EnterpriseRemote.Host.Service.exe   ← Streaming engine & hardware input injector
    EnterpriseRemote.Host.UserAgent.exe ← System tray agent with unblocked capture engine

================================================================================

  HOW TO STOP STREAMING
  ─────────────────────
  • Silent: Double-click STOP_REMOTE_STREAMING.vbs.
  • Interactive: Double-click STOP_REMOTE_STREAMING.bat.
  • System Tray: Right-click the cyber shield icon in your taskbar (bottom-right ^)
    and select "Exit / Stop Streaming".

================================================================================
