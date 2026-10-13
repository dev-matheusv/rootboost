"""
Paginas educativas por codigo (zero credito) pro livro toddler "Color and Learn":
  cores   giz de cera grande vazado + nome da cor
  formas  forma grande pra colorir + nome
  numeros conta e pinta: N copias de um desenho do proprio livro + o numero grande vazado
Saem em clean/ continuando a numeracao (41, 42, ...), com legenda em page_text do book.json.

Uso: python books/pipeline/learn.py toddler
"""

from __future__ import annotations

import json
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
FONTS = Path(__file__).resolve().parent / "fonts"
W, H = 2250, 3000
LW = 34  # traco grosso de toddler


def font(size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONTS / "LuckiestGuy.ttf"), size)


def hollow_text(dr: ImageDraw.ImageDraw, xy, text: str, size: int) -> None:
    dr.text(xy, text, font=font(size), fill=255, stroke_width=14, stroke_fill=0, anchor="mm")


def crayon_page(color: str) -> Image.Image:
    img = Image.new("L", (W, H), 255)
    dr = ImageDraw.Draw(img)
    # giz de cera deitado na diagonal, grande
    cx, cy, L, R = W / 2, H * 0.5, 1800, 300
    ang = math.radians(-30)
    ux, uy = math.cos(ang), math.sin(ang)
    px, py = -uy, ux

    def P(a, b):
        return (cx + ux * a + px * b, cy + uy * a + py * b)

    body = [P(-L / 2, -R), P(L / 2 - 300, -R), P(L / 2 - 300, R), P(-L / 2, R)]
    tip = [P(L / 2 - 300, -R * 0.75), P(L / 2, 0), P(L / 2 - 300, R * 0.75)]
    dr.polygon(body, outline=0, width=LW)
    dr.polygon(tip, outline=0, width=LW)
    for k in (-L / 2 + 220, L / 2 - 520):  # faixas do papel do giz
        dr.line([P(k, -R), P(k, R)], fill=0, width=LW)
    return img


def shape_page(shape: str) -> Image.Image:
    img = Image.new("L", (W, H), 255)
    dr = ImageDraw.Draw(img)
    cx, cy, r = W / 2, H * 0.45, 760
    if shape == "circle":
        dr.ellipse([cx - r, cy - r, cx + r, cy + r], outline=0, width=LW)
    elif shape == "square":
        dr.rounded_rectangle([cx - r, cy - r, cx + r, cy + r], 40, outline=0, width=LW)
    elif shape == "triangle":
        dr.polygon([(cx, cy - r), (cx + r, cy + r * 0.8), (cx - r, cy + r * 0.8)], outline=0, width=LW)
    elif shape == "rectangle":
        dr.rounded_rectangle([cx - r, cy - r * 0.6, cx + r, cy + r * 0.6], 40, outline=0, width=LW)
    elif shape == "oval":
        dr.ellipse([cx - r, cy - r * 0.62, cx + r, cy + r * 0.62], outline=0, width=LW)
    elif shape == "diamond":
        dr.polygon([(cx, cy - r), (cx + r * 0.75, cy), (cx, cy + r), (cx - r * 0.75, cy)], outline=0, width=LW)
    elif shape == "star":
        pts = [(cx + (r if k % 2 == 0 else r * 0.45) * math.cos(math.radians(-90 + k * 36)),
                cy + (r if k % 2 == 0 else r * 0.45) * math.sin(math.radians(-90 + k * 36))) for k in range(10)]
        dr.polygon(pts, outline=0, width=LW)
    elif shape == "heart":
        pts = [(cx + r * 0.95 * 16 * math.sin(t) ** 3 / 16,
                cy - r * 0.95 * (13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)) / 16)
               for t in [i * 2 * math.pi / 400 for i in range(400)]]
        dr.polygon(pts, outline=0, width=LW)
    return img


def count_page(n: int, art: Path) -> Image.Image:
    img = Image.new("L", (W, H), 255)
    dr = ImageDraw.Draw(img)
    hollow_text(dr, (W / 2, 330), str(n), 520)
    obj = Image.open(art).convert("L")
    obj = obj.crop(obj.point(lambda v: 255 - v).getbbox())
    cols = 1 if n == 1 else 2 if n <= 4 else 3 if n <= 9 else 4
    rows = math.ceil(n / cols)
    top, bottom = 700, H - 60
    cw, ch = (W - 120) / cols, (bottom - top) / rows
    s = min(cw, ch) * 0.86
    o = obj.copy()
    o.thumbnail((int(s), int(s)), Image.LANCZOS)
    o = o.point(lambda v: 0 if v < 128 else 255)
    for i in range(n):
        r, k = divmod(i, cols)
        left_in_row = n - r * cols if r == rows - 1 else cols  # centraliza a ultima linha
        x0 = (W - left_in_row * cw) / 2
        img.paste(o, (int(x0 + k * cw + (cw - o.width) / 2), int(top + r * ch + (ch - o.height) / 2)))
    return img


def main(slug: str) -> None:
    d = ROOT / "titles" / slug
    p = d / "book.json"
    spec = json.loads(p.read_text(encoding="utf-8"))
    cfg = spec["learn"]
    clean = d / "clean"
    n = len(spec["pages"])
    texts = spec.setdefault("page_text", {})
    pages: list[tuple[Image.Image, dict]] = []
    for color in cfg["colors"]:
        pages.append((crayon_page(color), {"word": color.upper()}))
    for shape in cfg["shapes"]:
        pages.append((shape_page(shape), {"word": shape.upper()}))
    for k, (num, page_no, word) in enumerate(cfg["counting"], 1):
        pages.append((count_page(num, clean / f"{int(page_no):02d}.png"), {"word": f"{num} {word}".upper()}))
    for i, (img, cap) in enumerate(pages, n + 1):
        img.save(clean / f"{i:02d}.png", dpi=(300, 300), optimize=True)
        texts[f"{i:02d}"] = cap
    p.write_text(json.dumps(spec, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"{len(pages)} paginas educativas em {clean} ({n + 1:02d} a {n + len(pages):02d})")


if __name__ == "__main__":
    main(sys.argv[1])
