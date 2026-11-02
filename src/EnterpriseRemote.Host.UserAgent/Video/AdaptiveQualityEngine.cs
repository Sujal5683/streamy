using System;

namespace EnterpriseRemote.Host.UserAgent.Video;

public enum QualityProfile
{
    UltraHighFramerate,
    UltraLowLatency,
    Balanced,
    HighDefinitionText,
    ConstrainedBandwidth
}

public readonly record struct VideoEncodingParameters(
    int TargetFps,
    int TargetBitrateKbps,
    int FrameIntervalMs,
    QualityProfile Profile);

public sealed class AdaptiveQualityEngine
{
    private float _smoothedRttMs = 20.0f;
    private float _smoothedLossPercent = 0.0f;
    private const float Alpha = 0.2f; // Exponential moving average smoothing factor

    public VideoEncodingParameters CurrentParameters { get; private set; }

    public AdaptiveQualityEngine()
    {
        CurrentParameters = new VideoEncodingParameters(
            TargetFps: 60,
            TargetBitrateKbps: 6000,
            FrameIntervalMs: 16,
            Profile: QualityProfile.UltraLowLatency);
    }

    public VideoEncodingParameters UpdateNetworkMetrics(float sampleRttMs, float sampleLossPercent)
    {
        // Exponential moving average filter
        _smoothedRttMs = (_smoothedRttMs * (1.0f - Alpha)) + (sampleRttMs * Alpha);
        _smoothedLossPercent = (_smoothedLossPercent * (1.0f - Alpha)) + (sampleLossPercent * Alpha);

        int targetFps;
        int targetBitrate;
        QualityProfile profile;

        if (_smoothedLossPercent > 8.0f || _smoothedRttMs > 200.0f)
        {
            // Severe congestion
            targetFps = 15;
            targetBitrate = 1200;
            profile = QualityProfile.ConstrainedBandwidth;
        }
        else if (_smoothedLossPercent > 3.0f || _smoothedRttMs > 100.0f)
        {
            // Moderate congestion
            targetFps = 30;
            targetBitrate = 3000;
            profile = QualityProfile.Balanced;
        }
        else if (_smoothedRttMs <= 50.0f && _smoothedLossPercent <= 1.0f)
        {
            // Ideal LAN or low-latency WAN
            targetFps = 60;
            targetBitrate = 8000;
            profile = QualityProfile.UltraLowLatency;
        }
        else
        {
            // Normal broadband
            targetFps = 45;
            targetBitrate = 5000;
            profile = QualityProfile.Balanced;
        }

        int intervalMs = Math.Max(10, 1000 / targetFps);
        CurrentParameters = new VideoEncodingParameters(targetFps, targetBitrate, intervalMs, profile);
        return CurrentParameters;
    }

    /// <summary>
    /// Calculates dynamic JPEG quality based on real-time socket transmission duration to prevent Cloudflare bufferbloat.
    /// </summary>
    public int CalculateDynamicQuality(int targetQualityCeiling, long lastSendDurationMs, int frameIntervalMs)
    {
        if (lastSendDurationMs <= 0) return targetQualityCeiling;

        // If send took longer than the frame interval, the socket is buffering. Drop quality to shrink payload immediately.
        if (lastSendDurationMs > frameIntervalMs * 1.5f)
        {
            // Heavy network congestion or slow uplink: drop quality down to 25% minimum
            int penalty = (int)((lastSendDurationMs - frameIntervalMs) / 3);
            return Math.Max(25, targetQualityCeiling - penalty);
        }
        else if (lastSendDurationMs > frameIntervalMs)
        {
            // Mild congestion: throttle back 10-15%
            return Math.Max(30, targetQualityCeiling - 10);
        }

        // Network is clean, use full ceiling
        return targetQualityCeiling;
    }
}
