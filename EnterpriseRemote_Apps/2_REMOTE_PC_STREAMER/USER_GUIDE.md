# Enterprise Remote Access — Remote PC Streamer Guide

> **High-Performance Direct3D Screen Streaming & Hardware Input Agent**  
> Sub-Millisecond Input • Multi-Monitor Duplication • UIPI Lockdown Bypass

---

## 1. Quick Start & File Inventory

| File | Mode | Description |
| :--- | :--- | :--- |
| **`START_REMOTE_STREAMING.bat`** | ⭐ **Recommended** | **Auto-Elevated Launcher.** Prompts for Administrator elevation to bypass Windows UIPI restrictions, allowing full remote control over Task Manager, UAC prompts, and administrative tools. |
| **`START_REMOTE_STREAMING.vbs`** | Silent | Starts streaming silently into the Windows System Tray (bottom-right `^`). Zero taskbar clutter. |
| **`STOP_REMOTE_STREAMING.vbs`** | 🛑 **1-Click Silent Stop** | Instantly and silently terminates all streaming processes and stops the background service. |
| **`STOP_REMOTE_STREAMING.bat`** | 🛑 **Interactive Stop** | Displays a status console confirming that the capture agent, host service, and Windows Service have stopped. |
| **`1_CLICK_ENROLL_REMOTE_PC.bat`**| First-Time Setup | Interactive pairing wizard. Pairs this remote laptop with your Main PC via LAN IP, Tailscale, or Cloudflare Tunnel. |
| **`RESET_ENROLLMENT.bat`** | Factory Reset | Clears stored pairing identity and tokens so you can pair with a new Main PC. |
| **`Install_Windows_Service_Admin.bat`** | 24/7 Unattended | Installs the background Windows Service so this PC can be accessed before Windows login or after reboot. |
| **`Uninstall_Windows_Service_Admin.bat`** | Service Removal | Completely unregisters and removes the Windows Service. |
| **`USER_GUIDE.md`** | Documentation | This comprehensive user manual. |
| **`README_REMOTE_PC.txt`** | Reference | Quick reference text guide. |

---

## 2. First-Time Setup & Enrollment

If this is your first time connecting this Remote PC to your Main PC:

1. **On your Main PC Controller**:
   - Click **HOSTS** or open **Connections & Endpoint Inventory**.
   - Switch to the **Enroll Remote PC** tab.
   - Note the **Universal Quick PIN** (e.g. `8821`) or the **PowerShell Invitation Code**.
2. **On this Remote PC**:
   - Double-click **`1_CLICK_ENROLL_REMOTE_PC.bat`**.
   - Enter your Main PC's address:
     - **Local Wi-Fi**: Enter the Main PC IP (e.g. `192.168.1.50:5293`).
     - **Tailscale**: Enter the Main PC Tailscale IP (e.g. `100.85.20.10:5293`).
     - **Cloudflare Tunnel**: Paste the full HTTPS tunnel URL (e.g. `https://random.trycloudflare.com`).
   - Enter the Quick PIN or Invitation Code.
   - The wizard securely generates an ECDSA P-256 cryptographic keypair and exchanges permanent access tokens.
3. **From now on**:
   - Just double-click **`START_REMOTE_STREAMING.bat`** (or `.vbs`). Pairing is remembered permanently!

---

## 3. Why Administrator Elevation Matters (UIPI Bypass)

Windows enforces **User Interface Privilege Isolation (UIPI)**. If a remote access tool runs as a standard user:
- You cannot click inside **Task Manager**.
- You cannot interact with **User Account Control (UAC)** prompts.
- You cannot click inside elevated Command Prompts or administrative tools.
- Secure lockdown browsers (e.g. Safe Exam Browser) trap the cursor and prevent input.

**`START_REMOTE_STREAMING.bat`** automatically requests Administrator elevation so that:
- Synthetic mouse movements and hardware scancodes bypass UIPI boundaries.
- The operator on the Main PC can manage administrative dialogs, Task Manager, and elevated applications with zero freezing.

---

## 4. Multi-Monitor & Secure Exam Desktop Handling

This streaming agent includes enterprise-grade screen duplication capabilities:

1. **Multi-Monitor DXGI Duplication**:
   - If this PC has multiple monitors (laptop screen + external HDMI/DisplayPort monitors), the operator on the Main PC can switch between **Display 1** and **Display 2** on the fly via the top toolbar or fullscreen island bar.
2. **Unblocked Exam Browsers & Protected Windows**:
   - Applications that attempt to block screen sharing by calling `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` are automatically neutralized by the agent's affinity stripper so the operator does not see a black rectangle.
3. **Lock Screen & UAC Desktop Switching**:
   - If the remote PC locks (`Win + L`) or a UAC elevation prompt appears on a dedicated desktop (`Winlogon`), the streamer automatically attaches to the secure desktop context.

---

## 5. How to Stop Streaming

When you want to stop sharing this PC's screen:

- **1-Click Silent**: Double-click **`STOP_REMOTE_STREAMING.vbs`**. Both the tray agent and background service exit immediately.
- **Interactive**: Double-click **`STOP_REMOTE_STREAMING.bat`** to see confirmation in a console.
- **System Tray**: Right-click the cyber-shield icon in your taskbar notification area (`^`) and select **Exit / Stop Streaming**.

---

## 6. Troubleshooting & FAQs

### Problem: "Cannot connect to Main PC"
- **Solution**:
  - Verify both machines are on the same Wi-Fi, or both connected via Tailscale or Cloudflare Tunnel.
  - Check that the Main PC Controller is running (`START_MAIN_PC.vbs`).
  - Run the firewall command on the Main PC:
    ```cmd
    netsh advfirewall firewall add rule name="EnterpriseRemote ControlPlane" protocol=TCP dir=in localport=5293 action=allow
    ```
  - Note: This Remote PC does **NOT** require any inbound firewall ports open, because it connects *outbound* to the Main PC!

### Problem: "I want to re-pair with a different Main PC"
- **Solution**: Double-click **`RESET_ENROLLMENT.bat`**. This deletes `device_identity.json`. Then run **`1_CLICK_ENROLL_REMOTE_PC.bat`** to enter your new Main PC's details.

