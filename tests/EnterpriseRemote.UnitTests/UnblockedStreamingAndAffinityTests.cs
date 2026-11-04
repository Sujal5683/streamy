using System;
using System.Drawing;
using System.Threading;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Protocol;
using EnterpriseRemote.Host.Service.Input;
using EnterpriseRemote.Host.Service.Policy;
using EnterpriseRemote.Host.UserAgent.Capture;
using EnterpriseRemote.Host.UserAgent.Input;
using Xunit;

namespace EnterpriseRemote.UnitTests;

public class UnblockedStreamingAndAffinityTests
{
    [Fact]
    public void WindowAffinityManager_InitializesAndScansWithoutException()
    {
        using var manager = new WindowAffinityManager();
        Assert.NotNull(manager.ActiveProtectedWindows);

        // Scan windows on local test environment
        var protectedWindows = manager.ScanAndNeutralize();
        Assert.NotNull(protectedWindows);
    }

    [Fact]
    public void DirectCompositionWindowCapturer_HandlesInvalidWindowsSafely()
    {
        using var capturer = new DirectCompositionWindowCapturer();

        var invalidResult = capturer.CaptureWindow(IntPtr.Zero);
        Assert.False(invalidResult.Success);
        Assert.Null(invalidResult.Buffer);

        var nonExistentResult = capturer.CaptureWindow(new IntPtr(0x12345678));
        Assert.False(nonExistentResult.Success);

        byte[] frameBuffer = new byte[1920 * 1080 * 4];
        bool composited = capturer.CompositeWindowOntoFrame(frameBuffer, 1920, 1080, IntPtr.Zero);
        Assert.False(composited);
    }

    [Fact]
    public void DirectCompositionWindowCapturer_WindowBoundsClipping_Succeeds()
    {
        using var capturer = new DirectCompositionWindowCapturer();
        byte[] frame = new byte[100 * 100 * 4];

        // Should return false for invalid handle without corrupting frame buffer
        bool result = capturer.CompositeWindowOntoFrame(frame, 100, 100, IntPtr.Zero);
        Assert.False(result);
        Assert.All(frame, b => Assert.Equal(0, b));
    }

    [Fact]
    public void SystemInputInjector_ValidatesPayloadsGracefully()
    {
        Assert.False(SystemInputInjector.InjectInputPacket(null!));
        Assert.False(SystemInputInjector.InjectInputPacket(Array.Empty<byte>()));
        Assert.False(SystemInputInjector.InjectInputPacket(new byte[10])); // Truncated packet
    }

    [Fact]
    public void InputInjectionEngine_ElevatedFallback_CanBeConfiguredAndTriggered()
    {
        var display = new DisplayInfo
        {
            Left = 0,
            Top = 0,
            Width = 1920,
            Height = 1080,
            IsPrimary = true
        };

        var sink = new WindowsSendInputSink();
        bool fallbackInvoked = false;
        byte[]? receivedPayload = null;

        sink.ElevatedFallback = payload =>
        {
            fallbackInvoked = true;
            receivedPayload = payload;
        };

        var engine = new InputInjectionEngine(display, sink);

        // Verify SetElevatedFallback updates sink
        engine.SetElevatedFallback(payload =>
        {
            fallbackInvoked = true;
            receivedPayload = payload;
        });

        Assert.NotNull(sink.ElevatedFallback);
        sink.ElevatedFallback(new byte[] { 0x42 });
        Assert.True(fallbackInvoked);
        Assert.NotNull(receivedPayload);
        Assert.Single(receivedPayload);
        Assert.Equal(0x42, receivedPayload[0]);
    }

    [Fact]
    public void CapturedFrame_ProtectedContentMaskedOut_SetsAndReadsProperly()
    {
        var frame = new CapturedFrame
        {
            Status = CaptureStatus.Success,
            Width = 1920,
            Height = 1080,
            ProtectedContentMaskedOut = true
        };

        Assert.True(frame.ProtectedContentMaskedOut);

        frame.ProtectedContentMaskedOut = false;
        Assert.False(frame.ProtectedContentMaskedOut);
    }

    [Fact]
    public void BrowserPolicyConfigurator_ExecutesSafely()
    {
        // Should execute without throwing (handles permissions or registry safely)
        var exception = Record.Exception(() => BrowserPolicyConfigurator.ApplyBrowserPolicies());
        Assert.Null(exception);
    }

    [Fact]
    public void DirectCompositionWindowCapturer_HasNonZeroPixels_RejectsAllBlackBuffer()
    {
        byte[] blackBuffer = new byte[1920 * 1080 * 4]; // All zeros
        Assert.False(DirectCompositionWindowCapturer.HasNonZeroPixels(blackBuffer));

        byte[] emptyBuffer = Array.Empty<byte>();
        Assert.False(DirectCompositionWindowCapturer.HasNonZeroPixels(emptyBuffer));

        byte[] nullBuffer = null!;
        Assert.False(DirectCompositionWindowCapturer.HasNonZeroPixels(nullBuffer));
    }

    [Fact]
    public void DirectCompositionWindowCapturer_HasNonZeroPixels_AcceptsContentWithPixels()
    {
        byte[] activeBuffer = new byte[1024];
        activeBuffer[500] = 0xFF; // Non-zero pixel
        Assert.True(DirectCompositionWindowCapturer.HasNonZeroPixels(activeBuffer));
    }

    [Fact]
    public void WindowAffinityNeutralizer_Neutralize_HandlesInvalidHandleSafely()
    {
        bool result = WindowAffinityNeutralizer.Neutralize(IntPtr.Zero, 0);
        Assert.False(result);

        bool fakeHandleResult = WindowAffinityNeutralizer.Neutralize(new IntPtr(0xDEADBEEF), 99999);
        Assert.False(fakeHandleResult);
    }

    [Fact]
    public void WindowAffinityManager_ElevatedCallback_FiresWhenConfigured()
    {
        using var manager = new WindowAffinityManager();
        bool elevatedCalled = false;
        IntPtr calledHwnd = IntPtr.Zero;
        uint calledPid = 0;

        manager.RequestElevatedNeutralization = (hwnd, pid) =>
        {
            elevatedCalled = true;
            calledHwnd = hwnd;
            calledPid = pid;
        };

        Assert.NotNull(manager.RequestElevatedNeutralization);
        manager.RequestElevatedNeutralization(new IntPtr(0x1234), 5678);
        Assert.True(elevatedCalled);
        Assert.Equal(new IntPtr(0x1234), calledHwnd);
        Assert.Equal(5678u, calledPid);
    }

    [Fact]
    public void DesktopSwitchManager_Initializes_AndReportsActiveDesktop()
    {
        using var switchManager = new DesktopSwitchManager();
        Assert.NotNull(switchManager.CurrentDesktopName);
        Assert.NotEmpty(switchManager.CurrentDesktopName);

        bool changed = switchManager.CheckAndSyncDesktop(out string activeName);
        Assert.NotNull(activeName);
        Assert.NotEmpty(activeName);
    }

    [Fact]
    public void DesktopSwitchManager_AttachCallingThread_ExecutesSafely()
    {
        bool attached = DesktopSwitchManager.AttachCallingThreadToInputDesktop(out string desktopName);
        // On CI/headless/interactive sessions, should return a valid desktop name
        Assert.NotNull(desktopName);
    }

    [Fact]
    public void GdiDesktopCapturer_RebindToActiveDesktop_Succeeds()
    {
        using var capturer = new GdiDesktopCapturer();
        bool initialized = capturer.Initialize();
        Assert.True(initialized);

        bool rebound = capturer.RebindToActiveDesktop();
        Assert.True(rebound);
        Assert.True(capturer.IsInitialized);
    }

    [Fact]
    public void WindowAffinityManager_EventHook_RegistersAndDisposesSafely()
    {
        using var manager = new WindowAffinityManager();
        // Should start monitoring and hook events without throwing
        var ex = Record.Exception(() => manager.StartMonitoring(200));
        Assert.Null(ex);
    }

    [Fact]
    public void WindowAffinityManager_Neutralize_HandlesInvalidHandleSafely()
    {
        bool result = WindowAffinityManager.NeutralizeWindowAffinity(IntPtr.Zero, 0);
        Assert.False(result);

        bool fakeResult = WindowAffinityManager.NeutralizeWindowAffinity(new IntPtr(0xCAFEF00D), 12345);
        Assert.False(fakeResult);
    }

    [Fact]
    public void BrowserPolicyConfigurator_ExecutesSafeExamBrowserAndWebView2_Successfully()
    {
        var ex = Record.Exception(() => BrowserPolicyConfigurator.ApplyBrowserPolicies());
        Assert.Null(ex);
    }

    [Fact]
    public void DesktopSwitchManager_SyncCallingThreadToInputDesktop_ExecutesSafely()
    {
        string currentThreadDesktop = "Default";
        bool switched = DesktopSwitchManager.SyncCallingThreadToInputDesktop(ref currentThreadDesktop, out string activeName);
        Assert.NotNull(activeName);
        Assert.NotEmpty(activeName);
        Assert.Equal(activeName, currentThreadDesktop);
    }

    /*
    [Fact]
    public void WindowAffinityNeutralizer_ScanAndNeutralizeAllDesktops_ExecutesSafely()
    {
        var ex = Record.Exception(() =>
        {
            int count = WindowAffinityNeutralizer.ScanAndNeutralizeAllDesktops();
            Assert.True(count >= 0);
        });
        Assert.Null(ex);
    }
    */

    [Fact]
    public void GdiDesktopCapturer_CaptureFrame_CapturesWithCaptureBlt()
    {
        using var capturer = new GdiDesktopCapturer();
        bool initialized = capturer.Initialize();
        Assert.True(initialized);

        var frame = capturer.CaptureFrame(50);
        Assert.True(frame.Status == CaptureStatus.Success || frame.Status == CaptureStatus.Timeout);
        if (frame.Status == CaptureStatus.Success)
        {
            Assert.NotNull(frame.Buffer);
            Assert.True(frame.Width > 0);
            Assert.True(frame.Height > 0);
        }
    }

    [Fact]
    public void CleanNativeThread_AttachCallingThreadToInputDesktop_SucceedsWithoutErrorBusy()
    {
        bool success = false;
        string deskName = "";
        var t = new Thread(() =>
        {
            success = DesktopSwitchManager.AttachCallingThreadToInputDesktop(out deskName);
        })
        {
            IsBackground = true
        };
        t.Start();
        t.Join();

        Assert.True(success);
        Assert.NotEmpty(deskName);
    }

    [Fact]
    public void DesktopSwitchManager_CreateCleanAttachedThread_ExecutesAndAttachesSuccessfully()
    {
        string attached = "";
        var thread = DesktopSwitchManager.CreateCleanAttachedThread(deskName =>
        {
            attached = deskName;
        });

        thread.Start();
        bool joined = thread.Join(2000);

        Assert.True(joined);
        Assert.NotEmpty(attached);
    }

    [Fact]
    public void GdiDesktopCapturer_HasNonZeroPixels_AccuratelyDetectsBlackVsColoredFrames()
    {
        byte[] blackBuffer = new byte[1920 * 1080 * 4];
        Assert.False(GdiDesktopCapturer.HasNonZeroPixels(blackBuffer));
        Assert.False(GdiDesktopCapturer.HasNonZeroPixels(null!));
        Assert.False(GdiDesktopCapturer.HasNonZeroPixels(Array.Empty<byte>()));

        byte[] coloredBuffer = new byte[1920 * 1080 * 4];
        coloredBuffer[0] = 0xFF; // First pixel (B)
        Assert.True(GdiDesktopCapturer.HasNonZeroPixels(coloredBuffer));

        byte[] middleColoredBuffer = new byte[1920 * 1080 * 4];
        // Fill a region (simulating an active window)
        for (int i = 1000; i < 20000; i++) middleColoredBuffer[i] = 0x80;
        Assert.True(GdiDesktopCapturer.HasNonZeroPixels(middleColoredBuffer));
    }

    [Fact]
    public void CaptureStatus_BlackScreenDetected_EnumIsSupported()
    {
        var frame = new CapturedFrame
        {
            Status = CaptureStatus.BlackScreenDetected
        };
        Assert.Equal(CaptureStatus.BlackScreenDetected, frame.Status);
    }

    /*
    [Fact]
    public void DirectCompositionWindowCapturer_IsSafeBrowserOrProtectedWindow_HandlesSafely()
    {
        Assert.False(DirectCompositionWindowCapturer.IsSafeBrowserOrProtectedWindow(IntPtr.Zero));
        Assert.False(DirectCompositionWindowCapturer.IsSafeBrowserOrProtectedWindow(new IntPtr(0x12345)));
    }

    [Fact]
    public void WindowAffinityNeutralizer_UnlockDesktopDacl_ExecutesSafely()
    {
        var ex = Record.Exception(() => WindowAffinityNeutralizer.UnlockDesktopDacl(IntPtr.Zero));
        Assert.Null(ex);
    }
    */
}

