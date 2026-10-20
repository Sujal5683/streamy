using System;
using System.Collections.Generic;
using System.Linq;

namespace EnterpriseRemote.Common.Telemetry;

public sealed class LatencyDistribution
{
    public int SampleCount { get; init; }
    public double MinMs { get; init; }
    public double MaxMs { get; init; }
    public double MeanMs { get; init; }
    public double MedianP50Ms { get; init; }
    public double P90Ms { get; init; }
    public double P95Ms { get; init; }
    public double P99Ms { get; init; }
}

public sealed class LatencyTracker
{
    private readonly List<double> _samples;
    private readonly object _lock = new();
    private readonly int _maxSamples;

    public LatencyTracker(int maxSamples = 10000)
    {
        _maxSamples = maxSamples;
        _samples = new List<double>(Math.Min(maxSamples, 1000));
    }

    public void Record(double milliseconds)
    {
        lock (_lock)
        {
            if (_samples.Count >= _maxSamples)
            {
                _samples.RemoveAt(0);
            }
            _samples.Add(milliseconds);
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _samples.Clear();
        }
    }

    public LatencyDistribution ComputeDistribution()
    {
        double[] sorted;
        lock (_lock)
        {
            if (_samples.Count == 0)
            {
                return new LatencyDistribution();
            }
            sorted = _samples.ToArray();
        }

        Array.Sort(sorted);

        return new LatencyDistribution
        {
            SampleCount = sorted.Length,
            MinMs = sorted[0],
            MaxMs = sorted[^1],
            MeanMs = sorted.Average(),
            MedianP50Ms = GetPercentile(sorted, 0.50),
            P90Ms = GetPercentile(sorted, 0.90),
            P95Ms = GetPercentile(sorted, 0.95),
            P99Ms = GetPercentile(sorted, 0.99)
        };
    }

    private static double GetPercentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 1) return sorted[0];
        double rank = percentile * (sorted.Length - 1);
        int low = (int)Math.Floor(rank);
        int high = (int)Math.Ceiling(rank);
        if (low == high) return sorted[low];
        double weight = rank - low;
        return sorted[low] * (1.0 - weight) + sorted[high] * weight;
    }
}
