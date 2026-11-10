using System.IO;

namespace AIMouse.Voice;

/// <summary>Minimal WAV reader for tests: 16-bit PCM, any channel count / rate → 16 kHz mono floats.</summary>
public static class Wav
{
    public static float[] Read16k(string path)
    {
        using var br = new BinaryReader(File.OpenRead(path));
        br.ReadBytes(12);
        int channels = 1, rate = 16000, bits = 16;
        byte[]? data = null;
        while (br.BaseStream.Position < br.BaseStream.Length - 8)
        {
            var id = new string(br.ReadChars(4));
            int size = br.ReadInt32();
            if (id == "fmt ")
            {
                br.ReadInt16(); channels = br.ReadInt16(); rate = br.ReadInt32(); br.ReadInt32(); br.ReadInt16(); bits = br.ReadInt16();
                br.ReadBytes(size - 16);
            }
            else if (id == "data") { data = br.ReadBytes(size); break; }
            else br.ReadBytes(size);
        }
        if (data == null || bits != 16) throw new InvalidDataException("need 16-bit PCM wav");
        int frames = data.Length / 2 / channels;
        var mono = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++) sum += BitConverter.ToInt16(data, (i * channels + c) * 2) / 32768f;
            mono[i] = sum / channels;
        }
        return Resample(mono, rate, SpeechToText.SampleRate);
    }

    public static float[] Resample(float[] x, int from, int to)
    {
        if (from == to) return x;
        int n = (int)((long)x.Length * to / from);
        var y = new float[n];
        for (int i = 0; i < n; i++)
        {
            double p = (double)i * from / to;
            int k = (int)p;
            double f = p - k;
            y[i] = (float)(x[Math.Min(k, x.Length - 1)] * (1 - f) + x[Math.Min(k + 1, x.Length - 1)] * f);
        }
        return y;
    }
}
