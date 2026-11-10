using System.IO;
using AIMouse.Core;
using SherpaOnnx;

namespace AIMouse.Voice;

/// <summary>
/// Offline speech → text with SenseVoice-Small (Chinese, English, mixed; adds punctuation).
/// The model (~230 MB) is downloaded on demand into %LOCALAPPDATA%\AIMouse\models\sensevoice.
/// It is loaded only when the user speaks and unloaded after a few idle minutes, so it costs no memory otherwise.
/// </summary>
public static class SpeechToText
{
    public const int SampleRate = 16000;
    public static string ModelDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIMouse", "models", "sensevoice");
    static string ModelFile => Path.Combine(ModelDir, "model.int8.onnx");
    static string TokensFile => Path.Combine(ModelDir, "tokens.txt");

    public static bool Installed => File.Exists(ModelFile) && File.Exists(TokensFile) && new FileInfo(ModelFile).Length > 200_000_000;

    static OfflineRecognizer? _rec;
    static readonly object Gate = new();
    static Timer? _unload;
    static readonly TimeSpan KeepLoaded = TimeSpan.FromMinutes(3);

    /// <summary>Load the model ahead of time (e.g. when the talk key goes down), so the result comes faster.</summary>
    public static void Warm() => Task.Run(() => { try { lock (Gate) Get(); } catch (Exception ex) { Log.Write("ASR warm failed: " + ex.Message); } });

    static OfflineRecognizer Get()
    {
        if (_rec != null) return _rec;
        var cfg = new OfflineRecognizerConfig();
        cfg.FeatConfig.SampleRate = SampleRate;
        cfg.FeatConfig.FeatureDim = 80;
        cfg.ModelConfig.SenseVoice.Model = ModelFile;
        cfg.ModelConfig.SenseVoice.Language = "auto";
        cfg.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
        cfg.ModelConfig.Tokens = TokensFile;
        cfg.ModelConfig.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        cfg.ModelConfig.Provider = "cpu";
        cfg.ModelConfig.Debug = 0;
        cfg.DecodingMethod = "greedy_search";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _rec = new OfflineRecognizer(cfg);
        Log.Write($"ASR model loaded in {sw.ElapsedMilliseconds} ms");
        return _rec;
    }

    /// <summary>Recognize 16 kHz mono samples (-1..1). Runs on a background thread.</summary>
    public static Task<string> RecognizeAsync(float[] samples) => Task.Run(() =>
    {
        lock (Gate)
        {
            var rec = Get();
            using var stream = rec.CreateStream();
            stream.AcceptWaveform(SampleRate, samples);
            rec.Decode(stream);
            var text = stream.Result.Text.Trim();
            ScheduleUnload();
            return Tidy(text);
        }
    });

    static void ScheduleUnload()
    {
        _unload?.Dispose();
        _unload = new Timer(_ =>
        {
            lock (Gate)
            {
                _rec?.Dispose();
                _rec = null;
                GC.Collect();
                Log.Write("ASR model unloaded (idle)");
            }
        }, null, KeepLoaded, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Spaces between Chinese and English the way people write them; no space inside Chinese.</summary>
    static string Tidy(string s)
    {
        Func<string, string, string, string> R = System.Text.RegularExpressions.Regex.Replace;
        s = R(s, @"(?<=[一-鿿])\s+(?=[一-鿿])", "");          // no spaces inside Chinese
        s = R(s, @"\s+(?=[，。！？、：；])|(?<=[，。！？、：；])\s+", ""); // none around Chinese punctuation
        s = R(s, @"(?<=[一-鿿])(?=[A-Za-z0-9])|(?<=[A-Za-z0-9])(?=[一-鿿])", " "); // "一个 homework 老师"
        return s;
    }
}
