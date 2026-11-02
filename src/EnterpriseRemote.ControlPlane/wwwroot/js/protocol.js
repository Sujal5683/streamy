/**
 * Enterprise Remote Access - Web Controller Binary Protocol
 * 100% binary-compatible with DataChannelProtocol.cs
 */

export const InputEventType = {
    MouseMove: 0x01,
    MouseButtonDown: 0x02,
    MouseButtonUp: 0x03,
    MouseWheel: 0x04,
    KeyDown: 0x10,
    KeyUp: 0x11,
    SwitchDisplay: 0x20,
    SwitchDesktop: 0x21,
    DesktopListUpdate: 0x22,
    CursorUpdate: 0x25,
    StreamConfig: 0x35,
    LatencyPing: 0x40,
    LatencyPong: 0x41
};

export const MouseButton = {
    None: 0,
    Left: 1,
    Right: 2,
    Middle: 4,
    X1: 8,
    X2: 16
};

export const ModifierKeys = {
    None: 0,
    Control: 1,
    Alt: 2,
    Shift: 4,
    Windows: 8
};

export const RemoteCursorShape = {
    Default: 0, // Standard Arrow
    Hand: 1,    // Pointer / Hand (buttons, links)
    IBeam: 2,   // Text cursor (input fields, text editing)
    SizeAll: 3, // Move / Pan (4-way arrows)
    SizeWE: 4,  // Horizontal resize (horizontal double arrow)
    SizeNS: 5,  // Vertical resize (vertical double arrow)
    Wait: 6,    // Busy / Waiting
    Cross: 7    // Precision crosshair
};

export const CursorCssMap = {
    [RemoteCursorShape.Default]: 'default',
    [RemoteCursorShape.Hand]: 'pointer',
    [RemoteCursorShape.IBeam]: 'text',
    [RemoteCursorShape.SizeAll]: 'move',
    [RemoteCursorShape.SizeWE]: 'ew-resize',
    [RemoteCursorShape.SizeNS]: 'ns-resize',
    [RemoteCursorShape.Wait]: 'wait',
    [RemoteCursorShape.Cross]: 'crosshair'
};

/**
 * Serializes mouse input packet: 21 bytes
 * [0]: Type (0x01, 0x02, 0x03, 0x04)
 * [1]: MouseButton (1, 2, 4, etc.)
 * [2]: DisplayIndex (0)
 * [3..6]: NormalizedX (Float32, Little Endian)
 * [7..10]: NormalizedY (Float32, Little Endian)
 * [11..12]: WheelDelta (Int16, Little Endian)
 * [13..20]: TimestampMillis (UInt64, Little Endian)
 */
export function serializeMouse(type, button, normX, normY, wheelDelta = 0, displayIndex = 0) {
    const buffer = new ArrayBuffer(21);
    const view = new DataView(buffer);

    view.setUint8(0, type);
    view.setUint8(1, button);
    view.setUint8(2, displayIndex);
    view.setFloat32(3, Math.min(1.0, Math.max(0.0, normX)), true);
    view.setFloat32(7, Math.min(1.0, Math.max(0.0, normY)), true);
    view.setInt16(11, wheelDelta, true);
    view.setBigUint64(13, BigInt(Date.now()), true);

    return buffer;
}

/**
 * Serializes keyboard input packet: 18 bytes
 * [0]: Type (0x10 Down, 0x11 Up)
 * [1..2]: VirtualKeyCode (UInt16, Little Endian)
 * [3..4]: ScanCode (UInt16, Little Endian)
 * [5]: Modifiers (Ctrl=1, Alt=2, Shift=4, Win=8)
 * [6..9]: UnicodeChar (UInt32, Little Endian)
 * [10..17]: TimestampMillis (UInt64, Little Endian)
 */
export function serializeKeyboard(type, vk, scanCode = 0, modifiers = ModifierKeys.None, unicodeChar = 0) {
    const buffer = new ArrayBuffer(18);
    const view = new DataView(buffer);

    view.setUint8(0, type);
    view.setUint16(1, vk, true);
    view.setUint16(3, scanCode, true);
    view.setUint8(5, modifiers);
    view.setUint32(6, unicodeChar, true);
    view.setBigUint64(10, BigInt(Date.now()), true);

    return buffer;
}

/**
 * Serializes StreamConfig packet: 14 bytes
 * [0]: Type (0x35)
 * [1]: TargetFps (UInt8)
 * [2]: Quality (UInt8)
 * [3..4]: MaxHeight (UInt16, Little Endian)
 * [5]: LowLatencyMode (UInt8, 1 or 0)
 * [6..13]: TimestampMillis (UInt64, Little Endian)
 */
export function serializeStreamConfig(targetFps, quality, maxHeight = 0, lowLatencyMode = 1) {
    const buffer = new ArrayBuffer(14);
    const view = new DataView(buffer);

    view.setUint8(0, InputEventType.StreamConfig);
    view.setUint8(1, Math.min(200, Math.max(10, targetFps)));
    view.setUint8(2, Math.min(95, Math.max(30, quality)));
    view.setUint16(3, maxHeight, true);
    view.setUint8(5, lowLatencyMode ? 1 : 0);
    view.setBigUint64(6, BigInt(Date.now()), true);

    return buffer;
}

/**
 * Serializes LatencyPing packet: 9 bytes
 * [0]: Type (0x40)
 * [1..8]: TimestampMillis (UInt64, Little Endian)
 */
export function serializeLatencyPing(timestampMillis = Date.now()) {
    const buffer = new ArrayBuffer(9);
    const view = new DataView(buffer);

    view.setUint8(0, InputEventType.LatencyPing);
    view.setBigUint64(1, BigInt(timestampMillis), true);

    return buffer;
}

/**
 * Tries to deserialize a LatencyPong packet: 9 bytes
 */
export function tryDeserializeLatencyPong(dataView) {
    if (dataView.byteLength < 9 || dataView.getUint8(0) !== InputEventType.LatencyPong) {
        return null;
    }
    const ts = Number(dataView.getBigUint64(1, true));
    return { timestampMillis: ts };
}

/**
 * Tries to deserialize a CursorUpdate packet: 2 bytes
 */
export function tryDeserializeCursorUpdate(dataView) {
    if (dataView.byteLength < 2 || dataView.getUint8(0) !== InputEventType.CursorUpdate) {
        return null;
    }
    const shape = dataView.getUint8(1);
    return {
        shape,
        cssCursor: CursorCssMap[shape] || 'default'
    };
}

/**
 * Serializes SwitchDesktop packet
 * [0]: Type (0x21)
 * [1]: DisplayIndex (UInt8)
 * [2]: AutoFollow (UInt8, 1 or 0)
 * [3..4]: DesktopNameLen (UInt16, Little Endian)
 * [5..5+len-1]: DesktopName UTF-8 bytes
 * [5+len..5+len+7]: TimestampMillis (UInt64, Little Endian)
 */
export function serializeSwitchDesktop(displayIndex = 0, autoFollow = true, desktopName = 'Auto') {
    const encoder = new TextEncoder();
    const nameBytes = encoder.encode(desktopName || 'Auto');
    const nameLen = nameBytes.length;
    const buffer = new ArrayBuffer(1 + 1 + 1 + 2 + nameLen + 8);
    const view = new DataView(buffer);
    const u8 = new Uint8Array(buffer);

    view.setUint8(0, InputEventType.SwitchDesktop);
    view.setUint8(1, displayIndex);
    view.setUint8(2, autoFollow ? 1 : 0);
    view.setUint16(3, nameLen, true);
    u8.set(nameBytes, 5);
    view.setBigUint64(5 + nameLen, BigInt(Date.now()), true);

    return buffer;
}

/**
 * Tries to deserialize DesktopListUpdate packet
 */
export function tryDeserializeDesktopList(dataView) {
    if (dataView.byteLength < 4 || dataView.getUint8(0) !== InputEventType.DesktopListUpdate) {
        return null;
    }
    try {
        const u8 = new Uint8Array(dataView.buffer, dataView.byteOffset, dataView.byteLength);
        const decoder = new TextDecoder('utf-8');
        let offset = 1;

        // BinaryReader string has 7-bit encoded int length prefix
        function readString() {
            let len = 0;
            let shift = 0;
            while (offset < u8.length) {
                const b = u8[offset++];
                len |= (b & 0x7f) << shift;
                if ((b & 0x80) === 0) break;
                shift += 7;
            }
            const str = decoder.decode(u8.subarray(offset, offset + len));
            offset += len;
            return str;
        }

        const activeDesktop = readString();
        const deskCount = dataView.getUint16(offset, true);
        offset += 2;
        const desktops = [];
        for (let i = 0; i < deskCount; i++) {
            desktops.push(readString());
        }

        const dispCount = dataView.getUint16(offset, true);
        offset += 2;
        const displays = [];
        for (let i = 0; i < dispCount; i++) {
            const idx = dataView.getUint8(offset++);
            const dispName = readString();
            const w = dataView.getUint16(offset, true);
            offset += 2;
            const h = dataView.getUint16(offset, true);
            offset += 2;
            const isPrimary = dataView.getUint8(offset++) !== 0;
            displays.push({ index: idx, name: dispName, width: w, height: h, isPrimary });
        }

        return { activeDesktop, desktops, displays };
    } catch {
        return null;
    }
}
