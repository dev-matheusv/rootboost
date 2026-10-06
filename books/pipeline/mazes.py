"""
Labirintos 100% por codigo (zero credito de IA). Gera as paginas do livro em clean/
e as solucoes em clean_solutions/, no mesmo formato que o bookgen.py espera
(2250x3000 px @ 300 DPI). Dificuldade sobe ao longo do livro.

Uso: python books/pipeline/mazes.py <slug>   (le "maze" do book.json)
"""

from __future__ import annotations

import json
import random
import sys
from collections import deque
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
FONTS = Path(__file__).resolve().parent / "fonts"
W, H = 2250, 3000  # caixa de arte: 7.5 x 10 in @ 300 DPI

N, S, E, O = 1, 2, 4, 8
DX = {N: 0, S: 0, E: 1, O: -1}
DY = {N: -1, S: 1, E: 0, O: 0}
OPP = {N: S, S: N, E: O, O: E}


def carve(cols: int, rows: int, rng: random.Random) -> list[list[int]]:
    """Backtracker iterativo: labirinto perfeito (um unico caminho entre dois pontos)."""
    grid = [[0] * cols for _ in range(rows)]
    stack = [(0, 0)]
    seen = {(0, 0)}
    while stack:
        x, y = stack[-1]
        opts = [d for d in (N, S, E, O)
                if 0 <= x + DX[d] < cols and 0 <= y + DY[d] < rows and (x + DX[d], y + DY[d]) not in seen]
        if not opts:
            stack.pop()
            continue
        d = rng.choice(opts)
        nx, ny = x + DX[d], y + DY[d]
        grid[y][x] |= d
        grid[ny][nx] |= OPP[d]
        seen.add((nx, ny))
        stack.append((nx, ny))
    return grid


def solve(grid: list[list[int]]) -> list[tuple[int, int]]:
    rows, cols = len(grid), len(grid[0])
    goal = (cols - 1, rows - 1)
    prev = {(0, 0): None}
    q = deque([(0, 0)])
    while q:
        cur = q.popleft()
        if cur == goal:
            break
        x, y = cur
        for d in (N, S, E, O):
            if grid[y][x] & d:
                nxt = (x + DX[d], y + DY[d])
                if nxt not in prev:
                    prev[nxt] = cur
                    q.append(nxt)
    path, cur = [], goal
    while cur:
        path.append(cur)
        cur = prev[cur]
    return path[::-1]


def render(grid, title: str, labels: tuple[str, str], solution=None) -> Image.Image:
    rows, cols = len(grid), len(grid[0])
    img = Image.new("L", (W, H), 255)
    dr = ImageDraw.Draw(img)
    title_font = ImageFont.truetype(str(FONTS / "LuckiestGuy.ttf"), 120)
    label_font = ImageFont.truetype(str(FONTS / "LuckiestGuy.ttf"), 96)
    dr.text((W / 2, 40), title, font=title_font, fill=0, anchor="mt")

    top, bottom, side = 300, 220, 60
    cell = min((W - 2 * side) / cols, (H - top - bottom) / rows)
    ox = (W - cell * cols) / 2
    oy = top + (H - top - bottom - cell * rows) / 2
    lw = max(int(cell * 0.12), 8)  # traco grosso, facil pra crianca

    if solution:
        pts = [(ox + (x + 0.5) * cell, oy + (y + 0.5) * cell) for x, y in solution]
        pts = [(pts[0][0], oy - cell * 0.6)] + pts + [(pts[-1][0], oy + rows * cell + cell * 0.6)]
        dr.line(pts, fill=150, width=max(int(cell * 0.3), 10), joint="curve")

    def seg(x0, y0, x1, y1):
        dr.line([(x0, y0), (x1, y1)], fill=0, width=lw)
        r = lw / 2
        for px, py in ((x0, y0), (x1, y1)):
            dr.ellipse([px - r, py - r, px + r, py + r], fill=0)

    for y in range(rows):
        for x in range(cols):
            cx, cy = ox + x * cell, oy + y * cell
            if not grid[y][x] & N and not (x == 0 and y == 0):
                seg(cx, cy, cx + cell, cy)
            if not grid[y][x] & O:
                seg(cx, cy, cx, cy + cell)
            if y == rows - 1 and not (x == cols - 1):
                seg(cx, cy + cell, cx + cell, cy + cell)
            if x == cols - 1:
                seg(cx + cell, cy, cx + cell, cy + cell)
    # entrada (topo, primeira celula) e saida (base, ultima celula) com seta
    def arrow(x, y_tip, size=36):
        dr.polygon([(x - size, y_tip - size * 1.2), (x + size, y_tip - size * 1.2), (x, y_tip)], fill=0)

    sx = ox + cell / 2
    arrow(sx, oy - 12)
    dr.text((sx + 60, oy - 20), labels[0], font=label_font, fill=0, anchor="lb")
    ex = ox + (cols - 0.5) * cell
    arrow(ex, oy + rows * cell + 12 + 43)
    dr.text((ex - 60, oy + rows * cell + 20), labels[1], font=label_font, fill=0, anchor="rt")
    return img


def main(slug: str) -> None:
    d = ROOT / "titles" / slug
    spec = json.loads((d / "book.json").read_text(encoding="utf-8"))
    m = spec["maze"]
    rng = random.Random(m.get("seed", 42))  # seed fixa: livro reproduzivel
    out, sol = d / "clean", d / "clean_solutions"
    out.mkdir(parents=True, exist_ok=True)
    sol.mkdir(parents=True, exist_ok=True)
    n = m["count"]
    c0, c1 = m["cols"]
    for i in range(n):
        t = i / max(n - 1, 1)
        cols = round(c0 + (c1 - c0) * t)
        rows = round(cols * 1.25)
        grid = carve(cols, rows, rng)
        title = f"{m['title_prefix']} {i + 1}"
        render(grid, title, tuple(m["labels"])).save(out / f"{i + 1:02d}.png", dpi=(300, 300), optimize=True)
        render(grid, title, tuple(m["labels"]), solve(grid)).save(sol / f"{i + 1:02d}.png", dpi=(300, 300), optimize=True)
    print(f"{n} labirintos em {out} (+ solucoes)")


if __name__ == "__main__":
    main(sys.argv[1])
