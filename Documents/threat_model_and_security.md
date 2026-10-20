# Enterprise Windows Remote Access Platform: Threat Model & Security Architecture

## 1. Trust Boundaries & Attack Surfaces

The platform defines five distinct security boundaries, each governed by mutual cryptographic verification, least-privilege execution, and strict policy enforcement:

```
[ Operator Workstation ]
          |
    (Boundary 1: HTTPS / OIDC / WSS TLS 1.3)
          v
[ Enterprise Control Plane ] <--- (Boundary 2: WSS TLS 1.3 + Device Mutual ECDSA) ---> [ Host Session 0 Service ]
          |                                                                                        |
    (Boundary 5: TLS 1.3 / mTLS)                                                        (Boundary 3: Named Pipe SDDL)
          v                                                                                        v
[ PostgreSQL & Redis ]                                                                  [ Host Session 1+ Agent ]
                                                                                                   ^
                                                                                                   |
                                    (Boundary 4: DTLS 1.2/1.3 + SRTP + SCTP DataChannel) ----------+
                                    [ Operator Controller App WebRTC PeerConnection ]
```

### Trust Boundary Definitions
1. **Boundary 1 (Operator Workstation -> Control Plane)**:
   - Operators possess short-lived OAuth 2.0 / OIDC Bearer tokens.
   - Untrusted inputs: API requests, session negotiation parameters, device search filters.
   - Attack vectors: Credential stuffing, token theft, replay, unauthorized device traversal.

2. **Boundary 2 (Target Host Session 0 Service -> Control Plane)**:
   - Hosts authenticate using unique, per-device ECDSA P-256 / Ed25519 cryptographic keypairs generated during enrollment.
   - Untrusted inputs: Control plane commands, session dispatch notices, policy updates.
   - Attack vectors: Spoofed host heartbeats, unauthorized session dispatch, man-in-the-middle signaling.

3. **Boundary 3 (Host Session 0 Service <-> Host Session 1+ Interactive Agent)**:
   - Local IPC over Windows Named Pipes.
   - Untrusted inputs: Local non-admin processes attempting to inject messages into the pipe.
   - Mitigation: Restrictive SDDL denying all users except `NT AUTHORITY\SYSTEM` and the active interactive session's specific logon SID (`GetTokenInformation(TokenLogonSid)`).

4. **Boundary 4 (Controller WebRTC PeerConnection <-> Host WebRTC PeerConnection)**:
   - Direct or TURN-relayed peer transport.
   - Untrusted inputs: Video bitstreams, mouse/keyboard input frames, clipboard payloads, file chunks.
   - Mitigation: DTLS-SRTP encryption with key exchange bound to Session Authorization Tickets (SAT). Relay servers only see encrypted UDP/TCP packets (zero plaintext access).

5. **Boundary 5 (Control Plane -> Storage & Databases)**:
   - Database connections encrypted via TLS 1.3 with credential isolation.
   - Untrusted inputs: SQL injection, unauthorized cross-tenant data leakage.
   - Mitigation: Parameterized queries, ORM entity scoping by `organization_id` on all database operations, row-level security.

---

## 2. STRIDE Threat Analysis Matrix

| Threat Category | Specific Threat Description | Affected Boundary | Architectural Mitigation | Verification Test |
| :--- | :--- | :--- | :--- | :--- |
| **Spoofing** | Adversary attempts to impersonate an enrolled host endpoint to intercept sessions | Boundary 2 | Hardware-backed ECDSA keypair per device (Windows CNG / TPM). Device signs all presence heartbeats with timestamp and nonce. Control plane rejects any duplicate or unverified public keys. | `Test_DeviceSpoofing_InvalidSignature_Rejected` |
| **Spoofing** | Attacker crafts fake operator session request | Boundary 1 | Server validates OIDC JWT signature, audience, expiration, and active MFA claim before issuing any session ticket. | `Test_OperatorSpoofing_InvalidJWT_Rejected` |
| **Tampering** | Rogue user modifies mouse/keyboard coordinates or injects out-of-bounds input packets | Boundary 4 | Binary input protocol enforces strict bounds checking against reported display resolution (e.g., $0 \le x < width$, $0 \le y < height$), rate-limiting ($<1000$ events/sec), and message size ceilings (max 64 bytes per input event). | `Test_InputInjection_OutOfBounds_Discarded` |
| **Tampering** | Local standard user modifies host configuration files on disk | Host Endpoint | Config and identity keys stored in `%ProgramData%\EnterpriseRemote` protected with NTFS ACLs (`SYSTEM: F`, `Administrators: F`, `Users: R` or No Access). Secrets encrypted via DPAPI `CryptProtectData` with `CRYPTPROTECT_LOCAL_MACHINE`. | `Test_LocalFileTampering_ACL_Enforced` |
| **Repudiation** | Operator denies performing privileged action or connecting to sensitive endpoint | Entire Platform | Immutable audit event log stored in PostgreSQL with append-only write patterns. Every session start, consent approval, file transfer, and termination includes operator ID, device ID, timestamp, and client IP. | `Test_AuditLog_Completeness_Verified` |
| **Information Disclosure** | Compromised TURN relay operator attempts to record desktop video or view keystrokes | Boundary 4 | WebRTC DTLS 1.2/1.3 end-to-end encryption. TURN relays only forward encrypted UDP/TCP packets (RFC 8656) and never possess the DTLS private keys or SRTP master keys. | `Test_RelayZeroKnowledge_NoPlaintextAccess` |
| **Information Disclosure** | Passwords, tokens, or private keys leaked into application logs | All Components | Automated structured log scrubber (`SensitiveDataRedactor`) sanitizes all log outputs, stripping Authorization headers, tokens, private keys, and clipboard contents before emission. | `Test_LogScrubber_Redaction_Effective` |
| **Denial of Service** | Malicious actor spams enrollment invitation endpoint with invalid redemption attempts | Boundary 1 / 2 | Rate-limiting on enrollment endpoint (max 5 attempts per IP per minute). Single-use tokens atomically marked REDEEMED on first attempt to prevent race conditions. | `Test_EnrollmentRateLimiting_Enforced` |
| **Elevation of Privilege** | Standard user connects to Session 0 Named Pipe to execute arbitrary commands | Boundary 3 | Pipe server validates client token via `GetNamedPipeClientProcessId` and enforces SDDL restricting connect rights to SYSTEM and the active console logon SID. | `Test_NamedPipe_UnauthorizedUser_AccessDenied` |
| **Elevation of Privilege** | Operator attempts unauthorized file transfer or remote shell on locked host | Boundary 4 | Host enforces policy received directly from Control Plane via SAT token. Capability flags (`PermitFileTransfer`, `PermitClipboard`, `PermitReboot`) are validated on host before execution. | `Test_PrivilegeEscalation_DisabledCapability_Blocked` |

---

## 3. Cryptographic Specifications & Key Management

### Cryptographic Algorithms & Standards
- **Signaling & REST Transport**: TLS 1.3 with AES-256-GCM or ChaCha20-Poly1305.
- **Device Cryptographic Identity**: ECDSA P-256 (secp256r1) with SHA-256 (`SHA256withECDSA`).
- **Session Transport Encryption**: DTLS 1.2/1.3 with `TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256` or `TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256`.
- **Media & Data Channel Protection**: SRTP (AES-GCM-128) and SCTP over DTLS.
- **Invitation Tokens**: 256 bits of cryptographically secure pseudo-randomness (CSPRNG), Base64URL-encoded, stored in the database only as a SHA-256 hash.
- **Local Secret Protection**: Windows Cryptography Next Generation (CNG) with TPM 2.0 key storage provider (`MS_PLATFORM_KEY_STORAGE_PROVIDER`) where available, falling back to DPAPI (`CryptProtectData`) machine store.

### Key Lifecycle & Emergency Revocation
1. **Enrollment**: Device generates keypair locally. Private key never leaves the secure store. Public key is uploaded during enrollment.
2. **Session Authorization Ticket (SAT)**: Issued by Control Plane, signed with Control Plane ECDSA private key. Contains:
   - `session_id`: Unique UUIDv4.
   - `operator_id`: Operator sub claim.
   - `device_id`: Target device ID.
   - `capabilities`: Bitmask of authorized features (`VIEW`, `INPUT`, `CLIPBOARD`, `FILE_TRANSFER`).
   - `expires_at`: Strict 60-second validity window for connection establishment.
3. **Emergency Kill-Switch (Device Level)**:
   - Admin triggers "Emergency Disable" in Control Plane.
   - Control Plane terminates active WSS connection for device, invalidates active sessions, and broadcasts `ForceDisconnect` over WebRTC signaling.
   - Host Service immediately closes active Named Pipe, terminates the User Agent peer connection, and sets local operational state to `DISABLED`.
4. **Emergency Kill-Switch (Organization Level)**:
   - In the event of an active intrusion, organization-wide emergency toggle immediately rejects all new session requests and drops all active peer sessions across all endpoints.

---

## 4. Windows Operating System Security Protections

### Handling Windows Secure Desktop (UAC & Winlogon)
- When a Windows User Account Control (UAC) prompt or Winlogon screen (Ctrl+Alt+Del) activates, Windows switches the active display to the isolated `Winlogon` desktop.
- Standard user processes in Session 1 cannot capture the secure desktop or inject input into it.
- **Security Policy**:
  - The application will **never** employ unauthorized hacks or memory injection into `winlogon.exe` or `csrss.exe`.
  - When the secure desktop is detected, the host agent safely pauses screen capture and sends a structured status message to the controller: `SessionStatus::SecureDesktopActive`.
  - The controller displays an informative banner: *"Windows Secure Desktop active. Awaiting local user elevation response."*
  - If unattended administrative support is enabled with the elevated helper component properly configured and signed, the elevated helper handles the secure desktop transition within supported Windows OS APIs.

### Anti-Malware & EDR Transparency
- The service binary (`EnterpriseRemoteHostSvc.exe`) and user agent (`EnterpriseRemoteUserAgent.exe`) will be fully signed with an Authenticode code-signing certificate.
- Service is registered transparently in `Services.msc` with complete description, binary path, and manufacturer information.
- All operational and security-relevant events are written to Windows Event Log (`Applications and Services Logs/EnterpriseRemote`) with ETW (Event Tracing for Windows) providers for SIEM / EDR ingestion.
- Zero obfuscation, packing, or process hiding techniques are utilized.
