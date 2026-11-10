Add-Type -AssemblyName System.Drawing
$w=900; $h=420
$bmp = New-Object System.Drawing.Bitmap $w,$h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::White)
$g.TextRenderingHint = 'ClearTypeGridFit'
$f = New-Object System.Drawing.Font 'Segoe UI', 11
$fb = New-Object System.Drawing.Font 'Segoe UI', 16, ([System.Drawing.FontStyle]::Bold)
$g.DrawString('Climate Change and Ocean Life', $fb, [System.Drawing.Brushes]::Black, 20, 15)
$p1 = "Rising ocean temperatures are forcing many marine species to migrate toward the poles. Scientists are reluctant to predict exactly how fast this will happen, but the trend is clear. Dr. Smith, a researcher at the U.S. National Oceanic Agency, says coral reefs are especially vulnerable."
$p2 = "Some fishing communities have already noticed the shift. They now travel much farther from the coast to find the same fish their parents caught near home. Is this the new normal?"
$rect1 = New-Object System.Drawing.RectangleF 20,60,860,150
$rect2 = New-Object System.Drawing.RectangleF 20,220,860,150
$g.DrawString($p1, $f, [System.Drawing.Brushes]::Black, $rect1)
$g.DrawString($p2, $f, [System.Drawing.Brushes]::Black, $rect2)
$bmp.Save($args[0], [System.Drawing.Imaging.ImageFormat]::Png)
