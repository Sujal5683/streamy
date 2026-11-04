using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;

namespace EnterpriseRemote.ControlPlane.Services;

public class ViewerContext
{
    public WebSocket Socket { get; }
    public SemaphoreSlim SendLock { get; } = new(1, 1);
    public Channel<byte[]> FrameChannel { get; }
    public volatile byte[]? PendingCursorPacket;
    public readonly SemaphoreSlim AckSignal = new(0, 1);
    public volatile bool IsViewerFrameInFlight;
    public CancellationTokenSource Cts { get; } = new();

    public ViewerContext(WebSocket socket)
    {
        Socket = socket;
        FrameChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        });
    }

    public void CancelAndDispose()
    {
        try { Cts.Cancel(); } catch { }
        try { FrameChannel.Writer.TryComplete(); } catch { }
        try { Cts.Dispose(); } catch { }
        try { SendLock.Dispose(); } catch { }
        try { AckSignal.Dispose(); } catch { }
    }
}

public class DeviceStreamSession
{
    public WebSocket? HostSocket;
    public readonly SemaphoreSlim HostSendLock = new(1, 1);
    public readonly ConcurrentDictionary<WebSocket, ViewerContext> Viewers = new();
    public volatile byte[]? LatestFrameJpeg;
}

public static class StreamRelayHub
{
    private static readonly ConcurrentDictionary<string, DeviceStreamSession> _sessions = new();

    public static void BroadcastHostStatus(string deviceId, bool isConnected)
    {
        if (_sessions.TryGetValue(deviceId, out var session))
        {
            byte[] statusPacket = new byte[] { 0x26, (byte)(isConnected ? 1 : 0) };
            foreach (var kvp in session.Viewers)
            {
                var viewer = kvp.Value;
                if (viewer.Socket.State == WebSocketState.Open)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await viewer.SendLock.WaitAsync(viewer.Cts.Token);
                            try
                            {
                                if (viewer.Socket.State == WebSocketState.Open)
                                {
                                    await viewer.Socket.SendAsync(new ArraySegment<byte>(statusPacket), WebSocketMessageType.Binary, true, viewer.Cts.Token);
                                }
                            }
                            finally
                            {
                                viewer.SendLock.Release();
                            }
                        }
                        catch { }
                    });
                }
            }
        }
    }

    public static void RegisterHost(string deviceId, WebSocket ws)
    {
        var session = _sessions.GetOrAdd(deviceId, _ => new DeviceStreamSession());
        session.HostSocket = ws;
        session.LatestFrameJpeg = null; // Clear any old cached frame so a new session starts clean
        BroadcastHostStatus(deviceId, true);
    }

    public static void UnregisterHost(string deviceId)
    {
        if (_sessions.TryGetValue(deviceId, out var s))
        {
            s.HostSocket = null;
            s.LatestFrameJpeg = null; // Wipe cached frame so viewers never see a frozen past frame
            BroadcastHostStatus(deviceId, false);
        }
    }

    public static void RegisterViewer(string deviceId, WebSocket ws)
    {
        var session = _sessions.GetOrAdd(deviceId, _ => new DeviceStreamSession());
        var context = new ViewerContext(ws);
        session.Viewers[ws] = context;
    }

    public static void UnregisterViewer(string deviceId, WebSocket? ws = null)
    {
        if (_sessions.TryGetValue(deviceId, out var s))
        {
            if (ws != null)
            {
                if (s.Viewers.TryRemove(ws, out var ctx))
                {
                    ctx.CancelAndDispose();
                }
            }
            else
            {
                foreach (var kvp in s.Viewers)
                {
                    if (s.Viewers.TryRemove(kvp.Key, out var ctx))
                    {
                        ctx.CancelAndDispose();
                    }
                }
            }

            // If no active viewers remain, wipe cached frame to prevent stale image retention
            if (s.Viewers.IsEmpty)
            {
                s.LatestFrameJpeg = null;
            }
        }
    }

    public static void ClearSession(string deviceId)
    {
        if (_sessions.TryRemove(deviceId, out var s))
        {
            s.LatestFrameJpeg = null;
            try { s.HostSocket?.Abort(); } catch { }
            s.HostSocket = null;
            foreach (var kvp in s.Viewers)
            {
                if (s.Viewers.TryRemove(kvp.Key, out var ctx))
                {
                    ctx.CancelAndDispose();
                }
            }
        }
    }

    public static void ResetAll()
    {
        foreach (var kvp in _sessions)
        {
            try
            {
                var s = kvp.Value;
                s.LatestFrameJpeg = null;
                try { s.HostSocket?.Abort(); } catch { }
                s.HostSocket = null;
                foreach (var vKvp in s.Viewers)
                {
                    try { vKvp.Value.CancelAndDispose(); } catch { }
                }
                s.Viewers.Clear();
            }
            catch { }
        }
        _sessions.Clear();
    }

    public static bool IsHostConnected(string deviceId)
    {
        return _sessions.TryGetValue(deviceId, out var s) && s.HostSocket?.State == WebSocketState.Open;
    }

    public static async Task RunHostLoopAsync(string deviceId, WebSocket hostWs)
    {
        byte[] buffer = new byte[1024 * 512]; // 512 KB buffer
        using var ms = new MemoryStream();

        try
        {
            while (hostWs.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await hostWs.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        try { await hostWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "Host closed", CancellationToken.None); } catch { }
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (ms.Length > 0 && _sessions.TryGetValue(deviceId, out var session))
                {
                    byte[] rawMsg = ms.ToArray();

                    // Check if this is a valid JPEG frame (starts with SOI marker 0xFF, 0xD8)
                    bool isJpegFrame = rawMsg.Length >= 2 && rawMsg[0] == 0xFF && rawMsg[1] == 0xD8;

                    if (isJpegFrame)
                    {
                        session.LatestFrameJpeg = rawMsg;

                        // Broadcast latest frame to all active viewers via their independent bounded channels
                        foreach (var kvp in session.Viewers)
                        {
                            var viewer = kvp.Value;
                            if (viewer.Socket.State == WebSocketState.Open)
                            {
                                viewer.FrameChannel.Writer.TryWrite(rawMsg);
                            }
                        }
                    }
                    else
                    {
                        // Control / Telemetry packet from host (e.g. 0x53 CursorPosition, 0x41 LatencyPong, 0x52 CursorUpdate, 0x51 DesktopList)
                        bool isCursor = rawMsg.Length == 11 && rawMsg[0] == 0x53;
                        foreach (var kvp in session.Viewers)
                        {
                            var viewer = kvp.Value;
                            if (viewer.Socket.State == WebSocketState.Open)
                            {
                                if (isCursor)
                                {
                                    viewer.PendingCursorPacket = rawMsg;
                                    if (viewer.SendLock.CurrentCount > 0)
                                    {
                                        _ = Task.Run(async () =>
                                        {
                                            try
                                            {
                                                if (await viewer.SendLock.WaitAsync(5, viewer.Cts.Token))
                                                {
                                                    try
                                                    {
                                                        byte[]? toSend = Interlocked.Exchange(ref viewer.PendingCursorPacket, null);
                                                        if (toSend != null && viewer.Socket.State == WebSocketState.Open)
                                                        {
                                                            await viewer.Socket.SendAsync(new ArraySegment<byte>(toSend), WebSocketMessageType.Binary, true, viewer.Cts.Token);
                                                        }
                                                    }
                                                    finally
                                                    {
                                                        viewer.SendLock.Release();
                                                    }
                                                }
                                            }
                                            catch { }
                                        });
                                    }
                                }
                                else
                                {
                                    _ = Task.Run(async () =>
                                    {
                                        try
                                        {
                                            await viewer.SendLock.WaitAsync(viewer.Cts.Token);
                                            try
                                            {
                                                if (viewer.Socket.State == WebSocketState.Open)
                                                {
                                                    await viewer.Socket.SendAsync(new ArraySegment<byte>(rawMsg), WebSocketMessageType.Binary, true, viewer.Cts.Token);
                                                }
                                            }
                                            finally
                                            {
                                                viewer.SendLock.Release();
                                            }
                                        }
                                        catch { }
                                    });
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (WebSocketException) { }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally
        {
            UnregisterHost(deviceId);
        }
    }

    public static async Task RunViewerLoopAsync(string deviceId, WebSocket viewerWs)
    {
        if (!_sessions.TryGetValue(deviceId, out var session))
        {
            return;
        }

        if (!session.Viewers.TryGetValue(viewerWs, out var viewerCtx))
        {
            viewerCtx = new ViewerContext(viewerWs);
            session.Viewers[viewerWs] = viewerCtx;
        }

        var ct = viewerCtx.Cts.Token;

        // 1. Immediately inform the viewer whether the remote host is currently streaming
        bool hostOnline = IsHostConnected(deviceId);
        byte[] statusPacket = new byte[] { 0x26, (byte)(hostOnline ? 1 : 0) };
        await viewerCtx.SendLock.WaitAsync(ct);
        try
        {
            if (viewerWs.State == WebSocketState.Open)
            {
                await viewerWs.SendAsync(new ArraySegment<byte>(statusPacket), WebSocketMessageType.Binary, true, ct);
            }
        }
        catch { }
        finally
        {
            viewerCtx.SendLock.Release();
        }

        // 2. Instantly send cached latest frame if host is online so viewer never sees a blank screen
        byte[]? cached = session.LatestFrameJpeg;
        if (hostOnline && cached != null && cached.Length > 0 && viewerWs.State == WebSocketState.Open)
        {
            await viewerCtx.SendLock.WaitAsync(ct);
            try
            {
                if (viewerWs.State == WebSocketState.Open)
                {
                    await viewerWs.SendAsync(new ArraySegment<byte>(cached), WebSocketMessageType.Binary, true, ct);
                }
            }
            catch { }
            finally
            {
                viewerCtx.SendLock.Release();
            }
        }

        // Run frame sender loop and input receiver loop concurrently
        var sendFramesTask = RunViewerFrameDeliveryLoopAsync(session, viewerCtx, ct);
        var receiveInputTask = RunViewerInputReceiverLoopAsync(session, viewerCtx, ct);

        try
        {
            await Task.WhenAny(sendFramesTask, receiveInputTask);
        }
        finally
        {
            UnregisterViewer(deviceId, viewerWs);
        }
    }

    private static async Task RunViewerFrameDeliveryLoopAsync(DeviceStreamSession session, ViewerContext viewerCtx, CancellationToken ct)
    {
        var channel = viewerCtx.FrameChannel;
        var viewerWs = viewerCtx.Socket;

        try
        {
            while (viewerWs.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                if (!await channel.Reader.WaitToReadAsync(ct))
                {
                    break;
                }

                // Single-in-flight WAN pacing: if previous frame is still buffering/in-flight over WAN, wait for ACK or max 500ms safety timeout
                if (viewerCtx.IsViewerFrameInFlight)
                {
                    try
                    {
                        await viewerCtx.AckSignal.WaitAsync(500, ct);
                    }
                    catch (OperationCanceledException) { break; }
                    viewerCtx.IsViewerFrameInFlight = false;
                }

                // Drain to get the latest frame if multiple arrived, ensuring zero latency
                byte[]? frameToSend = null;
                while (channel.Reader.TryRead(out var item))
                {
                    frameToSend = item;
                }

                if (frameToSend != null && viewerWs.State == WebSocketState.Open)
                {
                    await viewerCtx.SendLock.WaitAsync(ct);
                    try
                    {
                        if (viewerWs.State == WebSocketState.Open)
                        {
                            viewerCtx.IsViewerFrameInFlight = true;
                            await viewerWs.SendAsync(new ArraySegment<byte>(frameToSend), WebSocketMessageType.Binary, true, ct);
                        }

                        // Immediately flush any cursor packet that arrived while the video frame was transmitting
                        byte[]? pendingCursor = Interlocked.Exchange(ref viewerCtx.PendingCursorPacket, null);
                        if (pendingCursor != null && viewerWs.State == WebSocketState.Open)
                        {
                            await viewerWs.SendAsync(new ArraySegment<byte>(pendingCursor), WebSocketMessageType.Binary, true, ct);
                        }
                    }
                    catch (WebSocketException)
                    {
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch { }
                    finally
                    {
                        viewerCtx.SendLock.Release();
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch { }
    }

    private static async Task RunViewerInputReceiverLoopAsync(DeviceStreamSession session, ViewerContext viewerCtx, CancellationToken ct)
    {
        var viewerWs = viewerCtx.Socket;
        byte[] buffer = new byte[4096]; // Input and control packets are small

        try
        {
            while (viewerWs.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await viewerWs.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    try { await viewerWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "Viewer closed", ct); } catch { }
                    return;
                }

                if (result.Count > 0)
                {
                    // 0x42 FrameAck from viewer immediately clears in-flight state and wakes the delivery loop
                    if (buffer[0] == 0x42)
                    {
                        viewerCtx.IsViewerFrameInFlight = false;
                        if (viewerCtx.AckSignal.CurrentCount == 0)
                        {
                            try { viewerCtx.AckSignal.Release(); } catch { }
                        }
                    }

                    if (session.HostSocket?.State == WebSocketState.Open)
                {
                    byte[] inputPacket = new byte[result.Count];
                    Array.Copy(buffer, inputPacket, result.Count);

                    await session.HostSendLock.WaitAsync(ct);
                    try
                    {
                        if (session.HostSocket?.State == WebSocketState.Open)
                        {
                            await session.HostSocket.SendAsync(new ArraySegment<byte>(inputPacket), WebSocketMessageType.Binary, true, ct);
                        }
                    }
                    catch
                    {
                        // Host may have disconnected
                    }
                    finally
                    {
                        session.HostSendLock.Release();
                    }
                }
            }
        }
        }
        catch (WebSocketException) { }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }
}
