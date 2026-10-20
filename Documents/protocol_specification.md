# Enterprise Windows Remote Access Platform: Protocol Specification

## 1. Signaling Protocol Specification (Control Plane <-> Endpoints)

The signaling protocol operates over WebSocket Secure (`wss://`) using JSON-RPC 2.0 framing. All signaling frames are strictly validated against JSON schema models and rejected if malformed or excessive in size (>64 KB).

### 1.1 Envelope Format
```json
{
  "jsonrpc": "2.0",
  "id": "urn:uuid:6ba7b810-9dad-11d1-80b4-00c04fd430c8",
  "method": "session.offer",
  "params": { ... }
}
```

### 1.2 Core Methods & Message Schemas

#### Method: `session.request` (Controller -> Control Plane)
```json
{
  "method": "session.request",
  "params": {
    "device_id": "DEV-849204",
    "requested_capabilities": ["VIEW", "INPUT", "CLIPBOARD"],
    "reason": "Ticket #4928 - Disk Cleanup Assistance"
  }
}
```
**Response (Success)**:
```json
{
  "result": {
    "session_id": "SES-994821",
    "status": "AwaitingConsent | Approved",
    "sat_token": "eyJhbGciOiJFUzI1NiIs...",
    "ice_servers": [
      {
        "urls": ["turn:turn.enterprise.internal:3478"],
        "username": "user_ses_994821",
        "credential": "ephemeral_secret_token"
      }
    ]
  }
}
```

#### Method: `session.consent_prompt` (Control Plane -> Host Service)
```json
{
  "method": "session.consent_prompt",
  "params": {
    "session_id": "SES-994821",
    "operator_name": "Alice Smith (IT Tier-2)",
    "operator_email": "alice@corp.internal",
    "requested_capabilities": ["VIEW", "INPUT"],
    "timeout_seconds": 30
  }
}
```

#### Method: `webrtc.signal` (Bidirectional Offer/Answer/ICE)
```json
{
  "method": "webrtc.signal",
  "params": {
    "session_id": "SES-994821",
    "signal_type": "offer | answer | candidate",
    "sdp": "v=0\r\no=- 48201 2 IN IP4 ...",
    "candidate": {
      "candidate": "candidate:1 1 UDP 2130706431 192.168.1.50 50000 typ host ...",
      "sdpMid": "0",
      "sdpMLineIndex": 0
    }
  }
}
```

#### Method: `device.emergency_disable` (Control Plane -> Host Service)
```json
{
  "method": "device.emergency_disable",
  "params": {
    "device_id": "DEV-849204",
    "reason": "Security Incident INC-84021 - Device Quarantined",
    "timestamp_utc": "2026-10-20T16:50:00Z"
  }
}
```

---

## 2. Local IPC Protocol (Session 0 Service <-> Session 1+ User Agent)

The local IPC channel connects the background Windows Service (Session 0) to the Interactive User Agent (Session 1+) via a Windows Named Pipe:
`\\.\pipe\EnterpriseRemote_IPC_<TargetSessionId>`

### 2.1 Binary Frame Structure
Every frame over the Named Pipe uses a fixed-length 16-byte binary header followed by payload and an HMAC-SHA256 signature calculated over header + payload using an ephemeral session secret exchanged during handshake:

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|       Magic: 'E' 'R' 'P' 'C' (0x45525043)                     |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|          Version (0x0001)     |       Message Type Code       |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                        Sequence Number                        |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                        Payload Length                         |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                        Payload Data ...                       |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                    HMAC-SHA256 (32 bytes)                     |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

### 2.2 Message Type Codes
- `0x0001`: `IPC_HANDSHAKE_REQ` (Agent -> Svc with SessionId, PID, and Process Token Hash)
- `0x0002`: `IPC_HANDSHAKE_RESP` (Svc -> Agent with Ephemeral Session Secret & Allowed Features)
- `0x0010`: `IPC_PROMPT_CONSENT` (Svc -> Agent to display local notification/dialog)
- `0x0011`: `IPC_CONSENT_RESULT` (Agent -> Svc with user choice: Approved / Denied / Timeout)
- `0x0020`: `IPC_WEBRTC_OFFER` (Svc -> Agent forwarding remote operator offer)
- `0x0021`: `IPC_WEBRTC_ANSWER` (Agent -> Svc returning generated SDP answer)
- `0x0022`: `IPC_WEBRTC_CANDIDATE` (Bidirectional ICE candidate propagation)
- `0x0030`: `IPC_SESSION_STATE_UPDATE` (Agent -> Svc with active metrics / FPS / bandwidth)
- `0x0040`: `IPC_TERMINATE_SESSION` (Bidirectional disconnect directive)

---

## 3. WebRTC SCTP Data Channel Protocol (Input & Control)

All interactive controls operate over multiplexed, dedicated WebRTC SCTP Data Channels:

### 3.1 Channel 0: `input` (Unordered, Low-Latency Binary Channel)
Designed for ultra-low latency keyboard and mouse inputs. Max packet size: 32 bytes.

#### Packet Format: Mouse Event (0x01)
```
[Type: 1B (0x01)] [Flags: 1B] [DisplayIndex: 1B] [Reserved: 1B]
[Normalized X: 4B float (0.0 to 1.0)]
[Normalized Y: 4B float (0.0 to 1.0)]
[Delta Wheel: 2B int16] [Buttons Bitmask: 2B]
[Timestamp Millis: 8B uint64]
```
- **Flags**: `0x01 = Move`, `0x02 = Down`, `0x04 = Up`, `0x08 = Wheel`
- **Buttons Bitmask**: `Bit 0 = Left`, `Bit 1 = Right`, `Bit 2 = Middle`, `Bit 3 = X1`, `Bit 4 = X2`
- **Safety Enforcement**:
  - The host converts `Normalized X/Y` to pixels based solely on the active monitored desktop bounds.
  - Rate-limit: Maximum 1,000 mouse packets per second per session. Any excess is dropped with a rate-limit audit warning.

#### Packet Format: Keyboard Event (0x02)
```
[Type: 1B (0x02)] [Action: 1B (0=Down, 1=Up)] [VirtualKeyCode: 2B uint16]
[ScanCode: 2B uint16] [ModifierFlags: 2B] [UnicodeChar: 4B UTF-32]
[Timestamp Millis: 8B uint64]
```
- **ModifierFlags**: `Bit 0 = Ctrl`, `Bit 1 = Alt`, `Bit 2 = Shift`, `Bit 3 = Win`
- **Safety Enforcement**:
  - Direct injection of Windows Secure sequences (e.g., `Ctrl+Alt+Del`, `Win+L`) via raw keyboard packets is rejected. Dedicated administrative commands must be requested and authorized via Control Channel.

### 3.2 Channel 1: `control` (Reliable, Ordered JSON Channel)
Handles high-level session events:
- `DisplaySwitch`: Switch captured display monitor (e.g., Monitor 1 -> Monitor 2).
- `QualityChange`: Operator requests quality profile (e.g., "Crisp Text" vs "Smooth Motion").
- `PauseStreaming`: Temporarily pauses desktop video while keeping session open.
- `EmergencyStop`: Either party initiates instant session termination.

### 3.3 Channel 2: `clipboard` (Reliable, Policy-Governed Channel)
- Maximum payload limit: 4 MB.
- Format negotiation: `text/plain;charset=utf-8` or `image/png`.
- Requires host capability flag `CLIPBOARD_SYNC` in SAT token.
- Automatic redaction: Host logs audit event `ClipboardTransferred { byte_count, format }` without logging the text/image contents.

### 3.4 Channel 3: `file_transfer` (Reliable, Policy-Governed Channel)
- Chunk size: 64 KB.
- Manifest phase: `TransferInit { filename, size_bytes, sha256_checksum }`.
- Host prompt: If configured by policy, local user receives prompt: *"Alice wants to send file 'setup.log' (140 KB). Accept?"*
- Chunk streaming with backpressure and pause/resume.
- Finalization: Complete SHA-256 hash verified before destination file is committed.
