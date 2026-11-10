# Draws the AI翻译鼠标 logo at every icon size and packs Assets\logo.ico + preview PNGs.
Add-Type -AssemblyName System.Drawing
$dir = $args[0]
function Draw([int]$n) {
  $bmp = New-Object System.Drawing.Bitmap $n, $n
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
  $k = $n / 256.0
  # rounded square, blue -> indigo gradient
  $r = 58 * $k; $m = [Math]::Max(0.5, 6 * $k); $w = $n - 2 * $m
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $p.AddArc($m, $m, 2*$r, 2*$r, 180, 90); $p.AddArc($m+$w-2*$r, $m, 2*$r, 2*$r, 270, 90)
  $p.AddArc($m+$w-2*$r, $m+$w-2*$r, 2*$r, 2*$r, 0, 90); $p.AddArc($m, $m+$w-2*$r, 2*$r, 2*$r, 90, 90); $p.CloseFigure()
  $lg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0,0), (New-Object System.Drawing.PointF $n,$n), ([System.Drawing.Color]::FromArgb(255,0x5B,0x8C,0xFF)), ([System.Drawing.Color]::FromArgb(255,0x4F,0x46,0xE5))
  $g.FillPath($lg, $p)
  # soft top highlight
  $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0,-1), (New-Object System.Drawing.PointF 0,($n+2)), ([System.Drawing.Color]::FromArgb(46,255,255,255)), ([System.Drawing.Color]::FromArgb(0,255,255,255))
  $g.FillPath($hl, $p)
  # mouse pointer (unit shape, tip at 0,0)
  $pts = @(@(0,0),@(0,17),@(4.1,13.2),@(7,19.6),@(9.7,18.4),@(6.9,12.2),@(12.2,12.2))
  $s = 7.6 * $k; $ox = 80 * $k; $oy = 48 * $k
  if ($n -le 20) { $s = 8.6 * $k; $ox = 70 * $k; $oy = 36 * $k }
  $arr = New-Object System.Drawing.Drawing2D.GraphicsPath
  $pf = $pts | ForEach-Object { New-Object System.Drawing.PointF ($ox + $_[0]*$s), ($oy + $_[1]*$s) }
  $arr.AddPolygon([System.Drawing.PointF[]]$pf)
  if ($n -ge 48) {
    $sh = $arr.Clone(); $mx = New-Object System.Drawing.Drawing2D.Matrix; $mx.Translate(3*$k, 6*$k); $sh.Transform($mx)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(45,20,20,80))), $sh)
  }
  $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.0, 12*$k)); $pen.LineJoin = 'Round'
  $g.DrawPath($pen, $arr); $g.FillPath([System.Drawing.Brushes]::White, $arr)
  # AI sparkle (amber), only where it can be seen
  if ($n -ge 24) {
    $cx = 190*$k; $cy = 70*$k; $rr = 34*$k; $q = 0.26
    $sp = @(@(0,-1),@($q,-$q),@(1,0),@($q,$q),@(0,1),@(-$q,$q),@(-1,0),@(-$q,-$q)) | ForEach-Object { New-Object System.Drawing.PointF ($cx + $_[0]*$rr), ($cy + $_[1]*$rr) }
    $star = New-Object System.Drawing.Drawing2D.GraphicsPath; $star.AddPolygon([System.Drawing.PointF[]]$sp)
    $amber = [System.Drawing.Color]::FromArgb(255,0xFF,0xC5,0x3D)
    $sp2 = New-Object System.Drawing.Pen $amber, ([Math]::Max(1.0, 6*$k)); $sp2.LineJoin = 'Round'
    $g.DrawPath($sp2, $star); $g.FillPath((New-Object System.Drawing.SolidBrush $amber), $star)
  }
  $g.Dispose(); return $bmp
}
$sizes = 16,20,24,32,40,48,64,128,256
$pngs = @()
foreach ($n in $sizes) {
  $b = Draw $n; $ms = New-Object System.IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngs += ,($ms.ToArray()); if ($n -eq 256) { $b.Save("$dir\logo_256.png") }; $b.Dispose()
}
$fs = [System.IO.File]::Create("$dir\logo.ico"); $bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$off = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $n = $sizes[$i]; $d = if ($n -ge 256) { 0 } else { $n }
  $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$pngs[$i].Length); $bw.Write([UInt32]$off); $off += $pngs[$i].Length
}
foreach ($pb in $pngs) { $bw.Write($pb) }
$bw.Close()
# preview sheet: all sizes on light and dark backgrounds
$sheet = New-Object System.Drawing.Bitmap 760, 340
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245,247,250))), 0, 0, 760, 170)
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(32,33,36))), 0, 170, 760, 170)
$x = 12
foreach ($n in 16,24,32,48,64,128) {
  $b = Draw $n; $g.DrawImage($b, $x, [int](85 - $n/2)); $g.DrawImage($b, $x, [int](255 - $n/2)); $x += $n + 24; $b.Dispose()
}
$sheet.Save("$dir\logo_preview.png"); "ok"
