# Implementation Plan: Enterprise Windows Remote Access Platform

## Goal Description
Design and implement a production-oriented, enterprise-controlled Windows remote-access application. The platform provides authorized remote desktop control, managed unattended access, zero-trust device enrollment, hardware-accelerated screen streaming (DXGI Desktop Duplication & WebRTC DTLS-SRTP), centralized RBAC authorization, tamper-resistant auditability, and customer-controlled deployment.

The implementation strictly enforces privilege separation (Windows Session 0 service decoupled from Session 1+ interactive desktop agent via authenticated Named Pipe IPC), zero hardcoded secrets, EDR transparency, and fails closed under policy expiration or revocation.

---

## User Review Required

> [!IMPORTANT]
> **Toolchain & Technology Selection (ADR-001)**:
> - **Control Plane**: Chosen **C# / ASP.NET Core 8 Kestrel** with Entity Framework Core (PostgreSQL) and Redis for presence/rate-limiting. .NET 8 is natively installed and verified on this Windows workstation (`C:\Program Files\dotnet\sdk\8.0.424`).
> - **Windows Host Agent & Controller**: Chosen **C# / .NET 8** with direct native P/Invoke to Win32, DXGI Desktop Duplication (`IDXGIOutputDuplication`), Direct3D 11, Windows Media Foundation, Windows CNG Cryptography (`NCrypt`), and `SendInput`.
> - *Rationale*: While C++20 was an initial candidate in the prompt, the host workstation does not have a standalone MSVC C++ linker configured in PATH (`link.exe` missing), whereas .NET 8 has full native Windows desktop capabilities, compiles reproducibly, executes with high performance, and eliminates buffer overflow vulnerabilities in protocol parsing.

> [!WARNING]
> **Windows Secure Desktop & UAC Handling**:
> Per Section 2 & Section 5 requirements, the host agent **will not** employ unauthorized memory injection or process tampering with `winlogon.exe` or `csrss.exe` to bypass Windows Secure Desktop. When UAC or Winlogon activates, the agent safely reports `SessionStatus::SecureDesktopActive` and pauses capture until the prompt resolves or is handled by an authorized elevated helper component.

> [!CAUTION]
> **Zero Bearer Secrets**:
> Permanent bearer URLs, embedded shared secrets, or persistent API keys will never be used for unattended desktop control. All unattended sessions require real-time validation by the Control Plane against active policy rules and short-lived, digitally-signed Session Authorization Tickets (SAT).

---

## Open Questions

1. **Identity Provider Integration**:
   - For local development and automated testing, should we include a built-in mock OIDC IdP with pre-configured test users (e.g., `admin@corp.internal`, `operator@corp.internal`, `auditor@corp.internal`) alongside the generic OIDC discovery configuration for Microsoft Entra ID / Okta?
2. **Relational Database Backend**:
   - Should the default local development configuration run with SQLite (zero external dependencies) with seamless migration switching to PostgreSQL for enterprise production, or should we assume PostgreSQL is pre-configured? *(Recommendation: Support both via EF Core provider abstractions)*.
3. **TURN Server Strategy**:
   - For offline/LAN testing, direct ICE host candidates allow high-speed zero-hop streaming. Would you like an integrated managed STUN/TURN test server for local firewall simulation tests?

---

## Proposed Changes & Staged Execution Plan

The project will be delivered across seven rigorously tested milestones:

```
Milestone 1: Architectural Foundation, Threat Model & Decision Records (COMPLETE)
       |
Milestone 2: Native DXGI Screen Capture & Streaming Benchmark Prototype
       |
Milestone 3: Control Plane, OIDC Authentication, Device Enrollment & RBAC
       |
Milestone 4: Authenticated WebRTC Remote Sessions & Core Control (Mouse/Keyboard)
       |
Milestone 5: Windows Service, Session 0/1 Isolation, Named Pipe IPC & Installer
       |
Milestone 6: Enterprise Management, Audit Trail, File Transfer & Clipboard
       |
Milestone 7: Automated Security & Performance Test Suite, Pilot Validation & Readiness Report
```

---

### Component Breakdown & File Structure

```
c:\My Projects\SECURE AND ENTERPRISE STREAMING APP\
├── src\
│   ├── EnterpriseRemote.Common\             # Shared protocol models, crypto, and telemetry
│   │   ├── Protocol\                        # Signaling (JSON-RPC), IPC, and DataChannel schemas
│   │   ├── Cryptography\                    # CNG, ECDSA P-256, SAT token validation, DPAPI
│   │   ├── Redaction\                       # Structured logging sanitizer
│   │   └── Models\                          # Device, Session, Policy, and Audit entity models
│   ├── EnterpriseRemote.Host.Service\       # Session 0 Windows Background Service
│   │   ├── ServiceHost.cs                   # Windows Service lifecycle management
│   │   ├── DeviceIdentityManager.cs         # Hardware key generation & enrollment client
│   │   ├── NamedPipeServer.cs               # Restrictive SDDL Named Pipe IPC Server
│   │   └── SignalingClient.cs               # WSS connection to Control Plane
│   ├── EnterpriseRemote.Host.UserAgent\     # Session 1+ Interactive Console Agent
│   │   ├── Capture\                         # DXGI Desktop Duplication & Direct3D 11 engine
│   │   ├── Input\                           # Bounds-checked SendInput injection engine
│   │   ├── WebRtc\                          # WebRTC PeerConnection, DTLS-SRTP, DataChannels
│   │   ├── Tray\                            # System tray indicator & consent prompt dialog
│   │   └── NamedPipeClient.cs               # Authenticated client for Session 0 IPC
│   ├── EnterpriseRemote.Controller\         # Windows Operator Desktop Application
│   │   ├── ViewModels\                      # MVVM view models (Auth, Devices, Viewer)
│   │   ├── Views\                           # WinUI / WPF rendering surfaces
│   │   ├── InputCapture\                    # Keyboard hooks & pointer capture
│   │   └── QualityOverlay\                  # Real-time RTT, loss, bitrate, FPS telemetry
│   └── EnterpriseRemote.ControlPlane\       # Enterprise Management & Signaling API
│       ├── Controllers\                     # REST APIs (Auth, Devices, Invitations, Audit)
│       ├── Signaling\                       # WebSocket JSON-RPC 2.0 Signaling Hub
│       ├── Policies\                        # RBAC & Unattended access policy engine
│       ├── Database\                        # EF Core DbContext, PostgreSQL / SQLite models
│       └── Audit\                           # Immutable append-only audit event sink
├── tests\
│   ├── EnterpriseRemote.UnitTests\          # Authentication, protocol parsing, rate limits
│   ├── EnterpriseRemote.SecurityTests\      # Replay resistance, token expiry, privilege checks
│   ├── EnterpriseRemote.IntegrationTests\   # End-to-end enrollment, connect, and teardown
│   └── EnterpriseRemote.Benchmarks\         # DXGI capture latency, FPS, memory allocation
└── docs\                                    # Comprehensive system documentation & manuals
```

---

### Milestone 1: Specifications & Architectural Baseline
- [x] Deliver System Architecture and Data-Flow documentation with Mermaid diagrams ([architecture_and_dataflow.md](file:///C:/Users/sujal/.gemini/antigravity-ide/brain/f9c49dfa-6d97-4c2a-bbd8-00dd91083993/architecture_and_dataflow.md)).
- [x] Deliver STRIDE Threat Model and Trust-Boundary documentation ([threat_model_and_security.md](file:///C:/Users/sujal/.gemini/antigravity-ide/brain/f9c49dfa-6d97-4c2a-bbd8-00dd91083993/threat_model_and_security.md)).
- [x] Deliver Requirements Traceability Matrix ([requirements_traceability_matrix.md](file:///C:/Users/sujal/.gemini/antigravity-ide/brain/f9c49dfa-6d97-4c2a-bbd8-00dd91083993/requirements_traceability_matrix.md)).
- [x] Deliver Protocol Specifications for Signaling, Named Pipe IPC, and WebRTC Data Channels ([protocol_specification.md](file:///C:/Users/sujal/.gemini/antigravity-ide/brain/f9c49dfa-6d97-4c2a-bbd8-00dd91083993/protocol_specification.md)).
- [x] Deliver Architecture Decision Records ADR-001 through ADR-006 ([architecture_decision_records.md](file:///C:/Users/sujal/.gemini/antigravity-ide/brain/f9c49dfa-6d97-4c2a-bbd8-00dd91083993/architecture_decision_records.md)).

---

### Milestone 2: Native Screen-Capture & Streaming Prototype
- **Goal**: Measure and validate desktop capture and frame streaming latency.
- **Implementation**:
  - Implement `DxgiDesktopDuplicator` using Direct3D 11 and `IDXGIOutputDuplication`.
  - Implement dirty rect extraction from `DXGI_OUTDUPL_FRAME_INFO` to detect static vs modified regions.
  - Implement hardware cursor extraction via `GetFramePointerShape`.
  - Create a benchmark runner measuring:
    - Frame capture time (target: < 3 ms per frame on 1080p/4K).
    - CPU utilization and memory throughput.
    - Output video frame buffer formatting (BGRA32 to NV12/I420).

---

### Milestone 3: Control Plane, Authentication & Enrollment
- **Goal**: Implement zero-trust enrollment and centralized authorization.
- **Implementation**:
  - Build ASP.NET Core Web API with JWT/OIDC authentication.
  - Database schema: Organizations, Devices, Users, Roles, Invitations, Policies, AuditEvents.
  - Device Enrollment Engine: High-entropy CSPRNG invitations (Base64URL, 256 bits, 15m TTL), rate-limited redemption, ECDSA public key binding.
  - Unattended Policy Engine: Evaluates operator role, time schedules, source IP, device groups, and approval requirements.
  - Cryptographic Session Authorization Ticket (SAT) issuer.

---

### Milestone 4: Authenticated Remote Sessions & Input Control
- **Goal**: Connect operator to host with low-latency display and input control.
- **Implementation**:
  - WebSocket JSON-RPC Signaling gateway.
  - WebRTC PeerConnection negotiation (ICE, DTLS-SRTP, SCTP).
  - Controller UI: Screen renderer, monitor selector, connection health stats overlay.
  - Host User Agent: Authenticated input receiver with strict bounds checking and injection via `SendInput`.
  - Multi-monitor coordinate translation.

---

### Milestone 5: Privilege Isolation & Windows Service
- **Goal**: Deploy Session 0 service and interactive Session 1+ agent.
- **Implementation**:
  - Windows Service (`EnterpriseRemoteHostSvc`) running in Session 0.
  - Process launcher into active user session via `WTSGetActiveConsoleSessionId` and `CreateProcessAsUser`.
  - Named Pipe Server with SDDL ACL (`D:(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGWGX;;;<LogonSid>)`).
  - Local session tray indicator and consent prompt dialog.
  - Emergency disable kill switch.

---

### Milestone 6: Enterprise Management, Audit & Controlled Privileged Actions
- **Goal**: Comprehensive auditing, policy-governed clipboard, and file transfer.
- **Implementation**:
  - Structured audit logging to PostgreSQL with SIEM export (CEF / RFC 5424 JSON).
  - Sensitive data redactor (zero keys/tokens/clipboard contents in logs).
  - Chunked file transfer protocol over SCTP data channel with SHA-256 verification and local approval prompt.
  - Clipboard sharing protocol with format filtering and size caps.

---

### Milestone 7: Testing, Performance Validation & Pilot Readiness
- **Goal**: Formal test verification and production readiness report.
- **Implementation**:
  - Full automated test suite (Unit, Integration, Security, and Reliability).
  - Measured latency and throughput benchmarks (p50, p95, p99).
  - Disaster recovery and incident response manual.
  - Software Bill of Materials (SBOM) and production readiness report.

---

## Verification Plan

### Automated Tests
1. **Unit Tests** (`dotnet test tests/EnterpriseRemote.UnitTests`):
   - Cryptographic key generation, signature verification, and tamper detection.
   - Enrollment invitation generation, single-use enforcement, and expiration.
   - OIDC token parsing and claim verification.
   - Input packet bounds checking (ensuring out-of-bounds clicks and invalid keycodes are rejected).
   - Log sanitizer validation (ensuring tokens, private keys, and passwords are redacted).
2. **Integration Tests** (`dotnet test tests/EnterpriseRemote.IntegrationTests`):
   - Full enrollment lifecycle: Admin creates invitation -> Host redeems -> Device registered with ECDSA key.
   - Session negotiation lifecycle: Operator requests session -> Policy evaluated -> SAT issued -> WebRTC handshake completed.
   - Emergency kill switch: Admin revokes device -> Active session terminates immediately -> Further requests rejected.
3. **Performance Benchmarks** (`dotnet run --project tests/EnterpriseRemote.Benchmarks -c Release`):
   - DXGI capture latency distribution across 1000 frames.
   - Input-to-display end-to-end pipeline latency.
   - Memory allocation and GC pressure profiling.

### Manual Verification
1. Run Controller and Host applications on local Windows desktop.
2. Verify visual appearance of active session notification banner and system tray icon.
3. Test local user "Emergency Stop" button to confirm immediate session termination.
4. Verify Windows Event Viewer records structured audit events without sensitive data leaks.
