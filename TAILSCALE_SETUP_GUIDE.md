# 🚀 Tailscale Low-Latency Mode — Complete Setup Guide

> **Why Tailscale?** Your Cloudflare Quick Tunnel routes all video frames through a US/EU server over TCP — adding 50–200ms of extra latency. Tailscale creates a direct WireGuard UDP connection between your two PCs, cutting that overhead to under 5ms. Same two PCs, same software, dramatically better performance.

---

## How the Two Modes Compare

```
CLOUDFLARE MODE (existing):
  [Remote PC] ──TCP WS──► [Your Main PC: cloudflared] ──TCP──► [Cloudflare Edge: USA/EU] ──TCP──► [Controller PC]
               frame                                              adds 50–200ms RTT
               data                                              + bandwidth throttling
               
TAILSCALE MODE (new):
  [Remote PC] ──WireGuard UDP (Direct P2P)──────────────────────────────────────────────► [Controller PC]
               frame data travels directly between PCs         <5ms overhead over any internet
```

| | Cloudflare Quick Tunnel | Tailscale (WireGuard) |
|---|---|---|
| **Protocol** | TCP WebSocket relay | UDP direct peer-to-peer |
| **Extra latency** | 50–200ms | <5ms |
| **Bandwidth throttle** | Yes (free tier) | None |
| **URL stability** | New URL on every restart | Permanent `100.x.x.x` IP |
| **Account required** | No | Yes (free) |
| **Setup time** | Zero | ~5 minutes (both PCs once) |
| **Best for** | Quick access, no setup | Daily use, best quality |

> [!IMPORTANT]
> Both modes coexist. You can switch between them at any time. Use Cloudflare when you need instant access without any setup. Use Tailscale for the best streaming quality.

---

## Prerequisites

- Both PCs running Windows 10 (64-bit) or Windows 11
- Internet access on both PCs (any network — WiFi, mobile hotspot, office, anywhere)
- A free Tailscale account (sign up with Google, Microsoft, or GitHub at [tailscale.com](https://tailscale.com))

---

## Part 1 — Main PC Setup (Do Once)

### Step 1.1 — Install Tailscale on Main PC

1. Go to **[https://tailscale.com/download](https://tailscale.com/download)**
2. Download **Tailscale for Windows** and run the installer
3. When the installer finishes, Tailscale opens a browser window — **sign in with your Google/Microsoft/GitHub account**
4. After sign-in, you'll see a Tailscale icon appear in your System Tray (bottom-right `^`)
5. Click the tray icon and confirm it says **"Connected"**

### Step 1.2 — Get Your Main PC's Tailscale IP

In the `1_MAIN_PC_CONTROLLER` folder, double-click:

```
⭐ START_TAILSCALE_MODE.bat   ← shows your Tailscale IP + setup instructions
   (or START_TAILSCALE_MODE.vbs for no console window)
```

The script will:
- Detect your Tailscale IP (a permanent `100.x.x.x` address)
- Display it in a color-coded summary
- **Copy the full URL to your clipboard** (e.g. `http://100.64.1.23:5293`)
- Confirm whether the Control Plane is running

> [!TIP]
> Your Tailscale IP (`100.x.x.x`) is **permanent** — it never changes unless you reinstall Tailscale. You only need to look it up once.

### Step 1.3 — Allow Firewall (If Not Done Already)

If you haven't already run the firewall batch on Main PC, do it now:

```
ALLOW_FIREWALL_PORT_5293.bat   ← run once, allows TCP 5293 inbound
```

This is the same step as LAN setup — Tailscale traffic arrives on the same port.

---

## Part 2 — Remote PC Setup (Do Once)

Copy the `2_REMOTE_PC_STREAMER` folder to your Remote PC (via USB, OneDrive, etc.) and then:

### Step 2.1 — Install Tailscale on Remote PC

Same as Step 1.1 — go to **[https://tailscale.com/download](https://tailscale.com/download)** and install on the Remote PC.

> [!IMPORTANT]
> Sign in with the **same Tailscale account** as the Main PC. Both PCs must be on the same "tailnet" (same account) to connect directly. If you need to use different accounts, use Tailscale's "Share node" feature.

### Step 2.2 — Verify Connectivity

Double-click:
```
SETUP_TAILSCALE_REMOTE.bat
```

This script will:
- Confirm Tailscale is connected on the Remote PC
- Show this Remote PC's own Tailscale IP
- Give you the exact URL to enter in the next step

### Step 2.3 — Enroll the Remote PC

Double-click:
```
1_CLICK_ENROLL_REMOTE_PC.bat
```

When asked for the **Control Plane / Server URL**, enter the Main PC's Tailscale URL:

```
http://100.x.x.x:5293
```

*(Replace `100.x.x.x` with the Tailscale IP from Step 1.2 — it's already in your clipboard!)*

> [!NOTE]
> If the Remote PC was already enrolled with a Cloudflare URL, you can re-enroll at any time by running `1_CLICK_ENROLL_REMOTE_PC.bat` again. It will overwrite the previous server URL.

---

## Part 3 — Daily Use

### Starting a Session

| Computer | Action |
|---|---|
| **Main PC** | Double-click `START_MAIN_PC.vbs` (same as before — Tailscale runs in the background) |
| **Remote PC** | Double-click `START_REMOTE_STREAMING.vbs` |

That's it. No tunnel window needed. Tailscale is a background service that's always connected.

### What You'll Notice

- **Input latency**: Keyboard and mouse clicks feel near-instant (comparable to same-room LAN)
- **Frame rate**: Achieves the target FPS (60 @ 720p, 45 @ 900p) even over mobile hotspot
- **Connection quality**: No random URL rotation — same IP every time

---

## Part 4 — Switching Between Modes

You can switch at any time by re-enrolling with a different URL:

| Mode | Server URL to use |
|---|---|
| **LAN** (same WiFi) | `http://192.168.x.x:5293` (Main PC's LAN IP) |
| **Tailscale** (best WAN) | `http://100.x.x.x:5293` (Main PC's Tailscale IP) |
| **Cloudflare** (no-setup WAN) | `https://xxxx.trycloudflare.com` (from tunnel window) |

To switch, run `1_CLICK_ENROLL_REMOTE_PC.bat` on the Remote PC and enter the new URL.

---

## Part 5 — Troubleshooting

### "Cannot connect to 100.x.x.x:5293"

1. **Check Tailscale on both PCs**: Look for the Tailscale tray icon — it should show "Connected" (green dot). If it shows "Disconnected", click it and sign back in.
2. **Check Firewall**: Run `ALLOW_FIREWALL_PORT_5293.bat` on Main PC if not done already.
3. **Check Control Plane**: Make sure `START_MAIN_PC.vbs` is running on the Main PC.
4. **Ping test**: Open Command Prompt on Remote PC and run `ping 100.x.x.x` — if it replies, networking works; if not, Tailscale isn't connected.

### "Tailscale says Connected but ping fails"

- On Main PC: Click Tailscale tray → "Admin Console" → check that both devices appear as "Connected"
- Try `tailscale ping 100.x.x.x` in Command Prompt to diagnose the path

### "My Tailscale IP changed"

Tailscale IPs are permanent within an account and do **not** change. If it appears different:
- You may have reinstalled Tailscale (reinstall assigns a new IP)
- Re-run `START_TAILSCALE_MODE.bat` to get the current IP and re-enroll the Remote PC

### "I want to use Cloudflare instead temporarily"

Run `START_PUBLIC_INTERNET_TUNNEL.bat` on Main PC and use that Cloudflare URL for the Remote PC's enrollment. The two modes don't conflict — just re-enroll with whichever URL you want to use.

### "The Remote PC is on a different Tailscale account"

Use Tailscale's **"Share node"** feature:
1. Go to [login.tailscale.com](https://login.tailscale.com)
2. Select the Main PC device → "Share…"
3. Enter the email of the Remote PC's Tailscale account
4. The Remote PC owner accepts the share — they can now reach the Main PC's `100.x.x.x`

---

## Architecture Reference

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    ENTERPRISE REMOTE — DUAL TUNNEL ARCHITECTURE             │
├────────────────────────┬────────────────────────────────────────────────────┤
│   CLOUDFLARE MODE      │   TAILSCALE MODE (recommended for WAN)             │
├────────────────────────┼────────────────────────────────────────────────────┤
│                        │                                                    │
│  [Remote Host PC]      │  [Remote Host PC]                                  │
│    UserAgent + Svc     │    UserAgent + Svc                                 │
│       │                │       │                                            │
│       │ TCP WS         │       │ WireGuard UDP (100.x.x.x:5293)             │
│       ▼                │       │ Direct P2P — no relay                      │
│  [cloudflared]         │       ▼                                            │
│       │ TCP            │  [ControlPlane on Main PC]                         │
│       ▼                │    StreamRelayHub                                  │
│  [CF Edge: US/EU]      │       │ in-process (no network hop)                │
│  +50–200ms RTT         │       ▼                                            │
│       │ TCP            │  [Controller UI]                                   │
│       ▼                │    RemoteViewerForm                                │
│  [ControlPlane]        │                                                    │
│  [Controller UI]       │  <5ms overhead | No bandwidth limit                │
│                        │  Permanent IP  | Auto-adapts quality (AQE)         │
└────────────────────────┴────────────────────────────────────────────────────┘
```

### What Changed in the Code (No Functionality Removed)

| Component | Change | Benefit |
|---|---|---|
| `UrlUtils.cs` | Detects Tailscale IPs (`100.64–127.x.x`) — routes as plain HTTP, no HTTPS upgrade | Correct URL handling for Tailscale addresses |
| `LanDiscoveryUtils.cs` | Added `GetTailscaleIp()` and `IsTailscaleConnected()` helpers | Programmatic Tailscale status detection |
| `DesktopStreamingService.cs` | Keepalive changed from full JPEG every 100ms → 9-byte ping every 500ms | Saves 6–9 Mbps upload bandwidth on static screens |
| `DesktopStreamingService.cs` | `AdaptiveQualityEngine` now wired up — RTT-based auto FPS/quality | Stream auto-degrades gracefully on congested WAN |
| `StreamRelayHub.cs` | Relay buffer read uses `ms.Read()` → avoids `ms.ToArray()` heap allocation per frame | Reduces relay GC pressure by ~150 KB/frame |
