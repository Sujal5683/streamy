# Streamy — Secure Enterprise Remote Desktop & Ultra-Low Latency Streaming Platform

[![Release](https://img.shields.io/badge/release-Streamy%20V1.0.17-blue.svg)](https://github.com/Sujal5683/streamy/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011%20(x64)-brightgreen.svg)]()
[![Runtime](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![License](https://img.shields.io/badge/license-Proprietary-red.svg)]()

> **Streamy** is an enterprise-grade, high-performance Windows remote desktop access and streaming solution built for ultra-low latency, resilient network traversal, protected window capture, and seamless multi-device control.

---

## 🚀 Key Features

* **⚡ Ultra-Low Latency Engine:** Hardware-accelerated Desktop Duplication API (DXGI) with optimized GDI fallback, multi-threaded sub-millisecond SIMD fractional pixel scaling, single-flight zero-bufferbloat transmission gating, uncapped 100–144–200 FPS high-refresh streaming, and raw WebSocket/QUIC streaming delivering **15–30 ms glass-to-glass LAN latency** and silky-smooth remote control over Cloudflare edge tunnels.
* **🛡️ Session 0 Windows Service & 24/7 Access:** Runs as a dedicated background service (`EnterpriseRemote.Host.Service`) capable of handling Windows lock screens, UAC security prompts, and user logon/logoff transitions automatically.
* **👁️ Protected Window & Exam Affinity Stripping:** Dynamic desktop switch management and window affinity decoupling (`SetWindowDisplayAffinity` stripping) ensures reliable streaming even under exam software, secure browsers, or DRM-protected windows.
* **🔒 Military-Grade Cryptography & Zero-Trust Pairing:** Mutual challenge-response enrollment, Ed25519/ECDSA device identity certificates, AES-256-GCM session payload encryption, and strict role-based access control (RBAC).
* **🌐 Universal Network Traversal:**
  * **LAN Direct Mode:** Automatic multicast UDP beacon discovery and port `5293` direct streaming.
  * **Cloudflare Edge Tunnel (QUIC Optimized):** 1-Click zero-port-forwarding public access via Cloudflare Edge network with `--protocol auto`, zero buffer bloat, and automatic bitrate (ABR) adaptation.
  * **Tailscale Mesh VPN:** Zero-configuration secure private mesh networking across different physical networks.
* **🖥️ Dual Controller Interfaces:**
  * **WPF Desktop Controller:** High-framerate, dark-cybersecurity themed desktop operator console with multi-monitor switching, latency HUD, clipboard synchronization, and hardware input injection.
  * **Web Controller:** Browser-based control plane (`EnterpriseRemote.ControlPlane`) with real-time WebRTC/WebSocket client streaming.
* **🖱️ Silent Execution & Zero Terminals:** 1-Click silent `.vbs` and `.bat` launchers that run streamer hosts silently in the Windows System Tray with zero taskbar intrusion.

---

## 📁 Repository Structure

```text
streamy/
├── EnterpriseRemote_Apps/               # Ready-to-run distribution packages
│   ├── 1_MAIN_PC_CONTROLLER/           # Main operator controller app & control plane
│   │   ├── START_MAIN_PC.vbs           # Silent 1-click launcher (Zero terminal windows)
│   │   ├── START_MAIN_PC.bat           # Batch launcher
│   │   ├── START_PUBLIC_INTERNET_TUNNEL.bat # 1-Click Cloudflare Tunnel
│   │   ├── START_TAILSCALE_MODE.bat    # 1-Click Tailscale Mesh Mode
│   │   ├── ADMIN_ACCOUNTS.txt          # Pre-configured administrative credentials
│   │   └── Bin/                        # ControlPlane & Controller binaries + cloudflared
│   │
│   └── 2_REMOTE_PC_STREAMER/           # Streamer agent for target remote machines
│       ├── START_REMOTE_STREAMING.vbs  # Silent System Tray launcher (No taskbar button)
│       ├── START_REMOTE_STREAMING.bat  # Batch launcher
│       ├── 1_CLICK_ENROLL_REMOTE_PC.bat# 1-Click device pairing wizard
│       ├── Install_Windows_Service_Admin.bat # Installs 24/7 Session 0 Windows Service
│       ├── Uninstall_Windows_Service_Admin.bat # Uninstalls background service
│       └── Bin/                        # Host Service & UserAgent capture engine
│
├── src/                                # Core .NET 8 source code
│   ├── EnterpriseRemote.Common/        # Cryptography, identity, protocols, networking
│   ├── EnterpriseRemote.ControlPlane/  # ASP.NET Core API, WebSockets, enrollment store
│   ├── EnterpriseRemote.Controller/    # WPF dark cyber operator desktop application
│   ├── EnterpriseRemote.Host.Service/  # Session 0 Windows Service orchestrator
│   └── EnterpriseRemote.Host.UserAgent/# Desktop duplication, input injection & capture
│
├── tests/                              # Benchmark & Comprehensive Unit Test Suites
│   ├── EnterpriseRemote.UnitTests/     # 142 unit & integration tests (100% passing)
│   └── EnterpriseRemote.Benchmarks/    # Latency & throughput benchmarking suite
│
├── scripts/                            # Operational, release & automation scripts
│   ├── push_streamy_version.ps1        # Official version bump & Git push script
│   ├── update_bins.ps1                 # Universal binary compilation & packaging
│   ├── start_tunnel.ps1                # Cloudflare tunnel automation
│   └── start_tailscale_tunnel.ps1      # Tailscale tunnel automation
│
├── Latency.md                          # Latency benchmarking & optimization analysis
├── TAILSCALE_SETUP_GUIDE.md            # Comprehensive Tailscale deployment guide
└── UI_ARCHITECTURE_GUIDELINES.md       # Dark cybersecurity UI design tokens & specifications
```

---

## ⚡ Quick Start Guide

### 1. Main PC (Operator / Controller)
1. Navigate to `EnterpriseRemote_Apps/1_MAIN_PC_CONTROLLER/`.
2. Double-click **`START_MAIN_PC.vbs`**.
3. The dark enterprise controller window will appear immediately, and the background control plane service will launch silently on port `5293`.
4. Click **`[Admin Accounts ▾]`** to sign in with pre-seeded credentials or enter your administrator account.

> **LAN Note:** If connecting across local Wi-Fi / LAN, allow port 5293 once by running `ALLOW_FIREWALL_PORT_5293.bat` as Administrator.

### 2. Remote PC (Machine to Stream / Control)
1. Copy `EnterpriseRemote_Apps/2_REMOTE_PC_STREAMER/` to the target machine.
2. Double-click **`1_CLICK_ENROLL_REMOTE_PC.bat`** (or double-click `START_REMOTE_STREAMING.vbs`).
3. Enter the Main PC's IP address (e.g., `http://192.168.1.100:5293`) or Cloudflare/Tailscale URL.
4. The remote agent links with the controller and docks into the **System Tray (`^`)** silently.
5. On the Main PC, click **Connect** to start ultra-low latency streaming and control!

---

## 🛠️ Building From Source

### Prerequisites
* Windows 10 (64-bit) or Windows 11
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
* Visual Studio 2022 / JetBrains Rider / VS Code with C# Dev Kit

### Build Solution
```powershell
dotnet restore EnterpriseRemote.sln
dotnet build EnterpriseRemote.sln -c Release
```

### Run Unit Tests
```powershell
dotnet test tests/EnterpriseRemote.UnitTests/EnterpriseRemote.UnitTests.csproj
```

### Publish Release Packages
```powershell
powershell -ExecutionPolicy Bypass -File scripts/update_bins.ps1
```

---

## 📦 Versioning & Push Convention

All releases in this repository strictly adhere to the convention:

$$\text{Commit Message} = \textbf{"Streamy V<Major>.<Minor>.<Patch>"}$$
$$\text{Git Tag} = \textbf{"Streamy-V<Major>.<Minor>.<Patch>"}$$

### Automated Version Push Script
To commit and push future versions cleanly according to this convention:

```powershell
# Bumps and pushes the specified version
powershell -ExecutionPolicy Bypass -File ./scripts/push_streamy_version.ps1 -Version "1.0.1"

# Or with custom release notes:
powershell -ExecutionPolicy Bypass -File ./scripts/push_streamy_version.ps1 -Version "1.1.0" -Notes "Added multi-monitor support enhancements"
```

---

## 📄 License
Copyright © 2026 Sujal Kumar. All rights reserved. Enterprise and educational proprietary software.
