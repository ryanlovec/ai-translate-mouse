using System.Runtime.InteropServices;
using AIMouse.Native;
using SD = System.Drawing;
using SDI = System.Drawing.Imaging;

namespace AIMouse.Capture;

/// <summary>
/// Tells how far a page has scrolled by comparing "row profiles" (average brightness of each pixel row).
/// The screen is split into vertical strips, each tracked on its own, so parts that don't scroll
/// (side panels, toolbars, the scrollbar) can't confuse the result.
/// </summary>
public static class ScrollTracker
{
    public const int Bands = 6;

    /// <summary>One row profile per vertical strip: [band][row].</summary>
    public static float[][] Profile(RECT r)
    {
        using var bmp = ScreenCapture.Capture(r);
        return Profile(bmp);
    }

    public static float[][] Profile(SD.Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var prof = new float[Bands][];
        for (int b = 0; b < Bands; b++) prof[b] = new float[h];
        var data = bmp.LockBits(new SD.Rectangle(0, 0, w, h), SDI.ImageLockMode.ReadOnly, SDI.PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[data.Stride];
            int step = Math.Max(1, w / 900);
            for (int y = 0; y < h; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                for (int b = 0; b < Bands; b++)
                {
                    int x0 = w * b / Bands, x1 = w * (b + 1) / Bands;
                    long sum = 0;
                    int n = 0;
                    for (int x = x0; x < x1; x += step)
                    {
                        int i = x * 4;
                        sum += row[i] * 114 + row[i + 1] * 587 + row[i + 2] * 299;
                        n++;
                    }
                    prof[b][y] = n == 0 ? 0 : sum / (float)(n * 1000);
                }
            }
            return prof;
        }
        finally { bmp.UnlockBits(data); }
    }

    /// <summary>Band index (0..Bands-1) of an x position inside a region of the given width.</summary>
    public static int BandOf(int xInRegion, int regionWidth) => Math.Clamp(xInRegion * Bands / Math.Max(1, regionWidth), 0, Bands - 1);

    /// <summary>How well "now" equals "old" moved by k rows (now[y] ≈ old[y + k]); only rows with text count.</summary>
    public static (double Score, int Rows) Score(float[] old, float[] now, int k, int stride = 1)
    {
        float bg = Median(now);
        int n = 0, m = 0;
        for (int y = 0; y < now.Length; y += stride)
        {
            int oy = y + k;
            if (oy < 0 || oy >= old.Length) continue;
            float a = now[y], b = old[oy];
            if (Math.Abs(a - bg) < 1.0f && Math.Abs(b - bg) < 1.0f) continue; // blank on both: says nothing
            if (k != 0 && Math.Abs(a - old[y]) < 0.5f && Math.Abs(a - bg) >= 1.0f) continue; // row didn't move: fixed toolbar/header, ignore
            n++;
            if (Math.Abs(a - b) < 2.5f) m++;
        }
        return (n == 0 ? 1 : m / (double)n, n);
    }

    /// <summary>True if nothing changed (every strip identical).</summary>
    /// Small local changes (a blinking status dot, a hover highlight, a ticking number) don't count.
    public static bool Same(float[][] old, float[][] now)
    {
        int h = now[0].Length, changed = 0;
        int allow = Math.Max(8, h / 12);
        for (int b = 0; b < Bands; b++)
            for (int y = 0; y < h; y++)
                if (Math.Abs(old[b][y] - now[b][y]) >= 2.5f && ++changed > allow) return false;
        return true;
    }

    /// <summary>Header/Footer = rows at the top/bottom that stayed put while the page scrolled (fixed toolbars).</summary>
    public sealed record Shift(int Dy, bool[] Moved, bool[] Still, int Header = 0, int Footer = 0);

    /// <summary>
    /// The scroll between two moments. Each strip votes for its own offset; the offset backed by the most text wins.
    /// Dy = change of the content's Y position (negative = content moved up). Moved[b] = strip b scrolled by Dy,
    /// Still[b] = strip b didn't change. Null if the screen changed in a way that isn't a scroll (new page).
    /// </summary>
    public static Shift? FindShift(float[][] old, float[][] now, int maxShift)
    {
        var still = new bool[Bands];
        var votes = new List<(int dy, int rows)>();
        int textRows = 0;
        for (int b = 0; b < Bands; b++)
        {
            var (s0, n0) = Score(old[b], now[b], 0);
            if (n0 < 6 || s0 >= 0.95) { still[b] = true; continue; }
            textRows += n0;
            var d = BandShift(old[b], now[b], maxShift);
            if (d is { } v) votes.Add((v.dy, v.rows));
        }
        if (votes.Count == 0) return null;
        var winner = votes.GroupBy(v => v.dy / 3).OrderByDescending(g => g.Sum(v => v.rows)).First();
        int dy = (int)Math.Round(winner.Average(v => v.dy));
        // The winning offset must explain most of the strips that changed; otherwise it's a different page.
        if (winner.Sum(v => v.rows) < 0.5 * votes.Sum(v => v.rows) || winner.Sum(v => v.rows) < 30) return null;
        int changed = still.Count(x => !x);
        if (winner.Count() * 2 < changed) return null;   // most of the columns that changed must agree it was a scroll
        var moved = new bool[Bands];
        for (int b = 0; b < Bands; b++)
        {
            if (still[b]) continue;
            var (s, n) = Score(old[b], now[b], -dy);
            moved[b] = n >= 6 && s >= 0.8;
        }
        if (dy == 0 || !moved.Any(x => x)) return new Shift(dy, moved, still);
        int h = now[0].Length;
        bool Fixed(int y)
        {
            for (int b = 0; b < Bands; b++)
                if (moved[b] && Math.Abs(now[b][y] - old[b][y]) >= 0.6f) return false;
            return true;
        }
        int header = 0, footer = 0;
        while (header < h / 3 && Fixed(header)) header++;
        while (footer < h / 3 && Fixed(h - 1 - footer)) footer++;
        return new Shift(dy, moved, still, header, footer);
    }

    public static string Explain(float[][] old, float[][] now, int maxShift)
    {
        var sb = new System.Text.StringBuilder();
        for (int b = 0; b < Bands; b++)
        {
            var (s0, n0) = Score(old[b], now[b], 0);
            int bestK = 0; double best = -1; int bestRows = 0;
            for (int k = -maxShift; k <= maxShift; k++)
            {
                var (sc, rows) = Score(old[b], now[b], k);
                if (rows >= 6 && sc > best) { best = sc; bestK = k; bestRows = rows; }
            }
            sb.AppendLine($"band {b}: k0 score={s0:0.00} rows={n0} | best k={bestK} score={best:0.00} rows={bestRows} | bandshift={BandShift(old[b], now[b], maxShift)}");
        }
        sb.AppendLine("FindShift: " + (FindShift(old, now, maxShift)?.Dy.ToString() ?? "null"));
        return sb.ToString();
    }

    static (int dy, int rows)? BandShift(float[] old, float[] now, int maxShift)
    {
        int bestK = 0;
        double best = -1;
        for (int k = -maxShift; k <= maxShift; k++)
        {
            var (s, rows) = Score(old, now, k, stride: 2);
            if (rows < 6) continue;
            if (s > best) { best = s; bestK = k; }
        }
        if (best < 0.7) return null;
        int fineK = bestK, fineRows = 0;
        double fine = -1;
        for (int k = bestK - 3; k <= bestK + 3; k++)
        {
            var (s, rows) = Score(old, now, k);
            if (rows >= 8 && s > fine) { fine = s; fineK = k; fineRows = rows; }
        }
        return fine < 0.82 ? null : (-fineK, fineRows);
    }

    static float Median(float[] a)
    {
        var c = new float[Math.Min(a.Length, 400)];
        int step = Math.Max(1, a.Length / c.Length);
        for (int i = 0; i < c.Length; i++) c[i] = a[Math.Min(a.Length - 1, i * step)];
        Array.Sort(c);
        return c[c.Length / 2];
    }
}
