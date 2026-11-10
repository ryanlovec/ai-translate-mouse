"""Turn the raw screenshots from `AIMouse.exe --readme-shots <dir> --visible-to-capture` into README images.

Usage: python -I tools/make_readme_images.py <shots_dir> docs/images
Adds a caption bar (gesture in Chinese + English) on top of each shot, rounded corners and a soft shadow.
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
FONT = "C:/Windows/Fonts/msyh.ttc"
BOLD = "C:/Windows/Fonts/msyhbd.ttc"
BLUE = (0, 122, 255)
INK = (29, 29, 31)
GREY = (110, 110, 115)


def keycap(d, x, y, text, f):
    w = d.textlength(text, font=f) + 26
    d.rounded_rectangle([x, y, x + w, y + 40], 9, fill=(255, 255, 255), outline=(210, 210, 215), width=2)
    d.rounded_rectangle([x + 2, y + 34, x + w - 2, y + 40], 6, fill=(232, 232, 237))
    d.text((x + 13, y + 5), text, font=f, fill=INK)
    return x + w


def caption(img, keys, zh, en):
    f_key = ImageFont.truetype(BOLD, 22)
    f_zh = ImageFont.truetype(BOLD, 26)
    f_en = ImageFont.truetype(FONT, 19)
    bar_h = 92
    W = img.width
    canvas = Image.new("RGB", (W, img.height + bar_h), (245, 245, 247))
    d = ImageDraw.Draw(canvas)
    x = 30
    for i, k in enumerate(keys):
        if i:
            d.text((x + 8, 30), "+", font=f_key, fill=GREY)
            x += 30
        x = keycap(d, x, 26, k, f_key)
    x += 26
    d.text((x, 14), zh, font=f_zh, fill=INK)
    d.text((x, 52), en, font=f_en, fill=GREY)
    d.line([0, bar_h - 1, W, bar_h - 1], fill=(225, 225, 230), width=2)
    canvas.paste(img, (0, bar_h))
    return canvas


def frame(img, scale=1.0):
    if scale != 1.0:
        img = img.resize((int(img.width * scale), int(img.height * scale)), Image.LANCZOS)
    pad, r = 28, 18
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, img.width - 1, img.height - 1], r, fill=255)
    shadow = Image.new("RGBA", (img.width + pad * 2, img.height + pad * 2), (0, 0, 0, 0))
    sm = Image.new("L", shadow.size, 0)
    ImageDraw.Draw(sm).rounded_rectangle([pad, pad + 6, pad + img.width, pad + img.height + 6], r, fill=70)
    sm = sm.filter(ImageFilter.GaussianBlur(14))
    shadow.putalpha(sm)
    shadow.paste(img, (pad, pad), mask)
    return shadow


def load(name, crop=None):
    im = Image.open(os.path.join(src, name)).convert("RGB")
    return im.crop(crop) if crop else im


def save(im, name):
    im.save(os.path.join(out, name), optimize=True)
    print(name, im.size)


# 1. word card
save(frame(caption(load("1_word.png", (0, 0, 1400, 800)), ["Ctrl"], "按住 Ctrl，指向单词", "Hold Ctrl and point at a word — meaning, pronunciation, read-aloud"), 0.7), "word.png")
# 2. box → side panel
save(frame(caption(load("2_side.png", (0, 0, 1880, 760)), ["Ctrl", "左键拖框"], "按住 Ctrl 拖一个框，翻译框里的内容", "Hold Ctrl and drag a box to translate it"), 0.55), "box.png")
# 3. 对照 view
save(frame(caption(load("3_under.png", (0, 0, 1400, 800)), ["Ctrl", "左键拖框"], "“对照”模式：英文和中文逐句对照", "\u201cSide-by-side\u201d view: English and Chinese sentence by sentence"), 0.7), "under.png")
# 4. before / after (译文)
before = load("0_page.png", (0, 0, 1400, 800))
after = load("4_replace.png", (0, 0, 1400, 800))
gap = 24
both = Image.new("RGB", (before.width * 2 + gap, before.height), (232, 234, 237))
both.paste(before, (0, 0))
both.paste(after, (before.width + gap, 0))
d = ImageDraw.Draw(both)
f = ImageFont.truetype(BOLD, 26)
for i, t in enumerate(["原文  Original", "译文  Translated in place"]):
    x = 30 + i * (before.width + gap)
    tw = d.textlength(t, font=f)
    d.rounded_rectangle([x, 740, x + tw + 32, 786], 23, fill=BLUE)
    d.text((x + 16, 745), t, font=f, fill=(255, 255, 255))
save(frame(caption(both, ["Ctrl", "Ctrl"], "连按两下 Ctrl：整页变中文，滚动也跟着走", "Double-tap Ctrl: the whole screen in Chinese, follows scrolling"), 0.45), "fullscreen.png")
# 5. copy
save(frame(caption(load("5_copy.png", (0, 0, 1400, 800)), ["Ctrl", "右键拖框"], "按住 Ctrl 右键拖框：识别文字并复制", "Hold Ctrl and right-drag: copy text from images or any app"), 0.7), "copy.png")
