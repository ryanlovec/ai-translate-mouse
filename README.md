<p align="center"><img src="Assets/logo_256.png" width="96" alt="AI翻译鼠标"></p>

<h1 align="center">AI翻译鼠标 · AI Mouse</h1>

<p align="center">让鼠标变成英译中翻译器 —— 在 Windows 上的任何软件、网页、PDF、图片里都能用。<br>
Turn your mouse into an English → Chinese translator that works on top of every Windows app.</p>

---

## 功能

| 操作 | 效果 |
|---|---|
| 按住 **Ctrl**，指向一个单词 | 在这一行下方弹出单词卡片：音标、释义、例句，可朗读 |
| 按住 **Ctrl** + 左键拖一个框 | 翻译框里的内容（侧栏 / 对照 / 译文 三种显示方式） |
| 按住 **Ctrl** + 右键拖一个框 | 识别框里的文字（中文或英文）并复制，图片、不能复制的软件也行 |
| 连按两下 **Ctrl** | 全屏实时翻译；“译文”模式用中文直接盖住英文，滚动时译文跟着走，翻过的页面会被记住 |
| **Esc** | 关闭 / 退出 |

- 翻译：DeepSeek API（也可以接本地模型：Ollama / LM Studio / llama.cpp / Jan）
- 文字识别：Windows 自带 OCR，不上传截图
- 离线词典：ECDICT，断网也能查单词
- 朗读：Windows 自带语音

## 运行要求

- Windows 10 / 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)（编译用）
- DeepSeek API 密钥（[platform.deepseek.com](https://platform.deepseek.com)），或一个本地模型
- Windows 英文 OCR（一般已自带）；识别中文需要安装“中文(简体)”语言包

## 编译与运行

```bash
dotnet publish -c Release -o publish
publish/AIMouse.exe
```

第一次打开会弹出设置窗口，填入 DeepSeek 密钥即可。程序在右下角托盘里运行。
密钥保存在本机 `%APPDATA%\AIMouse`，不会进入代码仓库。

### 离线词典（可选）

词典数据不在仓库里（约 27MB）。需要的话：

1. 从 [ECDICT](https://github.com/skywind3000/ECDICT) 下载 `ecdict.csv` 和 `lemma.en.txt` 到一个文件夹
2. 运行 `python -I tools/build_dict.py <下载文件夹> data/ecdict.tsv`
3. 重新编译，词典会自动复制到 `dict/` 目录

没有词典时，查词会直接用 AI。

## 项目结构

```
Input/      全局键盘鼠标钩子（Ctrl 手势）
Capture/    截屏、滚动跟踪、颜色采样
Ocr/        Windows OCR、复制文本整理
Text/       分句、分段
Translate/  DeepSeek / 本地模型、离线词典
Core/       控制器、翻译会话、配置、朗读
UI/         卡片、侧栏、设置、译文覆盖层
```

设计说明见 [DESIGN.md](DESIGN.md)。

## 许可证

[MIT](LICENSE)。离线词典数据来自 [ECDICT](https://github.com/skywind3000/ECDICT)（MIT）。
