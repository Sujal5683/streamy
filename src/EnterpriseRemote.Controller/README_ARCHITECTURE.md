# Enterprise Remote Access — UI Architecture & Parity Guidelines

> **CRITICAL ARCHITECTURAL DIRECTIVE FOR ALL DEVELOPERS & AI AGENTS:**
>
> 1. **Single Source of Truth for UI:**
>    The modern, production user interface for Enterprise Remote is the **Cyber UI** located in:
>    `src/EnterpriseRemote.ControlPlane/wwwroot/`
>    - Layout: `src/EnterpriseRemote.ControlPlane/wwwroot/index.html`
>    - Styles & Cyber Theme: `src/EnterpriseRemote.ControlPlane/wwwroot/css/app.css`
>    - Interactions, WebSockets & Streaming: `src/EnterpriseRemote.ControlPlane/wwwroot/js/app.js`
>
> 2. **Native Windows Desktop Controller (`EnterpriseRemote.Controller.exe`):**
>    - Powered by **Microsoft Edge WebView2** (`Microsoft.Web.WebView2` NuGet).
>    - Defined in: `src/EnterpriseRemote.Controller/UI/RemoteViewerForm.cs`.
>    - Renders the `wwwroot` UI with hardware-accelerated Direct3D GPU pipelines supporting uncapped 60 to 200 FPS high-refresh rate displays.
>    - Features:
>      - F11 borderless fullscreen toggle and Esc restore.
>      - System Tray integration with "Open", "Toggle Full Screen", and "Exit".
>      - Silent background auto-launch of `EnterpriseRemote.ControlPlane.exe`.
>      - Zero DPI scaling distortion, zero font truncation, zero GDI+ bottlenecks.
>
> 3. **Browser Web Controller:**
>    - Accessible directly at `http://127.0.0.1:5293/` (or via Cloudflare Internet Tunnel / LAN IP).
>    - Runs identically in Chrome, Edge, Brave, Firefox, etc.
>
> 4. **DO NOT MODIFY OR REINTRODUCE LEGACY GDI+ WINFORMS CONTROLS:**
>    - Files in `UI/Components/` and `UI/EnrollRemoteDeviceForm.cs` are legacy artifacts kept only for unit test compatibility.
>    - **NEVER** add new GDI+ WinForms controls or buttons to `RemoteViewerForm.cs`.
>    - **ALL UI ENHANCEMENTS MUST BE APPLIED DIRECTLY IN `src/EnterpriseRemote.ControlPlane/wwwroot/`** so that both the Windows Native Desktop App and the Browser Controller stay in 100% lockstep parity.

5. **Deterministic Device Deduplication & Key Coordination Architecture:**
   - **Canonical Device Deduplication**: Workstations are keyed deterministically by canonical hostname. Re-enrolling or switching networks never generates duplicate ghost `DEV-XXXX` records; it reuses and updates the existing record with the active network credentials.
   - **Unified Key Coordination**: Universal PIN (`1234` / `0000`), Permanent Master Keys (`PERM-MASTER-...`), and individual device tokens (`PERM-XXXX`) are fully harmonized. Operators can connect seamlessly using either universal keys or machine-specific tokens.
   - **Real-Time Host Status Telemetry (`0x26 HostStatus`)**: The StreamRelayHub automatically broadcasts host presence states (`0x01` Online, `0x00` Offline). Viewers display clear, responsive states ("Waiting for Remote PC (Host Offline)") rather than hanging or freezing silently.
   - **Seamless Network Switching**: Switching gateways (Wi-Fi LAN ↔ Cloudflare Tunnels ↔ Tailscale WireGuard) gracefully tears down previous streaming sessions, terminates conflicting DXGI duplication processes on the host, and immediately syncs the UI with the active network route.
