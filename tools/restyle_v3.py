# One-off restyle to the Apple-like look (v0.3). Safe to re-run: replacements are idempotent.
import os
os.chdir(os.path.join(os.path.dirname(__file__), ".."))


def edit(path, pairs):
    s = open(path, encoding="utf-8").read()
    o = s
    for a, b in pairs:
        s = s.replace(a, b)
    if s != o:
        open(path, "w", encoding="utf-8").write(s)
        print("updated", path)


edit("UI/PanelWindow.xaml", [
    ('''    <Border Margin="8" CornerRadius="12" Background="#F7F9FC" BorderBrush="#D5DCE8" BorderThickness="1">
        <Border.Effect>
            <DropShadowEffect BlurRadius="16" ShadowDepth="2" Opacity="0.18" />
        </Border.Effect>''',
     '''    <Border Margin="14" CornerRadius="14" Background="#FCFCFD" BorderBrush="#E3E3E8" BorderThickness="1"
            TextOptions.TextFormattingMode="Display" UseLayoutRounding="True">
        <Border.Effect>
            <DropShadowEffect BlurRadius="28" ShadowDepth="8" Direction="270" Opacity="0.14" />
        </Border.Effect>'''),
    ('<Border DockPanel.Dock="Top" Height="40" Background="Transparent" Padding="12,0,6,0"',
     '<Border DockPanel.Dock="Top" Height="44" Background="Transparent" Padding="14,0,8,0"'),
    ('<Image Source="pack://application:,,,/Assets/logo_256.png" Width="18" Height="18" Margin="0,0,8,0"',
     '<Image Source="pack://application:,,,/Assets/logo_256.png" Width="16" Height="16" Margin="0,0,8,0"'),
    ('<TextBlock x:Name="TitleText" FontSize="14" FontWeight="SemiBold" Foreground="#111827" FontFamily="Microsoft YaHei UI"',
     '<TextBlock x:Name="TitleText" FontSize="13" FontWeight="SemiBold" Foreground="#1D1D1F" FontFamily="{StaticResource UIFont}"'),
    ('<Ellipse x:Name="LiveDot" Width="7" Height="7" Fill="#22C55E"', '<Ellipse x:Name="LiveDot" Width="6" Height="6" Fill="#34C759"'),
    ('<TextBlock x:Name="StatusText" FontSize="12" Foreground="#6B7280" Margin="10,0,0,0" FontFamily="Microsoft YaHei UI"',
     '<TextBlock x:Name="StatusText" FontSize="12" Foreground="#86868B" Margin="8,0,0,0" FontFamily="{StaticResource UIFont}"'),
    ('<Border DockPanel.Dock="Top" Height="1" Background="#E3E8F0" />', '<Border DockPanel.Dock="Top" Height="1" Background="#EDEDF0" Margin="14,0" />'),
    ('Padding="6,6,6,16" PreviewMouseWheel', 'Padding="8,8,8,16" PreviewMouseWheel'),
    ('<Border CornerRadius="6" Padding="10,8" Margin="0,0,0,2" Background="{Binding Background}"',
     '<Border CornerRadius="10" Padding="12,10" Margin="0,0,0,2" Background="{Binding Background}"'),
    ('FontSize="13" Foreground="#4B5563" Padding="0" Margin="-2,0,0,0"', 'FontSize="13" Foreground="#6E6E73" Padding="0" Margin="-2,0,0,0"'),
    ('Cursor="Arrow" FontFamily="Segoe UI" IsTabStop="False"', 'Cursor="Arrow" FontFamily="{StaticResource UIFont}" IsTabStop="False"'),
    ('SelectionBrush="#4C6EF5"', 'SelectionBrush="#007AFF"'),
    ('FontFamily="Microsoft YaHei UI" Margin="0,3,0,0" />', 'FontFamily="Microsoft YaHei UI" Margin="0,4,0,0" LineHeight="22" />'),
])

edit("UI/PanelWindow.xaml.cs", [
    ("Color.FromRgb(0x11, 0x18, 0x27)", "Color.FromRgb(0x1D, 0x1D, 0x1F)"),
    ("Color.FromRgb(0x9C, 0xA3, 0xAF)", "Color.FromRgb(0xAE, 0xAE, 0xB2)"),
    ("Color.FromRgb(0xFF, 0xF4, 0xD6)", "Color.FromRgb(0xEE, 0xF5, 0xFF)"),
    ("Color.FromRgb(0x4C, 0x6E, 0xF5) : Color.FromRgb(0x37, 0x41, 0x51)", "Color.FromRgb(0x00, 0x7A, 0xFF) : Color.FromRgb(0x6E, 0x6E, 0x73)"),
    ("error ? Color.FromRgb(0xDC, 0x26, 0x26) : Color.FromRgb(0x6B, 0x72, 0x80)", "error ? Color.FromRgb(0xFF, 0x3B, 0x30) : Color.FromRgb(0x86, 0x86, 0x8B)"),
])

edit("UI/WordCard.xaml", [
    ('''    <Border x:Name="Card" Margin="10" CornerRadius="12" Background="#FFFFFF" BorderBrush="#D5DCE8" BorderThickness="1"
            Padding="12,10,8,10" MinWidth="170" MaxWidth="360">
        <Border.Effect>
            <DropShadowEffect BlurRadius="14" ShadowDepth="2" Opacity="0.18" />
        </Border.Effect>''',
     '''    <Border x:Name="Card" Margin="14" CornerRadius="14" Background="#FCFCFD" BorderBrush="#E3E3E8" BorderThickness="1"
            Padding="16,12,10,13" MinWidth="190" MaxWidth="370" TextOptions.TextFormattingMode="Display" UseLayoutRounding="True">
        <Border.Effect>
            <DropShadowEffect BlurRadius="24" ShadowDepth="6" Direction="270" Opacity="0.14" />
        </Border.Effect>'''),
    ('<TextBlock x:Name="WordText" FontSize="18" FontWeight="Bold" Foreground="#111827" Margin="0,0,8,0" FontFamily="Segoe UI"',
     '<TextBlock x:Name="WordText" FontSize="20" FontWeight="SemiBold" Foreground="#1D1D1F" Margin="0,0,8,0" FontFamily="{StaticResource DisplayFont}"'),
    ('<TextBlock x:Name="PhoneticText" FontSize="13" Foreground="#6B7280" VerticalAlignment="Bottom" Margin="0,0,0,3"',
     '<TextBlock x:Name="PhoneticText" FontSize="13" Foreground="#86868B" VerticalAlignment="Bottom" Margin="0,0,0,4"'),
    ('<TextBlock x:Name="MeaningLine" Margin="0,4,4,0"', '<TextBlock x:Name="MeaningLine" Margin="0,6,4,0"'),
    ('<Run x:Name="PosRun" Foreground="#4C6EF5" FontSize="13" /><Run x:Name="MeaningRun" FontSize="16" Foreground="#111827" />',
     '<Run x:Name="PosRun" Foreground="#007AFF" FontSize="13" FontStyle="Italic" /><Run x:Name="MeaningRun" FontSize="15.5" Foreground="#1D1D1F" />'),
    ('<TextBlock x:Name="NoteText" Margin="0,4,4,0" FontSize="12" Foreground="#6B7280"', '<TextBlock x:Name="NoteText" Margin="0,5,4,0" FontSize="12" Foreground="#6E6E73"'),
    ('Foreground="#9CA3AF" TextWrapping="Wrap"', 'Foreground="#AEAEB2" TextWrapping="Wrap"'),
    ('<Border x:Name="SentenceBox" BorderBrush="#E5E7EB" BorderThickness="0,1,0,0" Margin="0,8,0,0" Padding="0,6,0,0">',
     '<Border x:Name="SentenceBox" BorderBrush="#EDEDF0" BorderThickness="0,1,0,0" Margin="0,10,0,0" Padding="0,8,0,0">'),
    ('<TextBlock x:Name="SentenceText" FontSize="12" Foreground="#4B5563"', '<TextBlock x:Name="SentenceText" FontSize="12.5" Foreground="#424245"'),
    ('<TextBox x:Name="PhraseEn" FontSize="13" Foreground="#4B5563" TextWrapping="Wrap" FontFamily="Segoe UI"',
     '<TextBox x:Name="PhraseEn" FontSize="13" Foreground="#6E6E73" TextWrapping="Wrap" FontFamily="{StaticResource UIFont}"'),
    ('SelectionBrush="#4C6EF5"', 'SelectionBrush="#007AFF"'),
    ('<TextBlock x:Name="PhraseZh" FontSize="16" Foreground="#111827"', '<TextBlock x:Name="PhraseZh" FontSize="15.5" Foreground="#1D1D1F"'),
])

edit("UI/WordCard.xaml.cs", [
    ("Brush(0x11, 0x18, 0x27)", "Brush(0x1D, 0x1D, 0x1F)"),
    ("Brush(0xDC, 0x26, 0x26)", "Brush(0xFF, 0x3B, 0x30)"),
    ("Brush(0xF5, 0x9E, 0x0B) : Brush(0x37, 0x41, 0x51)", "Brush(0xFF, 0x9F, 0x0A) : Brush(0x6E, 0x6E, 0x73)"),
])

edit("UI/CopyToast.cs", [
    ('new FontFamily("Microsoft YaHei UI") };', 'new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI") };'),
    ("Color.FromRgb(0x4B, 0x55, 0x63)", "Color.FromRgb(0x6E, 0x6E, 0x73)"),
    ("CornerRadius = new CornerRadius(10),", "CornerRadius = new CornerRadius(14),"),
    ("Background = Brushes.White,", "Background = new SolidColorBrush(Color.FromRgb(0xFC, 0xFC, 0xFD)),"),
    ("Color.FromRgb(0xD5, 0xDC, 0xE8)", "Color.FromRgb(0xE3, 0xE3, 0xE8)"),
    ("Padding = new Thickness(12, 9, 12, 10),", "Padding = new Thickness(14, 10, 14, 11),"),
    ("new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.18 }", "new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.14 }"),
    ('Color.FromRgb(0x6B, 0x72, 0x80), "")', 'Color.FromRgb(0x86, 0x86, 0x8B), "")'),
    ("ok ? Color.FromRgb(0x16, 0xA3, 0x4A) : Color.FromRgb(0xDC, 0x26, 0x26)", "ok ? Color.FromRgb(0x24, 0x8A, 0x3D) : Color.FromRgb(0xFF, 0x3B, 0x30)"),
])

for p in ["Core/Controller.cs", "Core/RegionSession.cs", "UI/BadgeWindow.cs"]:
    edit(p, [
        ("0x4C, 0x6E, 0xF5", "0x00, 0x7A, 0xFF"),
        ("Color.FromRgb(0x16, 0xA3, 0x4A)", "Color.FromRgb(0x34, 0xC7, 0x59)"),
        ("Color.FromArgb(0x1A, 0x16, 0xA3, 0x4A)", "Color.FromArgb(0x1A, 0x34, 0xC7, 0x59)"),
        ("Color.FromRgb(0xFF, 0xB0, 0x00)", "Color.FromRgb(0x00, 0x7A, 0xFF)"),
        ("Color.FromArgb(0x1F, 0xFF, 0xB0, 0x00)", "Color.FromArgb(0x1A, 0x00, 0x7A, 0xFF)"),
    ])
