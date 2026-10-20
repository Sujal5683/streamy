using System;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Telemetry;
using EnterpriseRemote.Host.UserAgent.Capture;
using EnterpriseRemote.Host.UserAgent.Video;
using Xunit;

namespace EnterpriseRemote.UnitTests;

public class CaptureTests
{
    [Fact]
    public void SyntheticDesktopGenerator_InitializesAndCapturesValidFrames()
    {
        int width = 1280;
        int height = 720;
        using var generator = new SyntheticDesktopGenerator(width, height);
        
        bool initialized = generator.Initialize();
        Assert.True(initialized);
        Assert.True(generator.IsInitialized);
        Assert.Equal(width, generator.Display.Width);
        Assert.Equal(height, generator.Display.Height);

        var frame = generator.CaptureFrame();
        Assert.NotNull(frame);
        Assert.Equal(CaptureStatus.Success, frame.Status);
        Assert.Equal(width, frame.Width);
        Assert.Equal(height, frame.Height);
        Assert.Equal(width * 4, frame.Stride);
        Assert.Equal(CapturePixelFormat.Bgra32, frame.PixelFormat);
        Assert.NotNull(frame.Buffer);
        Assert.Equal(width * height * 4, frame.BufferLength);
        Assert.NotEmpty(frame.DirtyRects);
        Assert.NotNull(frame.Pointer);
        Assert.True(frame.Pointer.Visible);
    }

    [Fact]
    public void PixelConverter_BgraToNv12_ProducesCorrectDimensionsAndBoundedValues()
    {
        int width = 64;
        int height = 64;
        byte[] bgra = new byte[width * height * 4];

        // Fill with white (R=255, G=255, B=255, A=255)
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i + 0] = 255;
            bgra[i + 1] = 255;
            bgra[i + 2] = 255;
            bgra[i + 3] = 255;
        }

        byte[] nv12 = new byte[(int)(width * height * 1.5)];
        PixelConverter.BgraToNv12(bgra, width, height, nv12);

        // In standard Rec.601, pure white Y should be 235, U should be 128, V should be 128
        int yPlaneSize = width * height;
        for (int i = 0; i < yPlaneSize; i++)
        {
            Assert.True(nv12[i] >= 230 && nv12[i] <= 235, $"Y value {nv12[i]} out of expected white range");
        }

        for (int i = yPlaneSize; i < nv12.Length; i++)
        {
            Assert.True(nv12[i] >= 125 && nv12[i] <= 130, $"UV value {nv12[i]} out of expected white range");
        }
    }

    [Fact]
    public void LatencyTracker_CalculatesAccuratePercentiles()
    {
        var tracker = new LatencyTracker(100);
        // Add values 1.0 to 100.0
        for (int i = 1; i <= 100; i++)
        {
            tracker.Record(i);
        }

        var dist = tracker.ComputeDistribution();
        Assert.Equal(100, dist.SampleCount);
        Assert.Equal(1.0, dist.MinMs);
        Assert.Equal(100.0, dist.MaxMs);
        Assert.Equal(50.5, dist.MeanMs, 1);
        Assert.Equal(50.5, dist.MedianP50Ms, 1);
        Assert.Equal(90.1, dist.P90Ms, 1);
        Assert.Equal(95.05, dist.P95Ms, 1);
        Assert.Equal(99.01, dist.P99Ms, 1);
    }
}
