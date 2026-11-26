"""Builds Assets/ClubEQ.ico and Assets/ClubEQ.png: TrackGlow's tile (dark rounded square, violet border) with
glowing equaliser bars, purple to neon pink. Needs Pillow.  python tools/make-icon.py"""
import os
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
S = 1024                      # drawn at 4x of 256, scaled down for every icon size
BG, BORDER = "#16133A", "#2C2766"
BARS = [(0.42, "#B07CFF"), (0.70, "#9A72FF"), (0.52, "#C45CE0"), (0.82, "#E54AC2"), (0.60, "#FF3DA8")]
W, GAP, BOTTOM, TOP = 92, 40, 760, 250   # bar width, gap, baseline, top of the tallest possible bar


def rgba(h, a=255):
    h = h.lstrip("#"); return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)


def bars(draw):
    x = (S - (len(BARS) * W + (len(BARS) - 1) * GAP)) // 2
    for h, col in BARS:
        top = BOTTOM - (BOTTOM - TOP) * h
        draw.rounded_rectangle([x, top, x + W, BOTTOM], radius=W // 2, fill=rgba(col))
        draw.rounded_rectangle([x, top - 70, x + W, top - 70 + 36], radius=18, fill=rgba(col))  # peak cap
        x += W + GAP


img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle([24, 24, S - 24, S - 24], radius=210, fill=rgba(BG), outline=rgba(BORDER), width=28)

glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
bars(ImageDraw.Draw(glow))
glow = glow.filter(ImageFilter.GaussianBlur(44))
glow.putalpha(glow.getchannel("A").point(lambda v: int(v * .85)))
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([38, 38, S - 38, S - 38], radius=196, fill=255)  # glow stays inside the tile
glow.putalpha(Image.composite(glow.getchannel("A"), Image.new("L", (S, S), 0), mask))
img = Image.alpha_composite(img, glow)
bars(ImageDraw.Draw(img))

os.makedirs(os.path.join(ROOT, "Assets"), exist_ok=True)
img.resize((256, 256), Image.LANCZOS).save(os.path.join(ROOT, "Assets", "ClubEQ.png"))
img.save(os.path.join(ROOT, "Assets", "ClubEQ.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("ok")
