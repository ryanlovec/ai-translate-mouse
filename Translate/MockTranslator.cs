namespace AIMouse.Translate;

/// <summary>Fake translator for testing without an API key.</summary>
public sealed class MockTranslator : ITranslator
{
    public async Task TranslateAsync(IReadOnlyList<string> segments, Action<int, string> onPartial, CancellationToken ct)
    {
        for (int i = 0; i < segments.Count; i++)
        {
            var target = "（模拟）" + segments[i][..Math.Min(30, segments[i].Length)];
            for (int n = 1; n <= target.Length; n += 2)
            {
                ct.ThrowIfCancellationRequested();
                onPartial(i, target[..n]);
                await Task.Delay(15, ct);
            }
            onPartial(i, target);
        }
    }

    public async Task<WordInfo> LookupAsync(string word, string sentence, CancellationToken ct)
    {
        await Task.Delay(300, ct);
        return new WordInfo
        {
            Word = word,
            Phonetic = "/ˈmɒk/",
            Pos = "n.",
            Meaning = "（模拟）释义",
            Note = "这是测试数据",
            SentenceZh = "（模拟）" + sentence[..Math.Min(40, sentence.Length)],
        };
    }

    public async Task<string> TestAsync(CancellationToken ct)
    {
        await Task.Delay(200, ct);
        return "（模拟）你好，很高兴认识你。";
    }
}
