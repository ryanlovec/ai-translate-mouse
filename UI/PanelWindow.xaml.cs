using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIMouse.Native;

namespace AIMouse.UI;

public sealed class SentenceItem : INotifyPropertyChanged
{
    static readonly Brush Normal = Freeze(new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1F)));
    static readonly Brush Pending = Freeze(new SolidColorBrush(Color.FromRgb(0xAE, 0xAE, 0xB2)));
    static readonly Brush HighlightBg = Freeze(new SolidColorBrush(Color.FromRgb(0xEE, 0xF5, 0xFF)));

    string _zh = "";
    bool _highlighted;

    public string En { get; init; } = "";
    public List<RECT> Fragments { get; init; } = new();
    public int Para { get; init; }

    public string Zh
    {
        get => _zh;
        set { if (_zh == value) return; _zh = value; OnChanged(); OnChanged(nameof(ZhDisplay)); OnChanged(nameof(ZhBrush)); }
    }

    public bool IsHighlighted
    {
        get => _highlighted;
        set { if (_highlighted == value) return; _highlighted = value; OnChanged(); OnChanged(nameof(Background)); }
    }

    public string ZhDisplay => string.IsNullOrEmpty(_zh) ? "…" : _zh;
    public Brush ZhBrush => string.IsNullOrEmpty(_zh) ? Pending : Normal;
    public Brush Background => _highlighted ? HighlightBg : Brushes.Transparent;

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}

public record PanelWordHit(string Word, string Sentence, RECT WordRect);

public partial class PanelWindow : Window
{
    public event Action? CloseRequested;
    public event Action? PinToggled;
    public event Action<SentenceItem?>? ItemHovered;

    public bool UserMoved { get; private set; }
    public ViewSwitch ViewSwitch { get; } = new();
    ObservableCollection<SentenceItem> _items = new();

    public PanelWindow()
    {
        InitializeComponent();
        Items.ItemsSource = _items;
        SwitchHost.Content = ViewSwitch;
        SourceInitialized += (_, _) => WindowHelper.MakeToolWindow(this, clickThrough: false);
        SizeChanged += (_, e) => { if (IsMouseOver && Mouse.LeftButton == MouseButtonState.Pressed) UserMoved = true; };
        Closed += (_, _) => { if (_reading) Core.Speech.Stop(); };
        if (!Core.Speech.Available) ReadAllBtn.Visibility = Visibility.Collapsed;
    }

    bool _reading;

    void ReadAll_Click(object sender, RoutedEventArgs e)
    {
        if (_reading)
        {
            Core.Speech.Stop();
            return;
        }
        var list = _items.Where(i => i.En.Length > 0).ToList();
        if (list.Count == 0) return;
        _reading = true;
        ReadAllBtn.Content = "";
        ReadAllBtn.ToolTip = "停止朗读";
        Core.Speech.SpeakAll(list.Select(i => i.En).ToList(), k =>
        {
            var it = list[k];
            foreach (var x in _items) x.IsHighlighted = ReferenceEquals(x, it);
            ScrollIntoView(it);
            ItemHovered?.Invoke(it);
        }, () =>
        {
            _reading = false;
            ReadAllBtn.Content = "";
            ReadAllBtn.ToolTip = "从头朗读全部英文（屏幕上会跟着框出正在读的句子）";
            foreach (var x in _items) x.IsHighlighted = false;
            ItemHovered?.Invoke(null);
        });
    }

    void En_MouseUp(object sender, MouseButtonEventArgs e) => TextHit.SpeakWordUnderMouse(sender, e);

    void En_MouseMove(object sender, MouseEventArgs e) => TextHit.UpdateCursor(sender, e);

    void ItemSpeak_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SentenceItem it }) Core.Speech.Toggle(it.En);
    }

    public void SetMode(string title, bool live, bool showPin)
    {
        TitleText.Text = title;
        LiveDot.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        PinBtn.Visibility = showPin ? Visibility.Visible : Visibility.Collapsed;
        PinBtn.Foreground = new SolidColorBrush(live ? Color.FromRgb(0x00, 0x7A, 0xFF) : Color.FromRgb(0x6E, 0x6E, 0x73));
        PinBtn.ToolTip = live ? "停止实时翻译" : "实时翻译：内容变化（滚动）时自动更新";
    }

    public void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = new SolidColorBrush(error ? Color.FromRgb(0xFF, 0x3B, 0x30) : Color.FromRgb(0x86, 0x86, 0x8B));
    }

    public void SetItems(IEnumerable<SentenceItem> items)
    {
        _items = new ObservableCollection<SentenceItem>(items);
        Items.ItemsSource = _items;
        Scroller.ScrollToTop();
    }

    public IReadOnlyList<SentenceItem> CurrentItems => _items;

    public void ScrollIntoView(SentenceItem item)
    {
        if (Items.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement fe) fe.BringIntoView();
    }

    /// <summary>Find the English word under a physical screen point inside the panel.</summary>
    public PanelWordHit? TryGetWordAt(POINT p)
    {
        foreach (var tb in FindVisualChildren<TextBox>(Items))
        {
            if (!tb.IsVisible || tb.ActualWidth <= 0) continue;
            Point local;
            try { local = tb.PointFromScreen(new Point(p.X, p.Y)); }
            catch (InvalidOperationException) { continue; }
            if (local.X < 0 || local.Y < 0 || local.X > tb.ActualWidth || local.Y > tb.ActualHeight) continue;
            int idx = tb.GetCharacterIndexFromPoint(local, false);
            if (idx < 0) return null;
            var text = tb.Text;
            if (idx >= text.Length || !IsWordChar(text[idx])) return null;
            int a = idx, b = idx;
            while (a > 0 && IsWordChar(text[a - 1])) a--;
            while (b < text.Length - 1 && IsWordChar(text[b + 1])) b++;
            var word = text[a..(b + 1)];
            var r1 = tb.GetRectFromCharacterIndex(a);
            var r2 = tb.GetRectFromCharacterIndex(b, true);
            var tl = tb.PointToScreen(new Point(r1.Left, r1.Top));
            var br = tb.PointToScreen(new Point(r2.Right, r2.Bottom));
            if (br.Y <= tl.Y + 1) br = tb.PointToScreen(new Point(r1.Left + 40, r1.Bottom));
            var rect = new RECT((int)tl.X, (int)tl.Y, (int)Math.Max(br.X, tl.X + 4), (int)Math.Max(br.Y, tl.Y + 4));
            return new PanelWordHit(word, text, rect);
        }
        return null;
    }

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '\'' || c == '-' || c == '’';

    static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in FindVisualChildren<T>(child)) yield return d;
        }
    }

    void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try
        {
            DragMove();
            UserMoved = true;
        }
        catch (InvalidOperationException) { }
    }

    void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset - e.Delta / 2.0);
        e.Handled = true;
    }

    void Item_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SentenceItem it }) ItemHovered?.Invoke(it);
    }

    void Item_MouseLeave(object sender, MouseEventArgs e) => ItemHovered?.Invoke(null);

    void Pin_Click(object sender, RoutedEventArgs e) => PinToggled?.Invoke();

    void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        foreach (var it in _items) sb.Append(it.En).Append('\n').Append(it.Zh).Append("\n\n");
        try
        {
            Clipboard.SetText(sb.ToString().TrimEnd());
            SetStatus("已复制");
        }
        catch (Exception ex) { Core.Log.Write("Clipboard failed: " + ex.Message); }
    }
}
