===============================================================================
       ENTERPRISE REMOTE ACCESS — REMOTE PC SETUP GUIDE
===============================================================================

This package contains the Host Endpoint Agent for Enterprise Windows Remote Access.
Copy this folder to any computer you want to remotely view and control from your
Main / Controller PC.

SYSTEM REQUIREMENTS:
--------------------
- Operating System: Windows 10, Windows 11, or Windows Server (64-bit)
- .NET Framework / Runtime: NONE REQUIRED!
  The binaries in this package are self-contained with the .NET runtime
  embedded inside the executables. You do NOT need to install .NET.
- Network: Connected to the same Wi-Fi, LAN, or Mobile Hotspot as your Controller PC.

===============================================================================
QUICK START (3 SIMPLE STEPS)
===============================================================================

STEP 1: Copy this folder to your other computer
        Copy this folder via USB flash drive, shared network folder, or cloud drive.

STEP 2: Enroll the device (First time only)
        On your Main PC:
        - Click '➕ Connect Other PC (Enroll Host)' in the Controller UI.
        - Note your Main PC's IP address (e.g. 192.168.1.15).
        - Click '✨ Generate New Invitation Code' (e.g. INV-XXXX-XXXX-XXXX).
        
        On this Remote PC:
        - Double-click '1-Click_Enroll_And_Start.bat' (or '1_CLICK_ENROLL_REMOTE_PC.bat').
        - Enter the Main PC's IP (e.g. 192.168.1.15) and Invitation Code.
        - Enrollment completes in ~3 seconds and streaming starts automatically!

STEP 3: Connect from your Main PC
        On your Main PC:
        - Click the Refresh (🔄) button in the Target Device list.
        - Select this computer from the dropdown.
        - Click '▶ START REMOTE SESSION'.
        - You will see the remote desktop live at 60 FPS!

===============================================================================
DAILY USE (AFTER FIRST-TIME ENROLLMENT)
===============================================================================

- ⭐ DOUBLE-CLICK: 'Start_Remote_Streaming.vbs'
  Starts desktop streaming silently in the System Tray (ZERO terminal windows!).
- Alternative: 'Start_Remote_Streaming.bat'
- To stop streaming: Double-click 'Stop_Remote_Streaming.bat'.

No re-enrollment is required on daily reboots; the cryptographic hardware
identity is securely remembered in device_identity.json.

===============================================================================
TROUBLESHOOTING & NETWORK TIPS
===============================================================================

1. Server URL:
   Use the Main PC's LAN IP (e.g. 192.168.1.15 or http://192.168.1.15:5293).
   Do NOT use 127.0.0.1 or localhost when connecting between two different PCs!

2. Campus / Enterprise Wi-Fi (e.g., University / Corporate Networks):
   Institutional Wi-Fi networks frequently block peer-to-peer laptop connections
   using "AP Isolation". If this happens, simply enable Mobile Hotspot on one
   phone or laptop and connect both PCs to that hotspot.

3. Windows Firewall:
   Ensure the Main PC allows inbound connections on port 5293 TCP:
   Run in an Administrator Command Prompt on the Main PC:
   netsh advfirewall firewall add rule name="EnterpriseRemote" protocol=TCP dir=in localport=5293 action=allow

4. Status & System Tray:
   When streaming is active, a 🛡️ shield icon appears in the Windows System Tray
   (near the clock). Right-click the icon anytime to see connection status or disconnect.
===============================================================================
