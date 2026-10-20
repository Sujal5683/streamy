using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using EnterpriseRemote.Common.Models;

namespace EnterpriseRemote.Host.UserAgent.Capture;

public sealed class GdiDesktopCapturer : IScreenCapturer
{
    private IntPtr _hDesktopDc = IntPtr.Zero;
    private IntPtr _hMemoryDc = IntPtr.Zero;
    private IntPtr _hBitmap = IntPtr.Zero;
    private IntPtr _hOldBitmap = IntPtr.Zero;
    private IntPtr _pBits = IntPtr.Zero;
    private byte[]? _buffer;
    private int _width;
    private int _height;

    public DisplayInfo Display { get; private set; } = new();
    public bool IsInitialized { get; private set; }

    public bool Initialize()
    {
        try
        {
            _width = GetSystemMetrics(SM_CXSCREEN);
            _height = GetSystemMetrics(SM_CYSCREEN);

            if (_width <= 0 || _height <= 0)
            {
                _width = 1920;
                _height = 1080;
            }

            Display = new DisplayInfo
            {
                Index = 0,
                DeviceName = @"\\.\DISPLAY1 (GDI Primary)",
                Left = 0,
                Top = 0,
                Width = _width,
                Height = _height,
                IsPrimary = true
            };

            _hDesktopDc = GetDC(IntPtr.Zero);
            if (_hDesktopDc == IntPtr.Zero) return false;

            _hMemoryDc = CreateCompatibleDC(_hDesktopDc);
            if (_hMemoryDc == IntPtr.Zero) return false;

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = _width;
            bmi.bmiHeader.biHeight = -_height; // Top-down DIB
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = BI_RGB;

            _hBitmap = CreateDIBSection(_hDesktopDc, ref bmi, DIB_RGB_COLORS, out _pBits, IntPtr.Zero, 0);
            if (_hBitmap == IntPtr.Zero || _pBits == IntPtr.Zero) return false;

            _hOldBitmap = SelectObject(_hMemoryDc, _hBitmap);
            _buffer = new byte[_width * _height * 4];

            IsInitialized = true;
            return true;
        }
        catch
        {
            Dispose();
            return false;
        }
    }

    public CapturedFrame CaptureFrame(int timeoutMilliseconds = 50)
    {
        if (!IsInitialized || _hDesktopDc == IntPtr.Zero || _hMemoryDc == IntPtr.Zero || _pBits == IntPtr.Zero || _buffer == null)
        {
            return new CapturedFrame { Status = CaptureStatus.NotInitialized };
        }

        var sw = Stopwatch.StartNew();

        // Perform GDI BitBlt
        bool success = BitBlt(_hMemoryDc, 0, 0, _width, _height, _hDesktopDc, 0, 0, SRCCOPY | CAPTUREBLT);
        if (!success)
        {
            return new CapturedFrame { Status = CaptureStatus.Error };
        }

        // Copy DIBSection bits to managed buffer
        Marshal.Copy(_pBits, _buffer, 0, _buffer.Length);

        // Capture cursor position
        var cursorInfo = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        var pointer = new PointerInfo { Visible = false };
        if (GetCursorInfo(ref cursorInfo) && (cursorInfo.flags & CURSOR_SHOWING) != 0)
        {
            pointer.Visible = true;
            pointer.X = cursorInfo.ptScreenPos.x;
            pointer.Y = cursorInfo.ptScreenPos.y;
            pointer.ShapeType = PointerShapeType.Color;
        }

        sw.Stop();

        byte[] frameCopy = new byte[_buffer.Length];
        Buffer.BlockCopy(_buffer, 0, frameCopy, 0, _buffer.Length);

        return new CapturedFrame
        {
            Status = CaptureStatus.Success,
            Width = _width,
            Height = _height,
            Stride = _width * 4,
            PixelFormat = CapturePixelFormat.Bgra32,
            Buffer = frameCopy,
            BufferLength = frameCopy.Length,
            DirtyRects = new List<DirtyRect> { new(0, 0, _width, _height) },
            Pointer = pointer,
            CaptureDurationTicks = sw.ElapsedTicks,
            TimestampUtcTicks = DateTime.UtcNow.Ticks
        };
    }

    public void Dispose()
    {
        if (_hMemoryDc != IntPtr.Zero && _hOldBitmap != IntPtr.Zero)
        {
            SelectObject(_hMemoryDc, _hOldBitmap);
            _hOldBitmap = IntPtr.Zero;
        }

        if (_hBitmap != IntPtr.Zero)
        {
            DeleteObject(_hBitmap);
            _hBitmap = IntPtr.Zero;
        }

        if (_hMemoryDc != IntPtr.Zero)
        {
            DeleteDC(_hMemoryDc);
            _hMemoryDc = IntPtr.Zero;
        }

        if (_hDesktopDc != IntPtr.Zero)
        {
            ReleaseDC(IntPtr.Zero, _hDesktopDc);
            _hDesktopDc = IntPtr.Zero;
        }

        IsInitialized = false;
    }

    #region Win32 P/Invoke
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;
    private const int DIB_RGB_COLORS = 0;
    private const int BI_RGB = 0;
    private const int CURSOR_SHOWING = 0x00000001;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, [In] ref BITMAPINFO pbmi, uint pila, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CURSORINFO pci);

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
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }
    #endregion
}
