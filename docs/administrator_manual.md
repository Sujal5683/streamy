# Enterprise Windows Remote Access Platform: Administrator Manual

## 1. System Overview & Deployment

The Enterprise Windows Remote Access Platform is designed for customer-controlled private deployment with zero external dependencies, verifiable cryptographic enrollment, and strict privilege isolation.

### Component Deployment Topology
- **Enterprise Control Plane**: Hosted on Windows Server or Linux container running ASP.NET Core Kestrel (port 443 / HTTPS & WSS).
- **PostgreSQL Database**: Relational store for durable state (Tenants, Devices, Groups, Policies, and Immutable Audit Logs).
- **Host Agent**: Installed on enterprise Windows endpoints (Windows 10/11, Windows Server 2016+).
  - Background Windows Service: `EnterpriseRemoteHostSvc.exe` (Session 0).
  - Interactive Desktop Agent: `EnterpriseRemoteUserAgent.exe` (Session 1+).
- **Controller Application**: Installed on operator workstations for remote desktop control.

---

## 2. Installation & Enrollment (Workflow B)

### Automated Silent Enterprise Deployment
Administrators can deploy the host agent via Microsoft Intune, SCCM, or Group Policy using silent command-line parameters:

```powershell
msiexec.exe /i "EnterpriseRemoteSetup.msi" /qn /norestart `
  CONTROL_PLANE_URL="https://controlplane.corp.internal" `
  INVITATION_CODE="INV-8F21-4B90-99E1"
```

### Invitation Lifecycle & Device Group Assignment
1. In the Administrative Console, navigate to **Device Groups** -> **Enrollment Invitations**.
2. Click **Create Single-Use Invitation**.
3. Select target **Device Group** (e.g. `Finance_Desktops`, `Engineering_CAD`).
4. Set **Expiration Time (TTL)**: Default is 15 minutes. High-entropy CSPRNG ensures 256 bits of entropy.
5. Specify whether **Attended Local Approval** is required or **Unattended Access** is permitted for this group.
6. The invitation code is generated (`INV-XXXX-XXXX-XXXX`) and only its SHA-256 hash is saved to the database.
7. Upon execution on the endpoint, the host generates an ECDSA P-256 keypair locally, stores it in the machine's Windows CNG / TPM store, and redeems the invitation. Once redeemed, the token cannot be reused or replayed.

---

## 3. Unattended Access & Attended Approval Policies

Administrators configure granular policy rules per device group:

```json
{
  "group_id": "GRP-FINANCE-DESKTOPS",
  "minimum_role": "Operator",
  "attended_required": true,
  "operating_hours": {
    "start_utc": "08:00:00",
    "end_utc": "18:00:00"
  },
  "permitted_capabilities": ["VIEW", "INPUT", "CLIPBOARD"]
}
```

- **Attended Required (`true`)**: When an operator requests a session, the local user in Session 1+ is presented with a 30-second consent prompt displaying the operator's name and reason. If unanswered, the session is auto-rejected.
- **Unattended Permitted (`false`)**: Directly authorizes connection during allowed operating hours, subject to real-time SAT verification.

---

## 4. Emergency Kill-Switch & Revocation

In the event of an active security incident:
1. **Device-Level Emergency Disable**:
   - Run via Admin API or Console: `POST /api/v1/devices/{id}/emergency-disable`
   - Immediately disconnects all active WebRTC sessions, closes local Named Pipe, and marks device status as `Disabled` (Fail-Closed).
2. **Device Cryptographic Revocation**:
   - Revokes the device's public key in the database, preventing any future sign-in or session dispatch.
3. **Organization-Wide Kill-Switch**:
   - Rejects all inbound session authorization requests across all tenants in one atomic update.

---

## 5. SIEM & Audit Event Export

All operational and security-relevant events are written to the database and can be streamed via RFC 5424 Syslog, Common Event Format (CEF), or JSON to Microsoft Sentinel, Splunk, Elastic, or Datadog.

### Sample CEF Event
```
CEF:0|EnterpriseRemote|RemoteAccessPlatform|1.0|SessionAuthorized|SessionAuthorized|5|dvc=DEV-FIN-01 suser=USR-ALICE src=10.0.1.20 msg=Session SES-48192 authorized. cs1=ORG-CORP cs1Label=OrgId
```
All tokens, passwords, and private keys are scrubbed automatically before emission.
