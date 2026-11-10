Add-Type -AssemblyName System.Drawing
$out = $args[0]
$bmp = New-Object System.Drawing.Bitmap 1300, 900
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::White)
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$h = New-Object System.Drawing.Font "Segoe UI", 30, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$f = New-Object System.Drawing.Font "Segoe UI", 22, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$b = [System.Drawing.Brushes]::Black
$g.DrawString("Why Sleep Matters for Students", $h, $b, 30, 25)
$p1 = 'A 2023 study by researchers at the U.S. National Institutes of Health (NIH) found that students who slept less than 6.5 hours a night scored 12% lower on exams. Dr. Maria Lopez, the lead author, said: "Sleep is not a luxury. It is when the brain files away what you learned." The effect was strongest in STEM courses, e.g. calculus and organic chemistry.'
$g.DrawString($p1, $f, $b, (New-Object System.Drawing.RectangleF 30, 90, 1200, 200))
$p2 = 'However, many students still pull all-nighters before finals... Is it worth it? Probably not. According to the report, even one night without sleep can reduce memory recall by up to 40 percent.'
$g.DrawString($p2, $f, $b, (New-Object System.Drawing.RectangleF 30, 300, 1200, 120))
$g.DrawString("Tips:", $f, $b, 30, 430)
$g.DrawString("- Go to bed at the same time every day", $f, $b, 30, 465)
$g.DrawString("- Avoid caffeine after 3 p.m.", $f, $b, 30, 500)
$g.DrawString("- Keep your phone out of the bedroom", $f, $b, 30, 535)
$g.DrawString("Read more at www.nih.gov/sleep-research. Sign in    Settings    Share", $f, $b, 30, 600)
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
"saved"
