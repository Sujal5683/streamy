# Enterprise Windows Remote Access Platform: Architecture Decision Records (ADRs)

## ADR-001: Platform & Language Stack Selection

### Status
Accepted

### Context
The project requires:
1. A Windows Host Agent with Session 0 service and Session 1+ interactive desktop integration.
2. A Windows Controller desktop application.
3. An enterprise Control Plane providing APIs, OIDC authentication, RBAC, presence, and WebRTC signaling.

The user's prompt suggested C++20 for the native agent, WinUI 3 with C#/.NET for the controller, and Go or Rust for the control plane.
Upon inspecting the development workstation environment, we established:
- .NET 8 SDK (`8.0.424`) with `Microsoft.WindowsDesktop.App` runtime is installed and fully functional.
- Rust (`rustc 1.98.1`) is installed, but `link.exe` was missing due to an incomplete standalone MSVC linker environment on the workstation.
- Go is not installed.
- Modern .NET 8 provides first-class support for both high-performance backends (ASP.NET Core Kestrel) and direct native Windows OS integration (P/Invoke to Win32, DXGI, Direct3D11, Media Foundation, Windows CNG, DPAPI, and Named Pipes).

### Decision
1. **Control Plane**: Implement in **C# / ASP.NET Core 8** (Kestrel).
   - *Rationale*: ASP.NET Core is among the fastest web and WebSocket servers in enterprise production benchmarks, supports native OpenID Connect / JWT security libraries (`Microsoft.IdentityModel`), EF Core / Npgsql for PostgreSQL, StackExchange.Redis, and compiles cleanly with zero external linker dependencies.
2. **Windows Host Agent & Controller**: Implement in **C# / .NET 8** utilizing native Windows interop (Direct P/Invoke / CsWin32 bindings to DXGI Desktop Duplication, Direct3D11, Windows Media Foundation, SendInput, and CNG Cryptography).
   - *Rationale*: Provides 100% native access to the exact Windows SDK APIs requested by the user, while benefiting from memory safety, strict type checking, and zero-risk of buffer overflows in protocol parsing.
   - *Alternative Considered*: Standalone C++20 was considered, but requires complex build harnesses and MSVC installation, whereas .NET 8 with direct native P/Invoke produces reproducible builds, native performance, and full access to DXGI and Media Foundation.

### Consequences
- Single unified toolchain (`dotnet build`, `dotnet test`) across all components.
- Zero impedance mismatch between Host, Controller, and Control Plane data types.
- Native performance verified through hardware acceleration.

---

## ADR-002: Screen Capture Mechanism: DXGI Desktop Duplication

### Status
Accepted

### Context
Screen capture on Windows can be achieved via:
1. GDI `BitBlt`: High CPU overhead, high latency, no dirty rect detection, does not capture hardware-accelerated DirectX/Vulkan windows.
2. DXGI Desktop Duplication (`IDXGIOutputDuplication`): Direct GPU frame access, returns DirectX 11 textures, hardware dirty rect metadata, hardware cursor shape and position tracking, sub-millisecond capture times.
3. Windows Graphics Capture API: Modern UWP/Win32 capture API, but shows an intrusive yellow border on older Windows versions and requires additional packaging considerations.

### Decision
Use **DXGI Desktop Duplication** via Direct3D 11 as the primary screen capture engine.
- *Dirty Rect Optimization*: Read `DXGI_OUTDUPL_FRAME_INFO.TotalMetadataBufferSize` to extract only changed regions and skip identical static frames, saving bandwidth and CPU.
- *Cursor Shape*: Handle hardware cursor pointer shapes and positions directly via `GetFramePointerShape`.

### Consequences
- Requires WDDM-compliant graphics drivers (standard across all modern Windows 10/11 and Windows Server 2016+).
- In virtual machines without GPU virtualization, fallback to Direct3D WARP (Windows Advanced Rasterization Platform) is supported.

---

## ADR-003: Real-Time Transport: WebRTC (DTLS-SRTP & SCTP)

### Status
Accepted

### Context
Options for streaming video and control input:
1. Proprietary TCP/TLS: High latency under packet loss (TCP head-of-line blocking), no native congestion control for live video.
2. Custom UDP with DTLS: Requires reinventing congestion control, NACK, packet pacing, and ICE NAT traversal.
3. WebRTC: Battle-tested standard for real-time peer-to-peer communication, RFC 8825 compliant, hardware-accelerated SRTP encryption, ICE/STUN/TURN NAT traversal, SCTP data channels, and Google Congestion Control (GCC).

### Decision
Standardize on **WebRTC** with DTLS 1.2/1.3 and SRTP for media, and SCTP for data channels.
- Relays (TURN) only forward opaque encrypted packets, guaranteeing zero plaintext exposure.

### Consequences
- Industry-standard security and compliance.
- Direct P2P connectivity on LAN and favorable WANs; automatic fallback to TURN relay on restrictive corporate symmetric NATs.

---

## ADR-004: Windows Privilege Isolation: Session 0 vs Session 1+

### Status
Accepted

### Context
Windows Vista introduced Session 0 isolation to mitigate Shatter attacks. Services in Session 0 cannot interact with the desktop or run interactive UI. Legacy remote tools often resort to insecure hacks to run everything as LocalSystem.

### Decision
Decompose the host agent into two processes:
1. `EnterpriseRemoteHostSvc` (Session 0, LocalSystem/NetworkService): Handles system lifecycle, cryptographic identity, and control-plane signaling.
2. `EnterpriseRemoteUserAgent` (Session 1+, Standard User): Launched in the interactive console session using `CreateProcessAsUser`. Runs DXGI capture and input injection within the user's desktop context.
3. Connected via an authenticated Windows Named Pipe with a restrictive Security Descriptor (SDDL) that explicitly allows only SYSTEM and the active console logon SID.

### Consequences
- Meets enterprise compliance and EDR requirements.
- Zero risk of Session 0 desktop elevation attacks.

---

## ADR-005: Cryptographic Device Identity & Zero-Trust Enrollment

### Status
Accepted

### Context
Legacy tools often use shared deployment tokens or static machine IDs (e.g. MAC address / motherboard serial), which can be easily spoofed or replayed.

### Decision
1. **Enrollment**: High-entropy (256-bit CSPRNG), single-use invitation tokens with 15-minute expiration, rate-limited redemption, and atomic database state transitions.
2. **Device Identity**: During enrollment, the host generates a unique ECDSA P-256 keypair via Windows CNG (TPM 2.0 backed if available; DPAPI machine store fallback). The private key never leaves the device.
3. **Presence & Signaling**: The host signs heartbeats and session handshakes with its private key. The control plane validates the signature against the registered public key.

### Consequences
- Spoofed host endpoints are mathematically impossible without physical compromise of the machine's cryptographic hardware store.
- Replayed invitations are rejected immediately.

---

## ADR-006: Data Persistence & Ephemeral State Management

### Status
Accepted

### Context
The control plane requires durable storage for tenants, devices, roles, invitations, and immutable audit events, as well as high-throughput ephemeral storage for real-time presence heartbeats, signaling queues, and rate-limiting.

### Decision
- **PostgreSQL**: Durable transactional database for Organizations, Device Inventory, RBAC, Policies, and Audit Logs.
- **Redis (Optional/Configurable)**: In-memory presence tracker and distributed rate-limiter. For single-instance or embedded deployments, an in-memory concurrent provider with identical interfaces is provided to avoid external dependencies.

### Consequences
- Clean separation between durable audit records and ephemeral connection state.
- Supports both air-gapped single-server deployments and multi-node clustered enterprise environments.
