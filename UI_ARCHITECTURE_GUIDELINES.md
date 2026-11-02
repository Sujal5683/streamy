# Enterprise Remote Access — UI Architecture & Design System Guidelines

## Core Directive for All AI Agents & Human Developers

The user interface of Enterprise Remote is unified and shared across both the **Native Windows Desktop Application** and the **Browser Web Controller**:

```
                       ┌──────────────────────────────────────────────┐
                       │   src/EnterpriseRemote.ControlPlane/wwwroot/ │
                       │    - index.html (Modern Cyber UI DOM)        │
                       │    - css/app.css (Cyber Design System)       │
                       │    - js/app.js (Zero-Trust Logic & Streaming)│
                       └──────────────────────┬───────────────────────┘
                                              │
                    ┌─────────────────────────┴────────────────────────┐
                    ▼                                                  ▼
     [ NATIVE WINDOWS CONTROLLER ]                          [ BROWSER WEB CONTROLLER ]
      EnterpriseRemote.Controller.exe                        Chrome / Edge / Firefox
      (Microsoft Edge WebView2 Desktop Shell)                (Direct HTTP/WebSocket Origin)
      - Direct3D 60 FPS GPU Rendering                        - Zero-Install Remote Control
      - Native F11 Borderless Full-Screen                     - http://127.0.0.1:5293
      - System Tray Icon & Shortcuts                         - Cloudflare Internet Tunnel / LAN
      - Auto-starts Control Plane Backend
```

### 1. Where to make UI changes:
- **ALL UI changes must be made in `src/EnterpriseRemote.ControlPlane/wwwroot/`** (`index.html`, `js/app.js`, `css/app.css`).
- After updating files in `wwwroot`, run `scripts/update_bins.ps1` to synchronize all binaries and package folders (`EnterpriseRemote_Apps/1_MAIN_PC_CONTROLLER/Bin/wwwroot`).

### 2. What NOT to do:
- **DO NOT** create or edit WinForms GDI+ controls (`ToolStrip`, `Label`, `LinkLabel`, `CyberButton`, `CyberComboBox`, etc.) in `src/EnterpriseRemote.Controller/UI/`.
- `src/EnterpriseRemote.Controller/UI/RemoteViewerForm.cs` is intentionally a lightweight WebView2 desktop container (~230 lines) that hosts the cyber interface. It must remain lean and clean.

### 3. Feature Parity Checklist:
- **60 FPS Video Streaming**: Hardware-accelerated canvas via WebSockets / WebCodecs.
- **Input Injection**: Mouse clicks, drags, mouse wheel, and full keyboard injection.
- **System Shortcuts**: Quick Keys dropdown (Ctrl+Alt+Del, Alt+Tab, Win+L, etc.).
- **Enrollment Wizard**: 3-step pairing guide with live invitation codes and PowerShell commands.
- **Permanent Master Keys**: Universal unattended access keys that never expire and connect to all devices.
- **Gateway Switching**: Instant 1-click toggling between Localhost, Local Wi-Fi (LAN IP), and Cloudflare Tunnel.
- **Full Screen**: F11 borderless full screen in both native desktop app and browser.

