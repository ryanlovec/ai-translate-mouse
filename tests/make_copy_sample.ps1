Add-Type -AssemblyName System.Drawing
$out = $args[0]; $px = [int]$args[1]; $k = $px / 15.0
$bmp = New-Object System.Drawing.Bitmap ([int](760*$k)), ([int](300*$k))
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::White)
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$f = New-Object System.Drawing.Font "Microsoft YaHei UI", $px, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$b = [System.Drawing.Brushes]::Black
$p1 = "人工智能正在改变我们学习语言的方式。通过实时翻译和即时查词，留学生可以在阅读英文资料的同时积累词汇，提高阅读速度，也能更快适应课堂。"
$g.DrawString($p1, $f, $b, (New-Object System.Drawing.RectangleF (20*$k), (20*$k), (440*$k), (120*$k)))
$g.DrawString("请在 2024 年 12 月 31 日之前提交 Final Project 报告。", $f, $b, (20*$k), (140*$k))
$g.DrawString("1. 打开设置窗口", $f, $b, (20*$k), (190*$k))
$g.DrawString("2. 输入 DeepSeek API 密钥", $f, $b, (20*$k), (215*$k))
$g.DrawString("3. 点击保存", $f, $b, (20*$k), (240*$k))
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
"saved"
