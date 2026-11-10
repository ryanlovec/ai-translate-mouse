using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AIMouse.Native;

namespace AIMouse.UI;

public enum ViewMode { Side, Under, Replace }

/// <summary>Three-way switch 侧栏 / 对照 / 译文 used in the panel header and the floating bar.</summary>
public sealed class ViewSwitch : Border
{
    readonly RadioButton[] _btns;
    public event Action<ViewMode>? Changed;
    bool _setting;

    public ViewSwitch()
    {
        Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xED));
        CornerRadius = new CornerRadius(7);
        Padding = new Thickness(2);
        VerticalAlignment = VerticalAlignment.Center;
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        string group = "view" + Guid.NewGuid().ToString("N");
        _btns = new[] { ("侧栏", ViewMode.Side, "译文显示在旁边"), ("对照", ViewMode.Under, "在原文位置显示：每句英文下面跟着中文"), ("译文", ViewMode.Replace, "直接用中文盖住英文（Esc 退出）") }
            .Select(t =>
            {
                var rb = new RadioButton
                {
                    Content = t.Item1, GroupName = group, Tag = t.Item2, ToolTip = t.Item3,
                    Style = (Style)Application.Current.FindResource("SegBtn"), FontSize = 11.5, MinWidth = 0, Focusable = false,
                };
                rb.Checked += (_, _) => { if (!_setting) Changed?.Invoke((ViewMode)rb.Tag); };
                panel.Children.Add(rb);
                return rb;
            }).ToArray();
        foreach (var b in _btns) b.Template = SmallSeg();
        Child = panel;
    }

    /// <summary>Compact version of the segmented button.</summary>
    static ControlTemplate SmallSeg()
    {
        var t = new ControlTemplate(typeof(RadioButton));
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        bd.SetValue(Border.PaddingProperty, new Thickness(9, 2, 9, 3));
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        bd.AppendChild(cp);
        t.VisualTree = bd;
        var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        on.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.White, "Bd"));
        on.Setters.Add(new Setter(Border.EffectProperty, new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Direction = 270, Opacity = 0.12 }, "Bd"));
        on.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        t.Triggers.Add(on);
        return t;
    }

    public void Set(ViewMode v)
    {
        _setting = true;
        foreach (var b in _btns) b.IsChecked = (ViewMode)b.Tag == v;
        _setting = false;
    }
}

/// <summary>Small floating pill shown in 译文 mode: switch view, status, close.</summary>
public sealed class ModeBar : Window
{
    public ViewSwitch Switch { get; } = new();
    readonly TextBlock _status;
    public event Action? CloseRequested;

    public ModeBar()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        UseLayoutRounding = true;
        FontFamily = (FontFamily)Application.Current.FindResource("UIFont");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        _status = new TextBlock
        {
            FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x86, 0x86, 0x8B)), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 6, 0), MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var close = new Button { Style = (Style)Application.Current.FindResource("IconBtn"), Content = "", ToolTip = "退出（Esc）", Width = 26, Height = 26, FontSize = 11 };
        close.Click += (_, _) => CloseRequested?.Invoke();
        var logo = new Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo_256.png")),
            Width = 16, Height = 16, Margin = new Thickness(2, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(logo);
        row.Children.Add(Switch);
        row.Children.Add(_status);
        row.Children.Add(close);
        Content = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(8, 5, 5, 5),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0xF5, 0xFC, 0xFC, 0xFD)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xE3, 0xE8)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Direction = 270, Opacity = 0.16 },
            Child = row,
        };
        SourceInitialized += (_, _) => WindowHelper.MakeToolWindow(this, clickThrough: false);
    }

    public void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.Foreground = new SolidColorBrush(error ? Color.FromRgb(0xFF, 0x3B, 0x30) : Color.FromRgb(0x86, 0x86, 0x8B));
    }

    /// <summary>Top-center of the region (inside it when the region touches the top of the screen).</summary>
    public void PlaceAt(RECT region)
    {
        var mon = WindowHelper.GetMonitor(region.Center);
        double s = mon.Scale;
        if (!IsVisible) WindowHelper.MovePhysical(this, -32000, -32000);
        UpdateLayout();
        int w = (int)Math.Ceiling(ActualWidth * s), h = (int)Math.Ceiling(ActualHeight * s);
        int x = region.Left + (region.Width - w) / 2;
        int y = region.Top - h + (int)(10 * s);
        if (y < mon.Work.Top) y = Math.Max(mon.Work.Top, region.Top) + (int)(2 * s);
        x = Math.Clamp(x, mon.Work.Left, Math.Max(mon.Work.Left, mon.Work.Right - w));
        WindowHelper.MovePhysical(this, x, y);
    }
}
