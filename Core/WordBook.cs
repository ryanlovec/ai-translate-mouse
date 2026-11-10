using System.Text;
using AIMouse.Translate;

namespace AIMouse.Core;

public static class WordBook
{
    public static string FilePath => Path.Combine(Config.Dir, "生词本.csv");

    public static void EnsureFile()
    {
        if (File.Exists(FilePath)) return;
        Directory.CreateDirectory(Config.Dir);
        File.WriteAllText(FilePath, "日期,单词,音标,词性,意思,例句,例句翻译\r\n", new UTF8Encoding(true));
    }

    public static void Add(WordInfo info, string sentence)
    {
        try
        {
            EnsureFile();
            var cells = new[] { DateTime.Now.ToString("yyyy-MM-dd"), info.Word, info.Phonetic, info.Pos, info.Meaning, sentence, info.SentenceZh };
            File.AppendAllText(FilePath, string.Join(",", cells.Select(Csv)) + "\r\n", new UTF8Encoding(false));
        }
        catch (Exception ex) { Log.Write("WordBook add failed: " + ex.Message); }
    }

    static string Csv(string? s)
    {
        s ??= "";
        return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
