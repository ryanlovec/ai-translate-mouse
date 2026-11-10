using System.Text;
using System.Text.RegularExpressions;
using AIMouse.Native;

namespace AIMouse.Ocr;

/// <summary>Turns OCR lines into text that pastes nicely: Chinese without stray spaces, wrapped lines joined into paragraphs.</summary>
public static class CopyText
{
    // Chinese OCR often reads "1." at a line start as "1，" / "1．" / "1 ·".
    static readonly Regex ListFix = new(@"^(\d{1,3})\s*[，,．·・。]\s*(?=\D)", RegexOptions.Compiled);
    static readonly Regex TimeFix = new(@"(?<=\d)\s*：\s*(?=\d)", RegexOptions.Compiled); // 11：59 → 11:59
    static readonly Regex ListStart = new(@"^([•●▪■◆◦·\-–—*]|\d{1,3}[.)、]|[a-zA-Z][.)]\s|[（(]\d{1,3}[)）])", RegexOptions.Compiled);

    public static bool IsCjk(char c) =>
        (c >= '\u4E00' && c <= '\u9FFF') || (c >= '\u3400' && c <= '\u4DBF') || (c >= '\u3000' && c <= '\u303F') ||
        (c >= '\uFF00' && c <= '\uFFEF') || (c >= '\u3040' && c <= '\u30FF') || (c >= '\uAC00' && c <= '\uD7AF');

    static bool IsHan(char c) => (c >= '\u4E00' && c <= '\u9FFF') || (c >= '\u3400' && c <= '\u4DBF');

    public static bool LooksChinese(string text)
    {
        int han = 0, all = 0;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) continue;
            all++;
            if (IsHan(c)) han++;
        }
        return han >= 2 && han * 20 >= all;
    }

    static bool HasCjk(string s) => s.Any(IsCjk);

    /// <summary>
    /// Mixed Chinese/English: keep the Chinese reader's result, but replace lines that contain no Chinese
    /// with the English reader's words at the same spot (it reads English much better).
    /// Lines mixing both scripts stay as the Chinese reader saw them.
    /// </summary>
    public static OcrPage MergeEnglish(OcrPage zh, OcrPage en)
    {
        var page = new OcrPage();
        foreach (var line in zh.Lines)
        {
            if (line.Words.Any(w => HasCjk(w.Text)))
            {
                page.Lines.Add(line);
                continue;
            }
            var lb = line.Bounds;
            var repl = en.Lines
                .Where(l => Math.Min(l.Bounds.Bottom, lb.Bottom) - Math.Max(l.Bounds.Top, lb.Top) > 0.5 * Math.Min(l.Bounds.Height, lb.Height))
                .SelectMany(l => l.Words)
                .Where(w => Math.Min(w.Bounds.Right, lb.Right) - Math.Max(w.Bounds.Left, lb.Left) >= 0.7 * Math.Max(1, w.Bounds.Width))
                .OrderBy(w => w.Bounds.Left).ToList();
            if (repl.Count == 0 || repl.Any(w => HasCjk(w.Text)))
            {
                page.Lines.Add(line);
                continue;
            }
            var u = repl[0].Bounds;
            foreach (var w in repl) u = RECT.Union(u, w.Bounds);
            page.Lines.Add(new OcrLineM { Words = repl, Bounds = u });
        }
        return page;
    }

    public static string Build(OcrPage page)
    {
        var lines = page.Lines.Where(l => l.Words.Count > 0).ToList();
        if (lines.Count == 0) return "";
        var texts = lines.Select(l => TimeFix.Replace(ListFix.Replace(JoinWords(l), "$1. "), ":")).ToList();

        var sb = new StringBuilder(texts[0]);
        for (int i = 1; i < lines.Count; i++)
        {
            string prev = texts[i - 1], cur = texts[i];
            if (cur.Length == 0) continue;
            if (prev.Length > 0 && IsWrapped(lines, i - 1, prev, cur))
            {
                char last = prev[^1], first = cur[0];
                if (last == '-' && prev.Length > 1 && char.IsLetter(prev[^2]) && char.IsLower(first)) sb.Length--;
                else if (!IsCjk(last) && !IsCjk(first)) sb.Append(' ');
            }
            else sb.Append('\n');
            sb.Append(cur);
        }
        return sb.ToString().Trim();
    }

    /// <summary>Words of one line: spaces between English words, none between Chinese characters.</summary>
    static string JoinWords(OcrLineM line)
    {
        var sb = new StringBuilder();
        double h = Math.Max(1, line.Bounds.Height);
        for (int i = 0; i < line.Words.Count; i++)
        {
            var w = line.Words[i];
            if (w.Text.Length == 0) continue;
            if (sb.Length > 0)
            {
                char pc = sb[^1], nc = w.Text[0];
                bool pCjk = IsCjk(pc), nCjk = IsCjk(nc);
                if (!pCjk && !nCjk) sb.Append(' ');
                else if (pCjk != nCjk && w.Bounds.Left - line.Words[i - 1].Bounds.Right > 0.3 * h) sb.Append(' ');
            }
            sb.Append(w.Text);
        }
        return sb.ToString();
    }

    /// <summary>Does line a run to the right edge so that b is its continuation (same paragraph)?</summary>
    internal static bool IsWrapped(List<OcrLineM> lines, int ia, string ta, string tb)
    {
        if (ta.Length == 0 || tb.Length == 0 || lines[ia + 1].Words.Count == 0) return false;
        OcrLineM a = lines[ia], b = lines[ia + 1];
        var ra = a.Bounds; var rb = b.Bounds;
        double h = Math.Max(1, Math.Min(ra.Height, rb.Height));
        if (Math.Max(ra.Height, rb.Height) > 1.6 * h) return false;          // different font size
        int gap = rb.Top - ra.Bottom;
        if (gap < -0.5 * h || gap > 1.0 * h) return false;                   // not the next line down
        if (ListStart.IsMatch(tb)) return false;                             // bullet / numbered item
        double cw = Math.Max(2, ra.Width / (double)Math.Max(1, ta.Length));  // rough character width
        if (rb.Left - ra.Left > 2 * cw) return false;                        // next line indented

        // Right edge of this text block: widest nearby line of the same size.
        int left = int.MaxValue, right = int.MinValue;
        for (int j = Math.Max(0, ia - 4); j <= Math.Min(lines.Count - 1, ia + 5); j++)
        {
            var r = lines[j].Bounds;
            if (Math.Max(r.Height, ra.Height) > 1.6 * Math.Max(1, Math.Min(r.Height, ra.Height))) continue;
            left = Math.Min(left, r.Left);
            right = Math.Max(right, r.Right);
        }
        if (ra.Width < 0.5 * (right - left)) return false;

        // Wrapped = the next line's first word would not have fit at the end of this line.
        var first = b.Words[0].Bounds;
        double firstWidth = CopyText.IsCjk(tb[0]) ? h : first.Width;
        return right - ra.Right < firstWidth + 1.5 * cw;
    }
}
