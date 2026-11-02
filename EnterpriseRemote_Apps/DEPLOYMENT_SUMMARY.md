# 🛡️ Enterprise Remote Access — Deployment & Architecture Reference

## 1. Simplified Folder Organization

The entire application suite is organized into a clean, distribution-ready folder called `EnterpriseRemote_Apps/` at the root of the workspace. All internal binaries, assemblies, and dependencies are encapsulated inside `Bin/` folders so operators and end-users only ever interact with **1-click silent launchers**:

```text
EnterpriseRemote_Apps/
├── 1_MAIN_PC_CONTROLLER/
│   ├── START_MAIN_PC.vbs               <-- ⭐ DOUBLE-CLICK THIS (Silent, 0 terminal windows!)
│   ├── START_MAIN_PC.bat               <-- Alternative batch launcher (exits immediately)
│   ├── START_PUBLIC_INTERNET_TUNNEL.bat <-- 🌐 1-Click Cloudflare Tunnel (Connect Anywhere)
│   ├── ADMIN_ACCOUNTS.txt               <-- Pre-seeded admin directory & fast credentials
│   ├── start_tunnel.ps1                 <-- Self-contained edge tunnel automation engine
│   ├── README_MAIN_PC.txt              <-- Quick notes & setup instructions
│   └── Bin/                            <-- ControlPlane + Controller binaries + cloudflared + icons
│
├── 2_REMOTE_PC_STREAMER/               <-- 📂 COPY THIS ENTIRE FOLDER TO YOUR OTHER LAPTOP
│   ├── START_REMOTE_STREAMING.vbs      <-- ⭐ DOUBLE-CLICK THIS (Silent System Tray only, 0 taskbar!)
│   ├── START_REMOTE_STREAMING.bat      <-- Alternative batch launcher (exits immediately)
│   ├── STOP_REMOTE_STREAMING.bat       <-- 1-Click to stop streaming & exit
│   ├── 1_CLICK_ENROLL_REMOTE_PC.bat    <-- First-time enrollment (LAN IP or Tunnel URL)
│   ├── Install_Windows_Service_Admin.bat <-- 🛡️ Installs Session 0 Windows Service for 24/7 access
│   ├── Uninstall_Windows_Service_Admin.bat <-- Removes the background Windows Service cleanly
│   ├── README_REMOTE_PC.txt            <-- Quick remote host notes
│   └── Bin/                            <-- Internal streaming engine, affinity stripper & service
│
├── ADMIN_ACCOUNTS.txt                  <-- Master administrative directory
├── DEPLOYMENT_SUMMARY.md               <-- Architecture, tokens & build verification reference
└── QUICK_START_GUIDE.md                <-- Complete user guide & troubleshooting
```

---

## 2. One-Click Execution Flow (Zero Terminals)

### On Main PC
* **File to run:** `EnterpriseRemote_Apps\1_MAIN_PC_CONTROLLER\START_MAIN_PC.vbs` (or `START_MAIN_PC.bat`)
* **What it does automatically:**
  1. Starts the background Control Plane service on `http://0.0.0.0:5293` silently with zero command prompt windows.
  2. Launches the modern `EnterpriseRemote.Controller.exe` application with custom branding, dark theme, and icons.
  3. No command prompt or terminal windows are displayed on your taskbar!
  4. Quick Admin Login: Click `[Admin Accounts ▾]` to log in in 1 click without manual typing.

### On Remote PC (Laptop to control)
* **File to run:** `EnterpriseRemote_Apps\2_REMOTE_PC_STREAMER\START_REMOTE_STREAMING.vbs` (or `START_REMOTE_STREAMING.bat`)
* **What it does automatically:**
  1. Checks if the device has an identity (`device_identity.json`).
  2. If not enrolled, automatically triggers `1_CLICK_ENROLL_REMOTE_PC.bat` to link with the Main PC.
  3. If enrolled, starts the background streaming agent silently.
  4. **Taskbar Behavior:** The application DOES NOT show up in the main taskbar row. It runs **exclusively in the hidden section of the taskbar (System Tray ^)** with a cyber-shield icon!
  5. The remote laptop's physical mouse, trackpad, and keyboard remain 100% free and responsive.

---

## 3. System Requirements If Switching to Any New PC

If you take these folders to a completely fresh PC (either as Main PC or Remote PC), here are the only requirements:

| Component | Requirement | Direct Download Link |
| :--- | :--- | :--- |
| **Operating System** | Windows 10 (64-bit) or Windows 11 | Supported out-of-the-box |
| **Runtime** | **Microsoft .NET 8.0 Desktop Runtime (x64)** | [Download .NET 8.0 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/8.0/runtime) |
| **Hardware** | Any modern x64 CPU, 2 GB RAM | Very low CPU & memory consumption |
| **Network** | Wi-Fi / LAN, or Internet (Cloudflare Tunnel) | Port `5293` on local LAN, or port 443 via Cloudflare |

---

## 4. Custom Enterprise App Logo & Icon Branding

* **Design Concept:** Minimalist cyber-security shield emblem with electric indigo & glowing neon cyan gradients, featuring an arrow cursor and wireless streaming waves.
* **Icon Formats:** Embedded high-resolution multi-size `.ico` (16×16 to 256×256) embedded as native Windows executable icons for:
  - `EnterpriseRemote.Controller.exe`
  - `EnterpriseRemote.Host.UserAgent.exe`
  - `EnterpriseRemote.Host.Service.exe`
* **Taskbar & Tray:**
  - Main PC: Clean Controller window with custom cyber-shield taskbar icon.
  - Remote PC: Clean cyber-shield icon residing in the hidden notification area (`^`). Zero taskbar buttons!

---

## 5. High-Refresh (100–144–200 FPS) Streaming, Cloudflare QUIC & Zero-Bufferbloat Pipeline

* **Detailed Manual:** [docs/connect_from_anywhere_manual.md](file:///c:/My%20Projects/SECURE%20AND%20ENTERPRISE%20STREAMING%20APP/docs/connect_from_anywhere_manual.md)
* **Single-Flight Transmission Pacing:** The streaming engine incorporates a zero-wait network gate (`_networkSendGate.Wait(0)`) that prevents queuing in the OS TCP stack while streaming over Cloudflare. Stale intermediate frames are discarded at the source; only the freshest screen capture is transmitted.
* **Fractional SIMD Scaling:** Supports dynamic downscaling to **540p Turbo**, **720p HD**, **1080p FHD**, or **Native Monitor Resolution** with sub-pixel mapped SIMD box filtering (`(dx * srcW) / dstW`) preventing aspect ratio distortion.
* **Cloudflare QUIC Edge Tunnel:** Updated tunnel launcher utilizes `--protocol auto --edge-ip-version 4 --post-quantum=false --compression-quality 0` to prioritize UDP QUIC transport with under 100ms latency even on remote WAN links.
* **Multi-Viewer Channel Isolation:** Control plane `StreamRelayHub` maintains isolated bounded channels per client and prevents control packets (cursor updates, latency pongs) from corrupting the cached JPEG frame stream.
* **Permanent Unattended Access:** Auto-assigned `PERM-XXXXXXXXXXXX` keys enable unattended 24/7 control without human consent popups on the remote PC.
* **Display Scaling & Mouse Precision:** Cycle between:
  - **Fit Screen:** Preserves aspect ratio with letterboxes.
  - **Stretch to Fill (100% Screen):** Eliminates letterboxes and fills the entire screen.
  - **1:1 Native Resolution:** Pixel-perfect native resolution with 100% sub-pixel click alignment.

---

## 6. Unblocked Exam & Protected Windows Streaming

* **Window Affinity Stripping:** `WindowAffinityManager` dynamically strips `WDA_EXCLUDEFROMCAPTURE` (`0x00000011`) and `WDA_MONITOR` (`0x00000001`) protection flags from lock-down exam browsers (e.g., Safe Exam Browser, Proctorio, test platforms) so they never render as black boxes.
* **Desktop Switching Sync:** `DesktopSwitchManager` detects transitions to alternate WinSta0 desktops (e.g., `WinSta0\SafeExamBrowser`, `WinSta0\Winlogon`, UAC secure desktop) and re-attaches capture and input worker threads seamlessly.
* **DirectComposition & GDI Failover:** Fallback capture pipelines ensure no frame drop or blank screen occurs if DXGI desktop duplication is temporarily unavailable.

---

## 7. Build Verification & Integrity Status

* **Solution:** `EnterpriseRemote.sln`
* **Configuration:** `Release | Any CPU / x64`
* **Compiler Status:** Build Succeeded with **0 Warning(s)**, **0 Error(s)**.
* **Automated Unit Tests:** **122 passed, 0 failed, 0 skipped** (100% pass rate).
* **Binary Synchronization:** Fresh Release binaries, PDBs, configs, and assets synchronized across `EnterpriseRemote_Apps/1_MAIN_PC_CONTROLLER/Bin`, `EnterpriseRemote_Apps/2_REMOTE_PC_STREAMER/Bin`, `RemotePC_Package/`, and `dist/`.
