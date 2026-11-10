using System.Text.RegularExpressions;

namespace AIMouse.Translate;

public sealed class TranslationCache
{
    const int Max = 5000;
    static readonly Regex Ws = new(@"\s+", RegexOptions.Compiled);
    readonly Dictionary<string, string> _map = new();
    readonly Queue<string> _order = new();
    readonly object _lock = new();

    static string Norm(string en) => Ws.Replace(en, " ").Trim();

    public string? Get(string en)
    {
        lock (_lock) return _map.TryGetValue(Norm(en), out var zh) ? zh : null;
    }

    public void Set(string en, string zh)
    {
        var k = Norm(en);
        lock (_lock)
        {
            if (!_map.ContainsKey(k))
            {
                _order.Enqueue(k);
                while (_order.Count > Max) _map.Remove(_order.Dequeue());
            }
            _map[k] = zh;
        }
    }
}
