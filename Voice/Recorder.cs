using NAudio.Wave;

namespace AIMouse.Voice;

/// <summary>Records the default microphone as 16 kHz mono while the talk key is held. Reports a 0..1 loudness level.</summary>
public sealed class Recorder : IDisposable
{
    public const int MaxSeconds = 60;
    WaveInEvent? _in;
    readonly List<float> _samples = new();
    readonly object _gate = new();

    /// <summary>Loudness 0..1, about 20 times a second (raised on a background thread).</summary>
    public event Action<double>? Level;

    public static bool HasMicrophone => WaveInEvent.DeviceCount > 0;

    public void Start()
    {
        Stop();
        lock (_gate) _samples.Clear();
        _in = new WaveInEvent { WaveFormat = new WaveFormat(SpeechToText.SampleRate, 16, 1), BufferMilliseconds = 50, NumberOfBuffers = 4 };
        _in.DataAvailable += (_, e) =>
        {
            double peak = 0;
            lock (_gate)
            {
                if (_samples.Count > MaxSeconds * SpeechToText.SampleRate) return;
                for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
                {
                    float v = BitConverter.ToInt16(e.Buffer, i) / 32768f;
                    _samples.Add(v);
                    peak = Math.Max(peak, Math.Abs(v));
                }
            }
            Level?.Invoke(Math.Min(1, Math.Sqrt(peak) * 1.3));
        };
        _in.StartRecording();
    }

    /// <summary>Stop and return what was recorded.</summary>
    public float[] Stop()
    {
        if (_in != null)
        {
            try { _in.StopRecording(); } catch { }
            _in.Dispose();
            _in = null;
        }
        lock (_gate) return _samples.ToArray();
    }

    public void Dispose() => Stop();
}
