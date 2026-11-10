using System.Windows;
using System.Windows.Media;
using AIMouse.Core;
using AIMouse.Native;
using AIMouse.Translate;

namespace AIMouse.UI;

public partial class WordCard : Window
{
    WordInfo? _info;
    string _sentence = "";
    RECT _line;
    int _anchorX;

    public WordCard()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowHelper.MakeToolWindow(this, clickThrough: false);
        var sv = Speech.Available ? Visibility.Visible : Visibility.Collapsed;
        SpeakBtn.Visibility = sv;
        PhraseSpeakBtn.Visibility = sv;
        SentenceSpeakBtn.Visibility = sv;
    }

    public void ShowLoading(string word, string sentence)
    {
        _info = null;
        _sentence = sentence;
        WordPanel.Visibility = Visibility.Visible;
        PhrasePanel.Visibility = Visibility.Collapsed;
        WordText.Text = word;
        PhoneticText.Text = "";
        PosRun.Text = "";
        MeaningRun.Text = "…";
        MeaningRun.Foreground = Brush(0x1D, 0x1D, 0x1F);
        NoteText.Visibility = Visibility.Collapsed;
        SentenceBox.Visibility = Visibility.Collapsed;
        SourceText.Visibility = Visibility.Collapsed;
        StarBtn.IsEnabled = false;
        SetStar(false);
    }

    public void ShowInfo(WordInfo info)
    {
        _info = info;
        WordText.Text = string.IsNullOrWhiteSpace(info.Word) ? WordText.Text : info.Word;
        PhoneticText.Text = info.Phonetic;
        PosRun.Text = string.IsNullOrWhiteSpace(info.Pos) ? "" : info.Pos + "  ";
        MeaningRun.Text = info.Meaning;
        MeaningRun.Foreground = Brush(0x1D, 0x1D, 0x1F);
        NoteText.Text = info.Note;
        NoteText.Visibility = string.IsNullOrWhiteSpace(info.Note) ? Visibility.Collapsed : Visibility.Visible;
        SentenceText.Text = info.SentenceZh;
        SentenceBox.Visibility = string.IsNullOrWhiteSpace(info.SentenceZh) ? Visibility.Collapsed : Visibility.Visible;
        StarBtn.IsEnabled = true;
        SourceText.Visibility = Visibility.Collapsed;
    }

    /// <summary>Offline dictionary result shown instantly; the AI's in-context meaning may replace it shortly.</summary>
    public void ShowDict(WordInfo info, bool aiPending)
    {
        ShowInfo(info);
        SourceText.Text = aiPending ? "离线词典 · AI 释义加载中…" : "离线词典";
        SourceText.Visibility = Visibility.Visible;
    }

    /// <summary>The AI could not answer: keep the dictionary meaning and say why.</summary>
    public void MarkOffline(string reason)
    {
        SourceText.Text = "离线词典（AI 暂时用不了：" + reason + "）";
        SourceText.Visibility = Visibility.Visible;
    }

    public bool ShowingDict => _info?.Offline == true;

    public void ShowError(string message)
    {
        PosRun.Text = "";
        MeaningRun.Text = message;
        MeaningRun.Foreground = Brush(0xFF, 0x3B, 0x30);
    }

    public void ShowPhrase(string en)
    {
        WordPanel.Visibility = Visibility.Collapsed;
        PhrasePanel.Visibility = Visibility.Visible;
        PhraseEn.Text = en;
        PhraseZh.Text = "…";
        PhraseZh.Foreground = Brush(0x1D, 0x1D, 0x1F);
    }

    public void SetPhraseZh(string zh) => PhraseZh.Text = string.IsNullOrEmpty(zh) ? "…" : zh;

    public void SetPhraseError(string message)
    {
        PhraseZh.Text = message;
        PhraseZh.Foreground = Brush(0xFF, 0x3B, 0x30);
    }

    /// <summary>Place the card just below the given line (never over it); above if there is no room.</summary>
    public void PlaceNear(RECT line, int anchorX)
    {
        _line = line;
        _anchorX = anchorX;
        Reposition();
    }

    public void Reposition()
    {
        var mon = WindowHelper.GetMonitor(new POINT(_anchorX, _line.Bottom));
        double s = mon.Scale;
        if (!IsVisible) WindowHelper.MovePhysical(this, -32000, -32000);
        UpdateLayout();
        int cw = (int)Math.Ceiling(ActualWidth * s), ch = (int)Math.Ceiling(ActualHeight * s);
        int m = (int)(14 * s), gap = (int)(6 * s);
        var wa = mon.Work;
        int x = _anchorX - m;
        int y = _line.Bottom + gap - m;
        if (y + ch > wa.Bottom) y = _line.Top - gap - ch + m;
        x = Math.Max(wa.Left, Math.Min(x, wa.Right - cw));
        y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - ch));
        WindowHelper.MovePhysical(this, x, y);
    }

    void Speak_Click(object sender, RoutedEventArgs e) => Speech.Toggle(WordText.Text);

    void PhraseSpeak_Click(object sender, RoutedEventArgs e) => Speech.Toggle(PhraseEn.Text);

    void Word_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => Speech.Say(WordText.Text);

    void PhraseEn_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e) => TextHit.SpeakWordUnderMouse(sender, e);

    void PhraseEn_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) => TextHit.UpdateCursor(sender, e);

    void SentenceSpeak_Click(object sender, RoutedEventArgs e) => Speech.Toggle(_sentence);

    void Star_Click(object sender, RoutedEventArgs e)
    {
        if (_info == null) return;
        WordBook.Add(_info, _sentence);
        SetStar(true);
    }

    void SetStar(bool filled)
    {
        StarBtn.Content = filled ? "\uE735" : "\uE734";
        StarBtn.Foreground = filled ? Brush(0xFF, 0x9F, 0x0A) : Brush(0x6E, 0x6E, 0x73);
        StarBtn.ToolTip = filled ? "已加入生词本" : "加入生词本";
    }

    static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));
}
