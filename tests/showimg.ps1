# Shows an image in a borderless window at x,y (physical pixels) for N seconds.
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class D { [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v); }
"@
[void][D]::SetProcessDpiAwarenessContext([IntPtr](-4))
$img = [System.Drawing.Image]::FromFile($args[0])
$f = New-Object System.Windows.Forms.Form
$f.FormBorderStyle = 'None'; $f.StartPosition = 'Manual'
$f.Location = New-Object System.Drawing.Point ([int]$args[1]), ([int]$args[2])
$f.ClientSize = $img.Size; $f.BackgroundImage = $img; $f.ShowInTaskbar = $false; $f.TopMost = $true
$t = New-Object System.Windows.Forms.Timer; $t.Interval = [int]$args[3] * 1000; $t.Add_Tick({ $f.Close() }); $t.Start()
[void]$f.ShowDialog()
