using System;
using EnterpriseRemote.Common.Models;

namespace EnterpriseRemote.Host.UserAgent.Capture;

public interface IScreenCapturer : IDisposable
{
    DisplayInfo Display { get; }
    bool IsInitialized { get; }
    bool Initialize();
    bool RebindToActiveDesktop() => Initialize();
    CapturedFrame CaptureFrame(int timeoutMilliseconds = 50);
}
