# 🛡️ Enterprise Remote Access — Quick Start Guide

> **Zero-setup, zero-terminal, enterprise-grade desktop remote access.**  
> Built with .NET 8, ultra-low latency WebSocket/DTLS streaming, unblocked exam & protected window affinity stripping, hardware input injection, and a modern dark cybersecurity UI.

---

## 📁 Folder Structure

```text
EnterpriseRemote_Apps/
├── 1_MAIN_PC_CONTROLLER/
│   ├── ⭐ START_MAIN_PC.vbs                ← Double-click (ZERO console windows, pure dark UI)
│   ├── START_MAIN_PC.bat                   ← Alternative batch launcher
│   ├── 🌐 START_PUBLIC_INTERNET_TUNNEL.bat  ← 1-Click Cloudflare Edge Tunnel (Connect Anywhere, no setup)
│   ├── 🚀 START_TAILSCALE_MODE.bat          ← 1-Click Tailscale WireGuard Mode (Best WAN Quality)
│   ├── START_TAILSCALE_MODE.vbs            ← Silent Tailscale mode launcher (no console window)
│   ├── ADMIN_ACCOUNTS.txt                  ← Authorized administrator directory & fast login info
│   ├── start_tunnel.ps1                    ← Cloudflare tunnel PowerShell automation engine
│   ├── start_tailscale_tunnel.ps1          ← Tailscale mode PowerShell helper & IP display
│   ├── README_MAIN_PC.txt                  ← Detailed notes & firewall setup
│   └── Bin/                                ← ControlPlane + Controller binaries + cloudflared + assets
│
├── 2_REMOTE_PC_STREAMER/                   ← 📂 COPY THIS to your Remote/Second PC
│   ├── ⭐ START_REMOTE_STREAMING.vbs       ← Double-click (runs in System Tray ONLY, NO taskbar button)
│   ├── START_REMOTE_STREAMING.bat          ← Alternative batch launcher
│   ├── STOP_REMOTE_STREAMING.bat           ← 1-click stop all streaming processes
│   ├── 1_CLICK_ENROLL_REMOTE_PC.bat        ← First-time pairing wizard (LAN IP, Tunnel URL, or Tailscale IP)
│   ├── 🚀 SETUP_TAILSCALE_REMOTE.bat       ← Confirms Tailscale connectivity on Remote PC
│   ├── 🛡️ Install_Windows_Service_Admin.bat ← Installs Session 0 Windows Service for 24/7 access
│   ├── Uninstall_Windows_Service_Admin.bat ← Removes the background Windows Service cleanly
│   ├── README_REMOTE_PC.txt                ← Detailed remote host notes
│   └── Bin/                                ← Host Service + UserAgent unblocked capture engine
│
├── ADMIN_ACCOUNTS.txt                      ← Root directory of pre-configured administrator credentials
├── DEPLOYMENT_SUMMARY.md                   ← Architecture, design tokens & technical verification
└── QUICK_START_GUIDE.md                    ← This file
```

---

## ⚡ Getting Started (5-Minute Setup)

### Step 1 — On Your Main PC (Operator / Controller)

1. Open `EnterpriseRemote_Apps\1_MAIN_PC_CONTROLLER\`
2. **Double-click `START_MAIN_PC.vbs`**
3. The Controller GUI will appear on your screen with the dark enterprise UI.
4. The Control Plane API backend starts silently in the background (no console windows).
5. In Card 1: Click **`[Admin Accounts ▾]`** to log in as `admin@corp.internal`, `sujal@corp.internal`, or your custom ID with 1 click!

> 💡 **LAN Firewall note:** If connecting across local Wi-Fi, run this ONCE in an Admin Command Prompt on the Main PC:  
> `netsh advfirewall firewall add rule name="EnterpriseRemote" protocol=TCP dir=in localport=5293 action=allow`

---

### Step 2 — Connect Across the Internet (Optional: Coffee Shop, Hotspot, Anywhere)

If your Remote PC is on a different network (different Wi-Fi, 4G/5G phone hotspot, or another location):
1. On Main PC: Double-click **`START_PUBLIC_INTERNET_TUNNEL.bat`**.
2. A secure Cloudflare edge tunnel launches in seconds and **automatically copies your public HTTPS URL to your clipboard** (e.g. `https://xxxx-xxxx.trycloudflare.com`).
3. Keep this tunnel window open while streaming over the Internet.

---

### Step 3 — On Your Remote PC (Laptop to Control)

1. **Copy the `2_REMOTE_PC_STREAMER\` folder** to your second laptop (via USB drive, network share, or Google Drive/OneDrive).
2. On the Remote PC: Double-click **`1_CLICK_ENROLL_REMOTE_PC.bat`**.
3. When prompted:
   - Enter your Main PC's **LAN IP** (for same Wi-Fi) or the **Cloudflare Tunnel URL** from Step 2.
   - Enter an **Invitation Code** generated in the Main PC Controller ("Connect Other PC" dialog).
4. Pairing finishes in ~5 seconds!
5. An unattended permanent key (`PERM-XXXXXXXXXXXX`) is automatically generated and securely stored for 24/7 zero-touch access.
6. The streaming engine starts immediately — look for the 🛡️ shield icon in your System Tray (bottom-right `^`)!

---

### Step 4 — Connect & Control!

1. In the Main PC Controller window:
   - Your remote PC will appear in the **"Select Remote Workstation"** dropdown.
   - The permanent access key is auto-filled for instant unattended access.
   - Click the green **"START REMOTE SESSION"** button!
2. The remote desktop appears instantly in high frame-rate streaming!

---

## 🔁 Daily Use (Subsequent Launches)

| Computer | Action |
|---|---|
| **Main PC** | Double-click `START_MAIN_PC.vbs` |
| **Remote PC** | Double-click `START_REMOTE_STREAMING.vbs` *(or run as Windows Service)* |

No re-enrollment needed. The cryptographic keypair and credentials persist safely.

---

## 🛡️ Running as an Unattended 24/7 Windows Service

To allow the remote laptop to be accessed even before any user logs in, at the Windows lock screen, or after a system restart:
1. Complete enrollment first using `1_CLICK_ENROLL_REMOTE_PC.bat`.
2. Right-click **`Install_Windows_Service_Admin.bat`** and choose **"Run as Administrator"**.
3. The host service is registered with Windows Service Control Manager in privileged Session 0 and set to automatic startup (`start=auto`).
4. To remove it at any time: Right-click **`Uninstall_Windows_Service_Admin.bat`** and choose "Run as Administrator".

---

## 🎮 Controller UI Features & Shortcuts

| Feature | Action / Control |
|---|---|
| **High-Refresh Rate Profiles** | Click top presets: **144** (144 FPS / 1080p), **120** (120 FPS / 720p Cloudflare Ultra), **60** (LAN Direct), **WAN** (40 FPS Adaptive), **4K** (30 FPS Native) |
| **FPS Quick Chips & Slider** | Tuning ▾ → Slider spans **15 to 200 FPS** with 1-click quick chips: 30, 60, 100, 120, 144, 200 |
| **Stream Resolution Chips** | Tuning ▾ → **🚀 540p Turbo** (Lowest latency WAN), **⚡ 720p HD**, **🖥️ 1080p**, **💎 Native** |
| **Full Screen Mode** | Press `F11` or click "Full Screen" |
| **Exit Full Screen** | Press `Esc` or `F11` |
| **Display Scale Mode** | Click the **"Scale Mode"** button to cycle between:<br>• **Fit Screen** (preserves aspect ratio with letterboxes)<br>• **Stretch to Fill (100% Screen)** (fills entire viewer window)<br>• **1:1 Native Resolution** (pixel-perfect native resolution with 100% mouse precision) |
| **Unattended 24/7 Access** | Auto-filled `PERM-XXXXXXXXXXXX` key bypasses remote consent popups |
| **Unblocked Exam Browsers** | Automatically removes `WDA_EXCLUDEFROMCAPTURE` / `WDA_MONITOR` window affinity so Safe Exam Browser and lock-down apps stream crystal clear without black screens |
| **Desktop Switch Sync** | Automatically syncs and attaches capture to active WinSta0 desktops (UAC elevation, LockApp, secure exam desktops) |
| **Remote Ctrl+Alt+Del** | Actions ▾ → "Send Ctrl+Alt+Del" |
| **Lock Remote Screen** | Actions ▾ → "Lock Remote Workstation" |
| **Screenshot Capture** | "Capture" button in header (saved to disk with timestamp) |
| **Toggle Remote Input** | "Control: ON/OFF" toggle in top bar |
| **Paste Clipboard** | Actions ▾ → "Paste Local Clipboard" (with sanitization) |
| **Fast Admin Switcher** | Card 1 → Click "[Admin Accounts ▾]" for 1-click login |
| **Emergency Disconnect** | Red power button (top-right) |

---

## 🛑 Stopping Everything

| Computer | Action |
|---|---|
| **Main PC** | Close the Controller window (or stop processes in Task Manager) |
| **Remote PC** | Double-click `STOP_REMOTE_STREAMING.bat` |

---

## 🔧 Troubleshooting & Network Switching Checklist

| Issue | Resolution |
|---|---|
| **Switching Networks (Wi-Fi ↔ Cloudflare ↔ Tailscale)** | Simply re-run `1_CLICK_ENROLL_REMOTE_PC.bat` on the Remote PC with the new Gateway URL (or switch the Gateway Chip in the Controller UI). Previous streamer processes are terminated cleanly to release GPU DXGI duplication handles, and the connection reconnects instantly. |
| **Deterministic Device Deduplication** | Re-enrolling never creates duplicate "ghost" devices. The Control Plane reuses the canonical device record based on your machine's hostname, ensuring the Controller UI always points to the live workstation. |
| **Key Hierarchy & Coordination** | • **Universal PIN**: `1234` or `0000` for fast unattended access.<br>• **Permanent Master Key**: `PERM-MASTER-...` configured once works across all enrolled machines.<br>• **Device Token**: Unique `PERM-XXXX` saved in `device_identity.json`.<br>All keys coordinate seamlessly without lockouts. |
| **Terminal Output on Enrollment** | `1_CLICK_ENROLL_REMOTE_PC.bat` keeps the terminal open with live progress messages (`[*] Connecting...`, `[+] Key validated`, `[+] Enrolled successfully`), followed by a 5-second countdown on success or a pause on failure. |
| **Host Offline Status in Viewer** | If the viewer connects before the Remote PC starts streaming, the viewer HUD displays `[Waiting for Remote PC (Host Offline)]` rather than freezing, and transitions to live video the moment the streamer starts. |

| Issue | Resolution |
|---|---|
| **Cannot connect over LAN** | Ensure `START_MAIN_PC.vbs` is active on Main PC. Add firewall rule on port 5293. Verify both PCs are on the same Wi-Fi. |
| **PCs on different networks** | Use `START_PUBLIC_INTERNET_TUNNEL.bat` on Main PC and paste the `https://*.trycloudflare.com` URL into Remote PC's `1_CLICK_ENROLL_REMOTE_PC.bat`. |
| **Invitation Code expired** | Codes expire after 2 hours. Click "Connect Other PC (Enroll Host)" in Controller to generate a new token. |
| **Remote device not in dropdown** | Click the 🔄 refresh icon next to the device dropdown or re-login. |
| **Re-pairing / fresh start** | Run `1_CLICK_ENROLL_REMOTE_PC.bat` on the Remote PC to overwrite identity with new connection details. |
