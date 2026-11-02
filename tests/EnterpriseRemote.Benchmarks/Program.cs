using System;
using System.Diagnostics;
using EnterpriseRemote.Common.Models;
using EnterpriseRemote.Common.Telemetry;
using EnterpriseRemote.Host.UserAgent.Capture;
using EnterpriseRemote.Host.UserAgent.Video;

namespace EnterpriseRemote.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("===============================================================================");
        Console.WriteLine("   Enterprise Windows Remote Access Platform: Native Capture Benchmarks");
        Console.WriteLine("===============================================================================\n");

        RunSyntheticBenchmark(1920, 1080, "1080p Full HD", 500);
        RunSyntheticBenchmark(3840, 2160, "4K UHD", 200);
        RunLiveCaptureBenchmark(120);

        Console.WriteLine("\n[+] All capture benchmarks completed successfully.");
    }

    private static void RunSyntheticBenchmark(int width, int height, string label, int frameCount)
    {
        Console.WriteLine($"--- Running Benchmark: Synthetic Engine ({label}: {width}x{height}, {frameCount} frames) ---");

        using var generator = new SyntheticDesktopGenerator(width, height);
        generator.Initialize();

        var captureTracker = new LatencyTracker(frameCount);
        var nv12Tracker = new LatencyTracker(frameCount);
        byte[] nv12Buffer = new byte[(int)(width * height * 1.5)];

        long totalDirtyPixels = 0;
        long totalPixels = (long)width * height * frameCount;

        var totalSw = Stopwatch.StartNew();

        for (int i = 0; i < frameCount; i++)
        {
            var frame = generator.CaptureFrame(0);
            captureTracker.Record(frame.CaptureDurationMilliseconds);

            if (frame.DirtyRects.Count > 0)
            {
                foreach (var r in frame.DirtyRects)
                {
                    totalDirtyPixels += (long)r.Width * r.Height;
                }
            }

            if (frame.Buffer != null)
            {
                var convSw = Stopwatch.StartNew();
                PixelConverter.BgraToNv12(frame.Buffer, width, height, nv12Buffer);
                convSw.Stop();
                nv12Tracker.Record(convSw.Elapsed.TotalMilliseconds);
            }
        }

        totalSw.Stop();
        double fps = frameCount / totalSw.Elapsed.TotalSeconds;
        double dirtyRatio = (double)totalDirtyPixels / totalPixels * 100.0;
        double bandwidthSaving = 100.0 - dirtyRatio;

        var capDist = captureTracker.ComputeDistribution();
        var convDist = nv12Tracker.ComputeDistribution();

        PrintLatencyTable(label, frameCount, fps, capDist, convDist, bandwidthSaving);
    }

    private static void RunLiveCaptureBenchmark(int frameCount)
    {
        Console.WriteLine($"\n--- Running Benchmark: Live Desktop Duplication / Fallback ({frameCount} frames) ---");

        using var capturer = new DxgiDesktopDuplicator();
        bool init = capturer.Initialize();

        if (!init)
        {
            Console.WriteLine("[!] Live display capturer could not be initialized in current environment (headless/no display). Skipping live benchmark.");
            return;
        }

        string engineType = capturer.IsUsingFallback ? "GDI BitBlt Fallback" : "DXGI Desktop Duplication";
        Console.WriteLine($"[+] Engine Active: {engineType} | Display: {capturer.Display.Width}x{capturer.Display.Height}");

        var captureTracker = new LatencyTracker(frameCount);
        var convTracker = new LatencyTracker(frameCount);
        byte[] nv12Buffer = new byte[(int)(capturer.Display.Width * capturer.Display.Height * 1.5)];

        var totalSw = Stopwatch.StartNew();
        int capturedSuccess = 0;

        for (int i = 0; i < frameCount; i++)
        {
            var frame = capturer.CaptureFrame(33); // 30-33ms timeout
            if (frame.Status == CaptureStatus.Success)
            {
                capturedSuccess++;
                captureTracker.Record(frame.CaptureDurationMilliseconds);

                if (frame.Buffer != null)
                {
                    var convSw = Stopwatch.StartNew();
                    PixelConverter.BgraToNv12(frame.Buffer, frame.Width, frame.Height, nv12Buffer);
                    convSw.Stop();
                    convTracker.Record(convSw.Elapsed.TotalMilliseconds);
                }
            }
            else if (frame.Status == CaptureStatus.Timeout)
            {
                // Timeout is normal when screen content is static
            }
        }

        totalSw.Stop();

        if (capturedSuccess > 0)
        {
            double fps = capturedSuccess / totalSw.Elapsed.TotalSeconds;
            var capDist = captureTracker.ComputeDistribution();
            var convDist = convTracker.ComputeDistribution();
            PrintLatencyTable($"{engineType} Live", capturedSuccess, fps, capDist, convDist, 0.0);
        }
        else
        {
            Console.WriteLine("[!] No frames captured (static display).");
        }
    }

    private static void PrintLatencyTable(string label, int frames, double fps, LatencyDistribution cap, LatencyDistribution conv, double bandwidthSaving)
    {
        Console.WriteLine($"\nPerformance Metrics for [{label}] ({frames} frames, {fps:F1} FPS):");
        Console.WriteLine("+-------------------------+----------+----------+----------+----------+----------+");
        Console.WriteLine("| Stage                   | Mean     | Median   | p90      | p95      | p99      |");
        Console.WriteLine("+-------------------------+----------+----------+----------+----------+----------+");
        Console.WriteLine($"| Capture Latency         | {cap.MeanMs,6:F2} ms| {cap.MedianP50Ms,6:F2} ms| {cap.P90Ms,6:F2} ms| {cap.P95Ms,6:F2} ms| {cap.P99Ms,6:F2} ms|");
        Console.WriteLine($"| BGRA->NV12 Conversion   | {conv.MeanMs,6:F2} ms| {conv.MedianP50Ms,6:F2} ms| {conv.P90Ms,6:F2} ms| {conv.P95Ms,6:F2} ms| {conv.P99Ms,6:F2} ms|");
        Console.WriteLine($"| Total Frame Pipeline    | {cap.MeanMs + conv.MeanMs,6:F2} ms| {cap.MedianP50Ms + conv.MedianP50Ms,6:F2} ms| {cap.P90Ms + conv.P90Ms,6:F2} ms| {cap.P95Ms + conv.P95Ms,6:F2} ms| {cap.P99Ms + conv.P99Ms,6:F2} ms|");
        Console.WriteLine("+-------------------------+----------+----------+----------+----------+----------+");
        if (bandwidthSaving > 0)
        {
            Console.WriteLine($"[*] Dirty-Rect Delta Efficiency: {bandwidthSaving:F1}% bandwidth savings vs full-frame updates.\n");
        }
        else
        {
            Console.WriteLine();
        }
    }
}
