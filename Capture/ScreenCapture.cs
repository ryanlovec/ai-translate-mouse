using System.Runtime.InteropServices;
using AIMouse.Native;
using SD = System.Drawing;
using SDI = System.Drawing.Imaging;

namespace AIMouse.Capture;

public static class ScreenCapture
{
    public static SD.Bitmap Capture(RECT r)
    {
        var bmp = new SD.Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height), SDI.PixelFormat.Format32bppArgb);
        using var g = SD.Graphics.FromImage(bmp);
        g.CopyFromScreen(r.Left, r.Top, 0, 0, new SD.Size(bmp.Width, bmp.Height), SD.CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public static SD.Bitmap Scale(SD.Bitmap src, double s)
    {
        int w = Math.Max(1, (int)Math.Round(src.Width * s));
        int h = Math.Max(1, (int)Math.Round(src.Height * s));
        var dst = new SD.Bitmap(w, h, SDI.PixelFormat.Format32bppArgb);
        using var g = SD.Graphics.FromImage(dst);
        g.InterpolationMode = SD.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = SD.Drawing2D.PixelOffsetMode.HighQuality;
        g.DrawImage(src, new SD.Rectangle(0, 0, w, h));
        return dst;
    }

    /// <summary>64x64 grayscale thumbnail used to detect content changes cheaply.</summary>
    public static byte[] Fingerprint(RECT r)
    {
        using var full = Capture(r);
        using var small = new SD.Bitmap(64, 64, SDI.PixelFormat.Format32bppArgb);
        using (var g = SD.Graphics.FromImage(small))
        {
            g.InterpolationMode = SD.Drawing2D.InterpolationMode.Bilinear;
            g.DrawImage(full, new SD.Rectangle(0, 0, 64, 64));
        }
        var data = small.LockBits(new SD.Rectangle(0, 0, 64, 64), SDI.ImageLockMode.ReadOnly, SDI.PixelFormat.Format32bppArgb);
        try
        {
            var raw = new byte[data.Stride * 64];
            Marshal.Copy(data.Scan0, raw, 0, raw.Length);
            var fp = new byte[64 * 64];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int i = y * data.Stride + x * 4;
                    fp[y * 64 + x] = (byte)((raw[i] * 29 + raw[i + 1] * 150 + raw[i + 2] * 77) >> 8);
                }
            return fp;
        }
        finally { small.UnlockBits(data); }
    }

    public static double Diff(byte[] a, byte[] b)
    {
        long sum = 0;
        for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i] - b[i]);
        return (double)sum / a.Length;
    }
}
