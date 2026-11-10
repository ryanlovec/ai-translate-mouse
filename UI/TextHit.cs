using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AIMouse.UI;

/// <summary>Click an English word inside a read-only TextBox → it is highlighted and read aloud.</summary>
public static class TextHit
{
    static TextBox? _lastSelected;

    public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '\'' || c == '-' || c == '’';

    /// <summary>The word (start, length) under a point in the TextBox, without surrounding quotes/hyphens.</summary>
    public static (int Start, int Length)? WordAt(TextBox tb, Point local)
    {
        int idx = tb.GetCharacterIndexFromPoint(local, false);
        var text = tb.Text;
        if (idx < 0 || idx >= text.Length || !IsWordChar(text[idx])) return null;
        int a = idx, b = idx;
        while (a > 0 && IsWordChar(text[a - 1])) a--;
        while (b < text.Length - 1 && IsWordChar(text[b + 1])) b++;
        while (a <= b && !char.IsLetterOrDigit(text[a])) a++;
        while (b >= a && !char.IsLetterOrDigit(text[b])) b--;
        if (a > b || !text.Substring(a, b - a + 1).Any(char.IsLetter)) return null;
        return (a, b - a + 1);
    }

    /// <summary>MouseLeftButtonUp handler for English text boxes.</summary>
    public static void SpeakWordUnderMouse(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox tb) return;
        var hit = WordAt(tb, e.GetPosition(tb));
        if (hit == null) return;
        if (_lastSelected != null && !ReferenceEquals(_lastSelected, tb)) _lastSelected.Select(0, 0);
        tb.Select(hit.Value.Start, hit.Value.Length);
        _lastSelected = tb;
        Core.Speech.Say(tb.Text.Substring(hit.Value.Start, hit.Value.Length));
        e.Handled = true;
    }

    /// <summary>Show a hand cursor only while over a word.</summary>
    public static void UpdateCursor(object sender, MouseEventArgs e)
    {
        if (sender is TextBox tb) tb.Cursor = WordAt(tb, e.GetPosition(tb)) != null ? Cursors.Hand : Cursors.Arrow;
    }
}
