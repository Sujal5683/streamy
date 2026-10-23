using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Protocol;
using EnterpriseRemote.Host.UserAgent.Capture;
using EnterpriseRemote.Host.UserAgent.Input;

namespace EnterpriseRemote.Host.UserAgent.Streaming;

public sealed class DesktopStreamingService : IDisposable
{
    private readonly IScreenCapturer _capturer;
    private readonly InputInjectionEngine _inputEngine;
    private readonly WindowAffinityManager _affinityManager;
    private readonly DirectCompositionWindowCapturer _dcCapturer;
    private readonly DesktopSwitchManager _desktopSwitchManager;
    private readonly GdiDesktopCapturer _fallbackGdiCapturer = new();
    private readonly string _serverUrl;
    private readonly string _deviceId;
    private readonly CancellationTokenSource _cts = new();
    private Task? _streamingTask;

    // Dynamic Stream Configuration (Controllable in real-time from Viewer)
    private volatile int _targetFps = 60;
    private volatile int _jpegQuality = 70;
    private volatile int _maxHeight = 0; // 0 = native resolution
    private volatile bool _lowLatencyMode = true;

    public DesktopStreamingService(string serverUrl, string deviceId, IScreenCapturer capturer, InputInjectionEngine inputEngine, WindowAffinityManager? affinityManager = null, DirectCompositionWindowCapturer? dcCapturer = null, DesktopSwitchManager? desktopSwitchManager = null)
    {
        _serverUrl = serverUrl;
        _deviceId = deviceId;
        _capturer = capturer;
        _inputEngine = inputEngine;
        _affinityManager = affinityManager ?? new WindowAffinityManager();
        _dcCapturer = dcCapturer ?? new DirectCompositionWindowCapturer();
        _desktopSwitchManager = desktopSwitchManager ?? new DesktopSwitchManager();

        _affinityManager.StartMonitoring(400);
        _desktopSwitchManager.StartMonitoring(250);
        _desktopSwitchManager.OnDesktopChanged += newDesktopName =>
        {
            Console.WriteLine($"[+] Active desktop switched to '{newDesktopName}'. Synchronizing capture...");
            _affinityManager.ScanAndNeutralize();
            _fallbackGdiCapturer.RebindToActiveDesktop();
        };
    }

    public void Start()
    {
        _streamingTask = Task.Run(() => ConnectionManagerLoopAsync(_cts.Token));
    }

    private async Task ConnectionManagerLoopAsync(CancellationToken ct)
    {
        string wsBase = _serverUrl.TrimEnd('/')
            .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase);

        var wsUri = new Uri($"{wsBase}/api/v1/stream/host?deviceId={Uri.EscapeDataString(_deviceId)}");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);

                Console.WriteLine($"[*] Connecting real-time screen stream to Control Plane: {wsUri}...");
                await ws.ConnectAsync(wsUri, ct);
                Console.WriteLine($"[+] Live screen streaming WebSocket connected! Streaming active desktop display at {_targetFps} FPS (Quality: {_jpegQuality}%)...");

                var sendLock = new SemaphoreSlim(1, 1);
                var sendTask = RunSendFramesLoopAsync(ws, sendLock, ct);
                var recvTask = RunReceiveInputLoopAsync(ws, sendLock, ct);

                await Task.WhenAny(sendTask, recvTask);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Stream connection error: {ex.Message}. Reconnecting in 3s...");
            }

            try
            {
                await Task.Delay(3000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunSendFramesLoopAsync(ClientWebSocket ws, SemaphoreSlim sendLock, CancellationToken ct)
    {
        byte[]? lastJpeg = null;
        DateTime lastSentTime = DateTime.MinValue;

        // Ensure capture thread is attached to current active input desktop
        DesktopSwitchManager.AttachCallingThreadToInputDesktop(out _);

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int currentTargetFps = _targetFps;
            int frameIntervalMs = Math.Max(10, 1000 / currentTargetFps);

            // Periodic active desktop transition check (handles exam browser desktop switches)
            if (_desktopSwitchManager.CheckAndSyncDesktop(out _))
            {
                _fallbackGdiCapturer.RebindToActiveDesktop();
                try { _capturer.Initialize(); } catch { }
                _affinityManager.ScanAndNeutralize();
            }

            // Flow control: If network is currently busy sending previous frame,
            // drop this intermediate frame to guarantee zero bufferbloat
            bool lockAcquired = false;
            try
            {
                lockAcquired = await sendLock.WaitAsync(0, ct);
                if (!lockAcquired)
                {
                    // Network pipe is busy; skip capture and delay for next interval
                    sw.Stop();
                    int skipDelay = Math.Max(4, frameIntervalMs - (int)sw.ElapsedMilliseconds);
                    await Task.Delay(skipDelay, ct);
                    continue;
                }

                // Capture latest frame with a timeout matched to framerate
                int captureTimeout = Math.Clamp(frameIntervalMs / 2, 4, 15);
                var frame = _capturer.CaptureFrame(timeoutMilliseconds: captureTimeout);

                if (frame.Status == CaptureStatus.AccessLost)
                {
                    DesktopSwitchManager.AttachCallingThreadToInputDesktop(out _);
                    try { _capturer.Initialize(); } catch { }
                    _fallbackGdiCapturer.RebindToActiveDesktop();
                }

                if (frame.Status != CaptureStatus.Success && lastJpeg == null)
                {
                    // Baseline GDI fallback on startup if static
                    try
                    {
                        if (!_fallbackGdiCapturer.IsInitialized) _fallbackGdiCapturer.Initialize();
                        var fallbackFrame = _fallbackGdiCapturer.CaptureFrame(timeoutMilliseconds: 40);
                        if (fallbackFrame.Status == CaptureStatus.Success && fallbackFrame.Buffer != null)
                        {
                            frame = fallbackFrame;
                        }
                    }
                    catch { }
                }

                // If DXGI Output Duplication blacked out content due to DRM/WDA,
                // seamlessly acquire GDI composition frame to unblock the view
                if (frame.ProtectedContentMaskedOut)
                {
                    try
                    {
                        if (!_fallbackGdiCapturer.IsInitialized) _fallbackGdiCapturer.Initialize();
                        var unmaskedFrame = _fallbackGdiCapturer.CaptureFrame(timeoutMilliseconds: 40);
                        if (unmaskedFrame.Status == CaptureStatus.Success && unmaskedFrame.Buffer != null)
                        {
                            frame = unmaskedFrame;
                        }
                    }
                    catch { }
                }

                if (frame.Status == CaptureStatus.Success && frame.Buffer != null && frame.Width > 0 && frame.Height > 0)
                {
                    var protectedWindows = _affinityManager.ActiveProtectedWindows;
                    if (protectedWindows.Count > 0)
                    {
                        for (int i = 0; i < protectedWindows.Count; i++)
                        {
                            _dcCapturer.CompositeWindowOntoFrame(frame.Buffer, frame.Width, frame.Height, protectedWindows[i]);
                        }
                    }

                    byte[] jpeg = EncodeFrameToJpeg(frame, _jpegQuality, _capturer.Display.Left, _capturer.Display.Top, _maxHeight);
                    if (jpeg.Length > 0 && ws.State == WebSocketState.Open)
                    {
                        lastJpeg = jpeg;
                        lastSentTime = DateTime.UtcNow;
                        await ws.SendAsync(new ArraySegment<byte>(jpeg), WebSocketMessageType.Binary, true, ct);
                    }
                }
                else if (lastJpeg != null && (DateTime.UtcNow - lastSentTime).TotalMilliseconds >= 400)
                {
                    // Static screen keepalive heartbeat
                    lastSentTime = DateTime.UtcNow;
                    await ws.SendAsync(new ArraySegment<byte>(lastJpeg), WebSocketMessageType.Binary, true, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException)
            {
                break;
            }
            catch
            {
                // Non-fatal capture or send glitch, continue to next frame
            }
            finally
            {
                if (lockAcquired)
                {
                    sendLock.Release();
                }
            }

            sw.Stop();
            int elapsed = (int)sw.ElapsedMilliseconds;
            int delay = Math.Max(2, frameIntervalMs - elapsed);
            await Task.Delay(delay, ct);
        }
    }

    private async Task RunReceiveInputLoopAsync(ClientWebSocket ws, SemaphoreSlim sendLock, CancellationToken ct)
    {
        byte[] buffer = new byte[4096];

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Host closed", ct); } catch { }
                    break;
                }

                if (result.Count >= 9)
                {
                    ProcessIncomingInput(ws, buffer, result.Count, sendLock, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch { }
    }

    private void ProcessIncomingInput(ClientWebSocket ws, byte[] buffer, int count, SemaphoreSlim sendLock, CancellationToken ct)
    {
        var span = new ReadOnlySpan<byte>(buffer, 0, count);
        if (DataChannelProtocol.TryDeserializeMouse(span, out var mousePacket))
        {
            _inputEngine.ProcessMousePacket(in mousePacket);
        }
        else if (DataChannelProtocol.TryDeserializeKeyboard(span, out var keyPacket))
        {
            _inputEngine.ProcessKeyboardPacket(in keyPacket);
        }
        else if (DataChannelProtocol.TryDeserializeStreamConfig(span, out var configPacket))
        {
            _targetFps = Math.Clamp((int)configPacket.TargetFps, 10, 60);
            _jpegQuality = Math.Clamp((int)configPacket.Quality, 30, 95);
            _maxHeight = configPacket.MaxHeight;
            _lowLatencyMode = configPacket.LowLatencyMode != 0;
            Console.WriteLine($"[+] Dynamic Stream Config applied: {_targetFps} FPS, Quality {_jpegQuality}%, MaxHeight {_maxHeight}, LowLatency={_lowLatencyMode}");
        }
        else if (DataChannelProtocol.TryDeserializeLatencyPingPong(span, out bool isPing, out ulong ts))
        {
            if (isPing)
            {
                byte[] pong = DataChannelProtocol.SerializeLatencyPong(ts);
                _ = Task.Run(async () =>
                {
                    await sendLock.WaitAsync(ct);
                    try
                    {
                        if (ws.State == WebSocketState.Open)
                        {
                            await ws.SendAsync(new ArraySegment<byte>(pong), WebSocketMessageType.Binary, true, ct);
                        }
                    }
                    catch { }
                    finally
                    {
                        sendLock.Release();
                    }
                }, ct);
            }
        }
    }

    private static byte[] EncodeFrameToJpeg(CapturedFrame frame, long quality, int displayLeft = 0, int displayTop = 0, int maxHeight = 0)
    {
        if (frame.Buffer == null || frame.Width <= 0 || frame.Height <= 0) return Array.Empty<byte>();

        try
        {
            using var bmp = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppRgb);
            var bmpData = bmp.LockBits(
                new Rectangle(0, 0, frame.Width, frame.Height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppRgb);

            try
            {
                int bytesToCopy = Math.Min(frame.Buffer.Length, bmpData.Stride * frame.Height);
                Marshal.Copy(frame.Buffer, 0, bmpData.Scan0, bytesToCopy);
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }

            // Render live remote cursor onto frame buffer for real-time visibility with exact hotspot accuracy
            try
            {
                var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
                int curX = -1;
                int curY = -1;
                IntPtr hCursor = IntPtr.Zero;

                if (GetCursorInfo(out ci) && (ci.flags & CURSOR_SHOWING) != 0)
                {
                    curX = ci.ptScreenPos.x - displayLeft;
                    curY = ci.ptScreenPos.y - displayTop;
                    hCursor = ci.hCursor;
                }
                else if (frame.Pointer != null && frame.Pointer.Visible)
                {
                    curX = frame.Pointer.X - displayLeft;
                    curY = frame.Pointer.Y - displayTop;
                }

                if (curX >= 0 && curY >= 0 && curX < bmp.Width && curY < bmp.Height)
                {
                    using var g = Graphics.FromImage(bmp);
                    bool drawn = false;
                    if (hCursor != IntPtr.Zero)
                    {
                        int drawX = curX;
                        int drawY = curY;
                        if (GetIconInfo(hCursor, out ICONINFO ii))
                        {
                            drawX -= ii.xHotspot;
                            drawY -= ii.yHotspot;
                            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
                            if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
                        }

                        IntPtr hdc = g.GetHdc();
                        try
                        {
                            drawn = DrawIconEx(hdc, drawX, drawY, hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
                        }
                        finally
                        {
                            g.ReleaseHdc(hdc);
                        }
                    }

                    if (!drawn)
                    {
                        DrawDefaultCursor(g, curX, curY);
                    }
                }
            }
            catch
            {
                // Non-fatal if cursor drawing fails on a secure desktop transition
            }

            Bitmap finalBmp = bmp;
            Bitmap? scaledBmp = null;
            if (maxHeight > 0 && frame.Height > maxHeight)
            {
                int targetHeight = maxHeight;
                int targetWidth = (frame.Width * targetHeight) / frame.Height;
                scaledBmp = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppRgb);
                using (var gScaled = Graphics.FromImage(scaledBmp))
                {
                    gScaled.InterpolationMode = InterpolationMode.Bilinear;
                    gScaled.DrawImage(bmp, 0, 0, targetWidth, targetHeight);
                }
                finalBmp = scaledBmp;
            }

            try
            {
                using var ms = new MemoryStream();
                var encoder = GetEncoder(ImageFormat.Jpeg);
                if (encoder != null)
                {
                    using var encParams = new EncoderParameters(1);
                    encParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                    finalBmp.Save(ms, encoder, encParams);
                }
                else
                {
                    finalBmp.Save(ms, ImageFormat.Jpeg);
                }
                return ms.ToArray();
            }
            finally
            {
                scaledBmp?.Dispose();
            }
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    private static void DrawDefaultCursor(Graphics g, int x, int y)
    {
        Point[] points = new[]
        {
            new Point(x, y),
            new Point(x, y + 17),
            new Point(x + 4, y + 13),
            new Point(x + 8, y + 21),
            new Point(x + 11, y + 20),
            new Point(x + 7, y + 12),
            new Point(x + 12, y + 12)
        };
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillPolygon(Brushes.White, points);
        using var pen = new Pen(Color.Black, 1.5f);
        g.DrawPolygon(pen, points);
    }

    #region Win32 Cursor Declarations
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    private const int CURSOR_SHOWING = 0x00000001;
    private const uint DI_NORMAL = 0x0003;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorInfo(out CURSORINFO pci);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyHeight, uint istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);
    #endregion

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        foreach (var codec in ImageCodecInfo.GetImageEncoders())
        {
            if (codec.FormatID == format.Guid) return codec;
        }
        return null;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _desktopSwitchManager.Dispose();
        _affinityManager.Dispose();
        _dcCapturer.Dispose();
        _fallbackGdiCapturer.Dispose();
        _cts.Dispose();
    }
}
