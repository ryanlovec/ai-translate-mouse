using System.IO;
using System.Net.Http;
using AIMouse.Core;

namespace AIMouse.Voice;

/// <summary>
/// Downloads the SenseVoice model (~230 MB) into <see cref="SpeechToText.ModelDir"/>.
/// Tries Hugging Face first, then its mirror (reachable from mainland China). Keeps running if Settings is closed.
/// </summary>
public static class ModelDownloader
{
    const string Repo = "csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17";
    static readonly string[] Hosts = { "https://huggingface.co", "https://hf-mirror.com" };
    static readonly (string Name, long MinSize)[] Files = { ("tokens.txt", 100_000), ("model.int8.onnx", 200_000_000) };
    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static bool Running { get; private set; }
    /// <summary>(bytes done, bytes total or -1). Raised on a background thread.</summary>
    public static event Action<long, long>? Progress;
    /// <summary>null = success, otherwise a Chinese error message. Raised on a background thread.</summary>
    public static event Action<string?>? Finished;

    public static void Start()
    {
        if (Running || SpeechToText.Installed) return;
        Running = true;
        Task.Run(async () =>
        {
            string? error = null;
            try { await DownloadAsync(); }
            catch (Exception ex)
            {
                Log.Write("Model download failed: " + ex);
                error = "下载失败，请检查网络后重试";
            }
            Running = false;
            Finished?.Invoke(error);
        });
    }

    static async Task DownloadAsync()
    {
        Directory.CreateDirectory(SpeechToText.ModelDir);
        foreach (var (name, minSize) in Files)
        {
            var dest = Path.Combine(SpeechToText.ModelDir, name);
            if (File.Exists(dest) && new FileInfo(dest).Length >= minSize) continue;
            Exception? last = null;
            foreach (var host in Hosts)
            {
                try
                {
                    await DownloadFile($"{host}/{Repo}/resolve/main/{name}", dest, report: name.EndsWith(".onnx"));
                    if (new FileInfo(dest).Length < minSize) throw new IOException("file too small");
                    last = null;
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Log.Write($"Download {name} from {host} failed: {ex.Message}");
                }
            }
            if (last != null) throw last;
        }
    }

    static async Task DownloadFile(string url, string dest, bool report)
    {
        var part = dest + ".part";
        using var cts = new CancellationTokenSource();
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? -1;
        await using (var src = await resp.Content.ReadAsStreamAsync())
        await using (var fs = File.Create(part))
        {
            var buf = new byte[1 << 16];
            long done = 0, lastReport = 0;
            while (true)
            {
                // A stalled connection (common on blocked hosts) fails after 30 s without data.
                var read = src.ReadAsync(buf);
                var finished = await Task.WhenAny(read.AsTask(), Task.Delay(30_000));
                if (finished is not Task<int>) throw new TimeoutException("no data for 30 s");
                int n = await read;
                if (n == 0) break;
                await fs.WriteAsync(buf.AsMemory(0, n));
                done += n;
                if (report && done - lastReport > 1_000_000)
                {
                    lastReport = done;
                    Progress?.Invoke(done, total);
                }
            }
        }
        File.Move(part, dest, overwrite: true);
    }

    public static void Delete()
    {
        try { if (Directory.Exists(SpeechToText.ModelDir)) Directory.Delete(SpeechToText.ModelDir, true); }
        catch (Exception ex) { Log.Write("Delete model failed: " + ex.Message); }
    }
}
