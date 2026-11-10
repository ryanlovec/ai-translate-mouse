using System.Runtime.InteropServices;
using SD = System.Drawing;
using SDI = System.Drawing.Imaging;
using WColor = System.Windows.Media.Color;

namespace AIMouse.Capture;

/// <summary>Reads a screenshot's pixels once, then finds the page background and text color of any area.</summary>
public sealed class ColorSampler
{
    readonly byte[] _px;
    readonly int _w, _h, _stride;

    public ColorSampler(SD.Bitmap bmp)
    {
        _w = bmp.Width;
        _h = bmp.Height;
        var data = bmp.LockBits(new SD.Rectangle(0, 0, _w, _h), SDI.ImageLockMode.ReadOnly, SDI.PixelFormat.Format32bppArgb);
        try
        {
            _stride = data.Stride;
            _px = new byte[_stride * _h];
            Marshal.Copy(data.Scan0, _px, 0, _px.Length);
        }
        finally { bmp.UnlockBits(data); }
    }

    (int r, int g, int b) At(int x, int y)
    {
        x = Math.Clamp(x, 0, _w - 1);
        y = Math.Clamp(y, 0, _h - 1);
        int i = y * _stride + x * 4;
        return (_px[i + 2], _px[i + 1], _px[i]);
    }

    /// <summary>Background = most common color around the area's edge; text = the colors inside that differ most from it.</summary>
    public (WColor Bg, WColor Fg) Sample(int left, int top, int right, int bottom)
    {
        // Background: ring just outside + edge of the area, quantized histogram.
        var hist = new Dictionary<int, (int n, long r, long g, long b)>();
        void Add(int x, int y)
        {
            var (r, g, b) = At(x, y);
            int key = (r >> 4) << 8 | (g >> 4) << 4 | (b >> 4);
            hist.TryGetValue(key, out var v);
            hist[key] = (v.n + 1, v.r + r, v.g + g, v.b + b);
        }
        int step = Math.Max(1, (right - left) / 60);
        for (int x = left - 2; x <= right + 2; x += step) { Add(x, top - 2); Add(x, bottom + 2); }
        int vstep = Math.Max(1, (bottom - top) / 30);
        for (int y = top - 2; y <= bottom + 2; y += vstep) { Add(left - 3, y); Add(right + 3, y); }
        var best = hist.Values.OrderByDescending(v => v.n).First();
        var bg = (r: (int)(best.r / best.n), g: (int)(best.g / best.n), b: (int)(best.b / best.n));

        // Text: inside pixels farthest from the background (top 6%).
        var dists = new List<(int d, int r, int g, int b)>();
        int sx = Math.Max(1, (right - left) / 120), sy = Math.Max(1, (bottom - top) / 40);
        for (int y = top; y < bottom; y += sy)
            for (int x = left; x < right; x += sx)
            {
                var (r, g, b) = At(x, y);
                int d = Math.Abs(r - bg.r) + Math.Abs(g - bg.g) + Math.Abs(b - bg.b);
                dists.Add((d, r, g, b));
            }
        dists.Sort((a, b) => b.d.CompareTo(a.d));
        int take = Math.Max(1, dists.Count * 6 / 100);
        var top6 = dists.Take(take).ToList();
        var fg = (r: (int)top6.Average(p => p.r), g: (int)top6.Average(p => p.g), b: (int)top6.Average(p => p.b));
        int contrast = Math.Abs(fg.r - bg.r) + Math.Abs(fg.g - bg.g) + Math.Abs(fg.b - bg.b);
        bool darkBg = bg.r * 299 + bg.g * 587 + bg.b * 114 < 128_000;
        if (contrast < 150) fg = darkBg ? (235, 235, 240) : (29, 29, 31);
        return (WColor.FromRgb((byte)bg.r, (byte)bg.g, (byte)bg.b), WColor.FromRgb((byte)fg.r, (byte)fg.g, (byte)fg.b));
    }
}
