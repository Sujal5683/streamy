using System;
using System.Collections.Generic;

namespace EnterpriseRemote.Common.Models;

public enum CapturePixelFormat
{
    Bgra32,
    Rgba32,
    Nv12,
    I420
}

public enum PointerShapeType
{
    None = 0,
    Monochrome = 1,
    Color = 2,
    MaskedColor = 4
}

public readonly record struct DirtyRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);
    public int Area => Width * Height;
}

public readonly record struct MovedRect(int SourceX, int SourceY, int DestX, int DestY, int Width, int Height);

public sealed class PointerInfo
{
    public bool Visible { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public PointerShapeType ShapeType { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Pitch { get; set; }
    public byte[]? ShapeData { get; set; }
    public uint ShapeId { get; set; }
}

public sealed class DisplayInfo
{
    public int Index { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshRateNumerator { get; set; } = 60;
    public int RefreshRateDenominator { get; set; } = 1;
    public bool IsPrimary { get; set; }
    public int RotationDegrees { get; set; }
}

public enum CaptureStatus
{
    Success,
    Timeout,
    AccessLost,
    DisplayChanged,
    DeviceReset,
    NotInitialized,
    Error
}

public sealed class CapturedFrame : IDisposable
{
    public CaptureStatus Status { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Stride { get; set; }
    public CapturePixelFormat PixelFormat { get; set; }
    public byte[]? Buffer { get; set; }
    public int BufferLength { get; set; }
    public IReadOnlyList<DirtyRect> DirtyRects { get; set; } = Array.Empty<DirtyRect>();
    public IReadOnlyList<MovedRect> MovedRects { get; set; } = Array.Empty<MovedRect>();
    public PointerInfo? Pointer { get; set; }
    public long CaptureDurationTicks { get; set; }
    public double CaptureDurationMilliseconds => (double)CaptureDurationTicks / TimeSpan.TicksPerMillisecond;
    public long TimestampUtcTicks { get; set; }
    public bool HasChangedContent => DirtyRects.Count > 0 || MovedRects.Count > 0;
    public bool ProtectedContentMaskedOut { get; set; }

    public void Dispose()
    {
        // Buffer can be returned to ArrayPool if pooled
    }
}
