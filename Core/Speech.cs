using System.Speech.Synthesis;

namespace AIMouse.Core;

/// <summary>Shared English read-aloud (Windows built-in voices). Single words, single sentences, or a whole list.</summary>
public static class Speech
{
    static SpeechSynthesizer? _tts;
    static bool _checked;
    static int _gen;
    static string? _single;
    static Action? _onDone;
    static readonly Dictionary<Prompt, Action> Starts = new();
    static readonly Dictionary<Prompt, Action> Ends = new();

    static SpeechSynthesizer? Get()
    {
        if (_checked) return _tts;
        _checked = true;
        try
        {
            var s = new SpeechSynthesizer();
            var voice = s.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.Name.StartsWith("en-US"))
                        ?? s.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.Name.StartsWith("en"));
            if (voice == null) { s.Dispose(); return null; }
            s.SelectVoice(voice.VoiceInfo.Name);
            s.Rate = -1;
            s.SpeakStarted += (_, e) => { if (Starts.Remove(e.Prompt, out var a)) a(); };
            s.SpeakCompleted += (_, e) =>
            {
                Starts.Remove(e.Prompt);
                if (Ends.Remove(e.Prompt, out var a)) a();
            };
            _tts = s;
        }
        catch (Exception ex) { Log.Write("TTS init failed: " + ex.Message); }
        return _tts;
    }

    public static bool Available => Get() != null;

    /// <summary>Stop whatever is being read (and tell a running "read all" that it ended).</summary>
    public static void Stop()
    {
        _gen++;
        _single = null;
        var done = _onDone;
        _onDone = null;
        _tts?.SpeakAsyncCancelAll();
        done?.Invoke();
    }

    /// <summary>Read one word/sentence; clicking again on the same text while it is being read stops it.</summary>
    public static void Toggle(string text)
    {
        var t = Get();
        if (t == null || string.IsNullOrWhiteSpace(text)) return;
        bool same = _single == text && t.State == SynthesizerState.Speaking;
        Stop();
        if (same) return;
        _single = text;
        t.SpeakAsync(text);
    }

    /// <summary>Read a word now (restarts even if the same word is being read).</summary>
    public static void Say(string text)
    {
        var t = Get();
        if (t == null || string.IsNullOrWhiteSpace(text)) return;
        Stop();
        _single = text;
        t.SpeakAsync(text);
    }

    /// <summary>Read sentences one after another. onStart(i) fires as sentence i begins; onDone when finished or stopped.</summary>
    public static void SpeakAll(IReadOnlyList<string> texts, Action<int> onStart, Action onDone)
    {
        var t = Get();
        if (t == null || texts.Count == 0) { onDone(); return; }
        Stop();
        int gen = _gen;
        _onDone = onDone;
        for (int i = 0; i < texts.Count; i++)
        {
            int k = i;
            var p = new Prompt(texts[i]);
            Starts[p] = () => { if (gen == _gen) onStart(k); };
            if (i == texts.Count - 1)
                Ends[p] = () =>
                {
                    if (gen != _gen) return;
                    var d = _onDone;
                    _onDone = null;
                    d?.Invoke();
                };
            t.SpeakAsync(p);
        }
    }
}
