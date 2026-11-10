using System.Runtime.InteropServices;
using AIMouse.Capture;
using AIMouse.Native;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using SD = System.Drawing;
using SDI = System.Drawing.Imaging;

namespace AIMouse.Ocr;

public sealed class OcrWordM
{
    public string Text = "";
    public RECT Bounds;
}

public sealed class OcrLineM
{
    public List<OcrWordM> Words = new();
    public RECT Bounds;
    public string Text => string.Join(" ", Words.Select(w => w.Text));
}

public sealed class OcrPage
{
    public List<OcrLineM> Lines = new();
}

public sealed class OcrService
{
    readonly OcrEngine _engine;
    readonly OcrEngine? _zh;
    readonly SemaphoreSlim _gate = new(1, 1);

    public OcrService()
    {
        _engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
                  ?? OcrEngine.TryCreateFromUserProfileLanguages()
                  ?? throw new InvalidOperationException("Windows 文字识别不可用");
        foreach (var tag in new[] { "zh-Hans-CN", "zh-Hans", "zh-CN" })
        {
            try { _zh = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(tag)); }
            catch { _zh = null; }
            if (_zh != null) break;
        }
    }

    public bool HasChinese => _zh != null;
    public static double? ZhUpscaleOverride;

    /// <summary>Recognize any text (Chinese and/or English) in a screen region and return it as clean, pasteable text.</summary>
    public Task<string> RecognizeTextAsync(RECT r) => Task.Run(async () =>
    {
        using var bmp = ScreenCapture.Capture(r);
        return await RecognizeTextAsync(bmp, r.Left, r.Top, UpscaleFor(r));
    });

    public async Task<string> RecognizeTextAsync(SD.Bitmap bmp, int offX, int offY, double upscale)
    {
        if (_zh != null)
        {
            var zh = await RecognizeBitmapAsync(bmp, offX, offY, ZhUpscaleOverride ?? upscale, _zh);
            if (ZhUpscaleOverride == null && zh.Lines.Count > 0)
            {
                // Chinese OCR is most accurate when characters are ~44px tall: re-run at the ideal size if far off.
                var hs = zh.Lines.Select(l => l.Bounds.Height).OrderBy(x => x).ToList();
                double ideal = Math.Clamp(44.0 / Math.Max(1, hs[hs.Count / 2]), 1.0, 4.0);
                ideal = Math.Min(ideal, OcrEngine.MaxImageDimension / (double)Math.Max(bmp.Width, bmp.Height));
                ideal = Math.Min(ideal, Math.Sqrt(12_000_000.0 / Math.Max(1, (double)bmp.Width * bmp.Height)));
                if (Math.Abs(ideal - upscale) / upscale > 0.25)
                    zh = await RecognizeBitmapAsync(bmp, offX, offY, ideal, _zh);
            }
            if (CopyText.LooksChinese(CopyText.Build(zh)))
            {
                if (!zh.Lines.SelectMany(l => l.Words).Any(w => w.Text.Any(char.IsAsciiLetter))) return CopyText.Build(zh);
                var enPage = await RecognizeBitmapAsync(bmp, offX, offY, upscale, _engine);
                return CopyText.Build(CopyText.MergeEnglish(zh, enPage));
            }
        }
        return CopyText.Build(await RecognizeBitmapAsync(bmp, offX, offY, upscale, _engine));
    }

    public static double UpscaleFor(RECT r)
    {
        uint max = OcrEngine.MaxImageDimension;
        int big = Math.Max(r.Width, r.Height);
        if (big * 2 <= max && (long)r.Width * r.Height <= 2_500_000) return 2.0;
        if (big * 1.5 <= max) return 1.5;
        return Math.Min(1.0, max / (double)big);
    }

    public Task<OcrPage> RecognizeScreenAsync(RECT r, double upscale) => Task.Run(async () =>
    {
        using var bmp = ScreenCapture.Capture(r);
        return await RecognizeBitmapAsync(bmp, r.Left, r.Top, upscale);
    });

    public Task<OcrPage> RecognizeBitmapAsync(SD.Bitmap bmp, int offX, int offY, double upscale) =>
        RecognizeBitmapAsync(bmp, offX, offY, upscale, _engine);

    async Task<OcrPage> RecognizeBitmapAsync(SD.Bitmap bmp, int offX, int offY, double upscale, OcrEngine engine)
    {
        SD.Bitmap work = Math.Abs(upscale - 1.0) > 0.01 ? ScreenCapture.Scale(bmp, upscale) : bmp;
        try
        {
            var sb = ToSoftwareBitmap(work);
            Windows.Media.Ocr.OcrResult res;
            await _gate.WaitAsync();
            try { res = await engine.RecognizeAsync(sb); }
            finally { _gate.Release(); sb.Dispose(); }

            var page = new OcrPage();
            foreach (var line in res.Lines)
            {
                var lm = new OcrLineM();
                foreach (var w in line.Words)
                {
                    var b = w.BoundingRect;
                    lm.Words.Add(new OcrWordM
                    {
                        Text = w.Text,
                        Bounds = new RECT(
                            offX + (int)Math.Floor(b.X / upscale),
                            offY + (int)Math.Floor(b.Y / upscale),
                            offX + (int)Math.Ceiling((b.X + b.Width) / upscale),
                            offY + (int)Math.Ceiling((b.Y + b.Height) / upscale))
                    });
                }
                if (lm.Words.Count == 0) continue;
                var u = lm.Words[0].Bounds;
                foreach (var w in lm.Words) u = RECT.Union(u, w.Bounds);
                lm.Bounds = u;
                page.Lines.Add(lm);
            }
            return page;
        }
        finally
        {
            if (!ReferenceEquals(work, bmp)) work.Dispose();
        }
    }

    static SoftwareBitmap ToSoftwareBitmap(SD.Bitmap bmp)
    {
        var data = bmp.LockBits(new SD.Rectangle(0, 0, bmp.Width, bmp.Height), SDI.ImageLockMode.ReadOnly, SDI.PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = bmp.Width * 4;
            var bytes = new byte[rowBytes * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * rowBytes, rowBytes);
            for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
            var buf = CryptographicBuffer.CreateFromByteArray(bytes);
            return SoftwareBitmap.CreateCopyFromBuffer(buf, BitmapPixelFormat.Bgra8, bmp.Width, bmp.Height, BitmapAlphaMode.Premultiplied);
        }
        finally { bmp.UnlockBits(data); }
    }
}
