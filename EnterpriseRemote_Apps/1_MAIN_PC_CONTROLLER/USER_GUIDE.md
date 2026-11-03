# Enterprise Remote Access — Main PC Controller Guide

> **Zero-Trust Encrypted Remote Desktop & Server Administration Console**  
> FIPS 140-3 Compliant • Direct3D Direct Duplication • Hardware Input Injection

---

## 1. Quick Start (1-Click Launching)

| File | Mode | Description |
| :--- | :--- | :--- |
| **`START_MAIN_PC.vbs`** | ⭐ **Recommended** | **1-Click Silent Launcher.** Starts the Control Plane background server (Port 5293) and launches the Operator Console GUI with **zero flashing command windows**. |
| **`START_MAIN_PC.bat`** | Alternative | Batch launcher. Launches the background server and GUI. |
| **`OPEN_WEB_CONTROLLER.bat`** | Web Browser | Launches the modern Cyber UI directly in your default web browser (Chrome, Edge, Brave, etc.) at `http://127.0.0.1:5293`. |
| **`STOP_MAIN_PC.vbs`** | 🛑 **1-Click Stop** | **Silently terminates all Main PC processes**: GUI, background server, and any running cloudflared edge tunnels. Releases Port 5293 instantly. |
| **`STOP_MAIN_PC.bat`** | 🛑 **Interactive Stop** | Displays a status console confirming the termination of each background process and port release. |

---

## 2. Folder Contents & Architecture

```
1_MAIN_PC_CONTROLLER/
├── START_MAIN_PC.vbs                 ← 1-Click silent launcher (GUI + Server)
├── START_MAIN_PC.bat                 ← Batch launcher
├── STOP_MAIN_PC.vbs                  ← 1-Click silent stopper (Kills GUI, Server, Tunnels)
├── STOP_MAIN_PC.bat                  ← Interactive stopper with status feedback
├── OPEN_WEB_CONTROLLER.bat           ← Launches Web Controller in browser
├── START_TAILSCALE_MODE.bat          ← Activates Tailscale P2P WireGuard WAN mode
├── START_TAILSCALE_MODE.vbs          ← Silent Tailscale mode launcher
├── START_PUBLIC_INTERNET_TUNNEL.bat  ← 1-Click Cloudflare Edge Tunnel (connect anywhere)
├── USER_GUIDE.md                     ← This comprehensive user manual
├── README_MAIN_PC.txt                ← Quick text reference
├── ADMIN_ACCOUNTS.txt                ← Pre-seeded administrator directory & logins
├── Bin/                              ← Binaries (do not move or modify)
│   ├── EnterpriseRemote.Controller.exe    ← GPU-accelerated WebView2 native desktop shell
│   ├── EnterpriseRemote.ControlPlane.exe  ← ASP.NET Core API server & WebSocket relay
│   ├── cloudflared.exe                    ← Cloudflare edge tunnel binary
│   └── wwwroot/                           ← Modern cyber dark UI assets
```

---

## 3. Network Connection Modes

The Controller supports three versatile network routing modes to connect with your remote machines:

### Mode A: Local Wi-Fi / LAN (Lowest Latency, Same Network)
1. Ensure both your Main PC and Remote PC are connected to the same Wi-Fi router or office switch.
2. Open the **Connections & Endpoint Inventory** modal (click **Connections** or the **Gateway chip**).
3. The dialog automatically detects and shows your Wi-Fi IP (e.g. `192.168.1.50`).
4. On your Remote PC, enter this IP during enrollment or in `server_url.txt`.

### Mode B: Tailscale B2B Mode (Encrypted Peer-to-Peer UDP over Internet)
1. Double-click **`START_TAILSCALE_MODE.vbs`** (or `.bat`).
2. The controller binds to your Tailscale WireGuard adapter (IP: `100.x.x.x:5293`).
3. Provides sub-millisecond encryption and direct UDP hole-punching without third-party server exposure.

### Mode C: Cloudflare Edge Tunnel (Zero-Setup Connect From Anywhere)
1. Double-click **`START_PUBLIC_INTERNET_TUNNEL.bat`**.
2. A free, encrypted HTTPS tunnel URL is automatically generated (e.g. `https://random-words.trycloudflare.com`).
3. Click **"Use Active Tunnel"** in the controller or paste this URL into your Remote PC streamer.
4. Streams securely through firewalls, coffee shop Wi-Fi, and mobile hotspots with zero router port forwarding.

---

## 4. UI Layout & Controls Reference

### Top Header Bar
- **App Branding & Shield**: Displays the Enterprise Remote logo.
- **Gateway Status Chip**: Displays the active connection mode (e.g. `Localhost:5293`, `Wi-Fi: 192.168.x.x`, `Tunnel`). Clicking opens network gateway configuration.
- **Target Workstation Pill**: Shows the currently selected machine (e.g. `🟢 DELL_G15 (DEV-7182E2A8) — Live Online`).
- **Session State Badge**: Indicates `IDLE`, `CONNECTING`, `LIVE STREAMING`, or `RECONNECTING`.
- **Start Remote Session Button**: Initiates authenticated, cryptographic zero-trust streaming with the target device.
- **Disconnect Button**: Closes the active session and resets the viewport.
- **Three-Dot Menu (`⋮`)**:
  - **Operator Identity**: Quick switch between Admin, Operator, and Auditor accounts.
  - **Target Workstation**: Quick switch between enrolled machines.
  - **Quick PIN / Key**: Enter or view permanent authorization keys.
  - **Permanent Master Keys**: Manage universal keys valid across all endpoints.
  - **Zero-Trust Audit Trail**: View real-time security events, timestamps, and operator actions.
  - **🔄 Reset & Refresh Connection**: Flushes relay buffers, wipes cached frames, and refreshes device inventory.
  - **🛑 Terminate Background Processes & Exit**: Kills all background services and exits the controller.

### Secondary Control Toolbar
- **Scale Mode Toggle (`FIT` / `FILL` / `1:1`)**:
  - **Fit Screen (Default)**: Preserves exact remote aspect ratio with letterboxes.
  - **Stretch (Fill)**: Fills 100% of the window for widescreen displays.
  - **1:1 Native**: Pixel-perfect native resolution with direct coordinate mapping.
- **Control Toggle (`Control: ON / OFF`)**: Enables or pauses mouse and keyboard input forwarding to the remote PC.
- **Screen Switcher (`Auto ▾` / `Display 1` / `Display 2`)**:
  - Dynamically switches the capture engine between multiple physical monitors attached to the remote laptop without disconnecting.
- **Desktop Session Switcher (`Default` / `Winlogon` / `Safe Exam Browser`)**:
  - Dynamically attaches the capture engine to the active Windows desktop context (regular user desktop, UAC/lock screen, or isolated exam browser).
- **Quick Key Injections (`Keys ▾`)**:
  - Injects native system keystrokes: `Ctrl + Alt + Del`, `Win + L` (Lock), `Win + R` (Run), `Task Manager` (`Ctrl+Shift+Esc`), `Alt + Tab` (App Switcher), `Windows Key`, `Escape`.
- **LAN / WAN / 4K Tuning**: Quick performance presets optimizing framerate, compression, and bandwidth.
- **Hosts & Connections Modal (`HOSTS`)**: Opens paired machine inventory and new device pairing wizard.
- **Settings (`SETTINGS` / `⚙`)**: Opens stream quality sliders, resolution selectors, and advanced diagnostics.
- **Fullscreen (`FULL` / `F11`)**: Enters borderless, distraction-free fullscreen mode with zero OS title bars.

### Fullscreen Floating Island HUD
When you enter Fullscreen (`F11`), a sleek draggable cyber island bar appears at the top:
- **Grip (`⋮⋮`)**: Click and drag smoothly anywhere across the screen. Clamped safely to your monitor borders.
- **Screen Dropdown (`Screen ▾`)**: Switch displays or desktop sessions directly inside fullscreen.
- **Quick Keys Dropdown (`Keys ▾`)**: Inject system shortcuts without leaving fullscreen.
- **Settings Icon (`⚙`)**: Opens the Settings and Diagnostics dialog directly over the fullscreen stream.
- **Collapse Arrow (`▲`)**: Tucks the island bar away for 100% edge-to-edge view. Move your cursor to the top edge to reveal it again.
- **End Session (`⏹ End`)**: Disconnects and exits fullscreen cleanly.

---

## 5. How to Properly Disconnect vs. Terminate Everything

### Disconnecting a Session
- Click the **Disconnect** button on the top toolbar, or click **⏹ End** on the floating island.
- **What happens**: The video stream stops, the canvas is immediately cleared to pitch dark (`#07090e`), and the cyber splash screen (**Remote Viewport Ready**) appears.
- The background server remains running in case you want to connect to another device or view the audit logs.

### Terminating All Background Services Completely
To stop all processes (when you are done for the day or want a 100% fresh start):
- **Option 1 (From UI)**: Click the three-dot menu (`⋮`) or open **Settings → Diagnostics**, and click **"🛑 Terminate Background Processes & Exit"**.
- **Option 2 (1-Click Desktop File)**: Double-click **`STOP_MAIN_PC.vbs`** (silent) or **`STOP_MAIN_PC.bat`** (console).
- **Option 3 (Closing Window)**: Closing the main Controller window now automatically stops its background `ControlPlane.exe` server.

---

## 6. Troubleshooting & FAQs

### Problem: "The screen shows a frozen frame from a previous session"
- **Solution**: Click **🔄 Reset & Refresh Connection** in the three-dot menu, or run **`STOP_MAIN_PC.vbs`** and launch again. The stream relay now automatically wipes all cached frames when hosts or viewers disconnect.

### Problem: "Port 5293 is already in use / Connection timeout"
- **Solution**: A previous zombie server process was lingering. Double-click **`STOP_MAIN_PC.bat`** to force-kill all instances and release Port 5293, then double-click **`START_MAIN_PC.vbs`**.

### Problem: "Remote PC shows Offline"
1. Verify the Remote PC streamer is running (look for the shield icon in the Remote PC's system tray).
2. Ensure both PCs are on the same Wi-Fi, or both connected via Tailscale or Cloudflare Tunnel.
3. Check Windows Firewall on the Main PC. Run once in an Admin Command Prompt on Main PC:
   ```cmd
   netsh advfirewall firewall add rule name="EnterpriseRemote ControlPlane" protocol=TCP dir=in localport=5293 action=allow
   ```

