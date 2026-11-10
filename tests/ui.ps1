# Test helper: window placement, input simulation, screenshots.
# Usage: ui.ps1 <command> [args]
#   open <file>                  -> opens file in notepad at 80,80 size 900x560, prints window rect
#   shot <out.png> [x y w h]     -> screenshot (whole primary screen if no rect)
#   ctrlhover <x> <y> <ms>       -> hold Ctrl, move to x,y, wait ms, (no release)
#   ctrlup                       -> release Ctrl
#   ctrldrag <x1> <y1> <x2> <y2> -> hold Ctrl, drag a box, release
#   ctrlrdrag <x1> <y1> <x2> <y2> -> hold Ctrl, right-button drag a box (copy), release
#   doublectrl                   -> tap Ctrl twice
#   move <x> <y>
#   info                         -> screen size + dpi
$cmd = $args[0]
$rest = @($args | Select-Object -Skip 1)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class U {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool r);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetDpiForSystem();
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
[void][U]::SetProcessDpiAwarenessContext([IntPtr](-4))
$args2 = $rest
function CtrlDown { [U]::keybd_event(0xA2, 0x1D, 0, [UIntPtr]::Zero) }
function CtrlUp { [U]::keybd_event(0xA2, 0x1D, 2, [UIntPtr]::Zero) }
switch ($cmd) {
  "info" {
    Add-Type -AssemblyName System.Windows.Forms
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    "screen $($b.Width)x$($b.Height) dpi $([U]::GetDpiForSystem())"
  }
  "open" {
    $p = Start-Process notepad.exe -ArgumentList $args2[0] -PassThru
    Start-Sleep -Milliseconds 2500
    $name = [System.IO.Path]::GetFileNameWithoutExtension($args2[0])
    $h = $null
    for ($i = 0; $i -lt 10 -and -not $h; $i++) {
      $h = (Get-Process | Where-Object { $_.MainWindowTitle -like "*$name*" } | Select-Object -First 1).MainWindowHandle
      if (-not $h) { Start-Sleep -Milliseconds 500 }
    }
    [void][U]::MoveWindow($h, 80, 80, 900, 560, $true)
    [void][U]::SetForegroundWindow($h)
    Start-Sleep -Milliseconds 500
    $r = New-Object U+RECT; [void][U]::GetWindowRect($h, [ref]$r)
    "window $($r.L) $($r.T) $($r.R) $($r.B)"
  }
  "shot" {
    Add-Type -AssemblyName System.Windows.Forms
    if ($args2.Count -ge 5) { $x=[int]$args2[1]; $y=[int]$args2[2]; $w=[int]$args2[3]; $hh=[int]$args2[4] }
    else { $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds; $x=0; $y=0; $w=$b.Width; $hh=$b.Height }
    $bmp = New-Object System.Drawing.Bitmap $w, $hh
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $hh))
    $bmp.Save($args2[0], [System.Drawing.Imaging.ImageFormat]::Png)
    "saved"
  }
  "realmove" {
    Add-Type -AssemblyName System.Windows.Forms
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $nx = [int]([int]$args2[0] * 65535 / $b.Width); $ny = [int]([int]$args2[1] * 65535 / $b.Height)
    [U]::mouse_event(0x8001, $nx, $ny, 0, [UIntPtr]::Zero)
  }
  "move" { [void][U]::SetCursorPos([int]$args2[0], [int]$args2[1]) }
  "ctrlhover" {
    [void][U]::SetCursorPos([int]$args2[0] - 30, [int]$args2[1])
    CtrlDown; Start-Sleep -Milliseconds 300
    [void][U]::SetCursorPos([int]$args2[0], [int]$args2[1])
    Start-Sleep -Milliseconds ([int]$args2[2])
  }
  "ctrlup" { CtrlUp }
  "ctrldrag" {
    [void][U]::SetCursorPos([int]$args2[0], [int]$args2[1])
    CtrlDown; Start-Sleep -Milliseconds 350
    [U]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 50
    $steps = 12
    for ($i = 1; $i -le $steps; $i++) {
      $x = [int]$args2[0] + ([int]$args2[2] - [int]$args2[0]) * $i / $steps
      $y = [int]$args2[1] + ([int]$args2[3] - [int]$args2[1]) * $i / $steps
      [void][U]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 25
    }
    Start-Sleep -Milliseconds 100
    [U]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 50
    CtrlUp
  }
  "ctrlrdrag" {
    [void][U]::SetCursorPos([int]$args2[0], [int]$args2[1])
    CtrlDown; Start-Sleep -Milliseconds 350
    [U]::mouse_event(0x8, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 50
    $steps = 12
    for ($i = 1; $i -le $steps; $i++) {
      $x = [int]$args2[0] + ([int]$args2[2] - [int]$args2[0]) * $i / $steps
      $y = [int]$args2[1] + ([int]$args2[3] - [int]$args2[1]) * $i / $steps
      [void][U]::SetCursorPos($x, $y); Start-Sleep -Milliseconds 25
    }
    Start-Sleep -Milliseconds 100
    [U]::mouse_event(0x10, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 50
    CtrlUp
  }
  "doublectrl" {
    CtrlDown; Start-Sleep -Milliseconds 60; CtrlUp; Start-Sleep -Milliseconds 120
    CtrlDown; Start-Sleep -Milliseconds 60; CtrlUp
  }
}
