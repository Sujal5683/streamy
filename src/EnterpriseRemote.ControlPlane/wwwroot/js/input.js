/**
 * Enterprise Remote Access - Web Controller Input Engine
 * Handles sub-pixel mouse precision, keyboard translation, and synthetic quick-action keys
 */

import {
    InputEventType,
    MouseButton,
    ModifierKeys,
    serializeMouse,
    serializeKeyboard
} from './protocol.js';

// Complete Windows Virtual-Key (VK) Code Mapping
const KEY_CODE_TO_VK = {
    'KeyA': 0x41, 'KeyB': 0x42, 'KeyC': 0x43, 'KeyD': 0x44, 'KeyE': 0x45,
    'KeyF': 0x46, 'KeyG': 0x47, 'KeyH': 0x48, 'KeyI': 0x49, 'KeyJ': 0x4A,
    'KeyK': 0x4B, 'KeyL': 0x4C, 'KeyM': 0x4D, 'KeyN': 0x4E, 'KeyO': 0x4F,
    'KeyP': 0x50, 'KeyQ': 0x51, 'KeyR': 0x52, 'KeyS': 0x53, 'KeyT': 0x54,
    'KeyU': 0x55, 'KeyV': 0x56, 'KeyW': 0x57, 'KeyX': 0x58, 'KeyY': 0x59, 'KeyZ': 0x5A,

    'Digit0': 0x30, 'Digit1': 0x31, 'Digit2': 0x32, 'Digit3': 0x33, 'Digit4': 0x34,
    'Digit5': 0x35, 'Digit6': 0x36, 'Digit7': 0x37, 'Digit8': 0x38, 'Digit9': 0x39,

    'Numpad0': 0x60, 'Numpad1': 0x61, 'Numpad2': 0x62, 'Numpad3': 0x63, 'Numpad4': 0x64,
    'Numpad5': 0x65, 'Numpad6': 0x66, 'Numpad7': 0x67, 'Numpad8': 0x68, 'Numpad9': 0x69,
    'NumpadMultiply': 0x6A, 'NumpadAdd': 0x6B, 'NumpadSubtract': 0x6D, 'NumpadDecimal': 0x6E, 'NumpadDivide': 0x6F,

    'F1': 0x70, 'F2': 0x71, 'F3': 0x72, 'F4': 0x73, 'F5': 0x74, 'F6': 0x75,
    'F7': 0x76, 'F8': 0x77, 'F9': 0x78, 'F10': 0x79, 'F11': 0x7A, 'F12': 0x7B,

    'Enter': 0x0D, 'Escape': 0x1B, 'Backspace': 0x08, 'Tab': 0x09, 'Space': 0x20,
    'Delete': 0x2E, 'Insert': 0x2D, 'Home': 0x24, 'End': 0x23, 'PageUp': 0x21, 'PageDown': 0x22,
    'ArrowLeft': 0x25, 'ArrowUp': 0x26, 'ArrowRight': 0x27, 'ArrowDown': 0x28,

    'ShiftLeft': 0x10, 'ShiftRight': 0x10,
    'ControlLeft': 0x11, 'ControlRight': 0x11,
    'AltLeft': 0x12, 'AltRight': 0x12,
    'MetaLeft': 0x5B, 'MetaRight': 0x5C,

    'Semicolon': 0xBA, 'Equal': 0xBB, 'Comma': 0xBC, 'Minus': 0xBD, 'Period': 0xBE, 'Slash': 0xBF,
    'Backquote': 0xC0, 'BracketLeft': 0xDB, 'Backslash': 0xDC, 'BracketRight': 0xDD, 'Quote': 0xDE,
    'CapsLock': 0x14, 'ScrollLock': 0x91, 'NumLock': 0x90, 'PrintScreen': 0x2C, 'Pause': 0x13
};

// Complete Windows PS/2 & Hardware Scan Code Mapping for Chromium & Web IDEs
const KEY_CODE_TO_SCAN_CODE = {
    'Escape': 0x01,
    'Digit1': 0x02, 'Digit2': 0x03, 'Digit3': 0x04, 'Digit4': 0x05,
    'Digit5': 0x06, 'Digit6': 0x07, 'Digit7': 0x08, 'Digit8': 0x09, 'Digit9': 0x0A, 'Digit0': 0x0B,
    'Minus': 0x0C, 'Equal': 0x0D, 'Backspace': 0x0E,
    'Tab': 0x0F, 'KeyQ': 0x10, 'KeyW': 0x11, 'KeyE': 0x12, 'KeyR': 0x13,
    'KeyT': 0x14, 'KeyY': 0x15, 'KeyU': 0x16, 'KeyI': 0x17, 'KeyO': 0x18, 'KeyP': 0x19,
    'BracketLeft': 0x1A, 'BracketRight': 0x1B, 'Enter': 0x1C,
    'ControlLeft': 0x1D, 'ControlRight': 0x1D,
    'KeyA': 0x1E, 'KeyS': 0x1F, 'KeyD': 0x20, 'KeyF': 0x21, 'KeyG': 0x22,
    'KeyH': 0x23, 'KeyJ': 0x24, 'KeyK': 0x25, 'KeyL': 0x26,
    'Semicolon': 0x27, 'Quote': 0x28, 'Backquote': 0x29,
    'ShiftLeft': 0x2A, 'Backslash': 0x2B,
    'KeyZ': 0x2C, 'KeyX': 0x2D, 'KeyC': 0x2E, 'KeyV': 0x2F, 'KeyB': 0x30,
    'KeyN': 0x31, 'KeyM': 0x32, 'Comma': 0x33, 'Period': 0x34, 'Slash': 0x35,
    'ShiftRight': 0x36, 'NumpadMultiply': 0x37, 'AltLeft': 0x38, 'AltRight': 0x38,
    'Space': 0x39, 'CapsLock': 0x3A,
    'F1': 0x3B, 'F2': 0x3C, 'F3': 0x3D, 'F4': 0x3E, 'F5': 0x3F,
    'F6': 0x40, 'F7': 0x41, 'F8': 0x42, 'F9': 0x43, 'F10': 0x44,
    'NumLock': 0x45, 'ScrollLock': 0x46,
    'Numpad7': 0x47, 'Numpad8': 0x48, 'Numpad9': 0x49, 'NumpadSubtract': 0x4A,
    'Numpad4': 0x4B, 'Numpad5': 0x4C, 'Numpad6': 0x4D, 'NumpadAdd': 0x4E,
    'Numpad1': 0x4F, 'Numpad2': 0x50, 'Numpad3': 0x51, 'Numpad0': 0x52, 'NumpadDecimal': 0x53,
    'F11': 0x57, 'F12': 0x58,
    'ArrowUp': 0x48, 'ArrowLeft': 0x4B, 'ArrowRight': 0x4D, 'ArrowDown': 0x50,
    'Home': 0x47, 'End': 0x4F, 'PageUp': 0x49, 'PageDown': 0x51,
    'Insert': 0x52, 'Delete': 0x53
};

export class InputHandler {
    constructor(canvas, streamClient) {
        this.canvas = canvas;
        this.streamClient = streamClient;
        this.inputEnabled = true;

        this.isMouseDown = false;
        this.lastNormX = -1;
        this.lastNormY = -1;
        this.lastMouseMoveTime = 0;

        this.initMouseListeners();
        this.initKeyboardListeners();
    }

    setInputEnabled(enabled) {
        this.inputEnabled = enabled;
        if (!enabled) {
            this.canvas.style.cursor = 'default';
        }
    }

    /**
     * Converts DOM MouseEvent coordinates to normalized [0.0 - 1.0] remote screen coordinates
     */
    /**
     * Converts DOM MouseEvent coordinates to normalized [0.0 - 1.0] remote screen coordinates.
     * Features edge-clamping and boundary tolerance so clicks near corners/borders
     * (e.g. Windows Start Menu, Taskbar, Close button) never get dropped by letterboxing.
     */
    getNormalizedCoordinates(e) {
        let rect = this.streamClient.renderRect;
        const canvasRect = this.canvas.getBoundingClientRect();
        if (canvasRect.width <= 0 || canvasRect.height <= 0) {
            return { inside: false, normX: 0, normY: 0 };
        }

        const scaleX = this.canvas.width / canvasRect.width;
        const scaleY = this.canvas.height / canvasRect.height;

        const canvasX = (e.clientX - canvasRect.left) * scaleX;
        const canvasY = (e.clientY - canvasRect.top) * scaleY;

        // Fallback to full canvas dimensions if renderRect has not yet been populated by a frame
        if (!rect || rect.width <= 0 || rect.height <= 0) {
            rect = { x: 0, y: 0, width: this.canvas.width, height: this.canvas.height };
        }

        // Generous margin tolerance (30px) around render rect to ensure corner clicks (Start button, close icon) fire
        const margin = 30;
        const inside = canvasX >= (rect.x - margin) && canvasX <= (rect.x + rect.width + margin) &&
                       canvasY >= (rect.y - margin) && canvasY <= (rect.y + rect.height + margin);

        const normX = Math.min(1.0, Math.max(0.0, (canvasX - rect.x) / Math.max(1, rect.width)));
        const normY = Math.min(1.0, Math.max(0.0, (canvasY - rect.y) / Math.max(1, rect.height)));

        return { inside, normX, normY };
    }

    initMouseListeners() {
        const c = this.canvas;

        // Prevent browser right-click menu over viewport
        c.addEventListener('contextmenu', (e) => {
            e.preventDefault();
            return false;
        });

        // Mouse Move with 4ms cadence throttling (up to 250Hz input polling for ultra-low latency)
        c.addEventListener('mousemove', (e) => {
            if (!this.inputEnabled || !this.streamClient.isConnected) return;

            const { inside, normX, normY } = this.getNormalizedCoordinates(e);
            if (!inside && !this.isMouseDown) return;

            const now = performance.now();
            if (!this.isMouseDown && (now - this.lastMouseMoveTime) < 4.0) {
                return;
            }

            // Small movement epsilon filter
            if (!this.isMouseDown &&
                Math.abs(normX - this.lastNormX) < 0.0005 &&
                Math.abs(normY - this.lastNormY) < 0.0005) {
                return;
            }

            this.lastMouseMoveTime = now;
            this.lastNormX = normX;
            this.lastNormY = normY;

            const packet = serializeMouse(InputEventType.MouseMove, MouseButton.None, normX, normY, 0, 0);
            this.streamClient.sendPacket(packet);
        });

        // Mouse Down with preventDefault to eliminate text-selection interference and pointer capture
        c.addEventListener('mousedown', (e) => {
            e.preventDefault();
            c.focus();
            if (!this.inputEnabled || !this.streamClient.isConnected) return;

            const { inside, normX, normY } = this.getNormalizedCoordinates(e);
            if (!inside) return;

            this.isMouseDown = true;
            try { c.setPointerCapture?.(e.pointerId); } catch { }
            this.lastNormX = normX;
            this.lastNormY = normY;

            const btn = e.button === 2 ? MouseButton.Right :
                        e.button === 1 ? MouseButton.Middle : MouseButton.Left;

            const packet = serializeMouse(InputEventType.MouseButtonDown, btn, normX, normY, 0, 0);
            this.streamClient.sendPacket(packet);
        });

        // Mouse Up
        window.addEventListener('mouseup', (e) => {
            if (!this.isMouseDown) return;
            this.isMouseDown = false;
            try { c.releasePointerCapture?.(e.pointerId); } catch { }

            if (!this.inputEnabled || !this.streamClient.isConnected) return;

            const { normX, normY } = this.getNormalizedCoordinates(e);
            const btn = e.button === 2 ? MouseButton.Right :
                        e.button === 1 ? MouseButton.Middle : MouseButton.Left;

            const packet = serializeMouse(InputEventType.MouseButtonUp, btn, normX, normY, 0, 0);
            this.streamClient.sendPacket(packet);
        });

        // Continuous dragging across window boundaries
        window.addEventListener('mousemove', (e) => {
            if (!this.isMouseDown) return;
            if (!this.inputEnabled || !this.streamClient.isConnected) return;

            const { normX, normY } = this.getNormalizedCoordinates(e);
            this.lastNormX = normX;
            this.lastNormY = normY;

            const packet = serializeMouse(InputEventType.MouseMove, MouseButton.None, normX, normY, 0, 0);
            this.streamClient.sendPacket(packet);
        });

        // Mouse Wheel
        c.addEventListener('wheel', (e) => {
            if (!this.inputEnabled || !this.streamClient.isConnected) return;

            const { inside, normX, normY } = this.getNormalizedCoordinates(e);
            if (!inside) return;

            e.preventDefault();

            // Standard Windows wheel delta: 120 per click (invert DOM deltaY)
            const delta = e.deltaY < 0 ? 120 : -120;
            const packet = serializeMouse(InputEventType.MouseWheel, MouseButton.None, normX, normY, delta, 0);
            this.streamClient.sendPacket(packet);
        }, { passive: false });
    }

    getModifiers(e) {
        let mods = ModifierKeys.None;
        if (e.ctrlKey) mods |= ModifierKeys.Control;
        if (e.altKey) mods |= ModifierKeys.Alt;
        if (e.shiftKey) mods |= ModifierKeys.Shift;
        if (e.metaKey) mods |= ModifierKeys.Windows;
        return mods;
    }

    initKeyboardListeners() {
        if (this._keyboardInitialized) return;
        this._keyboardInitialized = true;

        const c = this.canvas;
        this.isHoveringCanvas = false;
        this.pressedKeys = new Map();

        c.addEventListener('mouseenter', () => { this.isHoveringCanvas = true; });
        c.addEventListener('mouseleave', () => { this.isHoveringCanvas = false; });

        // Release all pressed keys on blur or tab switch to prevent stuck keys on remote machine
        const releaseAllPressedKeys = () => {
            if (this.pressedKeys.size === 0) return;
            for (const [vk] of this.pressedKeys) {
                const packet = serializeKeyboard(InputEventType.KeyUp, vk, 0, ModifierKeys.None, 0);
                this.streamClient.sendPacket(packet);
            }
            this.pressedKeys.clear();
        };

        window.addEventListener('blur', releaseAllPressedKeys);
        document.addEventListener('visibilitychange', () => {
            if (document.hidden) releaseAllPressedKeys();
        });

        const handleKey = (e, isDown) => {
            if (!this.inputEnabled || !this.streamClient.isConnected) return;

            // Don't forward keys to stream if a settings or connections modal is currently open
            if (document.getElementById('modal-backdrop')?.classList.contains('show')) return;

            // If active element is a form input inside the controller UI, allow standard editing
            const tag = document.activeElement ? document.activeElement.tagName.toLowerCase() : '';
            if (tag === 'input' || tag === 'textarea' || document.activeElement?.isContentEditable) {
                return;
            }

            // Prevent default browser shortcuts (Alt+Left, Backspace, Tab, Space, etc.)
            if (e.code !== 'F12' && e.code !== 'F5') {
                e.preventDefault();
            }

            const vk = KEY_CODE_TO_VK[e.code] || e.keyCode || 0;
            const scan = KEY_CODE_TO_SCAN_CODE[e.code] || 0;
            const unicode = (e.key && e.key.length === 1) ? e.key.charCodeAt(0) : 0;
            if (vk === 0 && unicode === 0 && scan === 0) return;

            const now = performance.now();

            if (isDown) {
                // If this is an OS auto-repeat event (key held down)
                if (e.repeat) {
                    const lastTime = this.pressedKeys.get(vk) || 0;
                    // Clamp auto-repeat cadence to ~35ms (~28 chars/sec) to prevent WAN congestion
                    if ((now - lastTime) < 35.0) {
                        return;
                    }
                }
                this.pressedKeys.set(vk, now);
            } else {
                this.pressedKeys.delete(vk);
            }

            const mods = this.getModifiers(e);
            const type = isDown ? InputEventType.KeyDown : InputEventType.KeyUp;

            const packet = serializeKeyboard(type, vk, scan, mods, unicode);
            this.streamClient.sendPacket(packet);
        };

        // Global window listeners capture all keystrokes when canvas is focused, hovered, or in fullscreen
        window.addEventListener('keydown', (e) => handleKey(e, true));
        window.addEventListener('keyup', (e) => handleKey(e, false));
    }

    /**
     * Helper to send synthetic key press (Down + Up)
     */
    sendSyntheticKey(vk, mods = ModifierKeys.None, holdMs = 40) {
        if (!this.inputEnabled || !this.streamClient.isConnected) return;

        const downPacket = serializeKeyboard(InputEventType.KeyDown, vk, 0, mods, 0);
        this.streamClient.sendPacket(downPacket);

        setTimeout(() => {
            const upPacket = serializeKeyboard(InputEventType.KeyUp, vk, 0, mods, 0);
            this.streamClient.sendPacket(upPacket);
        }, holdMs);
    }

    /**
     * Quick Action: Ctrl + Alt + Delete
     */
    sendCtrlAltDel() {
        if (!this.inputEnabled || !this.streamClient.isConnected) return;
        const ctrl = 0x11, alt = 0x12, del = 0x2E;
        const mods = ModifierKeys.Control | ModifierKeys.Alt;

        this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyDown, ctrl, 0, ModifierKeys.Control, 0));
        this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyDown, alt, 0, mods, 0));
        this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyDown, del, 0, mods, 0));

        setTimeout(() => {
            this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyUp, del, 0, mods, 0));
            this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyUp, alt, 0, ModifierKeys.Control, 0));
            this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyUp, ctrl, 0, ModifierKeys.None, 0));
        }, 60);
    }

    /**
     * Quick Action: Lock Workstation (Win + L)
     */
    sendWinL() {
        if (!this.inputEnabled || !this.streamClient.isConnected) return;
        const win = 0x5B, l = 0x4C;
        this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyDown, win, 0, ModifierKeys.Windows, 0));
        this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyDown, l, 0, ModifierKeys.Windows, 0));
        setTimeout(() => {
            this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyUp, l, 0, ModifierKeys.Windows, 0));
            this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyUp, win, 0, ModifierKeys.None, 0));
        }, 50);
    }

    /**
     * Quick Action: Run Dialog (Win + R)
     */
    sendWinR() {
        if (!this.inputEnabled || !this.streamClient.isConnected) return;
        const win = 0x5B, r = 0x52;
        this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyDown, win, 0, ModifierKeys.Windows, 0));
        this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyDown, r, 0, ModifierKeys.Windows, 0));
        setTimeout(() => {
            this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyUp, r, 0, ModifierKeys.Windows, 0));
            this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyUp, win, 0, ModifierKeys.None, 0));
        }, 50);
    }

    /**
     * Quick Action: Task Manager (Ctrl + Shift + Esc)
     */
    sendTaskManager() {
        if (!this.inputEnabled || !this.streamClient.isConnected) return;
        const ctrl = 0x11, shift = 0x10, esc = 0x1B;
        const mods = ModifierKeys.Control | ModifierKeys.Shift;

        this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyDown, ctrl, 0, ModifierKeys.Control, 0));
        this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyDown, shift, 0, mods, 0));
        this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyDown, esc, 0, mods, 0));

        setTimeout(() => {
            this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyUp, esc, 0, mods, 0));
            this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyUp, shift, 0, ModifierKeys.Control, 0));
            this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyUp, ctrl, 0, ModifierKeys.None, 0));
        }, 50);
    }

    /**
     * Quick Action: Alt + Tab
     */
    sendAltTab() {
        if (!this.inputEnabled || !this.streamClient.isConnected) return;
        const alt = 0x12, tab = 0x09;
        this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyDown, alt, 0, ModifierKeys.Alt, 0));
        this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyDown, tab, 0, ModifierKeys.Alt, 0));
        setTimeout(() => {
            this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyUp, tab, 0, ModifierKeys.Alt, 0));
            this.streamClient.sendPacket(serializeKeyboard(InputEventType.KeyUp, alt, 0, ModifierKeys.None, 0));
        }, 50);
    }

    /**
     * Quick Action: Windows Start Key
     */
    sendWinKey() {
        this.sendSyntheticKey(0x5B);
    }

    /**
     * Quick Action: Escape Key
     */
    sendEscKey() {
        this.sendSyntheticKey(0x1B);
    }
}
