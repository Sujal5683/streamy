using System;
using System.Collections.Generic;
using System.Diagnostics;
using EnterpriseRemote.Common.Models;

namespace EnterpriseRemote.Host.UserAgent.Capture;

public sealed class SyntheticDesktopGenerator : IScreenCapturer
{
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _frameBuffer;
    private readonly byte[] _prevBuffer;
    private readonly Stopwatch _stopwatch = new();
    private int _frameCount;
    private int _ballX = 100;
    private int _ballY = 100;
    private int _ballVx = 6;
    private int _ballVy = 4;
    private const int BallSize = 64;

    public DisplayInfo Display { get; }
    public bool IsInitialized { get; private set; }

    public SyntheticDesktopGenerator(int width = 1920, int height = 1080)
    {
        _width = width;
        _height = height;
        int bufferSize = width * height * 4;
        _frameBuffer = new byte[bufferSize];
        _prevBuffer = new byte[bufferSize];

        Display = new DisplayInfo
        {
            Index = 0,
            DeviceName = @"\\.\DISPLAY1 (Synthetic)",
            Left = 0,
            Top = 0,
            Width = width,
            Height = height,
            RefreshRateNumerator = 60,
            RefreshRateDenominator = 1,
            IsPrimary = true
        };
    }

    public bool Initialize()
    {
        _stopwatch.Restart();
        // Initialize static desktop background (dark slate gray gradient with grid)
        for (int y = 0; y < _height; y++)
        {
            byte bgB = (byte)(30 + (y * 20 / _height));
            byte bgG = (byte)(35 + (y * 15 / _height));
            byte bgR = (byte)(45 + (y * 10 / _height));

            int rowOffset = y * _width * 4;
            for (int x = 0; x < _width; x++)
            {
                int pixelOffset = rowOffset + (x * 4);
                bool isGrid = (x % 100 == 0) || (y % 100 == 0);
                _frameBuffer[pixelOffset + 0] = isGrid ? (byte)(bgB + 25) : bgB; // Blue
                _frameBuffer[pixelOffset + 1] = isGrid ? (byte)(bgG + 25) : bgG; // Green
                _frameBuffer[pixelOffset + 2] = isGrid ? (byte)(bgR + 25) : bgR; // Red
                _frameBuffer[pixelOffset + 3] = 255; // Alpha
            }
        }
        Array.Copy(_frameBuffer, _prevBuffer, _frameBuffer.Length);
        IsInitialized = true;
        return true;
    }

    public CapturedFrame CaptureFrame(int timeoutMilliseconds = 50)
    {
        if (!IsInitialized)
        {
            return new CapturedFrame { Status = CaptureStatus.NotInitialized };
        }

        var sw = Stopwatch.StartNew();
        _frameCount++;

        // Calculate previous ball rect to clear
        int oldBallX = _ballX;
        int oldBallY = _ballY;

        // Update ball position
        _ballX += _ballVx;
        _ballY += _ballVy;
        if (_ballX <= 0 || _ballX + BallSize >= _width) _ballVx = -_ballVx;
        if (_ballY <= 0 || _ballY + BallSize >= _height) _ballVy = -_ballVy;

        // Restore background for old ball rect
        for (int y = Math.Max(0, oldBallY); y < Math.Min(_height, oldBallY + BallSize); y++)
        {
            int rowOffset = y * _width * 4;
            int startX = Math.Max(0, oldBallX);
            int count = (Math.Min(_width, oldBallX + BallSize) - startX) * 4;
            if (count > 0)
            {
                Buffer.BlockCopy(_prevBuffer, rowOffset + (startX * 4), _frameBuffer, rowOffset + (startX * 4), count);
            }
        }

        // Draw new ball (vibrant teal circle/rect)
        byte ballB = 220;
        byte ballG = 180;
        byte ballR = 20;
        for (int y = Math.Max(0, _ballY); y < Math.Min(_height, _ballY + BallSize); y++)
        {
            int rowOffset = y * _width * 4;
            for (int x = Math.Max(0, _ballX); x < Math.Min(_width, _ballX + BallSize); x++)
            {
                int pixelOffset = rowOffset + (x * 4);
                _frameBuffer[pixelOffset + 0] = ballB;
                _frameBuffer[pixelOffset + 1] = ballG;
                _frameBuffer[pixelOffset + 2] = ballR;
                _frameBuffer[pixelOffset + 3] = 255;
            }
        }

        // Compute dirty rect enclosing both old and new ball positions
        int dirtyLeft = Math.Max(0, Math.Min(oldBallX, _ballX));
        int dirtyTop = Math.Max(0, Math.Min(oldBallY, _ballY));
        int dirtyRight = Math.Min(_width, Math.Max(oldBallX + BallSize, _ballX + BallSize));
        int dirtyBottom = Math.Min(_height, Math.Max(oldBallY + BallSize, _ballY + BallSize));

        var dirtyRects = new List<DirtyRect>
        {
            new(dirtyLeft, dirtyTop, dirtyRight, dirtyBottom)
        };

        // Output buffer copy
        byte[] frameCopy = new byte[_frameBuffer.Length];
        Buffer.BlockCopy(_frameBuffer, 0, frameCopy, 0, _frameBuffer.Length);

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
                Visible = true,
                X = _ballX + (BallSize / 2),
                Y = _ballY + (BallSize / 2),
                ShapeType = PointerShapeType.Color
            },
            CaptureDurationTicks = sw.ElapsedTicks,
            TimestampUtcTicks = DateTime.UtcNow.Ticks
        };
    }

    public void Dispose()
    {
        IsInitialized = false;
    }
}
