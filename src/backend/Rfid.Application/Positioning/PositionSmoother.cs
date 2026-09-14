using System.Collections.Concurrent;

namespace Rfid.Application.Positioning;

/// <summary>Per-axis constant-velocity Kalman filter (position, velocity) used to smooth successive position fixes.</summary>
public sealed class KalmanTrack
{
    private double _x, _vx, _y, _vy;
    private double[] _px = { 4, 0, 0, 1 }, _py = { 4, 0, 0, 1 }; // 2x2 covariances [p00,p01,p10,p11]
    public DateTime UpdatedAt { get; private set; }
    public double ProcessNoise { get; init; } = 0.15;  // acceleration spectral density; small = assets mostly stationary
    public (double x, double y) Position => (_x, _y);
    public (double vx, double vy) Velocity => (_vx, _vy);

    public KalmanTrack(double x, double y, DateTime at) { _x = x; _y = y; UpdatedAt = at; }

    public (double x, double y, double accuracy) Update(double mx, double my, double measurementAccuracyM, DateTime at)
    {
        var dt = Math.Clamp((at - UpdatedAt).TotalSeconds, 0, 60);
        if (at < UpdatedAt) dt = 0;
        var r = Math.Max(0.25, measurementAccuracyM * measurementAccuracyM);
        (_x, _vx, _px) = Step(_x, _vx, _px, mx, dt, r, ProcessNoise);
        (_y, _vy, _py) = Step(_y, _vy, _py, my, dt, r, ProcessNoise);
        UpdatedAt = at > UpdatedAt ? at : UpdatedAt;
        return (Math.Round(_x, 2), Math.Round(_y, 2), Math.Round(Math.Sqrt(Math.Max(_px[0], _py[0])), 2));
    }

    private static (double p, double v, double[] P) Step(double p, double v, double[] P, double z, double dt, double r, double q)
    {
        // Predict: x' = x + v·dt ; P' = F P Fᵀ + Q
        p += v * dt;
        var p00 = P[0] + dt * (P[2] + P[1]) + dt * dt * P[3] + q * dt * dt * dt / 3;
        var p01 = P[1] + dt * P[3] + q * dt * dt / 2;
        var p10 = P[2] + dt * P[3] + q * dt * dt / 2;
        var p11 = P[3] + q * dt;
        // Update with position measurement z (H = [1 0])
        var s = p00 + r; var k0 = p00 / s; var k1 = p10 / s; var innov = z - p;
        p += k0 * innov; v += k1 * innov;
        return (p, v, new[] { (1 - k0) * p00, (1 - k0) * p01, p10 - k1 * p00, p11 - k1 * p01 });
    }
}

public interface IPositionSmoother { (double x, double y, double accuracy) Smooth(Guid itemId, Guid zoneId, double x, double y, double accuracy, DateTime at); }

/// <summary>Keeps one Kalman track per item (resets when the item changes zone or has been unseen for a while).</summary>
public class PositionSmoother : IPositionSmoother
{
    private readonly ConcurrentDictionary<Guid, (Guid zone, KalmanTrack track)> _tracks = new();
    public TimeSpan ResetAfter { get; init; } = TimeSpan.FromMinutes(10);

    public (double x, double y, double accuracy) Smooth(Guid itemId, Guid zoneId, double x, double y, double accuracy, DateTime at)
    {
        if (_tracks.TryGetValue(itemId, out var t) && t.zone == zoneId && at - t.track.UpdatedAt < ResetAfter) return t.track.Update(x, y, accuracy, at);
        _tracks[itemId] = (zoneId, new KalmanTrack(x, y, at));
        return (Math.Round(x, 2), Math.Round(y, 2), Math.Round(accuracy, 2));
    }
}
