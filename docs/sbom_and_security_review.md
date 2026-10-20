# Enterprise Windows Remote Access Platform: Software Bill of Materials (SBOM) & Security Review

## 1. Software Bill of Materials (SBOM)

The solution is architected with a strict minimal-dependency philosophy to minimize supply chain attack surfaces. The solution uses the official .NET 8 Base Class Library (BCL) and direct Win32 / DXGI operating system APIs.

| Component / Assembly | Version | Source / License | Cryptographic Hash (SHA-256) | Security Purpose |
| :--- | :--- | :--- | :--- | :--- |
| **EnterpriseRemote.Common** | 1.0.0 | In-tree (.NET 8) | Verified In-Tree | Shared protocols, cryptographic utilities, models, redaction |
| **EnterpriseRemote.Host.Service** | 1.0.0 | In-tree (.NET 8) | Verified In-Tree | Session 0 background Windows service, Named Pipe IPC server |
| **EnterpriseRemote.Host.UserAgent** | 1.0.0 | In-tree (.NET 8) | Verified In-Tree | DXGI Desktop Duplication, input injection, session tray UI |
| **EnterpriseRemote.Controller** | 1.0.0 | In-tree (.NET 8) | Verified In-Tree | Operator desktop controller and remote viewer client |
| **EnterpriseRemote.ControlPlane** | 1.0.0 | In-tree (.NET 8) | Verified In-Tree | REST & WSS signaling API, RBAC, enrollment, policy engine |
| **System.Security.Cryptography** | 8.0.0 | Microsoft (.NET BCL) | Native BCL Package | Hardware CNG, ECDSA P-256, SHA-256, HMAC, CSPRNG |
| **System.IO.Pipes** | 8.0.0 | Microsoft (.NET BCL) | Native BCL Package | Inter-process communication with restrictive SDDL ACLs |

Zero unverified third-party libraries, binaries, or native DLL wrappers are embedded.

---

## 2. Static Analysis & Vulnerability Review Results

### 1. Static Secret Scanning
- **Rule**: Zero hardcoded secrets, API tokens, private keys, or passwords.
- **Verification**: Regex and automated AST analysis over all source files.
- **Result**: **PASS**. Zero private keys, passwords, or persistent secrets exist in source code, configuration files, or build artifacts.

### 2. Privilege Boundary Validation
- **Rule**: Screen capture and input injection must not run in Session 0.
- **Verification**: Session 0 background service (`EnterpriseRemoteHostSvc`) delegates desktop capture to the standard user session agent (`EnterpriseRemoteUserAgent`) via authenticated Named Pipe with restrictive Security Descriptors (`SY`, `BA`, `LogonSid`).
- **Result**: **PASS**.

### 3. Fail-Closed Verification
- **Rule**: Expired tokens, revoked credentials, or emergency disable commands must immediately terminate connections.
- **Verification**: Validated by automated unit tests (`Test_FailClosed_OnTokenExpiry`, `PolicyEngine_FailsClosed_WhenDeviceIsDisabledOrRevoked`).
- **Result**: **PASS**.

### 4. Input Injection Bounds Checking
- **Rule**: Out-of-bounds mouse coordinates or unauthorized system key sequences must be rejected.
- **Verification**: Validated by automated unit tests (`InputInjectionEngine_RejectsOutOfBoundsMouseCoordinates`, `InputInjectionEngine_BlocksRawWindowsKeyAndCtrlAltDel`).
- **Result**: **PASS**.

### 5. Sensitive Data Redaction
- **Rule**: Logs must not leak authentication tokens, private keys, or passwords.
- **Verification**: Validated by automated unit tests (`SensitiveDataRedactor_RedactsTokensKeysAndSecrets`, `SiemAuditExporter_ExportsValidJsonCefAndSyslog_WithRedaction`).
- **Result**: **PASS**.
