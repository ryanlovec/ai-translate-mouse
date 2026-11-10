using System.Windows.Input;
using static AIMouse.Native.Win32;

namespace AIMouse.Input;

/// <summary>
/// A hold-down key combination such as "Ctrl+Win", "Ctrl+Alt+Space", "F8" or "RCtrl".
/// Tokens: Ctrl / Shift / Alt / Win (either side), LCtrl / RCtrl / LAlt / RAlt / LShift / RShift (one side),
/// or any WPF <see cref="Key"/> name ("Space", "F8", "CapsLock", "Oem3"…).
/// </summary>
public sealed class HotKey
{
    public const string Default = "Ctrl+Win";
    public IReadOnlyList<string> Tokens { get; }

    HotKey(List<string> tokens) => Tokens = tokens;

    static readonly string[] ModOrder = { "Ctrl", "LCtrl", "RCtrl", "Shift", "LShift", "RShift", "Alt", "LAlt", "RAlt", "Win" };

    public static HotKey Parse(string? s)
    {
        var t = (s ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var hk = new HotKey(t);
        return hk.Tokens.Count == 0 || hk.Tokens.Any(x => VksOf(x).Length == 0) || Problem(hk) != null ? new HotKey(Default.Split('+').ToList()) : hk;
    }

    public override string ToString() => string.Join("+", Tokens);

    /// <summary>For the UI: "Ctrl + Win", "右 Alt", "F8"…</summary>
    public string Display => string.Join(" + ", Tokens.Select(DisplayToken));

    public IEnumerable<string> DisplayTokens => Tokens.Select(DisplayToken);

    static string DisplayToken(string t) => t switch
    {
        "LCtrl" => "左 Ctrl", "RCtrl" => "右 Ctrl", "LAlt" => "左 Alt", "RAlt" => "右 Alt", "LShift" => "左 Shift", "RShift" => "右 Shift",
        "Space" => "空格", "Capital" => "CapsLock", "Oem3" => "`", "Scroll" => "ScrollLock", "Apps" => "菜单键",
        _ => t,
    };

    public static bool IsModifierToken(string t) => ModOrder.Contains(t);
    public bool HasWin => Tokens.Contains("Win");
    public bool HasAlt => Tokens.Any(t => t is "Alt" or "LAlt" or "RAlt");

    /// <summary>The virtual-key codes a token stands for.</summary>
    public static uint[] VksOf(string t) => t switch
    {
        "Ctrl" => new[] { VK_LCONTROL, VK_RCONTROL, VK_CONTROL },
        "LCtrl" => new[] { VK_LCONTROL },
        "RCtrl" => new[] { VK_RCONTROL },
        "Shift" => new uint[] { 0xA0, 0xA1, 0x10 },
        "LShift" => new uint[] { 0xA0 },
        "RShift" => new uint[] { 0xA1 },
        "Alt" => new uint[] { 0xA4, 0xA5, 0x12 },
        "LAlt" => new uint[] { 0xA4 },
        "RAlt" => new uint[] { 0xA5 },
        "Win" => new uint[] { 0x5B, 0x5C },
        _ => Enum.TryParse<Key>(t, out var k) && k != Key.None ? new[] { (uint)KeyInterop.VirtualKeyFromKey(k) } : Array.Empty<uint>(),
    };

    public bool Uses(uint vk) => Tokens.Any(t => VksOf(t).Contains(vk));
    public bool IsMainKey(uint vk) => Tokens.Any(t => !IsModifierToken(t) && VksOf(t).Contains(vk));

    /// <summary>True if every part of the combination is held.</summary>
    public bool AllHeld(ICollection<uint> held) => Tokens.All(t => VksOf(t).Any(held.Contains));

    /// <summary>A single right Ctrl as the talk key: the translate gestures must then ignore that key.</summary>
    public bool OwnsCtrlKey(uint vk) => Tokens.Count == 1 && Tokens[0] is "RCtrl" or "LCtrl" && VksOf(Tokens[0]).Contains(vk);

    /// <summary>Build from the keys held during a key-capture (VK codes in press order).</summary>
    public static HotKey FromVks(IReadOnlyList<uint> vks)
    {
        var mods = new List<string>();
        var main = new List<string>();
        foreach (var vk in vks.Distinct())
        {
            string? side = vk switch
            {
                VK_LCONTROL => "LCtrl", VK_RCONTROL => "RCtrl", 0xA0 => "LShift", 0xA1 => "RShift",
                0xA4 => "LAlt", 0xA5 => "RAlt", 0x5B or 0x5C => "Win", _ => null,
            };
            if (side != null) mods.Add(side);
            else
            {
                var k = KeyInterop.KeyFromVirtualKey((int)vk);
                if (k != Key.None) main.Add(k.ToString());
            }
        }
        // In a combination either side of a modifier works; alone, the side matters (so plain Ctrl stays free).
        if (mods.Count + main.Count > 1)
            mods = mods.Select(m => m switch { "LCtrl" or "RCtrl" => "Ctrl", "LShift" or "RShift" => "Shift", "LAlt" or "RAlt" => "Alt", _ => m }).Distinct().ToList();
        mods = mods.OrderBy(m => Array.IndexOf(ModOrder, m)).ToList();
        return new HotKey(mods.Concat(main.Take(1)).ToList());
    }

    /// <summary>Why this combination would get in the way of normal typing, or null if it's fine.</summary>
    public static string? Problem(HotKey hk)
    {
        var t = hk.Tokens;
        if (t.Count == 0) return "没有按到键";
        if (t.Count == 1)
        {
            var only = t[0];
            if (only is "LCtrl" or "Ctrl" or "Win" or "LShift" or "RShift" or "Shift" or "LAlt" or "Alt") return "单个这个键会影响正常使用，请加一个组合键";
            if (Enum.TryParse<Key>(only, out var k) &&
                (k is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or Key.Space or Key.Enter or Key.Tab or Key.Back or Key.Escape or Key.Delete
                 || only.StartsWith("Oem") || only.StartsWith("NumPad") || k is Key.Left or Key.Right or Key.Up or Key.Down))
                return "单个这个键打字时会用到，请加 Ctrl / Alt 等组合键";
        }
        if (t.Count == 2 && t.Contains("Ctrl") && t.Contains("Space")) return null; // allowed, the UI warns about input methods
        if (t.Count >= 2 && t.All(IsModifierToken) && t.Contains("Shift") && t.Contains("Ctrl") && t.Count == 2) return "Ctrl + Shift 是 Windows 切换输入法的键，请换一个";
        if (t.Count >= 2 && t.All(IsModifierToken) && t.Contains("Shift") && t.Contains("Alt") && t.Count == 2) return "Alt + Shift 是 Windows 切换输入法的键，请换一个";
        if (t.Contains("Esc") || t.Contains("Escape")) return "Esc 用来退出，请换一个";
        return null;
    }

    /// <summary>Extra warning (still allowed).</summary>
    public static string? Caution(HotKey hk) =>
        hk.Tokens.Contains("Space") && hk.Tokens.Contains("Ctrl") ? "注意：中文输入法常用 Ctrl + 空格 切换中英文" : null;
}
