using System.Net.Http;
using System.Text.Json;

namespace AIMouse.Translate;

/// <summary>Finds local AI servers (Ollama, LM Studio, llama.cpp, Jan) running on this PC and lists their models.</summary>
public static class LocalModels
{
    public sealed record Server(string Name, string Url, List<string> Models);

    static readonly (string Name, string Url)[] Known =
    {
        ("Ollama", "http://127.0.0.1:11434/v1"),
        ("LM Studio", "http://127.0.0.1:1234/v1"),
        ("llama.cpp", "http://127.0.0.1:8080/v1"),
        ("Jan", "http://127.0.0.1:1337/v1"),
    };

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    /// <summary>"localhost:11434" → "http://localhost:11434/v1".</summary>
    public static string NormalizeUrl(string url)
    {
        url = (url ?? "").Trim().TrimEnd('/');
        if (url.Length == 0) url = Known[0].Url;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "http://" + url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.AbsolutePath == "/" || u.AbsolutePath.Length == 0))
            url += "/v1";
        return url;
    }

    public static async Task<List<string>?> ListModelsAsync(string url)
    {
        try
        {
            var json = await Http.GetStringAsync(NormalizeUrl(url) + "/models");
            using var doc = JsonDocument.Parse(json);
            var list = new List<string>();
            if (doc.RootElement.TryGetProperty("data", out var data))
                foreach (var m in data.EnumerateArray())
                    if (m.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } s &&
                        !s.Contains("embed", StringComparison.OrdinalIgnoreCase))
                        list.Add(s);
            return list;
        }
        catch { return null; }
    }

    /// <summary>All known local servers that are running right now.</summary>
    public static async Task<List<Server>> DetectAsync(string? extraUrl = null)
    {
        var targets = Known.ToList();
        if (!string.IsNullOrWhiteSpace(extraUrl) && targets.All(t => t.Url != NormalizeUrl(extraUrl)))
            targets.Insert(0, ("自定义", NormalizeUrl(extraUrl)));
        var results = await Task.WhenAll(targets.Select(async t => (t, models: await ListModelsAsync(t.Url))));
        return results.Where(r => r.models != null).Select(r => new Server(r.t.Name, r.t.Url, r.models!)).ToList();
    }
}
