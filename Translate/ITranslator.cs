using System.Text.Json.Serialization;

namespace AIMouse.Translate;

public sealed class WordInfo
{
    public string Word { get; set; } = "";
    public string Phonetic { get; set; } = "";
    public string Pos { get; set; } = "";
    public string Meaning { get; set; } = "";
    public string Note { get; set; } = "";
    [JsonPropertyName("sentence_zh")] public string SentenceZh { get; set; } = "";
    /// <summary>From the offline dictionary (not the AI): no in-context meaning, no sentence translation.</summary>
    [JsonIgnore] public bool Offline { get; set; }
}

public sealed class TranslateException : Exception
{
    public TranslateException(string message) : base(message) { }
}

public interface ITranslator
{
    /// <summary>Translate segments; onPartial(index, chineseSoFar) is called repeatedly while streaming.</summary>
    Task TranslateAsync(IReadOnlyList<string> segments, Action<int, string> onPartial, CancellationToken ct);

    Task<WordInfo> LookupAsync(string word, string sentence, CancellationToken ct);

    Task<string> TestAsync(CancellationToken ct);
}
