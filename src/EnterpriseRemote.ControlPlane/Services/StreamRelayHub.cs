using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace EnterpriseRemote.ControlPlane.Services;

public static class StreamRelayHub
{
    private sealed class DeviceStreamSession
    {
        public WebSocket? HostSocket;
        public readonly SemaphoreSlim HostSendLock = new(1, 1);
        public WebSocket? ViewerSocket;
        public readonly SemaphoreSlim ViewerSendLock = new(1, 1);
        public volatile byte[]? LatestFrameJpeg;
        public Channel<byte[]>? ViewerFrameChannel;
        public CancellationTokenSource? ViewerCts;
    }

    private static readonly ConcurrentDictionary<string, DeviceStreamSession> _sessions = new();

    public static void RegisterHost(string deviceId, WebSocket ws)
    {
        var session = _sessions.GetOrAdd(deviceId, _ => new DeviceStreamSession());
        session.HostSocket = ws;
    }

    public static void UnregisterHost(string deviceId)
    {
        if (_sessions.TryGetValue(deviceId, out var s))
        {
            s.HostSocket = null;
        }
    }

    public static void RegisterViewer(string deviceId, WebSocket ws)
    {
        var session = _sessions.GetOrAdd(deviceId, _ => new DeviceStreamSession());
        session.ViewerSocket = ws;
        session.ViewerCts?.Cancel();
        session.ViewerCts?.Dispose();
        session.ViewerCts = new CancellationTokenSource();
        // Bounded capacity of 1 with DropOldest guarantees ZERO bufferbloat and freshest frame delivery
        session.ViewerFrameChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = true
        });
    }

    public static void UnregisterViewer(string deviceId)
    {
        if (_sessions.TryGetValue(deviceId, out var s))
        {
            s.ViewerSocket = null;
            try
            {
                s.ViewerCts?.Cancel();
                s.ViewerFrameChannel?.Writer.TryComplete();
            }
            catch { }
        }
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
                    // Check if message is a Pong or input reverse-packet from Host
                    if (ms.Length >= 9 && ms.Length <= 32)
                    {
                        byte[] rawMsg = ms.ToArray();
                        // If it's a Pong packet (0x41) or control packet, forward directly to Viewer without passing through frame channel
                        if (rawMsg[0] == 0x41 && session.ViewerSocket?.State == WebSocketState.Open)
                        {
                            await session.ViewerSendLock.WaitAsync();
                            try
                            {
                                if (session.ViewerSocket?.State == WebSocketState.Open)
                                {
                                    await session.ViewerSocket.SendAsync(new ArraySegment<byte>(rawMsg), WebSocketMessageType.Binary, true, CancellationToken.None);
                                }
                            }
                            catch { }
                            finally
                            {
                                session.ViewerSendLock.Release();
                            }
                            continue;
                        }
                    }

                    byte[] frameBytes = ms.ToArray();
                    session.LatestFrameJpeg = frameBytes;

                    // Instantly forward freshest frame to Viewer via non-blocking single-capacity channel (drops any stale backlog)
                    var channel = session.ViewerFrameChannel;
                    if (channel != null && session.ViewerSocket?.State == WebSocketState.Open)
                    {
                        channel.Writer.TryWrite(frameBytes);
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

        var ct = session.ViewerCts?.Token ?? CancellationToken.None;

        // 1. Instantly send cached latest frame so viewer never sees a blank screen
        byte[]? cached = session.LatestFrameJpeg;
        if (cached != null && cached.Length > 0 && viewerWs.State == WebSocketState.Open)
        {
            await session.ViewerSendLock.WaitAsync(ct);
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
                session.ViewerSendLock.Release();
            }
        }

        // Run frame sender loop and input receiver loop concurrently
        var sendFramesTask = RunViewerFrameDeliveryLoopAsync(session, viewerWs, ct);
        var receiveInputTask = RunViewerInputReceiverLoopAsync(session, viewerWs, ct);

        try
        {
            await Task.WhenAny(sendFramesTask, receiveInputTask);
        }
        finally
        {
            UnregisterViewer(deviceId);
        }
    }

    private static async Task RunViewerFrameDeliveryLoopAsync(DeviceStreamSession session, WebSocket viewerWs, CancellationToken ct)
    {
        var channel = session.ViewerFrameChannel;
        if (channel == null) return;

        try
        {
            while (viewerWs.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                if (!await channel.Reader.WaitToReadAsync(ct))
                {
                    break;
                }

                // Drain to get the latest frame if multiple arrived, ensuring zero latency
                byte[]? frameToSend = null;
                while (channel.Reader.TryRead(out var item))
                {
                    frameToSend = item;
                }

                if (frameToSend != null && viewerWs.State == WebSocketState.Open)
                {
                    await session.ViewerSendLock.WaitAsync(ct);
                    try
                    {
                        if (viewerWs.State == WebSocketState.Open)
                        {
                            await viewerWs.SendAsync(new ArraySegment<byte>(frameToSend), WebSocketMessageType.Binary, true, ct);
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
                        session.ViewerSendLock.Release();
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch { }
    }

    private static async Task RunViewerInputReceiverLoopAsync(DeviceStreamSession session, WebSocket viewerWs, CancellationToken ct)
    {
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

                if (result.Count > 0 && session.HostSocket?.State == WebSocketState.Open)
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
        catch (WebSocketException) { }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }
}

