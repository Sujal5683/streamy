using System;
using System.Drawing;
using System.Runtime.InteropServices;
using EnterpriseRemote.Common.Models;

namespace EnterpriseRemote.Host.UserAgent.Capture;

/// <summary>
/// Captures hardware-accelerated DirectComposition and desktop windows using PrintWindow (PW_RENDERFULLCONTENT).
/// Can composite unmasked window pixels over blacked-out regions on the desktop frame.
/// </summary>
public sealed class DirectCompositionWindowCapturer : IDisposable
{
    private const uint PW_CLIENTONLY = 0x00000001;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    public sealed class WindowCaptureResult
    {
        public bool Success { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public Rectangle Bounds { get; set; }
        public byte[]? Buffer { get; set; }
    }

    /// <summary>
    /// Captures the specified window using DirectComposition full content rendering.
    /// </summary>
    public WindowCaptureResult CaptureWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd) || !IsWindowVisible(hWnd))
        {
            return new WindowCaptureResult { Success = false };
        }

        if (!GetWindowRect(hWnd, out RECT rect))
        {
            return new WindowCaptureResult { Success = false };
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        if (width <= 0 || height <= 0 || width > 7680 || height > 4320)
        {
            return new WindowCaptureResult { Success = false };
        }

        IntPtr hWindowDc = GetWindowDC(hWnd);
        if (hWindowDc == IntPtr.Zero)
        {
            return new WindowCaptureResult { Success = false };
        }

        IntPtr hMemDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hOldBitmap = IntPtr.Zero;
        IntPtr pBits = IntPtr.Zero;

        try
        {
            hMemDc = CreateCompatibleDC(hWindowDc);
            if (hMemDc == IntPtr.Zero) return new WindowCaptureResult { Success = false };

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = width;
            bmi.bmiHeader.biHeight = -height; // Top-down
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0; // BI_RGB

            hBitmap = CreateDIBSection(hMemDc, ref bmi, 0, out pBits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero || pBits == IntPtr.Zero)
            {
                return new WindowCaptureResult { Success = false };
            }

            hOldBitmap = SelectObject(hMemDc, hBitmap);

            byte[] buffer = new byte[width * height * 4];
            bool captured = false;

            // Tier 1: PW_RENDERFULLCONTENT (DirectComposition visual tree extraction)
            if (PrintWindow(hWnd, hMemDc, PW_RENDERFULLCONTENT))
            {
                Marshal.Copy(pBits, buffer, 0, buffer.Length);
                if (HasNonZeroPixels(buffer)) captured = true;
            }

            // Tier 2: Direct BitBlt from Window DC (handles GDI / DirectX presented buffers)
            if (!captured)
            {
                if (BitBlt(hMemDc, 0, 0, width, height, hWindowDc, 0, 0, SRCCOPY | CAPTUREBLT))
                {
                    Marshal.Copy(pBits, buffer, 0, buffer.Length);
                    if (HasNonZeroPixels(buffer)) captured = true;
                }
            }

            // Tier 3: Standard PrintWindow
            if (!captured && PrintWindow(hWnd, hMemDc, 0))
            {
                Marshal.Copy(pBits, buffer, 0, buffer.Length);
                if (HasNonZeroPixels(buffer)) captured = true;
            }

            // Tier 4: Desktop DC BitBlt (captures active screen rect directly through DWM composition)
            if (!captured)
            {
                IntPtr hDesktopDc = GetDC(IntPtr.Zero);
                if (hDesktopDc != IntPtr.Zero)
                {
                    try
                    {
                        if (BitBlt(hMemDc, 0, 0, width, height, hDesktopDc, rect.Left, rect.Top, SRCCOPY | CAPTUREBLT))
                        {
                            Marshal.Copy(pBits, buffer, 0, buffer.Length);
                            if (HasNonZeroPixels(buffer)) captured = true;
                        }
                    }
                    finally
                    {
                        ReleaseDC(IntPtr.Zero, hDesktopDc);
                    }
                }
            }

            if (!captured)
            {
                return new WindowCaptureResult { Success = false };
            }

            return new WindowCaptureResult
            {
                Success = true,
                Width = width,
                Height = height,
                Bounds = new Rectangle(rect.Left, rect.Top, width, height),
                Buffer = buffer
            };
        }
        catch
        {
            return new WindowCaptureResult { Success = false };
        }
        finally
        {
            if (hMemDc != IntPtr.Zero && hOldBitmap != IntPtr.Zero) SelectObject(hMemDc, hOldBitmap);
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (hMemDc != IntPtr.Zero) DeleteDC(hMemDc);
            if (hWindowDc != IntPtr.Zero) ReleaseDC(hWnd, hWindowDc);
        }
    }

    public static bool HasNonZeroPixels(byte[] buffer)
    {
        if (buffer == null || buffer.Length == 0) return false;
        int step = Math.Max(4, buffer.Length / 512);
        for (int i = 0; i < buffer.Length - 3; i += step)
        {
            if (buffer[i] != 0 || buffer[i + 1] != 0 || buffer[i + 2] != 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Composites a captured window directly onto a full desktop frame buffer.
    /// Never composites all-black rectangles.
    /// </summary>
    public bool CompositeWindowOntoFrame(byte[] frameBuffer, int frameWidth, int frameHeight, IntPtr hWnd)
    {
        var result = CaptureWindow(hWnd);
        if (!result.Success || result.Buffer == null || result.Width <= 0 || result.Height <= 0 || !HasNonZeroPixels(result.Buffer))
        {
            return false;
        }

        int winLeft = result.Bounds.Left;
        int winTop = result.Bounds.Top;
        int winWidth = result.Width;
        int winHeight = result.Height;

        int startX = Math.Max(0, winLeft);
        int startY = Math.Max(0, winTop);
        int endX = Math.Min(frameWidth, winLeft + winWidth);
        int endY = Math.Min(frameHeight, winTop + winHeight);

        if (startX >= endX || startY >= endY) return false;

        int copyWidthPixels = endX - startX;
        int bytesPerRow = copyWidthPixels * 4;

        for (int y = startY; y < endY; y++)
        {
            int srcY = y - winTop;
            int srcOffset = (srcY * winWidth + (startX - winLeft)) * 4;
            int dstOffset = (y * frameWidth + startX) * 4;

            if (srcOffset + bytesPerRow <= result.Buffer.Length && dstOffset + bytesPerRow <= frameBuffer.Length)
            {
                Buffer.BlockCopy(result.Buffer, srcOffset, frameBuffer, dstOffset, bytesPerRow);
            }
        }

        return true;
    }

    public void Dispose()
    {
    }

    #region Win32 P/Invoke
    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, [In] ref BITMAPINFO pbmi, uint pila, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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
