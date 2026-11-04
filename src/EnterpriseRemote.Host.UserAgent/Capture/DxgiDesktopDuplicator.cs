using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using EnterpriseRemote.Common.Models;

namespace EnterpriseRemote.Host.UserAgent.Capture;

public sealed class DxgiDesktopDuplicator : IScreenCapturer
{
    private IntPtr _d3dDevice = IntPtr.Zero;
    private IntPtr _d3dContext = IntPtr.Zero;
    private IntPtr _dxgiOutputDuplication = IntPtr.Zero;
    private IntPtr _stagingTexture = IntPtr.Zero;
    private int _width;
    private int _height;
    private byte[]? _frameBuffer;
    private readonly IScreenCapturer _fallbackCapturer;
    private DateTime _lastDxgiAttemptUtc = DateTime.MinValue;

    public DisplayInfo Display { get; private set; } = new();
    public bool IsInitialized { get; private set; }
    public bool IsUsingFallback { get; private set; }
    public int TargetDisplayIndex { get; set; } = 0;

    public DxgiDesktopDuplicator(IScreenCapturer? fallbackCapturer = null)
    {
        _fallbackCapturer = fallbackCapturer ?? new GdiDesktopCapturer();
    }

    public bool Initialize()
    {
        CleanupDxgiResources();

        // Try native DXGI Desktop Duplication first
        try
        {
            if (TryInitializeDxgi())
            {
                IsInitialized = true;
                IsUsingFallback = false;
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DXGI initialization exception: {ex.Message}");
        }

        // Seamless fallback to GDI Desktop Capturer if DXGI is unavailable (e.g. Session 0, VM, or custom exam desktop)
        try
        {
            if (_fallbackCapturer.Initialize())
            {
                Display = _fallbackCapturer.Display;
                IsInitialized = true;
                IsUsingFallback = true;
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Fallback initialization exception: {ex.Message}");
        }

        return false;
    }

    public bool RebindToActiveDesktop()
    {
        if (IsUsingFallback)
        {
            return (_fallbackCapturer as GdiDesktopCapturer)?.RebindToActiveDesktop() ?? _fallbackCapturer.Initialize();
        }
        CleanupDxgiResources();
        return Initialize();
    }

    private unsafe bool TryInitializeDxgi()
    {
        // 1. Multi-adapter discovery via CreateDXGIFactory1 to find the GPU driving the active display
        IntPtr pFactory = IntPtr.Zero;
        IntPtr pTargetAdapter = IntPtr.Zero;
        IntPtr pTargetOutput = IntPtr.Zero;
        Guid iidFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
        var attachedDisplays = new List<(IntPtr Adapter, IntPtr Output, string Name)>();

        try
        {
            if (CreateDXGIFactory1(ref iidFactory1, out pFactory) == S_OK && pFactory != IntPtr.Zero)
            {
                IntPtr* vtblFactory = *(IntPtr**)pFactory;
                var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, out IntPtr, int>)vtblFactory[12];

                for (uint adapterIndex = 0; ; adapterIndex++)
                {
                    IntPtr pCurrentAdapter = IntPtr.Zero;
                    int hrEnum = enumAdapters1(pFactory, adapterIndex, out pCurrentAdapter);
                    if (hrEnum != S_OK || pCurrentAdapter == IntPtr.Zero) break;

                    // Enumerate outputs for this adapter (Slot 7 of IDXGIAdapter)
                    IntPtr* vtblAdapter = *(IntPtr**)pCurrentAdapter;
                    var enumOutputs = (delegate* unmanaged[Stdcall]<IntPtr, uint, out IntPtr, int>)vtblAdapter[7];

                    for (uint outputIndex = 0; ; outputIndex++)
                    {
                        IntPtr pCurrentOutput = IntPtr.Zero;
                        int hrOut = enumOutputs(pCurrentAdapter, outputIndex, out pCurrentOutput);
                        if (hrOut != S_OK || pCurrentOutput == IntPtr.Zero) break;

                        // Check output description (Slot 7 of IDXGIOutput)
                        IntPtr* vtblOutput = *(IntPtr**)pCurrentOutput;
                        var getDesc = (delegate* unmanaged[Stdcall]<IntPtr, out DXGI_OUTPUT_DESC, int>)vtblOutput[7];
                        if (getDesc(pCurrentOutput, out DXGI_OUTPUT_DESC outDesc) == S_OK && outDesc.AttachedToDesktop)
                        {
                            Marshal.AddRef(pCurrentAdapter);
                            attachedDisplays.Add((pCurrentAdapter, pCurrentOutput, outDesc.DeviceName));
                        }
                        else
                        {
                            Marshal.Release(pCurrentOutput);
                        }
                    }

                    Marshal.Release(pCurrentAdapter);
                }

                if (attachedDisplays.Count > 0)
                {
                    int chosenIndex = Math.Clamp(TargetDisplayIndex, 0, attachedDisplays.Count - 1);
                    for (int i = 0; i < attachedDisplays.Count; i++)
                    {
                        if (i == chosenIndex)
                        {
                            pTargetAdapter = attachedDisplays[i].Adapter;
                            pTargetOutput = attachedDisplays[i].Output;
                        }
                        else
                        {
                            Marshal.Release(attachedDisplays[i].Output);
                            Marshal.Release(attachedDisplays[i].Adapter);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Multi-adapter enumeration notice: {ex.Message}");
        }
        finally
        {
            if (pFactory != IntPtr.Zero) Marshal.Release(pFactory);
        }

        // 2. Create D3D11 Device (on found display-driving adapter or default fallback)
        int hr;
        if (pTargetAdapter != IntPtr.Zero)
        {
            // Note: When pAdapter is non-null, DriverType MUST be D3D_DRIVER_TYPE_UNKNOWN (0)
            hr = D3D11CreateDevice(
                pTargetAdapter,
                D3D_DRIVER_TYPE_UNKNOWN,
                IntPtr.Zero,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                null,
                0,
                D3D11_SDK_VERSION,
                out _d3dDevice,
                out _,
                out _d3dContext);
        }
        else
        {
            hr = D3D11CreateDevice(
                IntPtr.Zero,
                D3D_DRIVER_TYPE_HARDWARE,
                IntPtr.Zero,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                null,
                0,
                D3D11_SDK_VERSION,
                out _d3dDevice,
                out _,
                out _d3dContext);

            if (hr != S_OK || _d3dDevice == IntPtr.Zero)
            {
                hr = D3D11CreateDevice(
                    IntPtr.Zero,
                    D3D_DRIVER_TYPE_WARP,
                    IntPtr.Zero,
                    D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                    null,
                    0,
                    D3D11_SDK_VERSION,
                    out _d3dDevice,
                    out _,
                    out _d3dContext);
            }
        }


        if (hr != S_OK || _d3dDevice == IntPtr.Zero)
        {
            if (pTargetOutput != IntPtr.Zero) Marshal.Release(pTargetOutput);
            if (pTargetAdapter != IntPtr.Zero) Marshal.Release(pTargetAdapter);
            return false;
        }

        IntPtr pDxgiOutput1 = IntPtr.Zero;
        try
        {
            if (pTargetOutput == IntPtr.Zero)
            {
                // Fallback to querying output from device's adapter
                IntPtr pDxgiDevice = IntPtr.Zero;
                IntPtr pDxgiAdapter = IntPtr.Zero;
                try
                {
                    Guid iidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
                    hr = Marshal.QueryInterface(_d3dDevice, ref iidDxgiDevice, out pDxgiDevice);
                    if (hr != S_OK || pDxgiDevice == IntPtr.Zero) return false;

                    IntPtr* vtblDxgiDevice = *(IntPtr**)pDxgiDevice;
                    var getAdapter = (delegate* unmanaged[Stdcall]<IntPtr, out IntPtr, int>)vtblDxgiDevice[7];
                    hr = getAdapter(pDxgiDevice, out pDxgiAdapter);
                    if (hr != S_OK || pDxgiAdapter == IntPtr.Zero) return false;

                    IntPtr* vtblDxgiAdapter = *(IntPtr**)pDxgiAdapter;
                    var enumOutputs = (delegate* unmanaged[Stdcall]<IntPtr, uint, out IntPtr, int>)vtblDxgiAdapter[7];
                    hr = enumOutputs(pDxgiAdapter, 0, out pTargetOutput);
                    if (hr != S_OK || pTargetOutput == IntPtr.Zero) return false;
                }
                finally
                {
                    if (pDxgiAdapter != IntPtr.Zero) Marshal.Release(pDxgiAdapter);
                    if (pDxgiDevice != IntPtr.Zero) Marshal.Release(pDxgiDevice);
                }
            }

            // Query IDXGIOutput1
            Guid iidDxgiOutput1 = new("00cddea8-939b-4b83-a340-a685226666cc");
            hr = Marshal.QueryInterface(pTargetOutput, ref iidDxgiOutput1, out pDxgiOutput1);

            if (hr != S_OK || pDxgiOutput1 == IntPtr.Zero) return false;

            // Direct vtable call: IDXGIOutput1::DuplicateOutput (Slot 22)
            IntPtr* vtblDxgiOutput1 = *(IntPtr**)pDxgiOutput1;
            var duplicateOutput = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, out IntPtr, int>)vtblDxgiOutput1[22];
            hr = duplicateOutput(pDxgiOutput1, _d3dDevice, out _dxgiOutputDuplication);

            if (hr != S_OK || _dxgiOutputDuplication == IntPtr.Zero) return false;

            // Query DXGI_OUTDUPL_DESC (Slot 7 of IDXGIOutputDuplication)
            IntPtr* vtblDupl = *(IntPtr**)_dxgiOutputDuplication;
            var getDuplDesc = (delegate* unmanaged[Stdcall]<IntPtr, out DXGI_OUTDUPL_DESC, void>)vtblDupl[7];
            getDuplDesc(_dxgiOutputDuplication, out DXGI_OUTDUPL_DESC desc);

            _width = desc.ModeDesc.Width > 0 ? (int)desc.ModeDesc.Width : 1920;
            _height = desc.ModeDesc.Height > 0 ? (int)desc.ModeDesc.Height : 1080;
            _frameBuffer = new byte[_width * _height * 4];

            Display = new DisplayInfo
            {
                Index = (byte)Math.Clamp(TargetDisplayIndex, 0, 255),
                DeviceName = $@"\\.\DISPLAY{TargetDisplayIndex + 1} (DXGI Multi-Adapter Duplication)",
                Left = desc.DesktopCoordinates.Left,
                Top = desc.DesktopCoordinates.Top,
                Width = _width,
                Height = _height,
                RefreshRateNumerator = (int)desc.ModeDesc.RefreshRate.Numerator,
                RefreshRateDenominator = (int)desc.ModeDesc.RefreshRate.Denominator,
                IsPrimary = (TargetDisplayIndex == 0)
            };

            // 3. Create Staging Texture via ID3D11Device::CreateTexture2D (Slot 5)
            var texDesc = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)_width,
                Height = (uint)_height,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Usage = D3D11_USAGE_STAGING,
                BindFlags = 0,
                CPUAccessFlags = D3D11_CPU_ACCESS_READ,
                MiscFlags = 0
            };

            IntPtr* vtblD3D11Device = *(IntPtr**)_d3dDevice;
            var createTexture2D = (delegate* unmanaged[Stdcall]<IntPtr, ref D3D11_TEXTURE2D_DESC, IntPtr, out IntPtr, int>)vtblD3D11Device[5];
            hr = createTexture2D(_d3dDevice, ref texDesc, IntPtr.Zero, out _stagingTexture);
            return hr == S_OK && _stagingTexture != IntPtr.Zero;
        }
        finally
        {
            if (pDxgiOutput1 != IntPtr.Zero) Marshal.Release(pDxgiOutput1);
            if (pTargetOutput != IntPtr.Zero) Marshal.Release(pTargetOutput);
            if (pTargetAdapter != IntPtr.Zero) Marshal.Release(pTargetAdapter);
        }
    }

    public unsafe CapturedFrame CaptureFrame(int timeoutMilliseconds = 50)
    {
        if (!IsInitialized)
        {
            return new CapturedFrame { Status = CaptureStatus.NotInitialized };
        }

        if (IsUsingFallback)
        {
            if ((DateTime.UtcNow - _lastDxgiAttemptUtc).TotalSeconds >= 2.0)
            {
                _lastDxgiAttemptUtc = DateTime.UtcNow;
                if (TryInitializeDxgi())
                {
                    IsUsingFallback = false;
                }
            }

            if (IsUsingFallback)
            {
                return _fallbackCapturer.CaptureFrame(timeoutMilliseconds);
            }
        }

        var sw = Stopwatch.StartNew();
        IntPtr pDesktopResource = IntPtr.Zero;
        try
        {
            // IDXGIOutputDuplication::AcquireNextFrame (Slot 8)
            IntPtr* vtblDupl = *(IntPtr**)_dxgiOutputDuplication;
            var acquireNextFrame = (delegate* unmanaged[Stdcall]<IntPtr, uint, out DXGI_OUTDUPL_FRAME_INFO, out IntPtr, int>)vtblDupl[8];
            int hr = acquireNextFrame(_dxgiOutputDuplication, (uint)timeoutMilliseconds, out DXGI_OUTDUPL_FRAME_INFO frameInfo, out pDesktopResource);

            if (hr == DXGI_ERROR_WAIT_TIMEOUT)
            {
                return new CapturedFrame { Status = CaptureStatus.Timeout };
            }
            if (hr == DXGI_ERROR_ACCESS_LOST)
            {
                // Active desktop switched (e.g. Safe Exam Browser started on dedicated desktop)
                CleanupDxgiResources();
                _lastDxgiAttemptUtc = DateTime.UtcNow;
                DesktopSwitchManager.AttachCallingThreadToInputDesktop(out _);
                if (TryInitializeDxgi())
                {
                    IsUsingFallback = false;
                    return new CapturedFrame { Status = CaptureStatus.AccessLost };
                }

                IsUsingFallback = true;
                _fallbackCapturer.RebindToActiveDesktop();
                return _fallbackCapturer.CaptureFrame(timeoutMilliseconds);
            }
            if (hr != S_OK || pDesktopResource == IntPtr.Zero)
            {
                return new CapturedFrame { Status = CaptureStatus.Error };
            }

            var dirtyRects = new List<DirtyRect>();
            if (frameInfo.TotalMetadataBufferSize > 0)
            {
                uint dirtyRectsBufferSize = frameInfo.TotalMetadataBufferSize;
                IntPtr dirtyRectsBuffer = Marshal.AllocHGlobal((int)dirtyRectsBufferSize);
                try
                {
                    // IDXGIOutputDuplication::GetFrameDirtyRects (Slot 9)
                    var getFrameDirtyRects = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, out uint, int>)vtblDupl[9];
                    if (getFrameDirtyRects(_dxgiOutputDuplication, dirtyRectsBufferSize, dirtyRectsBuffer, out uint bytesRequired) == S_OK)
                    {
                        int count = (int)bytesRequired / Marshal.SizeOf<RECT>();
                        for (int i = 0; i < count; i++)
                        {
                            IntPtr ptr = IntPtr.Add(dirtyRectsBuffer, i * Marshal.SizeOf<RECT>());
                            var r = Marshal.PtrToStructure<RECT>(ptr);
                            dirtyRects.Add(new DirtyRect(r.Left, r.Top, r.Right, r.Bottom));
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(dirtyRectsBuffer);
                }
            }
            else
            {
                dirtyRects.Add(new DirtyRect(0, 0, _width, _height));
            }

            // ID3D11DeviceContext::CopyResource (Slot 47)
            IntPtr* vtblD3D11Context = *(IntPtr**)_d3dContext;
            var copyResource = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)vtblD3D11Context[47];
            copyResource(_d3dContext, _stagingTexture, pDesktopResource);

            // ID3D11DeviceContext::Map (Slot 14)
            var map = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, out D3D11_MAPPED_SUBRESOURCE, int>)vtblD3D11Context[14];
            hr = map(_d3dContext, _stagingTexture, 0, D3D11_MAP_READ, 0, out D3D11_MAPPED_SUBRESOURCE mapped);
            if (hr != S_OK)
            {
                var releaseFrameEarly = (delegate* unmanaged[Stdcall]<IntPtr, int>)vtblDupl[14];
                releaseFrameEarly(_dxgiOutputDuplication);
                return new CapturedFrame { Status = CaptureStatus.Error };
            }

            byte[] frameCopy = new byte[_frameBuffer!.Length];
            if (mapped.RowPitch == _width * 4)
            {
                Marshal.Copy(mapped.pData, frameCopy, 0, frameCopy.Length);
            }
            else
            {
                for (int y = 0; y < _height; y++)
                {
                    IntPtr srcRow = IntPtr.Add(mapped.pData, y * (int)mapped.RowPitch);
                    Marshal.Copy(srcRow, frameCopy, y * _width * 4, _width * 4);
                }
            }

            // ID3D11DeviceContext::Unmap (Slot 15)
            var unmap = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)vtblD3D11Context[15];
            unmap(_d3dContext, _stagingTexture, 0);

            // IDXGIOutputDuplication::ReleaseFrame (Slot 14)
            var releaseFrame = (delegate* unmanaged[Stdcall]<IntPtr, int>)vtblDupl[14];
            releaseFrame(_dxgiOutputDuplication);

            sw.Stop();

            if (!GdiDesktopCapturer.HasNonZeroPixels(frameCopy))
            {
                return new CapturedFrame
                {
                    Status = CaptureStatus.BlackScreenDetected,
                    Width = _width,
                    Height = _height,
                    Buffer = frameCopy,
                    ProtectedContentMaskedOut = frameInfo.ProtectedContentMaskedOut
                };
            }

            return new CapturedFrame
            {
                Status = CaptureStatus.Success,
                Width = _width,
                Height = _height,
                Stride = _width * 4,
                PixelFormat = CapturePixelFormat.Bgra32,
                Buffer = frameCopy,
                BufferLength = frameCopy.Length,
                DirtyRects = dirtyRects,
                ProtectedContentMaskedOut = frameInfo.ProtectedContentMaskedOut,
                Pointer = new PointerInfo
                {
                    Visible = frameInfo.PointerPosition.Visible,
                    X = frameInfo.PointerPosition.Position.x,
                    Y = frameInfo.PointerPosition.Position.y,
                    ShapeType = PointerShapeType.Color
                },
                CaptureDurationTicks = sw.ElapsedTicks,
                TimestampUtcTicks = DateTime.UtcNow.Ticks
            };
        }
        catch
        {
            return new CapturedFrame { Status = CaptureStatus.Error };
        }
        finally
        {
            if (pDesktopResource != IntPtr.Zero)
            {
                Marshal.Release(pDesktopResource);
            }
        }
    }

    private void CleanupDxgiResources()
    {
        if (_stagingTexture != IntPtr.Zero)
        {
            Marshal.Release(_stagingTexture);
            _stagingTexture = IntPtr.Zero;
        }
        if (_dxgiOutputDuplication != IntPtr.Zero)
        {
            Marshal.Release(_dxgiOutputDuplication);
            _dxgiOutputDuplication = IntPtr.Zero;
        }
        if (_d3dContext != IntPtr.Zero)
        {
            Marshal.Release(_d3dContext);
            _d3dContext = IntPtr.Zero;
        }
        if (_d3dDevice != IntPtr.Zero)
        {
            Marshal.Release(_d3dDevice);
            _d3dDevice = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        CleanupDxgiResources();
        _fallbackCapturer.Dispose();
        IsInitialized = false;
    }

    #region Win32 / DXGI Structs
    private const int S_OK = 0;
    private const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
    private const int DXGI_ERROR_ACCESS_LOST = unchecked((int)0x887A0026);
    private const uint D3D11_SDK_VERSION = 7;
    private const uint D3D_DRIVER_TYPE_UNKNOWN = 0;
    private const uint D3D_DRIVER_TYPE_HARDWARE = 1;
    private const uint D3D_DRIVER_TYPE_WARP = 5;
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    private const uint D3D11_USAGE_STAGING = 3;
    private const uint D3D11_CPU_ACCESS_READ = 0x20000;
    private const uint D3D11_MAP_READ = 1;

    [DllImport("dxgi.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_OUTPUT_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public RECT DesktopCoordinates;
        public bool AttachedToDesktop;
        public uint Rotation;
        public IntPtr Monitor;
    }

    [DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        uint driverType,
        IntPtr Software,
        uint Flags,
        [MarshalAs(UnmanagedType.LPArray)] uint[]? pFeatureLevels,
        uint FeatureLevels,
        uint SDKVersion,
        out IntPtr ppDevice,
        out uint pFeatureLevel,
        out IntPtr ppImmediateContext);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_MODE_DESC
    {
        public uint Width;
        public uint Height;
        public DXGI_RATIONAL RefreshRate;
        public uint Format;
        public uint ScanlineOrdering;
        public uint Scaling;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_OUTDUPL_DESC
    {
        public DXGI_MODE_DESC ModeDesc;
        public uint Rotation;
        public bool DesktopImageInSystemMemory;
        public RECT DesktopCoordinates;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_OUTDUPL_POINTER_POSITION
    {
        public POINT Position;
        public bool Visible;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_OUTDUPL_FRAME_INFO
    {
        public long LastPresentTime;
        public long LastMouseUpdateTime;
        public uint AccumulatedFrames;
        public bool RectsCoalesced;
        public bool ProtectedContentMaskedOut;
        public DXGI_OUTDUPL_POINTER_POSITION PointerPosition;
        public uint TotalMetadataBufferSize;
        public uint BufferSizeRequired;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_SAMPLE_DESC
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public DXGI_SAMPLE_DESC SampleDesc;
        public uint Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_MAPPED_SUBRESOURCE
    {
        public IntPtr pData;
        public uint RowPitch;
        public uint DepthPitch;
    }
    #endregion
}
