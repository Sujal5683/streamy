# Enterprise Windows Remote Access Platform: Incident Response, Recovery & Uninstallation Guide

## 1. Incident Response Playbooks

### Incident Type 1: Compromised Operator Identity
**Indicators**: Unauthorized session requests detected, abnormal access hours, or anomalous device group access.
**Containment Steps**:
1. Revoke the operator's OIDC / Azure AD session in the Identity Provider immediately.
2. In the Control Plane Administrative Console, click **Revoke Operator Sessions** for the user. This invalidates all outstanding Session Authorization Tickets (SAT) and broadcasts disconnect directives over signaling channels.
3. Review audit logs (`/api/v1/audit/events?operatorId=...`) to determine all endpoints accessed by the operator.
4. Export audit trail in CEF / JSON for forensic analysis.

---

### Incident Type 2: Compromised or Lost Target Endpoint
**Indicators**: Physical theft of laptop, unauthorized local software installation, or malware detection by endpoint EDR.
**Containment Steps**:
1. In the Administrative Console, locate the device and trigger **Emergency Disable**:
   ```http
   POST /api/v1/devices/{deviceId}/emergency-disable
   ```
2. The Control Plane updates device status to `Disabled`.
3. If an active session is in progress, the Host Service immediately closes the local Named Pipe, shuts down the User Agent peer connection, and halts capture.
4. Cryptographically revoke the device by deleting or revoking its public key from the database.

---

### Incident Type 3: Suspected Relay / Infrastructure Compromise
**Zero-Knowledge Architecture Verification**:
- The platform uses WebRTC with DTLS 1.2/1.3 and SRTP. Relays (TURN servers) operate in zero-knowledge mode and only process opaque encrypted UDP/TCP packets. Even in the event of an adversary compromising a TURN relay, the adversary cannot decrypt video, keystrokes, clipboard, or file transfers.
- Rotate TURN relay credentials in the Control Plane configuration.

---

## 2. Certificate & Cryptographic Key Rotation

### Control Plane Signing Key Rotation
1. The Control Plane uses an ECDSA P-256 keypair to sign short-lived Session Authorization Tickets (SAT).
2. During key rotation, the Control Plane retains the previous public key in its verification list for 2 minutes to allow any in-flight 60-second tickets to establish their connection gracefully.
3. All new tickets are signed with the newly generated ECDSA private key.

### Enrolled Device Key Re-Registration
1. To rotate an enrolled device's hardware key, an administrator issues a re-enrollment invitation token.
2. The device generates a new ECDSA P-256 keypair in the machine's Windows CNG / TPM store and updates its public key registration in the Control Plane.

---

## 3. Clean Recovery & Complete Uninstallation

### Uninstallation Procedure
To completely uninstall the Enterprise Remote Access Host from an endpoint without leaving residual services, scheduled tasks, or configuration files:

```powershell
# Run in an elevated Administrator PowerShell prompt:
Stop-Service -Name "EnterpriseRemoteHostSvc" -ErrorAction SilentlyContinue
sc.exe delete "EnterpriseRemoteHostSvc"

# Remove application files and data directories
Remove-Item -Recurse -Force "C:\Program Files\EnterpriseRemote" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "C:\ProgramData\EnterpriseRemote" -ErrorAction SilentlyContinue

# Verify clean removal
Get-Service -Name "EnterpriseRemoteHostSvc" -ErrorAction SilentlyContinue
```
Upon completion, the endpoint is fully restored to its pre-installation state.
