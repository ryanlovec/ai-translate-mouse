Add-Type -AssemblyName System.Drawing
$out = $args[0]
$bmp = New-Object System.Drawing.Bitmap 1100, 330
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::White)
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$f = New-Object System.Drawing.Font "Microsoft YaHei UI", 22, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$b = [System.Drawing.Brushes]::Black
$g.DrawString("Now wiring it into the controller. Adding a self-test hook.", $f, $b, 20, 20)
$g.DrawString("现在将其接入控制器。添加一个自检钩子。", $f, $b, 20, 70)
$g.DrawString("Ran 2 commands, created CopyToast.cs, 1 failed +119", $f, $b, 20, 120)
$g.DrawString("运行了 2 条命令，创建了 CopyToast.cs，1 个失败", $f, $b, 20, 170)
$g.DrawString("请把 Homework 3 上传到 Canvas，截止时间是 Friday 11:59 PM。", $f, $b, 20, 220)
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
"saved"
