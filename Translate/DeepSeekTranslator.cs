using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AIMouse.Core;

namespace AIMouse.Translate;

public sealed class DeepSeekTranslator : ITranslator
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    static volatile bool _noThinking;
    const string BackupModel = "deepseek-chat";
    static DateTime _backupUntil = DateTime.MinValue;

    /// <summary>The configured cloud model, or DeepSeek's long-standing alias while the configured one is failing.</summary>
    string CloudModel
    {
        get
        {
            var m = string.IsNullOrWhiteSpace(_cfg.Model) ? "deepseek-flash" : _cfg.Model.Trim();
            return DateTime.UtcNow < _backupUntil ? BackupModel : m;
        }
    }
    static readonly Regex LineRx = new(@"^\s*\[(\d+)\]\s*(.*)$", RegexOptions.Compiled);
    static readonly Regex ThinkRx = new(@"<think>.*?(</think>|$)", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Local reasoning models may print their thinking inside &lt;think&gt; tags: drop it.</summary>
    static string StripThink(string s) => s.Contains("<think>") ? ThinkRx.Replace(s, "") : s;

    const string BatchSystem =
        "You are a professional English→Simplified Chinese translator helping a Chinese student read English. " +
        "Translate every numbered segment into natural, accurate Simplified Chinese. " +
        "Output exactly one line per segment in the form `[n] 译文`, same numbers, same order, no merging, no skipping, no extra text. " +
        "Keep names, code, URLs, numbers as they are. If a segment is a UI label or fragment, still translate it briefly.";

    const string LookupSystem =
        "You are an English dictionary for Chinese learners of English. Reply with ONLY one JSON object, no markdown, no explanation.";

    readonly Config _cfg;
    readonly string? _keyOverride;

    public DeepSeekTranslator(Config cfg, string? keyOverride = null)
    {
        _cfg = cfg;
        _keyOverride = keyOverride;
    }

    JsonObject Body(string system, string user, bool stream, int? maxTokens)
    {
        var o = new JsonObject
        {
            ["model"] = _cfg.UseLocal ? _cfg.LocalModel.Trim() : CloudModel,
            ["stream"] = stream,
            ["temperature"] = 0.3,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user }),
        };
        if (maxTokens is int mt) o["max_tokens"] = mt;
        if (!_noThinking && !_cfg.UseLocal) o["thinking"] = new JsonObject { ["type"] = "disabled" };
        return o;
    }

    async Task<HttpResponseMessage> PostAsync(Func<JsonObject> makeBody, bool stream, CancellationToken ct)
    {
        bool local = _cfg.UseLocal;
        var key = local ? "local" : _keyOverride ?? _cfg.GetApiKey();
        if (string.IsNullOrWhiteSpace(key)) throw new TranslateException("请先在设置里填写 DeepSeek API 密钥");
        if (local && string.IsNullOrWhiteSpace(_cfg.LocalModel)) throw new TranslateException("请先在设置里选择本地模型");
        var baseUrl = local ? LocalModels.NormalizeUrl(_cfg.LocalUrl)
                    : string.IsNullOrWhiteSpace(_cfg.BaseUrl) ? "https://api.deepseek.com" : _cfg.BaseUrl.Trim().TrimEnd('/');

        int serverRetries = 0;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/chat/completions");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
            req.Content = new StringContent(makeBody().ToJsonString(), Encoding.UTF8, "application/json");
            HttpResponseMessage resp;
            // A healthy DeepSeek reply starts within 1–2 s; a stuck one fails only after ~15 s. Don't wait for that.
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (!local) attemptCts.CancelAfter(TimeSpan.FromSeconds(9));
            try
            {
                resp = await Http.SendAsync(req, stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, attemptCts.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) when (!local)
            {
                Log.Write($"Translate attempt {attempt + 1} too slow, retrying");
                if (CloudModel != BackupModel)
                {
                    _backupUntil = DateTime.UtcNow.AddMinutes(10);
                    continue;
                }
                if (serverRetries++ < 3) continue;
                throw new TranslateException("DeepSeek 响应太慢，请稍后再试");
            }
            catch (Exception ex)
            {
                Log.Write("Translate network error: " + ex.Message);
                if (!local && serverRetries < 2 && ex is not TaskCanceledException)
                {
                    await Task.Delay(500 * ++serverRetries, ct); // brief network hiccup: try again
                    continue;
                }
                throw new TranslateException(local ? "连不上本地模型：请确认 Ollama / LM Studio 已经打开" : "网络连接失败");
            }
            if (resp.IsSuccessStatusCode) return resp;

            var text = await resp.Content.ReadAsStringAsync(ct);
            int code = (int)resp.StatusCode;
            resp.Dispose();
            if (code == 400 && !_noThinking && text.Contains("thinking", StringComparison.OrdinalIgnoreCase))
            {
                _noThinking = true;
                continue;
            }
            Log.Write($"Translate HTTP {code}: {text}");
            if (!local && code >= 500 && CloudModel != BackupModel)
            {
                // The configured model is failing on DeepSeek's side: use the backup name for the next 10 minutes.
                _backupUntil = DateTime.UtcNow.AddMinutes(10);
                Log.Write($"Switching to {BackupModel} for 10 minutes");
                continue;
            }
            if ((code >= 500 || code == 429) && serverRetries < 3)
            {
                await Task.Delay(code == 429 ? 1500 : 600 * ++serverRetries, ct); // server hiccup: wait a moment, try again
                if (code == 429) serverRetries++;
                continue;
            }
            if (local)
                throw new TranslateException(code == 404 || text.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    ? $"本地没有找到模型“{_cfg.LocalModel}”，请在设置里重新选择"
                    : $"本地模型出错（{code}）");
            throw new TranslateException(code switch
            {
                401 => "API 密钥不正确，请在设置里检查",
                402 => "DeepSeek 余额不足",
                429 => "请求太频繁，请稍后再试",
                400 when text.Contains("model", StringComparison.OrdinalIgnoreCase) => "模型名称不正确，请在设置里检查",
                >= 500 => $"DeepSeek 服务器暂时出错（{code}），请稍后再试",
                _ => $"翻译服务出错（{code}）",
            });
        }
        throw new TranslateException("翻译服务出错");
    }

    async Task StreamAsync(Func<JsonObject> makeBody, Action<string> onDelta, CancellationToken ct)
    {
        using var resp = await PostAsync(makeBody, true, ct);
        using var s = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(s, Encoding.UTF8);
        while (true)
        {
            string? line;
            try { line = await reader.ReadLineAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Log.Write("DeepSeek stream error: " + ex.Message);
                throw new TranslateException("网络连接中断");
            }
            if (line == null) break;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") break;
            if (data.Length == 0) continue;
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (!doc.RootElement.TryGetProperty("choices", out var ch) || ch.GetArrayLength() == 0) continue;
                if (!ch[0].TryGetProperty("delta", out var delta)) continue;
                if (delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                {
                    var t = c.GetString();
                    if (!string.IsNullOrEmpty(t)) onDelta(t);
                }
            }
            catch (JsonException) { }
        }
    }

    public async Task TranslateAsync(IReadOnlyList<string> segments, Action<int, string> onPartial, CancellationToken ct)
    {
        var batches = new List<List<int>>();
        var cur = new List<int>();
        int chars = 0;
        for (int i = 0; i < segments.Count; i++)
        {
            int len = segments[i].Length;
            if (cur.Count > 0 && (cur.Count >= 30 || chars + len > 2500))
            {
                batches.Add(cur);
                cur = new List<int>();
                chars = 0;
            }
            cur.Add(i);
            chars += len;
        }
        if (cur.Count > 0) batches.Add(cur);

        using var sem = new SemaphoreSlim(3);
        var tasks = batches.Select(async b =>
        {
            await sem.WaitAsync(ct);
            try { await TranslateBatchAsync(segments, b, onPartial, ct, retry: true); }
            finally { sem.Release(); }
        }).ToList();
        await Task.WhenAll(tasks);
    }

    async Task TranslateBatchAsync(IReadOnlyList<string> segs, List<int> idx, Action<int, string> onPartial, CancellationToken ct, bool retry)
    {
        var user = new StringBuilder();
        for (int k = 0; k < idx.Count; k++)
            user.Append('[').Append(k + 1).Append("] ").Append(segs[idx[k]].Replace('\n', ' ')).Append('\n');

        var got = new Dictionary<int, string>();
        var acc = new StringBuilder();
        await StreamAsync(() => Body(BatchSystem, user.ToString(), true, null), delta =>
        {
            acc.Append(delta);
            foreach (var ln in StripThink(acc.ToString()).Split('\n'))
            {
                var m = LineRx.Match(ln);
                if (!m.Success || !int.TryParse(m.Groups[1].Value, out var n) || n < 1 || n > idx.Count) continue;
                var t = m.Groups[2].Value.Trim();
                if (t.Length == 0) continue;
                if (!got.TryGetValue(n, out var old) || old != t)
                {
                    got[n] = t;
                    onPartial(idx[n - 1], t);
                }
            }
        }, ct);

        var missing = Enumerable.Range(1, idx.Count).Where(n => !got.ContainsKey(n)).Select(n => idx[n - 1]).ToList();
        if (missing.Count == 0) return;
        if (retry) await TranslateBatchAsync(segs, missing, onPartial, ct, retry: false);
        else foreach (var i in missing) onPartial(i, "（翻译失败）");
    }

    public async Task<WordInfo> LookupAsync(string word, string sentence, CancellationToken ct)
    {
        var user =
            $"Word: {word}\nSentence: {sentence}\n" +
            "Return exactly: {\"word\":\"<dictionary form>\",\"phonetic\":\"<US IPA with slashes>\",\"pos\":\"<adj./n./v./adv./prep./conj./...>\"," +
            "\"meaning\":\"<Chinese meaning of the word in THIS sentence, at most 12 Chinese characters>\"," +
            "\"note\":\"<optional Chinese hint at most 24 characters, e.g. another common meaning or a fixed phrase; empty string if none>\"," +
            "\"sentence_zh\":\"<natural Chinese translation of the sentence>\"}";
        using var resp = await PostAsync(() => Body(LookupSystem, user, false, 400), false, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            string content = "";
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object &&
                msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                content = StripThink(c.GetString() ?? "");
            if (content.Length == 0)
            {
                Log.Write("Lookup empty reply: " + (json.Length > 600 ? json[..600] : json));
                throw new TranslateException("查词没有返回内容，请再指一次");
            }
            int a = content.IndexOf('{'), b = content.LastIndexOf('}');
            if (a < 0 || b <= a) throw new TranslateException("查词失败");
            var info = JsonSerializer.Deserialize<WordInfo>(content[a..(b + 1)], new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (info == null) throw new TranslateException("查词失败");
            if (string.IsNullOrWhiteSpace(info.Word)) info.Word = word;
            return info;
        }
        catch (JsonException ex)
        {
            Log.Write("Lookup parse error: " + ex.Message + " :: " + json);
            throw new TranslateException("查词失败");
        }
    }

    public async Task<string> TestAsync(CancellationToken ct)
    {
        string result = "";
        await TranslateAsync(new[] { "Hello, nice to meet you." }, (_, zh) => result = zh, ct);
        return result;
    }
}
