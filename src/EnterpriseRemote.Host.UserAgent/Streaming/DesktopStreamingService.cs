using System;
using System.Diagnostics;
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
using EnterpriseRemote.Host.UserAgent.Video;

namespace EnterpriseRemote.Host.UserAgent.Streaming;

public sealed class DesktopStreamingService : IDisposable
{
    private static readonly ImageCodecInfo? _jpegEncoder = GetEncoder(ImageFormat.Jpeg);

    private readonly IScreenCapturer _capturer;
    private readonly InputInjectionEngine _inputEngine;
    private readonly WindowAffinityManager _affinityManager;
    private readonly DirectCompositionWindowCapturer _dcCapturer;
    private readonly DesktopSwitchManager _desktopSwitchManager;
    private readonly GdiDesktopCapturer _fallbackGdiCapturer = new();
    private readonly AdaptiveQualityEngine _abrEngine = new();
    private readonly string _serverUrl;
    private readonly string _deviceId;
    private readonly CancellationTokenSource _cts = new();

    private Task? _streamingTask;
    private Thread? _cleanCaptureThread;

    // Dynamic Stream Configuration (Controllable in real-time from Viewer)
    // Supports 10 to 200 FPS; default 120 FPS for ultra-low latency WAN/LAN
    private volatile int _targetFps = 120;
    private volatile int _jpegQuality = 75;
    private volatile int _maxHeight = 720;
    private volatile bool _lowLatencyMode = true;
    private volatile RemoteCursorShape _lastReportedCursorShape = RemoteCursorShape.Default;

    // Dynamic Desktop & Display Target State (Controllable from Remote Viewer)
    private volatile string _targetDesktopMode = "Auto";
    private volatile byte _targetDisplayIndex = 0;
    private volatile bool _autoFollowActiveDesktop = true;
    private DateTime _lastDesktopListBroadcast = DateTime.MinValue;

    // Zero-allocation encoding buffers
    private byte[]? _downscaleBuffer;
    private Bitmap? _cachedBmp;
    private readonly MemoryStream _reusableJpegStream = new(1024 * 128);

    // Thread-safe frame handoff from dedicated capture thread to async sender
    private readonly object _frameLock = new();
    private CapturedFrame? _latestFrame;
    private long _frameSequence = 0;
    private int _lastSentCursorX = -1;
    private int _lastSentCursorY = -1;
    private RemoteCursorShape _lastSentCursorShape = RemoteCursorShape.Default;
    private bool _lastSentCursorVisible = true;

    // Pipelined Sliding Window (MaxInFlight = 2) & Rate Pacer
    private readonly SemaphoreSlim _immediateFrameSignal = new(0, 1);
    private readonly SemaphoreSlim _ackSignal = new(0, 1);
    private long _sentSequence = 0;
    private long _lastAckedSequence = 0;
    private const int MaxInFlight = 2;
    private long _lastFrameSentTicks = 0;
    private long _lastSendDurationMs = 0;
    private double _measuredRttMs = 50.0;
    private volatile bool _requestCaptureThreadRestart = false;

    public Action<string>? RequestElevatedDesktopSwitch { get; set; }
    public Action? RequestDesktopAccess { get; set; }

    public DesktopStreamingService(
        string serverUrl,
        string deviceId,
        IScreenCapturer capturer,
        InputInjectionEngine inputEngine,
        WindowAffinityManager? affinityManager = null,
        DirectCompositionWindowCapturer? dcCapturer = null,
        DesktopSwitchManager? desktopSwitchManager = null)
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
            try { _capturer.Initialize(); } catch { }
            Interlocked.Exchange(ref _lastAckedSequence, Interlocked.Read(ref _sentSequence));
            if (_ackSignal.CurrentCount == 0)
            {
                try { _ackSignal.Release(); } catch { }
            }
            _requestCaptureThreadRestart = true;
            TriggerImmediateFrame();
        };

        // Wire immediate frame capture trigger on any typed key or mouse click
        _inputEngine.OnInputInjected += TriggerImmediateFrame;
    }

    private void TriggerImmediateFrame()
    {
        if (_immediateFrameSignal.CurrentCount == 0)
        {
            try { _immediateFrameSignal.Release(); } catch { }
        }
    }

    public void Start()
    {
        // 1. Dedicated high-priority capture supervisor managing clean threads attached to the active input desktop
        _cleanCaptureThread = new Thread(CleanCaptureSupervisorWorker)
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
            Name = "EnterpriseRemote.CleanCaptureSupervisor"
        };
        _cleanCaptureThread.Start();

        // 2. Async WebSocket streaming and input loop
        _streamingTask = Task.Run(() => ConnectionManagerLoopAsync(_cts.Token));
    }

    private void CleanCaptureSupervisorWorker()
    {
        while (!_cts.IsCancellationRequested)
        {
            _requestCaptureThreadRestart = false;

            // Spawns a dedicated pristine thread whose first action is SetThreadDesktop on the active input desktop.
            // Guaranteed to succeed with zero ERROR_BUSY (170) because the thread owns no windows, hooks, or GDI handles.
            var workerThread = DesktopSwitchManager.CreateCleanAttachedThread(desktopName =>
            {
                RunCleanCaptureWorker(desktopName);
            }, "EnterpriseRemote.CleanCaptureWorker");

            workerThread.Start();

            // Supervise the worker thread. If desktop switch or persistent black screen is detected,
            // worker sets _requestCaptureThreadRestart or exits.
            while (workerThread.IsAlive && !_cts.IsCancellationRequested && !_requestCaptureThreadRestart)
            {
                Thread.Sleep(30);
            }

            if (_requestCaptureThreadRestart && workerThread.IsAlive)
            {
                workerThread.Join(150);
            }
        }
    }

    private void RunCleanCaptureWorker(string initialDesktop)
    {
        int consecutiveBlackFrames = 0;
        bool isCustomDesktop = !string.Equals(initialDesktop, "Default", StringComparison.OrdinalIgnoreCase);

        if (isCustomDesktop)
        {
            _fallbackGdiCapturer.RebindToActiveDesktop();
        }
        else
        {
            try { _capturer.Initialize(); } catch { }
        }

        while (!_cts.IsCancellationRequested && !_requestCaptureThreadRestart)
        {
            try
            {
                // 1. Periodic desktop check & sync
                if (_desktopSwitchManager.CheckAndSyncDesktop(out string curDesktop))
                {
                    _requestCaptureThreadRestart = true;
                    break;
                }

                string currentDesktop = _desktopSwitchManager.CurrentDesktopName ?? initialDesktop;
                isCustomDesktop = !string.Equals(currentDesktop, "Default", StringComparison.OrdinalIgnoreCase);

                CapturedFrame frame;
                if (isCustomDesktop)
                {
                    if (!_fallbackGdiCapturer.IsInitialized) _fallbackGdiCapturer.Initialize();
                    frame = _fallbackGdiCapturer.CaptureFrame(timeoutMilliseconds: 15);
                }
                else
                {
                    int captureTimeout = Math.Clamp(1000 / (_targetFps * 2), 2, 10);
                    frame = _capturer.CaptureFrame(timeoutMilliseconds: captureTimeout);
                    if (frame.Status == CaptureStatus.Timeout)
                    {
                        // Desktop is static; no dirty rects in DXGI. Do not fallback to GDI or force a new identical frame.
                        Thread.Sleep(Math.Max(1, 1000 / _targetFps));
                        continue;
                    }
                    else if (frame.Status != CaptureStatus.Success)
                    {
                        if (!_fallbackGdiCapturer.IsInitialized) _fallbackGdiCapturer.Initialize();
                        var fallbackFrame = _fallbackGdiCapturer.CaptureFrame(timeoutMilliseconds: 15);
                        if (fallbackFrame.Status == CaptureStatus.Success && fallbackFrame.Buffer != null)
                        {
                            frame = fallbackFrame;
                        }
                    }
                }

                // 2. Validate black screen / zero-byte frame
                bool isBlack = (frame.Status == CaptureStatus.BlackScreenDetected) ||
                               (frame.Status == CaptureStatus.Success && frame.Buffer != null && !GdiDesktopCapturer.HasNonZeroPixels(frame.Buffer));

                if (isBlack)
                {
                    consecutiveBlackFrames++;
                    if (consecutiveBlackFrames >= 2)
                    {
                        _affinityManager.ScanAndNeutralize();
                        if (_desktopSwitchManager.CheckAndSyncDesktop(out _))
                        {
                            _requestCaptureThreadRestart = true;
                            break;
                        }

                        // Try direct window composition for any detected protected windows
                        var protectedWindows = _affinityManager.ActiveProtectedWindows;
                        if (protectedWindows.Count > 0 && frame.Buffer != null && frame.Width > 0 && frame.Height > 0)
                        {
                            bool anyComposited = false;
                            for (int i = 0; i < protectedWindows.Count; i++)
                            {
                                try
                                {
                                    if (_dcCapturer.CompositeWindowOntoFrame(frame.Buffer, frame.Width, frame.Height, protectedWindows[i]))
                                    {
                                        anyComposited = true;
                                    }
                                }
                                catch { }
                            }

                            if (anyComposited && GdiDesktopCapturer.HasNonZeroPixels(frame.Buffer))
                            {
                                consecutiveBlackFrames = 0;
                                frame.Status = CaptureStatus.Success;
                            }
                        }

                        if (consecutiveBlackFrames >= 4)
                        {
                            // Persistent black frames: rebind and restart clean thread attached to current input desktop
                            _fallbackGdiCapturer.RebindToActiveDesktop();
                            _requestCaptureThreadRestart = true;
                            break;
                        }
                    }
                }
                else if (frame.Status == CaptureStatus.Success && frame.Buffer != null)
                {
                    consecutiveBlackFrames = 0;
                }

                // 3. Resilient re-attachment if capture failed
                if (frame.Status != CaptureStatus.Success || frame.Buffer == null)
                {
                    bool attached = DesktopSwitchManager.AttachCallingThreadToInputDesktop(out string newDesktop);
                    if (!attached && newDesktop == "ProtectedSecureDesktop")
                    {
                        RequestDesktopAccess?.Invoke();
                    }
                    _fallbackGdiCapturer.RebindToActiveDesktop();
                    var retryFrame = _fallbackGdiCapturer.CaptureFrame(timeoutMilliseconds: 15);
                    if (retryFrame.Status == CaptureStatus.Success && retryFrame.Buffer != null && GdiDesktopCapturer.HasNonZeroPixels(retryFrame.Buffer))
                    {
                        frame = retryFrame;
                        consecutiveBlackFrames = 0;
                    }
                }

                // 4. Process and hand off successful non-black frames
                if (frame.Status == CaptureStatus.Success && frame.Buffer != null && frame.Width > 0 && frame.Height > 0 && GdiDesktopCapturer.HasNonZeroPixels(frame.Buffer))
                {
                    // Composite protected windows discovered by WindowAffinityManager
                    var protectedWindows = _affinityManager.ActiveProtectedWindows;
                    if (protectedWindows.Count > 0)
                    {
                        for (int i = 0; i < protectedWindows.Count; i++)
                        {
                            try
                            {
                                _dcCapturer.CompositeWindowOntoFrame(frame.Buffer, frame.Width, frame.Height, protectedWindows[i]);
                            }
                            catch { }
                        }
                    }

                    lock (_frameLock)
                    {
                        _latestFrame = frame;
                        _frameSequence++;
                    }
                    TriggerImmediateFrame();
                }

                // Smooth pacing for capture thread (tight 1ms poll for 100-200 FPS)
                Thread.Sleep(1);
            }
            catch (ThreadAbortException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Capture thread notice: {ex.Message}");
                Thread.Sleep(10);
            }
        }
    }

    private static string? TryGetLatestServerUrl()
    {
        try
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string[] candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_url.txt"),
                Path.Combine(programData, "EnterpriseRemote", "server_url.txt"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_identity.json"),
                Path.Combine(programData, "EnterpriseRemote", "device_identity.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "device_identity.json")
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    if (path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        string txt = File.ReadAllText(path).Trim();
                        if (txt.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            txt.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            return txt;
                        }
                    }
                    else
                    {
                        string json = File.ReadAllText(path);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("ControlPlaneUrl", out var cpProp))
                        {
                            string? cpUrl = cpProp.GetString();
                            if (!string.IsNullOrWhiteSpace(cpUrl)) return cpUrl.Trim();
                        }
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private async Task ConnectionManagerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            string activeServer = TryGetLatestServerUrl() ?? _serverUrl;
            string cleanServer = UrlUtils.NormalizeServerUrl(activeServer);
            string wsBase = cleanServer.TrimEnd('/')
                .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)
                .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase);

            var wsUri = new Uri($"{wsBase}/api/v1/stream/host?deviceId={Uri.EscapeDataString(_deviceId)}");

            try
            {
                using var ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
                try { ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true; } catch { }

                Console.WriteLine($"[*] Connecting real-time screen stream to Control Plane: {wsUri}...");
                await ws.ConnectAsync(wsUri, ct);
                Console.WriteLine($"[+] Live screen streaming WebSocket connected! Streaming active desktop display at {_targetFps} FPS (Quality: {_jpegQuality}%)...");
                Interlocked.Exchange(ref _sentSequence, 0);
                Interlocked.Exchange(ref _lastAckedSequence, 0);
                _measuredRttMs = 50.0;
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "streamer_status.log"), $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] STREAMING_ACTIVE to {wsUri} at {_targetFps} FPS (Quality: {_jpegQuality}%)\n"); } catch { }

                var sendLock = new SemaphoreSlim(1, 1);
                var sendTask = RunSendFramesLoopAsync(ws, sendLock, ct);
                var recvTask = RunReceiveInputLoopAsync(ws, sendLock, ct);
                var cursorTask = RunSendCursorLoopAsync(ws, sendLock, ct);

                await Task.WhenAny(sendTask, recvTask, cursorTask);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Stream connection interrupted: {ex.Message}. Reconnecting in 1s...");
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "streamer_status.log"), $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] CONNECTION_ERROR to {wsUri}: {ex.Message}\n"); } catch { }
            }

            try
            {
                await Task.Delay(1000, ct);
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
        long lastSentSequence = -1;

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            int currentTargetFps = _targetFps;
            int frameIntervalMs = Math.Max(2, 1000 / currentTargetFps);

            // 1. Broadcast available desktops & displays every 3 seconds
            if ((DateTime.UtcNow - _lastDesktopListBroadcast).TotalSeconds >= 3)
            {
                _lastDesktopListBroadcast = DateTime.UtcNow;
                try
                {
                    var desktops = DesktopSwitchManager.GetAvailableDesktops();
                    var displays = DesktopSwitchManager.GetPhysicalDisplays();
                    string active = _autoFollowActiveDesktop ? (_desktopSwitchManager.CurrentDesktopName ?? "Default") : _targetDesktopMode;
                    var listPacket = new DesktopListPacket(active, desktops.ToArray(), displays.ToArray(), (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    byte[] rawList = DataChannelProtocol.SerializeDesktopList(listPacket);
                    if (ws.State == WebSocketState.Open)
                    {
                        await sendLock.WaitAsync(ct);
                        try
                        {
                            if (ws.State == WebSocketState.Open)
                            {
                                await ws.SendAsync(new ArraySegment<byte>(rawList), WebSocketMessageType.Binary, true, ct);
                            }
                        }
                        finally
                        {
                            sendLock.Release();
                        }
                    }
                }
                catch { }
            }

            // 2. Acquire freshest frame from capture handoff slot (independent of cursor motion)
            CapturedFrame? frameToEncode = null;
            bool isNewFrame = false;

            lock (_frameLock)
            {
                if (_latestFrame != null && _frameSequence != lastSentSequence)
                {
                    frameToEncode = _latestFrame;
                    lastSentSequence = _frameSequence;
                    isNewFrame = true;
                }
            }

            // Periodic heartbeat on static screen (1 Hz) to guarantee continuous viewer synchronization
            if (!isNewFrame && lastJpeg != null && (DateTime.UtcNow - lastSentTime).TotalMilliseconds >= 1000)
            {
                lock (_frameLock)
                {
                    if (_latestFrame != null)
                    {
                        frameToEncode = _latestFrame;
                        isNewFrame = true;
                    }
                }
            }

            // Flow Control: Strictly pace frame delivery to targetFps (20-120 FPS).
            // Zero-latency in-place drop: if socket is busy sending the previous frame, skip and encode the freshest frame next tick.
            if (Program.IsKillSwitchActive())
            {
                Console.WriteLine("[!] Remote Streaming KILL SWITCH detected in streamer. Exiting cleanly.");
                Environment.Exit(0);
            }

            try
            {
                if (isNewFrame && frameToEncode != null && frameToEncode.Buffer != null && frameToEncode.Width > 0 && frameToEncode.Height > 0)
                {
                    var activeDisplay = _capturer.Display.Width > 0 ? _capturer.Display : _fallbackGdiCapturer.Display;
                    if (activeDisplay.Width > 0)
                    {
                        _inputEngine.UpdateDisplay(activeDisplay);
                    }
                    else
                    {
                        _inputEngine.UpdateDisplay(new DisplayInfo { Left = 0, Top = 0, Width = frameToEncode.Width, Height = frameToEncode.Height });
                    }

                    int dispLeft = activeDisplay.Width > 0 ? activeDisplay.Left : 0;
                    int dispTop = activeDisplay.Height > 0 ? activeDisplay.Top : 0;

                    // Strictly honor user-selected quality without erratic blur-unblur oscillations
                    int effectiveQuality = Math.Clamp(_jpegQuality, 30, 95);
                    int effectiveHeight = _maxHeight;

                    // CPU JPEG encoding executes OUTSIDE sendLock so cursor updates and inputs are never blocked
                    byte[] jpeg = EncodeFrameToJpeg(frameToEncode, effectiveQuality, dispLeft, dispTop, effectiveHeight, out RemoteCursorShape detectedShape);

                    if (jpeg.Length > 0 && ws.State == WebSocketState.Open)
                    {
                        // In-place drop: if socket is currently transmitting, don't stall the thread or backlog frames
                        bool lockAcquired = await sendLock.WaitAsync(5, ct);
                        if (lockAcquired)
                        {
                            try
                            {
                                if (ws.State == WebSocketState.Open)
                                {
                                    if (detectedShape != _lastReportedCursorShape)
                                    {
                                        _lastReportedCursorShape = detectedShape;
                                        byte[] cursorPacket = DataChannelProtocol.SerializeCursorUpdate(detectedShape);
                                        await ws.SendAsync(new ArraySegment<byte>(cursorPacket), WebSocketMessageType.Binary, true, ct);
                                    }

                                    lastJpeg = jpeg;
                                    lastSentTime = DateTime.UtcNow;

                                    var sendSw = Stopwatch.StartNew();
                                    await ws.SendAsync(new ArraySegment<byte>(jpeg), WebSocketMessageType.Binary, true, ct);
                                    sendSw.Stop();
                                    _lastSendDurationMs = sendSw.ElapsedMilliseconds;

                                    // Advance sent sequence in sliding window budget
                                    Interlocked.Increment(ref _sentSequence);
                                    _lastFrameSentTicks = Stopwatch.GetTimestamp();
                                }
                            }
                            finally
                            {
                                sendLock.Release();
                            }
                        }
                    }
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
                // Non-fatal frame glitch, continue to next frame
            }

            sw.Stop();
            int elapsed = (int)sw.ElapsedMilliseconds;
            int delay = Math.Max(1, frameIntervalMs - elapsed);
            try
            {
                await _immediateFrameSignal.WaitAsync(delay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunReceiveInputLoopAsync(ClientWebSocket ws, SemaphoreSlim sendLock, CancellationToken ct)
    {
        byte[] buffer = new byte[8192];

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

                if (result.Count >= 2)
                {
                    try
                    {
                        ProcessIncomingInput(ws, buffer, result.Count, sendLock, ct);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[!] Error processing incoming input batch: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] Receive input loop notice: {ex.Message}");
        }
    }

    private void ProcessIncomingInput(ClientWebSocket ws, byte[] buffer, int count, SemaphoreSlim sendLock, CancellationToken ct)
    {
        ulong? pingTimestamp = null;
        int offset = 0;

        while (offset < count)
        {
            var remaining = new ReadOnlySpan<byte>(buffer, offset, count - offset);
            if (remaining.Length < 2) break;

            byte type = remaining[0];
            try
            {
                if (type is 0x01 or 0x02 or 0x03 or 0x04) // Mouse (21 bytes)
                {
                    if (remaining.Length < 21) break;
                    if (DataChannelProtocol.TryDeserializeMouse(remaining.Slice(0, 21), out var mousePacket))
                    {
                        _inputEngine.ProcessMousePacket(in mousePacket);
                        if (type is 0x01 or 0x02 or 0x04) TriggerImmediateFrame();
                    }
                    offset += 21;
                }
                else if (type is 0x10 or 0x11) // Keyboard (18 bytes)
                {
                    if (remaining.Length < 18) break;
                    if (DataChannelProtocol.TryDeserializeKeyboard(remaining.Slice(0, 18), out var keyPacket))
                    {
                        _inputEngine.ProcessKeyboardPacket(in keyPacket);
                        TriggerImmediateFrame();
                    }
                    offset += 18;
                }
                else if (type == (byte)InputEventType.FrameAck) // 0x42 FrameAck (2 to 5 bytes)
                {
                    double elapsedAckMs = (Stopwatch.GetTimestamp() - _lastFrameSentTicks) * 1000.0 / Stopwatch.Frequency;
                    if (elapsedAckMs > 0 && elapsedAckMs < 10000)
                    {
                        _measuredRttMs = (_measuredRttMs <= 0) ? elapsedAckMs : (_measuredRttMs * 0.75 + elapsedAckMs * 0.25);
                    }

                    if (remaining.Length >= 5)
                    {
                        uint ackSeq = BitConverter.ToUInt32(remaining.Slice(1, 4));
                        if (ackSeq > Interlocked.Read(ref _lastAckedSequence))
                        {
                            Interlocked.Exchange(ref _lastAckedSequence, ackSeq);
                        }
                    }
                    else
                    {
                        Interlocked.Increment(ref _lastAckedSequence);
                    }

                    if (_ackSignal.CurrentCount == 0)
                    {
                        try { _ackSignal.Release(); } catch { }
                    }
                    TriggerImmediateFrame();
                    offset += remaining.Length >= 5 ? 5 : 2;
                }
                else if (type == (byte)InputEventType.StreamConfig) // StreamConfig (14 bytes)
                {
                    if (remaining.Length < 14) break;
                    if (DataChannelProtocol.TryDeserializeStreamConfig(remaining.Slice(0, 14), out var configPacket))
                    {
                        int oldHeight = _maxHeight;
                        _targetFps = Math.Clamp((int)configPacket.TargetFps, 10, 200);
                        _jpegQuality = Math.Clamp((int)configPacket.Quality, 25, 95);
                        _maxHeight = configPacket.MaxHeight;
                        _lowLatencyMode = configPacket.LowLatencyMode != 0;
                        if (oldHeight != _maxHeight)
                        {
                            _cachedBmp?.Dispose();
                            _cachedBmp = null;
                            _downscaleBuffer = null;
                        }
                        TriggerImmediateFrame();
                        Console.WriteLine($"[+] Dynamic Stream Config applied: {_targetFps} FPS, Quality {_jpegQuality}%, MaxHeight {_maxHeight}, LowLatency={_lowLatencyMode}");
                    }
                    offset += 14;
                }
                else if (type == (byte)InputEventType.SwitchDesktop) // SwitchDesktop
                {
                    if (remaining.Length >= 5)
                    {
                        ushort nameLen = BitConverter.ToUInt16(remaining.Slice(3, 2));
                        int totalPacketLen = 5 + nameLen + 8;
                        if (remaining.Length >= totalPacketLen)
                        {
                            if (DataChannelProtocol.TryDeserializeSwitchDesktop(remaining.Slice(0, totalPacketLen), out var switchPacket))
                            {
                                byte prevDisp = _targetDisplayIndex;
                                _targetDisplayIndex = switchPacket.DisplayIndex;
                                _autoFollowActiveDesktop = switchPacket.AutoFollow;
                                _targetDesktopMode = string.IsNullOrWhiteSpace(switchPacket.DesktopName) ? "Auto" : switchPacket.DesktopName;
                                Console.WriteLine($"[+] Switch Desktop received: Target='{_targetDesktopMode}', Display={_targetDisplayIndex}, AutoFollow={_autoFollowActiveDesktop}");

                                if (!_autoFollowActiveDesktop && !_targetDesktopMode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                                {
                                    bool switched = DesktopSwitchManager.SwitchToDesktopByName(_targetDesktopMode);
                                    if (!switched)
                                    {
                                        RequestElevatedDesktopSwitch?.Invoke(_targetDesktopMode);
                                    }
                                }

                                if (_capturer is DxgiDesktopDuplicator dxgi && dxgi.TargetDisplayIndex != _targetDisplayIndex)
                                {
                                    dxgi.TargetDisplayIndex = _targetDisplayIndex;
                                    try { dxgi.Initialize(); } catch { }
                                }
                                if (_fallbackGdiCapturer.TargetDisplayIndex != _targetDisplayIndex)
                                {
                                    _fallbackGdiCapturer.TargetDisplayIndex = _targetDisplayIndex;
                                    try { _fallbackGdiCapturer.Initialize(); } catch { }
                                }

                                _lastDesktopListBroadcast = DateTime.MinValue; // Trigger immediate list update
                            }
                            offset += totalPacketLen;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                else if (type == (byte)InputEventType.LatencyPing || type == (byte)InputEventType.LatencyPong) // Ping/Pong (9 bytes)
                {
                    if (remaining.Length < 9) break;
                    if (DataChannelProtocol.TryDeserializeLatencyPingPong(remaining.Slice(0, 9), out bool isPing, out ulong ts))
                    {
                        if (isPing)
                        {
                            pingTimestamp = ts;
                        }
                    }
                    offset += 9;
                }
                else
                {
                    offset++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Error processing packet type 0x{type:X2}: {ex.Message}");
                offset++;
            }
        }

        if (pingTimestamp.HasValue)
        {
            ulong ts = pingTimestamp.Value;
            _ = Task.Run(async () =>
            {
                try
                {
                    byte[] pong = DataChannelProtocol.SerializeLatencyPong(ts);
                    bool lockAcquired = await sendLock.WaitAsync(100, ct);
                    if (lockAcquired)
                    {
                        try
                        {
                            if (ws.State == WebSocketState.Open)
                            {
                                await ws.SendAsync(new ArraySegment<byte>(pong), WebSocketMessageType.Binary, true, ct);
                            }
                        }
                        finally
                        {
                            sendLock.Release();
                        }
                    }
                }
                catch { }
            }, ct);
        }
    }

    private async Task RunSendCursorLoopAsync(ClientWebSocket ws, SemaphoreSlim sendLock, CancellationToken ct)
    {
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            try
            {
                int currentCurX = -1;
                int currentCurY = -1;
                bool isVisible = false;
                RemoteCursorShape currentShape = RemoteCursorShape.Default;

                var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
                if (GetCursorInfo(out ci) && (ci.flags & CURSOR_SHOWING) != 0)
                {
                    currentCurX = ci.ptScreenPos.x;
                    currentCurY = ci.ptScreenPos.y;
                    isVisible = true;
                    if (ci.hCursor != IntPtr.Zero)
                    {
                        currentShape = DetectCursorShape(ci.hCursor);
                    }
                }
                else
                {
                    // Fallback to injected cursor if suppressed by lockdown/exam software
                    int injX = _inputEngine.LastInjectedScreenX;
                    int injY = _inputEngine.LastInjectedScreenY;
                    if (injX >= 0 && injY >= 0)
                    {
                        currentCurX = injX;
                        currentCurY = injY;
                        isVisible = true;
                    }
                }

                if (isVisible)
                {
                    if (currentCurX != _lastSentCursorX || currentCurY != _lastSentCursorY || currentShape != _lastSentCursorShape || !_lastSentCursorVisible)
                    {
                        var activeDisplay = _capturer.Display.Width > 0 ? _capturer.Display : _fallbackGdiCapturer.Display;
                        ushort dispW = (ushort)(activeDisplay.Width > 0 ? activeDisplay.Width : (_latestFrame?.Width ?? 1920));
                        ushort dispH = (ushort)(activeDisplay.Height > 0 ? activeDisplay.Height : (_latestFrame?.Height ?? 1080));
                        int dispL = activeDisplay.Width > 0 ? activeDisplay.Left : 0;
                        int dispT = activeDisplay.Height > 0 ? activeDisplay.Top : 0;

                        int relX = Math.Max(0, currentCurX - dispL);
                        int relY = Math.Max(0, currentCurY - dispT);

                        var cursorPacket = new CursorPositionPacket(
                            (ushort)Math.Min(ushort.MaxValue, relX),
                            (ushort)Math.Min(ushort.MaxValue, relY),
                            dispW,
                            dispH,
                            currentShape,
                            true);

                        byte[] rawCursorPos = DataChannelProtocol.SerializeCursorPosition(in cursorPacket);
                        if (ws.State == WebSocketState.Open)
                        {
                            bool lockAcquired = await sendLock.WaitAsync(25, ct);
                            if (lockAcquired)
                            {
                                try
                                {
                                    if (ws.State == WebSocketState.Open)
                                    {
                                        await ws.SendAsync(new ArraySegment<byte>(rawCursorPos), WebSocketMessageType.Binary, true, ct);
                                        // Update tracking coordinates only after successful socket transmission
                                        _lastSentCursorX = currentCurX;
                                        _lastSentCursorY = currentCurY;
                                        _lastSentCursorShape = currentShape;
                                        _lastSentCursorVisible = true;
                                    }
                                }
                                finally
                                {
                                    sendLock.Release();
                                }
                            }
                        }
                    }
                }
                else if (_lastSentCursorVisible)
                {
                    var activeDisplay = _capturer.Display.Width > 0 ? _capturer.Display : _fallbackGdiCapturer.Display;
                    ushort dispW = (ushort)(activeDisplay.Width > 0 ? activeDisplay.Width : (_latestFrame?.Width ?? 1920));
                    ushort dispH = (ushort)(activeDisplay.Height > 0 ? activeDisplay.Height : (_latestFrame?.Height ?? 1080));
                    var cursorPacket = new CursorPositionPacket(0, 0, dispW, dispH, RemoteCursorShape.Default, false);
                    byte[] rawCursorPos = DataChannelProtocol.SerializeCursorPosition(in cursorPacket);
                    if (ws.State == WebSocketState.Open)
                    {
                        bool lockAcquired = await sendLock.WaitAsync(25, ct);
                        if (lockAcquired)
                        {
                            try
                            {
                                if (ws.State == WebSocketState.Open)
                                {
                                    await ws.SendAsync(new ArraySegment<byte>(rawCursorPos), WebSocketMessageType.Binary, true, ct);
                                    _lastSentCursorVisible = false;
                                }
                            }
                            finally
                            {
                                sendLock.Release();
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }

            // Ultra-responsive 5ms cadence (~200 Hz polling) for remote cursor
            await Task.Delay(5, ct);
        }
    }

    private byte[] EncodeFrameToJpeg(CapturedFrame frame, long quality, int displayLeft, int displayTop, int maxHeight, out RemoteCursorShape detectedShape)
    {
        detectedShape = RemoteCursorShape.Default;
        if (frame.Buffer == null || frame.Width <= 0 || frame.Height <= 0) return Array.Empty<byte>();

        try
        {
            int targetWidth = frame.Width;
            int targetHeight = frame.Height;
            byte[] bufferToEncode = frame.Buffer;
            float scaleX = 1.0f;
            float scaleY = 1.0f;

            // Fast parallel SIMD downscaling (supports 1080p -> 720p / 540p)
            if (maxHeight > 0 && frame.Height > maxHeight)
            {
                targetHeight = maxHeight;
                targetWidth = (frame.Width * targetHeight) / frame.Height;
                if ((targetWidth & 1) != 0) targetWidth++;

                int requiredBytes = targetWidth * targetHeight * 4;
                if (_downscaleBuffer == null || _downscaleBuffer.Length < requiredBytes)
                {
                    _downscaleBuffer = new byte[requiredBytes];
                }

                PixelConverter.FastBoxDownscaleBgra32(
                    frame.Buffer, frame.Width, frame.Height,
                    _downscaleBuffer, targetWidth, targetHeight);

                bufferToEncode = _downscaleBuffer;
                scaleX = (float)targetWidth / frame.Width;
                scaleY = (float)targetHeight / frame.Height;
            }

            if (_cachedBmp == null || _cachedBmp.Width != targetWidth || _cachedBmp.Height != targetHeight)
            {
                _cachedBmp?.Dispose();
                _cachedBmp = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppRgb);
            }
            var bmp = _cachedBmp;
            var bmpData = bmp.LockBits(
                new Rectangle(0, 0, targetWidth, targetHeight),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppRgb);

            try
            {
                int bytesToCopy = Math.Min(bufferToEncode.Length, bmpData.Stride * targetHeight);
                Marshal.Copy(bufferToEncode, 0, bmpData.Scan0, bytesToCopy);
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }

            // Detect remote cursor shape for telemetry without burning cursor into video frame
            try
            {
                var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
                if (GetCursorInfo(out ci) && (ci.flags & CURSOR_SHOWING) != 0 && ci.hCursor != IntPtr.Zero)
                {
                    detectedShape = DetectCursorShape(ci.hCursor);
                }
            }
            catch { }

            lock (_reusableJpegStream)
            {
                _reusableJpegStream.SetLength(0);
                if (_jpegEncoder != null)
                {
                    using var encParams = new EncoderParameters(1);
                    encParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                    bmp.Save(_reusableJpegStream, _jpegEncoder, encParams);
                }
                else
                {
                    bmp.Save(_reusableJpegStream, ImageFormat.Jpeg);
                }
                return _reusableJpegStream.ToArray();
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

    private static readonly IntPtr _hArrow = LoadCursor(IntPtr.Zero, 32512);
    private static readonly IntPtr _hIBeam = LoadCursor(IntPtr.Zero, 32513);
    private static readonly IntPtr _hWait = LoadCursor(IntPtr.Zero, 32514);
    private static readonly IntPtr _hCross = LoadCursor(IntPtr.Zero, 32515);
    private static readonly IntPtr _hSizeAll = LoadCursor(IntPtr.Zero, 32646);
    private static readonly IntPtr _hSizeWE = LoadCursor(IntPtr.Zero, 32644);
    private static readonly IntPtr _hSizeNS = LoadCursor(IntPtr.Zero, 32645);
    private static readonly IntPtr _hHand = LoadCursor(IntPtr.Zero, 32649);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorInfo(out CURSORINFO pci);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyHeight, uint istepIfAniCur, IntPtr hbrFlickerFreeDraw, uint diFlags);

    private static RemoteCursorShape DetectCursorShape(IntPtr hCursor)
    {
        if (hCursor == IntPtr.Zero) return RemoteCursorShape.Default;

        if (hCursor == _hHand) return RemoteCursorShape.Hand;
        if (hCursor == _hIBeam) return RemoteCursorShape.IBeam;
        if (hCursor == _hSizeAll) return RemoteCursorShape.SizeAll;
        if (hCursor == _hSizeWE) return RemoteCursorShape.SizeWE;
        if (hCursor == _hSizeNS) return RemoteCursorShape.SizeNS;
        if (hCursor == _hWait) return RemoteCursorShape.Wait;
        if (hCursor == _hCross) return RemoteCursorShape.Cross;
        if (hCursor == _hArrow) return RemoteCursorShape.Default;

        try
        {
            if (GetIconInfo(hCursor, out ICONINFO ii))
            {
                if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
                if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);

                if (ii.xHotspot >= 4 && ii.xHotspot <= 12 && ii.yHotspot <= 3)
                {
                    return RemoteCursorShape.Hand;
                }
                if (ii.xHotspot >= 2 && ii.xHotspot <= 7 && ii.yHotspot >= 7 && ii.yHotspot <= 14)
                {
                    return RemoteCursorShape.IBeam;
                }
            }
        }
        catch { }

        return RemoteCursorShape.Default;
    }
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
        _inputEngine.Dispose();
        _cachedBmp?.Dispose();
        _cachedBmp = null;
        _reusableJpegStream.Dispose();
        _desktopSwitchManager.Dispose();
        _affinityManager.Dispose();
        _dcCapturer.Dispose();
        _fallbackGdiCapturer.Dispose();
        _immediateFrameSignal.Dispose();
        _ackSignal.Dispose();
        _cts.Dispose();
    }
}
