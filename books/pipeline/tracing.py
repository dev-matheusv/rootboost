"""
Livro de caligrafia (tracar linhas, letras e numeros) 100% por codigo, zero credito de IA.
Gera paginas 2250x3000 @ 300 DPI em clean/, no formato que o bookgen.py monta.

Conteudo: aquecimento (linhas retas, zigue zague, ondas, laços) -> A a ate Z z -> 0 a 10.
Letras pontilhadas no eixo central do traco (esqueleto da fonte), nao no contorno:
e assim que a crianca traca de verdade.

Uso: python books/pipeline/tracing.py <slug>
"""

from __future__ import annotations

import json
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont
from skimage.morphology import skeletonize

ROOT = Path(__file__).resolve().parent.parent
FONTS = Path(__file__).resolve().parent / "fonts"
W, H = 2250, 3000
M = 60  # margem interna


def fredoka(size: int, weight: str = "SemiBold") -> ImageFont.FreeTypeFont:
    f = ImageFont.truetype(str(FONTS / "Fredoka.ttf"), size)
    f.set_variation_by_name(weight)
    return f


def title_font(size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONTS / "LuckiestGuy.ttf"), size)


def dots_from_mask(mask: np.ndarray, spacing: float) -> list[tuple[int, int]]:
    """Pontos igualmente espacados ao longo do esqueleto (eixo central) do traco."""
    sk = skeletonize(mask)
    ys, xs = np.nonzero(sk)
    order = np.lexsort((xs, ys))
    pts: list[tuple[int, int]] = []
    grid: dict[tuple[int, int], list[tuple[int, int]]] = {}
    cell = max(int(spacing), 1)
    for i in order:
        x, y = int(xs[i]), int(ys[i])
        gx, gy = x // cell, y // cell
        ok = True
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for px, py in grid.get((gx + dx, gy + dy), ()):
                    if (px - x) ** 2 + (py - y) ** 2 < spacing ** 2:
                        ok = False
                        break
                if not ok:
                    break
            if not ok:
                break
        if ok:
            pts.append((x, y))
            grid.setdefault((gx, gy), []).append((x, y))
    return pts


def draw_dotted_text(img: Image.Image, xy, text: str, f: ImageFont.FreeTypeFont, anchor: str = "ls",
                     dot_r: float | None = None, spacing: float | None = None) -> None:
    """Desenha `text` como pontilhado no eixo central do traco."""
    mask_img = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask_img).text(xy, text, font=f, fill=255, anchor=anchor)
    box = mask_img.getbbox()
    if not box:
        return
    x0, y0, x1, y1 = box
    sub = np.asarray(mask_img.crop(box)) > 128
    size = f.size
    spacing = spacing or size * 0.085
    dot_r = dot_r or max(size * 0.022, 4)
    dr = ImageDraw.Draw(img)
    for x, y in dots_from_mask(sub, spacing):
        cx, cy = x0 + x, y0 + y
        dr.ellipse([cx - dot_r, cy - dot_r, cx + dot_r, cy + dot_r], fill=0)


def guide_lines(dr: ImageDraw.ImageDraw, top: int, base: int, x0: int = M, x1: int = W - M) -> None:
    """Pauta de caligrafia: linha de topo, linha do meio tracejada e linha de base."""
    mid = (top + base) // 2
    dr.line([(x0, top), (x1, top)], fill=110, width=5)
    dr.line([(x0, base), (x1, base)], fill=0, width=7)
    x = x0
    while x < x1:
        dr.line([(x, mid), (min(x + 30, x1), mid)], fill=150, width=4)
        x += 55


def dotted_path(dr, pts, spacing=28, r=9):
    """Pontilha uma polilinha (para o aquecimento)."""
    acc = 0.0
    dr.ellipse([pts[0][0] - r * 2.2, pts[0][1] - r * 2.2, pts[0][0] + r * 2.2, pts[0][1] + r * 2.2], fill=0)  # ponto de partida
    for (ax, ay), (bx, by) in zip(pts, pts[1:]):
        seg = math.hypot(bx - ax, by - ay)
        t = acc
        while t < seg:
            x, y = ax + (bx - ax) * t / seg, ay + (by - ay) * t / seg
            dr.ellipse([x - r, y - r, x + r, y + r], fill=0)
            t += spacing
        acc = t - seg


def warmup_page(kind: str, title: str) -> Image.Image:
    img = Image.new("L", (W, H), 255)
    dr = ImageDraw.Draw(img)
    dr.text((W / 2, 40), title, font=title_font(170), fill=0, anchor="mt")
    rows, top, gap = 6, 330, 430
    for r in range(rows):
        y = top + r * gap + 150
        x0, x1 = M + 80, W - M - 80
        if kind == "straight":
            pts = [(x0, y), (x1, y)]
        elif kind == "vertical":
            pts = None
            for k in range(9):
                xx = x0 + k * (x1 - x0) / 8
                dotted_path(dr, [(xx, y - 130), (xx, y + 130)])
        elif kind == "zigzag":
            n = 8
            pts = [(x0 + k * (x1 - x0) / n, y - 120 if k % 2 else y + 120) for k in range(n + 1)]
        elif kind == "wave":
            pts = [(x0 + t, y + 110 * math.sin(t / (x1 - x0) * 4 * math.pi)) for t in np.linspace(0, x1 - x0, 200)]
        elif kind == "loops":
            pts = [(x0 + 80 + t * 34 + 90 * math.cos(t * 0.5 + math.pi), y + 110 * math.sin(t * 0.5 + math.pi))
                   for t in np.linspace(0, 50, 600)]
        else:  # mountains
            n = 6
            pts = []
            for k in range(n):
                a = x0 + k * (x1 - x0) / n
                pts += [(a + (x1 - x0) / n * s, y + 120 - 240 * math.sin(math.pi * s)) for s in np.linspace(0, 1, 30)]
        if pts:
            dotted_path(dr, pts)
    return img


def letter_page(upper: str, lower: str | None, word: str | None) -> Image.Image:
    img = Image.new("L", (W, H), 255)
    dr = ImageDraw.Draw(img)
    pair = f"{upper}{lower}" if lower else upper
    # topo: palavra de titulo + letra grande vazada (contorno grosso) pra colorir dentro
    if word:
        dr.text((W / 2, 30), word, font=title_font(150), fill=0, anchor="mt")
    size = 760
    while fredoka(size).getlength(pair) > W * 0.8:
        size -= 20
    big = fredoka(size)
    base_y = 860
    inner = Image.new("L", (W, H), 0)
    ImageDraw.Draw(inner).text((W / 2, base_y), pair, font=big, fill=255, anchor="ms")
    from scipy import ndimage
    solid = np.asarray(inner) > 128
    hollow = solid & ~ndimage.binary_erosion(solid, iterations=14)
    arr = np.asarray(img).copy()
    arr[hollow] = 0
    img = Image.fromarray(arr)
    dr = ImageDraw.Draw(img)
    # pautas: 2 linhas da maiuscula, 2 da minuscula (ou 4 do numero), ultima livre pra escrever
    rows = [upper, upper] + ([lower, lower] if lower else [upper, upper])
    top0, row_h, gap = 1150, 290, 80
    for i, ch in enumerate(rows + [None]):
        top = top0 + i * (row_h + gap)
        base = top + row_h
        guide_lines(dr, top, base)
        if ch is None:
            continue
        f = fredoka(int(row_h / 0.70) if ch.isupper() or ch.isdigit() else int(row_h / 0.70))
        x = M + 30
        dr.text((x, base), ch, font=f, fill=0, anchor="ls")  # exemplo solido
        step = f.getlength(ch) + 70
        x += step
        while x + f.getlength(ch) < W - M:
            draw_dotted_text(img, (x, base), ch, f, dot_r=9, spacing=23)
            x += step
    return img


WORDS = {
    "A": "Apple", "B": "Ball", "C": "Cat", "D": "Dog", "E": "Egg", "F": "Fish", "G": "Gift", "H": "Hat", "I": "Ice cream",
    "J": "Juice", "K": "Kite", "L": "Lion", "M": "Moon", "N": "Nest", "O": "Owl", "P": "Pig", "Q": "Queen", "R": "Rainbow",
    "S": "Sun", "T": "Tree", "U": "Umbrella", "V": "Van", "W": "Whale", "X": "Xylophone", "Y": "Yo yo", "Z": "Zebra",
}
NUMS = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"]


WARMUPS = {"straight": "Trace the Lines", "vertical": "Up and Down", "zigzag": "Zig Zag",
           "wave": "Wavy Lines", "mountains": "Hills", "loops": "Loop de Loop"}


def main(slug: str) -> None:
    d = ROOT / "titles" / slug
    spec = json.loads((d / "book.json").read_text(encoding="utf-8"))
    t = spec.get("tracing", {})  # edicoes em outro idioma sobrescrevem textos e palavras
    warm = {**WARMUPS, **t.get("warmups", {})}
    words = t.get("words", WORDS)
    letters = t.get("letters", "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
    nums = t.get("numbers", NUMS)
    pattern = t.get("pattern", "{L} is for {W}")
    out = d / "clean"
    out.mkdir(parents=True, exist_ok=True)
    pages: list[Image.Image] = []
    for kind in ("straight", "vertical", "zigzag", "wave", "mountains", "loops"):
        pages.append(warmup_page(kind, warm[kind]))
    for up in letters:
        pages.append(letter_page(up, up.lower(), pattern.format(L=up, W=words[up])))
    for n in range(0, 11):
        pages.append(letter_page(str(n), None, nums[n].capitalize()))
    for i, p in enumerate(pages, 1):
        p.save(out / f"{i:02d}.png", dpi=(300, 300), optimize=True)
    print(f"{len(pages)} paginas em {out}")


if __name__ == "__main__":
    main(sys.argv[1])
