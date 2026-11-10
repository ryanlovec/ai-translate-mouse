using System.Text;
using System.Text.RegularExpressions;
using AIMouse.Core;
using Microsoft.Win32.SafeHandles;

namespace AIMouse.Translate;

/// <summary>
/// Offline English→Chinese dictionary (ECDICT, MIT) read straight from a sorted text file:
/// only a small line index is kept in memory; each lookup is a binary search on disk.
/// Line format: key \t word \t phonetic \t translation   or   key \t @lemma
/// </summary>
public static class OfflineDict
{
    static SafeFileHandle? _file;
    static int[] _starts = Array.Empty<int>();
    static long _length;
    static readonly Regex PosRx = new(@"^([a-z]{1,5}\.)\s*(.*)$", RegexOptions.Compiled);

    public static bool Available => _starts.Length > 0;

    public static void LoadInBackground() => Task.Run(() =>
    {
        try { Load(); }
        catch (Exception ex) { Log.Write("Offline dictionary load failed: " + ex.Message); }
    });

    static void Load()
    {
        var path = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "dict", "ecdict.tsv"),
            Path.Combine(AppContext.BaseDirectory, "ecdict.tsv"),
            Path.Combine(Config.Dir, "ecdict.tsv"),
        }.FirstOrDefault(File.Exists);
        if (path == null)
        {
            Log.Write("Offline dictionary not found");
            return;
        }
        var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
        long len = RandomAccess.GetLength(handle);
        var starts = new List<int>(450_000) { 0 };
        var buf = new byte[1 << 20];
        long pos = 0;
        while (pos < len)
        {
            int n = RandomAccess.Read(handle, buf, pos);
            if (n <= 0) break;
            for (int i = 0; i < n; i++)
                if (buf[i] == (byte)'\n' && pos + i + 1 < len) starts.Add((int)(pos + i + 1));
            pos += n;
        }
        _file = handle;
        _length = len;
        _starts = starts.ToArray();
        Log.Write($"Offline dictionary loaded: {_starts.Length} lines");
    }

    /// <summary>Dictionary meaning of a word as seen on screen (handles plurals, -ed, -ing, 's, capitals).</summary>
    public static WordInfo? Lookup(string word)
    {
        if (!Available || string.IsNullOrWhiteSpace(word)) return null;
        var w = word.Trim().Replace('’', '\'');
        foreach (var cand in Candidates(w))
        {
            var info = LookupExact(cand, w, depth: 0);
            if (info != null) return info;
        }
        return null;
    }

    static IEnumerable<string> Candidates(string w)
    {
        var lower = w.ToLowerInvariant();
        yield return lower;
        if (lower.EndsWith("'s")) yield return lower[..^2];
        if (lower.EndsWith("'")) yield return lower[..^1];
        if (lower.EndsWith("ies") && lower.Length > 4) yield return lower[..^3] + "y";
        if (lower.EndsWith("es") && lower.Length > 3) yield return lower[..^2];
        if (lower.EndsWith("s") && lower.Length > 3) yield return lower[..^1];
        if (lower.EndsWith("ied") && lower.Length > 4) yield return lower[..^3] + "y";
        if (lower.EndsWith("ed") && lower.Length > 3) { yield return lower[..^2]; yield return lower[..^1]; }
        if (lower.EndsWith("ing") && lower.Length > 4) { yield return lower[..^3]; yield return lower[..^3] + "e"; }
        if (lower.EndsWith("ly") && lower.Length > 4) yield return lower[..^2];
    }

    static WordInfo? LookupExact(string key, string original, int depth)
    {
        var lines = FindLines(key);
        if (lines.Count == 0) return null;
        var parts = lines.Select(l => l.Split('\t')).ToList();
        var alias = parts.FirstOrDefault(p => p.Length >= 2 && p[1].StartsWith('@'));
        var real = parts.Where(p => p.Length >= 4).ToList();
        if (real.Count == 0)
            return alias != null && depth == 0 ? LookupExact(alias[1][1..].ToLowerInvariant(), original, 1) : null;

        // Prefer the spelling that matches what is on screen ("US" vs "us"), else the common lowercase word.
        var pick = real.FirstOrDefault(p => p[1] == original)
                   ?? real.FirstOrDefault(p => p[1] == key)
                   ?? real[0];
        return ToInfo(pick[1], pick[2], pick[3]);
    }

    static WordInfo ToInfo(string word, string phonetic, string translation)
    {
        var lines = translation.Split("\\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var posLines = lines.Where(l => !l.StartsWith('[')).ToList();
        if (posLines.Count == 0) posLines = lines.ToList();
        var (pos, meaning) = SplitPos(posLines.Count > 0 ? posLines[0] : "");
        var rest = posLines.Skip(1).Take(2).Select(l =>
        {
            var (p, m) = SplitPos(l);
            return (p.Length > 0 ? p + " " : "") + Trim(FirstMeanings(m, 2), 24);
        });
        return new WordInfo
        {
            Word = word,
            Phonetic = phonetic.Length > 0 ? "/" + phonetic.Replace('ә', 'ə').Replace('ɚ', 'ə') + "/" : "",
            Pos = pos,
            Meaning = Trim(FirstMeanings(meaning, 3), 30),
            Note = string.Join("；", rest),
            SentenceZh = "",
            Offline = true,
        };
    }

    static (string pos, string meaning) SplitPos(string line)
    {
        var m = PosRx.Match(line);
        if (!m.Success) return ("", line);
        var p = m.Groups[1].Value switch { "a." => "adj.", "ad." => "adv.", _ => m.Groups[1].Value };
        return (p, m.Groups[2].Value);
    }

    /// <summary>"易受伤害的, 有弱点的, 易受影响的, 脆弱的" → first n meanings, Chinese commas.</summary>
    static string FirstMeanings(string m, int n)
    {
        var items = m.Split(new[] { ", ", "，", ",", "；", ";" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return items.Length == 0 ? m : string.Join("，", items.Take(n));
    }

    static string Trim(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd(',', '，', ';', '；', ' ') + "…";

    // ---------- binary search over the sorted file ----------

    static List<string> FindLines(string key)
    {
        var target = Encoding.UTF8.GetBytes(key);
        int lo = 0, hi = _starts.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (CompareKey(mid, target) < 0) lo = mid + 1;
            else hi = mid;
        }
        var result = new List<string>();
        for (int i = lo; i < _starts.Length && result.Count < 8; i++)
        {
            var line = ReadLine(i);
            int tab = line.IndexOf('\t');
            if (tab < 0 || line[..tab] != key) break;
            result.Add(line);
        }
        return result;
    }

    static int CompareKey(int index, byte[] target)
    {
        Span<byte> buf = stackalloc byte[96];
        int n = RandomAccess.Read(_file!, buf, _starts[index]);
        int k = 0;
        while (k < n && buf[k] != (byte)'\t' && buf[k] != (byte)'\n') k++;
        return buf[..k].SequenceCompareTo(target);
    }

    static string ReadLine(int index)
    {
        long start = _starts[index];
        long end = index + 1 < _starts.Length ? _starts[index + 1] - 1 : _length;
        var buf = new byte[(int)Math.Min(4096, end - start)];
        int n = RandomAccess.Read(_file!, buf, start);
        return Encoding.UTF8.GetString(buf, 0, n).TrimEnd('\r', '\n');
    }
}
