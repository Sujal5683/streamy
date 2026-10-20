# Enterprise Windows Remote Access Platform: Operator Manual

## 1. Operator Sign-In & Device Inventory (Workflow A)

### Sign-In Procedure
1. Launch the **Enterprise Remote Controller** application.
2. Click **Sign in with Organization Account**.
3. Complete the OIDC / Single Sign-On (SSO) authentication and MFA prompt in the enterprise browser.
4. Upon successful authentication, the Controller displays only the endpoints you are explicitly authorized to access according to your RBAC role and assigned device groups.

---

## 2. Requesting a Remote Session

1. From the device list, select the target computer (e.g. `FIN-TERMINAL-01`).
2. Review the device details: Hostname, Operating System, Group, and Attended/Unattended policy status.
3. Select requested capabilities:
   - **View Only**: Passive observation and monitoring.
   - **Remote Control**: Interactive keyboard and mouse control.
   - **Clipboard Sharing**: Synchronize text and image clippings (subject to policy).
   - **File Transfer**: Send and receive files (subject to policy).
4. Enter an optional business justification / ticket number (e.g. *"Assisting user with printer configuration - Ticket #8492"*).
5. Click **Connect**.

### Attended vs Unattended Connection Flow
- **If Unattended Access is Approved**: The session connects immediately, establishes an end-to-end encrypted WebRTC channel, and displays the remote desktop.
- **If Local Approval is Required**: The Controller displays: *"Awaiting local user approval on target endpoint (30s timeout)..."*. Once the local user clicks "Approve", the remote desktop streams instantly.

---

## 3. Remote Desktop Viewer & Controls

- **Multi-Monitor Selection**: Click the **Display Selector** toolbar icon to toggle between Monitor 1, Monitor 2, or a combined virtual desktop view.
- **Adaptive Quality**: The toolbar displays live streaming telemetry:
  - **RTT**: Round-trip latency in milliseconds.
  - **Loss**: Packet loss percentage.
  - **FPS**: Rendered frames per second.
  - **Bitrate**: Real-time throughput (e.g. 6.2 Mbps).
  - The system dynamically adapts resolution and frame pacing to maintain crisp responsiveness under changing network conditions.
- **Pause / Resume**: Click **Pause** to temporarily halt video streaming while retaining session context.
- **Disconnect**: Click **Disconnect** to cleanly terminate the session, flush local buffers, and emit an audit trail record.
