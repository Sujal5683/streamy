# Enterprise Windows Remote Access Platform: Requirements Traceability Matrix (RTM)

This matrix maps every requirement specified in the project specification to architectural components, protocols, and verification tests.

| Req ID | Requirement Description | Architectural Component | Implementation Details | Verification Test / Acceptance Criteria |
| :--- | :--- | :--- | :--- | :--- |
| **REQ-WFA-01** | Operator signs in through organization's approved IdP | Control Plane / Controller App | OIDC PKCE flow, JWT validation, MFA enforcement | `Test_OIDC_Authentication_Success`, `Test_OIDC_InvalidToken_Rejected` |
| **REQ-WFA-02** | Display only devices the operator is authorized to access | Control Plane API | RBAC scoping query `SELECT * FROM devices WHERE group_id IN (...)` | `Test_DeviceList_RBAC_Scoping_Isolated` |
| **REQ-WFA-03** | Operator selects device and requests session | Controller App / Control Plane | `POST /api/v1/sessions/request` with device ID and intent | `Test_SessionRequest_Flow` |
| **REQ-WFA-04** | Control plane evaluates user, device, role, health, and policy | Control Plane Policy Engine | Multi-factor policy evaluation (time window, device status, role) | `Test_PolicyEngine_Evaluation_AttendedVsUnattended` |
| **REQ-WFA-05** | Target endpoint requests local approval or permits unattended | Host Service / User Agent | Interactive tray prompt with 30s timeout or policy-bypass for approved unattended groups | `Test_LocalConsent_Prompt_Approve_Deny_Timeout` |
| **REQ-WFA-06** | Authenticated, encrypted session establishment | WebRTC Transport | DTLS 1.2/1.3 + SRTP, ICE candidates with TURN fallback | `Test_WebRTC_DTLS_Handshake_Encrypted` |
| **REQ-WFA-07** | Operator receives permitted remote-control capabilities | User Agent Input Engine | Capability bitmask enforced on host (`INPUT`, `CLIPBOARD`, `FILES`) | `Test_Host_Capability_Bitmask_Enforcement` |
| **REQ-WFA-08** | Session ends cleanly when disconnected, expired, or revoked | Host Agent / Controller / CP | Orderly SCTP teardown, WSS notification, audit event emission | `Test_CleanSessionTeardown_AuditLogged` |
| **REQ-WFB-01** | Signed installer through approved deployment process | Installer (MSI / InnoSetup) | Authenticode code-signing, silent enterprise install support | `Test_Installer_SilentInstall_Parameters` |
| **REQ-WFB-02** | Installer requests UAC elevation when needed | Windows Installer Package | Explicit `requireAdministrator` manifest | `Test_UAC_Manifest_Elevation` |
| **REQ-WFB-03** | High-entropy, expiring, single-use enrollment invitation | Control Plane / Host Service | CSPRNG 256-bit token, SHA-256 DB hash, 15m TTL, single-redemption | `Test_Invitation_SingleUse_And_Expiration` |
| **REQ-WFB-04** | Device receives unique cryptographic identity | Host Service Crypto Provider | CNG / TPM ECDSA P-256 keypair generated locally on device | `Test_DeviceCrypto_UniqueKeypair_Persistence` |
| **REQ-WFB-05** | Administrator assigns endpoint to org and device group | Admin API / DB | Relational binding `device.group_id -> groups.id` | `Test_DeviceGroupAssignment_AdminAPI` |
| **REQ-WFB-06** | Org configures interactive approval vs unattended access | Policy Engine | Policy schema with `attended_required: bool`, schedules, IP allowlists | `Test_UnattendedPolicy_Enforcement` |
| **REQ-WFB-07** | Computer becomes available according to policy | Signaling Server | Presence heartbeat updates device online state in Redis/DB | `Test_DevicePresence_Heartbeat` |
| **REQ-WFB-08** | Admin can revoke access, rotate credentials, disable service | Admin API / Host Service | Revocation API, key rotation endpoint, emergency kill-switch | `Test_DeviceRevocation_ImmediateDisconnect` |
| **REQ-SEC-01** | Properly signed installer and application binaries | Build Pipeline | Authenticode code-signing with timestamping | `Test_BinarySignatures_Valid` |
| **REQ-SEC-02** | Identifiable in Installed Apps, Services, and Logs | Host Setup / Service | Registered in Windows Add/Remove Programs, Services.msc, EventLog | `Test_ServiceRegistration_Identifiable` |
| **REQ-SEC-03** | Support EDR, App-Control policies, and monitoring | Host Agent | Documented service name, binary paths, ETW providers, zero obfuscation | `Test_EDR_Transparency_NoHooks_NoHiding` |
| **REQ-SEC-04** | Clear session-status indicator (tray / notification) | User Agent Tray UI | Persistent tray icon, top-of-screen translucent banner during active session | `Test_SessionTrayIndicator_Visible` |
| **REQ-SEC-05** | Organization-wide and per-device emergency-disable | Control Plane / Host Service | Emergency disable command drops WSS, tears down WebRTC, sets state | `Test_EmergencyKillSwitch_Execution` |
| **REQ-SEC-06** | Least-privilege execution & session isolation | Host Svc / User Agent | Session 0 background service vs Session 1+ standard user agent | `Test_PrivilegeSeparation_SessionIsolation` |
| **REQ-SEC-07** | Zero cryptographic secrets in code, logs, configs | Codebase / Log Redactor | Automated log sanitizer, DPAPI/CNG hardware-backed secret storage | `Test_ZeroHardcodedSecrets_StaticScan` |
| **REQ-SEC-08** | Server & host authorization checks for every privileged action | Host Agent & Control Plane | Dual-validation of capabilities before input, clipboard, or file ops | `Test_DualAuthorization_HostAndServer` |
| **REQ-SEC-09** | Emit security-relevant audit events | Audit Ingestion System | Immutable audit logs for connect, consent, disconnect, file transfer | `Test_AuditEvent_Ingestion_Structured` |
| **REQ-SEC-10** | Fail closed on expiry, policy failure, or credential revocation | All Components | Expired SAT or dropped heartbeat results in immediate connection close | `Test_FailClosed_OnTokenExpiry` |
| **REQ-PERF-01** | p95 input-to-display latency < 50ms (LAN) / < 150ms (WAN) | DXGI / MF / WebRTC | Hardware-accelerated capture & encoding, zero-copy pipeline | `Bench_InputToDisplay_Latency` |
| **REQ-PERF-02** | Adaptive bitrate, frame rate, and congestion control | WebRTC Transport | TWCC / GCC bandwidth estimation, dynamic FPS (15-60) and bitrate | `Bench_AdaptiveBitrate_CongestionResponse` |
| **REQ-PERF-03** | Respect Windows Secure Desktop without unauthorized bypasses | User Agent / Elevated Helper | Safe pause and status reporting when desktop switches to Winlogon | `Test_SecureDesktop_SafePause_Reporting` |
