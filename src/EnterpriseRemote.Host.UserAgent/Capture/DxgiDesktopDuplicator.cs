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

    public DisplayInfo Display { get; private set; } = new();
    public bool IsInitialized { get; private set; }
    public bool IsUsingFallback { get; private set; }

    public DxgiDesktopDuplicator(IScreenCapturer? fallbackCapturer = null)
    {
        _fallbackCapturer = fallbackCapturer ?? new GdiDesktopCapturer();
    }

    public bool Initialize()
    {
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

        // Seamless fallback to GDI Desktop Capturer if DXGI is unavailable (e.g. Session 0 or VM)
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

    private unsafe bool TryInitializeDxgi()
    {
        // 1. Create D3D11 Device (Try Hardware first, then WARP)
        int hr = D3D11CreateDevice(
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

            if (hr != S_OK || _d3dDevice == IntPtr.Zero)
            {
                return false;
            }
        }

        IntPtr pDxgiDevice = IntPtr.Zero;
        IntPtr pDxgiAdapter = IntPtr.Zero;
        IntPtr pDxgiOutput = IntPtr.Zero;
        IntPtr pDxgiOutput1 = IntPtr.Zero;

        try
        {
            Guid iidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            hr = Marshal.QueryInterface(_d3dDevice, ref iidDxgiDevice, out pDxgiDevice);
            if (hr != S_OK || pDxgiDevice == IntPtr.Zero) return false;

            // Direct vtable call: IDXGIDevice::GetAdapter (Slot 7)
            IntPtr* vtblDxgiDevice = *(IntPtr**)pDxgiDevice;
            var getAdapter = (delegate* unmanaged[Stdcall]<IntPtr, out IntPtr, int>)vtblDxgiDevice[7];
            hr = getAdapter(pDxgiDevice, out pDxgiAdapter);
            if (hr != S_OK || pDxgiAdapter == IntPtr.Zero) return false;

            // Direct vtable call: IDXGIAdapter::EnumOutputs (Slot 7)
            IntPtr* vtblDxgiAdapter = *(IntPtr**)pDxgiAdapter;
            var enumOutputs = (delegate* unmanaged[Stdcall]<IntPtr, uint, out IntPtr, int>)vtblDxgiAdapter[7];
            hr = enumOutputs(pDxgiAdapter, 0, out pDxgiOutput);
            if (hr != S_OK || pDxgiOutput == IntPtr.Zero) return false;

            // Query IDXGIOutput1
            Guid iidDxgiOutput1 = new("00cddea8-939b-4b83-a340-b1e03764a307");
            hr = Marshal.QueryInterface(pDxgiOutput, ref iidDxgiOutput1, out pDxgiOutput1);
            if (hr != S_OK || pDxgiOutput1 == IntPtr.Zero) return false;

            // Direct vtable call: IDXGIOutput1::DuplicateOutput (Slot 22)
            IntPtr* vtblDxgiOutput1 = *(IntPtr**)pDxgiOutput1;
            var duplicateOutput = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, out IntPtr, int>)vtblDxgiOutput1[22];
            hr = duplicateOutput(pDxgiOutput1, _d3dDevice, out _dxgiOutputDuplication);
            if (hr != S_OK || _dxgiOutputDuplication == IntPtr.Zero) return false;

            // Query DXGI_OUTDUPL_DESC (Slot 7 of IDXGIOutputDuplication)
            IntPtr* vtblDupl = *(IntPtr**)_dxgiOutputDuplication;
            var getDesc = (delegate* unmanaged[Stdcall]<IntPtr, out DXGI_OUTDUPL_DESC, void>)vtblDupl[7];
            getDesc(_dxgiOutputDuplication, out DXGI_OUTDUPL_DESC desc);

            _width = desc.ModeDesc.Width > 0 ? (int)desc.ModeDesc.Width : 1920;
            _height = desc.ModeDesc.Height > 0 ? (int)desc.ModeDesc.Height : 1080;
            _frameBuffer = new byte[_width * _height * 4];

            Display = new DisplayInfo
            {
                Index = 0,
                DeviceName = @"\\.\DISPLAY1 (DXGI Output Duplication)",
                Left = desc.DesktopCoordinates.Left,
                Top = desc.DesktopCoordinates.Top,
                Width = _width,
                Height = _height,
                RefreshRateNumerator = (int)desc.ModeDesc.RefreshRate.Numerator,
                RefreshRateDenominator = (int)desc.ModeDesc.RefreshRate.Denominator,
                IsPrimary = true
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
            if (pDxgiOutput != IntPtr.Zero) Marshal.Release(pDxgiOutput);
            if (pDxgiAdapter != IntPtr.Zero) Marshal.Release(pDxgiAdapter);
            if (pDxgiDevice != IntPtr.Zero) Marshal.Release(pDxgiDevice);
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
            return _fallbackCapturer.CaptureFrame(timeoutMilliseconds);
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
                return new CapturedFrame { Status = CaptureStatus.AccessLost };
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

    public void Dispose()
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

        _fallbackCapturer.Dispose();
        IsInitialized = false;
    }

    #region Win32 / DXGI Structs
    private const int S_OK = 0;
    private const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
    private const int DXGI_ERROR_ACCESS_LOST = unchecked((int)0x887A0026);
    private const uint D3D11_SDK_VERSION = 7;
    private const uint D3D_DRIVER_TYPE_HARDWARE = 1;
    private const uint D3D_DRIVER_TYPE_WARP = 5;
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    private const uint D3D11_USAGE_STAGING = 3;
    private const uint D3D11_CPU_ACCESS_READ = 0x20000;
    private const uint D3D11_MAP_READ = 1;

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
