namespace Rfid.Application.Positioning;

public record Anchor(double X, double Y, double Distance, double Weight = 1);
public record Estimate(double X, double Y, double AccuracyM, int Anchors);

/// <summary>
/// RSSI-based 2-D positioning. Distances come from the log-distance path-loss model; the position is the
/// non-linear least-squares solution (Gauss-Newton from a weighted centroid). With one anchor the tag is
/// placed at the anchor, with two on the line between them proportionally to the distances.
/// </summary>
public static class Trilateration
{
    public static double RssiToDistance(double rssi, double rssiAt1m = -45, double pathLossExponent = 2.2)
        => Math.Pow(10, (rssiAt1m - rssi) / (10 * Math.Max(1.0, pathLossExponent)));

    public static Estimate? Estimate(IReadOnlyList<Anchor> anchors, (double w, double h)? bounds = null)
    {
        if (anchors.Count == 0) return null;
        double x, y;
        if (anchors.Count == 1) { x = anchors[0].X; y = anchors[0].Y; }
        else if (anchors.Count == 2)
        {
            var a = anchors[0]; var b = anchors[1];
            var t = a.Distance / Math.Max(1e-6, a.Distance + b.Distance);
            x = a.X + (b.X - a.X) * t; y = a.Y + (b.Y - a.Y) * t;
        }
        else
        {
            // Weighted centroid start (closer anchors weigh more), then Gauss-Newton.
            var ws = anchors.Select(a => a.Weight / Math.Max(0.25, a.Distance * a.Distance)).ToArray();
            x = anchors.Select((a, i) => a.X * ws[i]).Sum() / ws.Sum(); y = anchors.Select((a, i) => a.Y * ws[i]).Sum() / ws.Sum();
            for (var iter = 0; iter < 25; iter++)
            {
                double jtj00 = 0, jtj01 = 0, jtj11 = 0, jtr0 = 0, jtr1 = 0;
                foreach (var a in anchors)
                {
                    var dx = x - a.X; var dy = y - a.Y; var r = Math.Max(1e-6, Math.Sqrt(dx * dx + dy * dy));
                    var res = r - a.Distance; var jx = dx / r; var jy = dy / r; var w = a.Weight;
                    jtj00 += w * jx * jx; jtj01 += w * jx * jy; jtj11 += w * jy * jy; jtr0 += w * jx * res; jtr1 += w * jy * res;
                }
                var det = jtj00 * jtj11 - jtj01 * jtj01;
                if (Math.Abs(det) < 1e-9) break;
                var sx = (jtj11 * jtr0 - jtj01 * jtr1) / det; var sy = (jtj00 * jtr1 - jtj01 * jtr0) / det;
                x -= sx; y -= sy;
                if (Math.Abs(sx) + Math.Abs(sy) < 1e-4) break;
            }
        }
        if (bounds is { } b2) { x = Math.Clamp(x, 0, b2.w); y = Math.Clamp(y, 0, b2.h); }
        // Accuracy: RMS residual of the fit (or the nearest anchor distance when under-determined).
        var rms = anchors.Count >= 3 ? Math.Sqrt(anchors.Average(a => { var d = Math.Sqrt((x - a.X) * (x - a.X) + (y - a.Y) * (y - a.Y)) - a.Distance; return d * d; })) : anchors.Min(a => a.Distance);
        return new Estimate(Math.Round(x, 2), Math.Round(y, 2), Math.Round(Math.Max(0.5, rms), 2), anchors.Count);
    }
}
