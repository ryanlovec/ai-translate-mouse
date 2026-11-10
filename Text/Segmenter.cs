using System.Text.RegularExpressions;
using AIMouse.Native;
using AIMouse.Ocr;

namespace AIMouse.Text;

public sealed class Sentence
{
    public string En = "";
    public List<RECT> Fragments = new();
    /// <summary>Paragraph number (sentences of one paragraph share it).</summary>
    public int Para;
}

/// <summary>Turns OCR lines into paragraphs, then sentences, keeping per-line rectangles of each sentence.</summary>
public static class Segmenter
{
    sealed class Tok
    {
        public string Text = "";
        public List<(RECT Rect, int Line)> Parts = new();
    }

    static readonly HashSet<string> Abbrev = new(StringComparer.Ordinal)
    {
        "mr.", "mrs.", "ms.", "dr.", "prof.", "sr.", "jr.", "st.", "vs.", "etc.", "e.g.", "i.e.", "u.s.", "u.k.",
        "fig.", "no.", "inc.", "ltd.", "co.", "a.m.", "p.m.", "jan.", "feb.", "aug.", "sept.", "oct.", "nov.", "dec.",
    };

    static readonly Regex Initial = new(@"^[A-Z]\.$", RegexOptions.Compiled);
    const string TrailClosers = "\"'”’)]";
    const string LeadOpeners = "\"'“‘([";

    public static List<Sentence> Segment(OcrPage page)
    {
        var paras = new List<List<OcrLineM>>();
        var lines = page.Lines;
        for (int i = 0; i < lines.Count; i++)
        {
            // Same paragraph only if close together AND the previous line ran to the right edge
            // (a line that ends early = end of paragraph / list item / label).
            if (i > 0 && SameParagraph(lines[i - 1], lines[i]) && CopyText.IsWrapped(lines, i - 1, lines[i - 1].Text, lines[i].Text))
                paras[^1].Add(lines[i]);
            else paras.Add(new List<OcrLineM> { lines[i] });
        }

        var result = new List<Sentence>();
        for (int pi = 0; pi < paras.Count; pi++)
        {
            var para = paras[pi];
            var toks = new List<Tok>();
            for (int li = 0; li < para.Count; li++)
            {
                var words = para[li].Words;
                for (int wi = 0; wi < words.Count; wi++)
                {
                    var w = words[wi];
                    // Merge line-end hyphenation: "trans-" + "lation"
                    if (wi == 0 && toks.Count > 0)
                    {
                        var last = toks[^1];
                        bool lastWasLineEnd = last.Parts[^1].Line == li - 1;
                        if (lastWasLineEnd && last.Text.Length > 1 && last.Text.EndsWith('-') &&
                            w.Text.Length > 0 && char.IsLower(w.Text[0]))
                        {
                            last.Text = last.Text[..^1] + w.Text;
                            last.Parts.Add((w.Bounds, li));
                            continue;
                        }
                    }
                    var t = new Tok { Text = w.Text };
                    t.Parts.Add((w.Bounds, li));
                    toks.Add(t);
                }
            }

            var cur = new List<Tok>();
            for (int i = 0; i < toks.Count; i++)
            {
                cur.Add(toks[i]);
                if (i + 1 < toks.Count && EndsSentence(toks[i].Text) && StartsSentence(toks[i + 1].Text) && !IsAbbrev(toks[i].Text))
                {
                    Flush(cur, result, pi);
                    cur = new List<Tok>();
                }
            }
            Flush(cur, result, pi);
        }
        return result;
    }

    static void Flush(List<Tok> toks, List<Sentence> into, int para)
    {
        if (toks.Count == 0) return;
        var en = string.Join(" ", toks.Select(t => t.Text)).Trim();
        if (!en.Any(c => c < 128 && char.IsLetter(c))) return;
        var byLine = new SortedDictionary<int, RECT>();
        foreach (var t in toks)
            foreach (var (r, line) in t.Parts)
                byLine[line] = byLine.TryGetValue(line, out var u) ? RECT.Union(u, r) : r;
        into.Add(new Sentence { En = en, Fragments = byLine.Values.ToList(), Para = para });
    }

    static bool SameParagraph(OcrLineM cur, OcrLineM next)
    {
        var a = cur.Bounds;
        var b = next.Bounds;
        int ha = a.Height, hb = b.Height;
        double avg = (ha + hb) / 2.0;
        int gap = b.Top - a.Bottom;
        if (gap > 0.9 * avg) return false;
        if (gap < -0.5 * avg) return false;
        if (Math.Abs(ha - hb) > 0.4 * Math.Max(ha, hb)) return false;
        bool overlap = b.Left < a.Right && b.Right > a.Left;
        if (!overlap && Math.Abs(b.Left - a.Left) >= 3 * avg) return false;
        return true;
    }

    static string TrimTrail(string s) => s.TrimEnd(TrailClosers.ToCharArray());

    static bool EndsSentence(string word)
    {
        var t = TrimTrail(word);
        if (t.Length == 0) return false;
        char c = t[^1];
        return c == '.' || c == '!' || c == '?';
    }

    static bool StartsSentence(string word)
    {
        if (word.Length == 0) return false;
        if (LeadOpeners.Contains(word[0])) return true;
        char c = word[0];
        return char.IsUpper(c) || char.IsDigit(c);
    }

    static bool IsAbbrev(string word)
    {
        var t = TrimTrail(word);
        if (Initial.IsMatch(t)) return true;
        return Abbrev.Contains(t.ToLowerInvariant());
    }
}
