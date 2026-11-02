/**
 * Enterprise Remote Access - Ultra-Low Latency Video Streaming Client
 * Uses HTML5 Canvas + createImageBitmap for hardware-accelerated zero-copy rendering
 */

import {
    InputEventType,
    serializeLatencyPing,
    serializeStreamConfig,
    serializeSwitchDesktop,
    tryDeserializeLatencyPong,
    tryDeserializeCursorUpdate,
    tryDeserializeDesktopList
} from './protocol.js';

export class RemoteStreamClient {
    constructor(canvas, options = {}) {
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d', { alpha: false, desynchronized: true });
        this.options = options;

        this.ws = null;
        this.isConnected = false;
        this.isPaused = false;
        this.deviceId = null;
        this.sessionId = null;

        // Viewport & frame metrics
        this.scaleMode = 'fit'; // 'fit' | 'stretch' | 'original'
        this.latestBitmap = null;
        this.remoteWidth = 1920;
        this.remoteHeight = 1080;
        this.renderRect = { x: 0, y: 0, width: 0, height: 0 };
        this.incomingSeq = 0;
        this.latestRenderedSeq = 0;

        // Telemetry
        this.frameCount = 0;
        this.fps = 0;
        this.rttMs = 0;
        this.bytesReceived = 0;
        this.bitrateKbps = 0;
        this.lastFpsUpdate = performance.now();
        this.lastBytesUpdate = performance.now();
        this.bytesIntervalCounter = 0;

        // Timers & Loops
        this.pingTimer = null;
        this.rafId = null;
        this.telemetryTimer = null;

        // Callbacks
        this.onStatusChange = options.onStatusChange || (() => {});
        this.onTelemetry = options.onTelemetry || (() => {});
        this.onCursorChange = options.onCursorChange || (() => {});
        this.onDesktopList = options.onDesktopList || (() => {});
        this.onError = options.onError || (() => {});
        this.onReauthorizeNeeded = options.onReauthorizeNeeded || null;

        this.userDisconnected = false;
        this.reconnectTimer = null;
        this.reconnectAttempts = 0;
        this.serverUrl = null;

        this.startRenderLoop();
    }

    /**
     * Connects to the Control Plane WebSocket streaming relay
     */
    connect(serverUrl, deviceId, sessionId) {
        if (this.reconnectTimer) {
            clearTimeout(this.reconnectTimer);
            this.reconnectTimer = null;
        }

        this.userDisconnected = false;
        this.serverUrl = serverUrl;
        this.deviceId = deviceId;
        this.sessionId = sessionId;

        if (this.ws) {
            try { this.ws.close(); } catch { }
            this.ws = null;
        }
        this.cleanup();

        let wsBase = serverUrl.trim().replace(/\/$/, '')
            .replace(/^http:\/\//i, 'ws://')
            .replace(/^https:\/\//i, 'wss://');

        const wsUrl = `${wsBase}/api/v1/stream/viewer?deviceId=${encodeURIComponent(deviceId)}&sessionId=${encodeURIComponent(sessionId || '')}`;

        this.onStatusChange('connecting', `Connecting stream socket to ${deviceId}...`);

        try {
            this.ws = new WebSocket(wsUrl);
            this.ws.binaryType = 'arraybuffer';

            this.ws.onopen = () => {
                this.isConnected = true;
                this.reconnectAttempts = 0;
                this.onStatusChange('connected', `Live stream connected to ${deviceId}`);
                this.startPingLoop();
                this.startTelemetryLoop();
            };

            this.ws.onmessage = async (event) => {
                if (!(event.data instanceof ArrayBuffer)) return;
                this.handleIncomingData(event.data);
            };

            this.ws.onerror = (err) => {
                // Non-fatal, onclose handles reconnect
            };

            this.ws.onclose = (ev) => {
                this.cleanup();
                if (!this.userDisconnected) {
                    this.reconnectAttempts++;
                    const backoffMs = Math.min(4000, 1000 * Math.min(this.reconnectAttempts, 4));
                    this.onStatusChange('reconnecting', `Connection dropped. Auto-reconnecting in ${Math.round(backoffMs/1000)}s (attempt ${this.reconnectAttempts})...`);
                    
                    if (this.reconnectAttempts >= 2 && typeof this.onReauthorizeNeeded === 'function') {
                        this.reconnectTimer = setTimeout(() => {
                            if (!this.userDisconnected && typeof this.onReauthorizeNeeded === 'function') {
                                this.onReauthorizeNeeded();
                            }
                        }, backoffMs);
                        return;
                    }

                    this.reconnectTimer = setTimeout(() => {
                        if (!this.userDisconnected) {
                            this.connect(this.serverUrl, this.deviceId, this.sessionId);
                        }
                    }, backoffMs);
                } else {
                    this.onStatusChange('disconnected', 'Disconnected from remote stream.');
                }
            };
        } catch (ex) {
            this.cleanup();
            if (!this.userDisconnected) {
                this.reconnectAttempts++;
                const backoffMs = Math.min(4000, 1000 * Math.min(this.reconnectAttempts, 4));
                if (this.reconnectAttempts >= 2 && typeof this.onReauthorizeNeeded === 'function') {
                    this.reconnectTimer = setTimeout(() => {
                        if (!this.userDisconnected && typeof this.onReauthorizeNeeded === 'function') {
                            this.onReauthorizeNeeded();
                        }
                    }, backoffMs);
                    return;
                }
                this.reconnectTimer = setTimeout(() => {
                    if (!this.userDisconnected) {
                        this.connect(this.serverUrl, this.deviceId, this.sessionId);
                    }
                }, backoffMs);
            } else {
                this.onError(`Failed to open stream socket: ${ex.message}`);
            }
        }
    }

    /**
     * Processes incoming binary payloads
     */
    async handleIncomingData(buffer) {
        const len = buffer.byteLength;
        if (len === 0) return;

        this.bytesReceived += len;
        this.bytesIntervalCounter += len;

        const view = new DataView(buffer);
        const firstByte = view.getUint8(0);

        // 1. Cursor update packet (0x25, 2 bytes)
        if (len === 2 && firstByte === InputEventType.CursorUpdate) {
            const cursorInfo = tryDeserializeCursorUpdate(view);
            if (cursorInfo) {
                this.onCursorChange(cursorInfo.cssCursor);
            }
            return;
        }

        // 2. Latency Pong packet (0x41, 9 bytes)
        if (len === 9 && firstByte === InputEventType.LatencyPong) {
            const pong = tryDeserializeLatencyPong(view);
            if (pong) {
                const now = Date.now();
                if (now >= pong.timestampMillis) {
                    this.rttMs = Math.max(1, now - pong.timestampMillis);
                }
            }
            return;
        }

        // 2B. Desktop / Display List packet (0x22)
        if (firstByte === InputEventType.DesktopListUpdate) {
            const list = tryDeserializeDesktopList(view);
            if (list) {
                this.onDesktopList(list);
            }
            return;
        }

        // 3. Desktop JPEG frame: starts with SOI marker 0xFF, 0xD8
        if (len > 4 && view.getUint8(0) === 0xFF && view.getUint8(1) === 0xD8) {
            try {
                this.incomingSeq = (this.incomingSeq || 0) + 1;
                const seq = this.incomingSeq;

                const blob = new Blob([buffer], { type: 'image/jpeg' });
                // createImageBitmap is hardware-accelerated and runs off the main JS thread
                const bitmap = await createImageBitmap(blob, {
                    imageOrientation: 'none',
                    premultiplyAlpha: 'none'
                });

                if (seq < this.latestRenderedSeq) {
                    bitmap.close();
                    return;
                }
                this.latestRenderedSeq = seq;

                this.remoteWidth = bitmap.width;
                this.remoteHeight = bitmap.height;

                const oldBitmap = this.latestBitmap;
                this.latestBitmap = bitmap;
                if (oldBitmap && oldBitmap !== bitmap) {
                    oldBitmap.close();
                }

                this.frameCount++;
            } catch (err) {
                // Ignore transient frame decode drops
            }
        }
    }

    /**
     * Send arbitrary binary packet over WebSocket (Mouse, Keyboard, Config, Ping)
     */
    sendPacket(arrayBuffer) {
        if (!this.isConnected || !this.ws || this.ws.readyState !== WebSocket.OPEN) {
            return false;
        }
        try {
            this.ws.send(arrayBuffer);
            return true;
        } catch (ex) {
            return false;
        }
    }

    /**
     * Send dynamic stream configuration (FPS, Quality, MaxHeight)
     */
    applyStreamConfig(targetFps, quality, maxHeight = 0, lowLatency = 1) {
        const packet = serializeStreamConfig(targetFps, quality, maxHeight, lowLatency);
        return this.sendPacket(packet);
    }

    /**
     * Starts continuous rendering on requestAnimationFrame
     */
    startRenderLoop() {
        const render = () => {
            if (this.latestBitmap && !this.isPaused) {
                this.drawFrame(this.latestBitmap);
            } else if (this.isConnected) {
                this.drawAwaitingStream();
            }
            this.rafId = requestAnimationFrame(render);
        };
        this.rafId = requestAnimationFrame(render);
    }

    /**
     * Draws an active awaiting state on canvas when connected but waiting for first frame
     */
    drawAwaitingStream() {
        const canvas = this.canvas;
        const ctx = this.ctx;
        const cw = canvas.width;
        const ch = canvas.height;
        if (cw <= 0 || ch <= 0) return;

        ctx.fillStyle = '#080c14';
        ctx.fillRect(0, 0, cw, ch);

        const cx = Math.floor(cw / 2);
        const cy = Math.floor(ch / 2) - 40;
        const now = performance.now();

        // Pulsing radar circles
        const pulse = (now % 2000) / 2000;
        ctx.save();
        ctx.lineWidth = 2;
        ctx.strokeStyle = `rgba(6, 182, 212, ${1 - pulse})`;
        ctx.beginPath();
        ctx.arc(cx, cy, 28 + pulse * 42, 0, Math.PI * 2);
        ctx.stroke();

        ctx.strokeStyle = '#06b6d4';
        ctx.lineWidth = 3;
        ctx.beginPath();
        ctx.arc(cx, cy, 26, 0, Math.PI * 2);
        ctx.stroke();

        // Rotating radar sweep
        const angle = (now % 1200) / 1200 * Math.PI * 2;
        ctx.strokeStyle = '#22d3ee';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(cx, cy);
        ctx.lineTo(cx + Math.cos(angle) * 26, cy + Math.sin(angle) * 26);
        ctx.stroke();
        ctx.restore();

        // Informative guidance text
        ctx.textAlign = 'center';
        ctx.fillStyle = '#f8fafc';
        ctx.font = 'bold 22px Segoe UI, system-ui, sans-serif';
        ctx.fillText('Awaiting Remote Desktop Video Feed', cx, cy + 66);

        ctx.fillStyle = '#06b6d4';
        ctx.font = '14px Segoe UI, system-ui, sans-serif';
        const targetHost = this.deviceId ? `Target: ${this.deviceId}` : 'Connecting to remote host...';
        ctx.fillText(targetHost, cx, cy + 92);

        ctx.fillStyle = '#94a3b8';
        ctx.font = '13px Segoe UI, system-ui, sans-serif';
        ctx.fillText('• Gateway session ticket verified; tunnel socket active.', cx, cy + 124);
        ctx.fillText('• Waiting for remote desktop frames from streamer agent.', cx, cy + 146);
        ctx.fillText('• Ensure START_REMOTE_STREAMING.bat is active on the Remote PC.', cx, cy + 168);
    }

    /**
     * Draws the latest bitmap according to the current scale mode
     */
    drawFrame(bitmap) {
        const canvas = this.canvas;
        const ctx = this.ctx;
        const cw = canvas.width;
        const ch = canvas.height;
        const bw = bitmap.width;
        const bh = bitmap.height;

        if (cw <= 0 || ch <= 0 || bw <= 0 || bh <= 0) return;

        let rx = 0, ry = 0, rw = cw, rh = ch;

        if (this.scaleMode === 'stretch') {
            rx = 0;
            ry = 0;
            rw = cw;
            rh = ch;
        } else if (this.scaleMode === 'original') {
            rw = bw;
            rh = bh;
            rx = Math.floor((cw - rw) / 2);
            ry = Math.floor((ch - rh) / 2);
        } else {
            // 'fit' (Preserve aspect ratio with letterboxes)
            const scale = Math.min(cw / bw, ch / bh);
            rw = Math.round(bw * scale);
            rh = Math.round(bh * scale);
            rx = Math.floor((cw - rw) / 2);
            ry = Math.floor((ch - rh) / 2);
        }

        this.renderRect = { x: rx, y: ry, width: rw, height: rh };

        // Clear letterbox borders with deep black/navy if not stretching
        if (this.scaleMode !== 'stretch') {
            ctx.fillStyle = '#05070e';
            if (rx > 0) {
                ctx.fillRect(0, 0, rx, ch);
                ctx.fillRect(rx + rw, 0, cw - (rx + rw), ch);
            }
            if (ry > 0) {
                ctx.fillRect(0, 0, cw, ry);
                ctx.fillRect(0, ry + rh, cw, ch - (ry + rh));
            }
        }

        ctx.drawImage(bitmap, rx, ry, rw, rh);
    }

    setScaleMode(mode) {
        this.scaleMode = mode;
        if (this.latestBitmap) {
            this.drawFrame(this.latestBitmap);
        }
    }

    startPingLoop() {
        this.stopPingLoop();
        this.pingTimer = setInterval(() => {
            if (this.isConnected) {
                this.sendPacket(serializeLatencyPing(Date.now()));
            }
        }, 1000);
    }

    stopPingLoop() {
        if (this.pingTimer) {
            clearInterval(this.pingTimer);
            this.pingTimer = null;
        }
    }

    startTelemetryLoop() {
        this.telemetryTimer = setInterval(() => {
            const now = performance.now();
            const elapsed = (now - this.lastFpsUpdate) / 1000;
            if (elapsed >= 0.5) {
                this.fps = Math.round(this.frameCount / elapsed);
                this.frameCount = 0;
                this.lastFpsUpdate = now;

                const bytesElapsed = (now - this.lastBytesUpdate) / 1000;
                this.bitrateKbps = Math.round((this.bytesIntervalCounter * 8) / (bytesElapsed * 1024));
                this.bytesIntervalCounter = 0;
                this.lastBytesUpdate = now;

                this.onTelemetry({
                    fps: this.fps,
                    rttMs: this.rttMs,
                    bitrateKbps: this.bitrateKbps,
                    remoteWidth: this.remoteWidth,
                    remoteHeight: this.remoteHeight
                });
            }
        }, 1000);
    }

    stopTelemetryLoop() {
        if (this.telemetryTimer) {
            clearInterval(this.telemetryTimer);
            this.telemetryTimer = null;
        }
    }

    sendSwitchDesktop(displayIndex = 0, autoFollow = true, desktopName = 'Auto') {
        if (!this.ws || this.ws.readyState !== WebSocket.OPEN) return;
        try {
            const pkt = serializeSwitchDesktop(displayIndex, autoFollow, desktopName);
            this.ws.send(pkt);
        } catch { }
    }

    disconnect() {
        this.userDisconnected = true;
        if (this.reconnectTimer) {
            clearTimeout(this.reconnectTimer);
            this.reconnectTimer = null;
        }
        this.cleanup();
        if (this.ws) {
            try {
                this.ws.close(1000, 'User disconnected');
            } catch { }
            this.ws = null;
        }
        this.onStatusChange('disconnected', 'Disconnected from remote stream.');
    }

    cleanup() {
        this.isConnected = false;
        this.stopPingLoop();
        this.stopTelemetryLoop();
        if (this.latestBitmap) {
            this.latestBitmap.close();
            this.latestBitmap = null;
        }
    }
}
