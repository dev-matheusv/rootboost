"""
Kit de marketing por livro, 100% por codigo (zero credito de IA):

  colorize   pinta as paginas por regiao (o "depois" do antes/depois)
  aplus      imagens do A+ Content do KDP (970x600 e 970x300)
  pins       pins do Pinterest 1000x1500 (pagina pintada + chamada pro livro)
  shorts     videos 1080x1920 de ~12s: a pagina sendo colorida (Shorts, TikTok, Reels)
  all        tudo acima

Saida em books/titles/<slug>/marketing/. Copy sem hifen e sem travessao (padrao da casa).

Uso: python books/pipeline/marketing.py all christmas
"""

from __future__ import annotations

import argparse
import colorsys
import json
import random
import sys
from pathlib import Path

import imageio.v2 as imageio
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

ROOT = Path(__file__).resolve().parent.parent
FONTS = Path(__file__).resolve().parent / "fonts"


def font(name: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONTS / ("LuckiestGuy.ttf" if name == "title" else "Fredoka.ttf")), size)


def hex_rgb(h: str) -> tuple[int, int, int]:
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


# ---------------------------------------------------------------- colorir

PALETTES = {
    # paletas vivas e "de giz de cera"; cada livro pode sobrescrever no book.json (marketing.palette)
    "default": ["#FF6B6B", "#FFD93D", "#6BCB77", "#4D96FF", "#FF8FAB", "#B983FF", "#FFA94D", "#38D9A9", "#74C0FC", "#F783AC"],
}


def regions(page: Path, work: int = 1100) -> tuple[np.ndarray, np.ndarray, int]:
    """Rotula as areas brancas fechadas da line art (em resolucao de trabalho)."""
    im = Image.open(page).convert("L")
    im = im.resize((work, int(work * im.height / im.width)), Image.LANCZOS)
    ink = np.asarray(im) < 140
    lab, n = ndimage.label(~ink)
    return ink, lab, n


AI_PROMPT = ("Color this exact coloring page like a finished, beautifully colored children's book illustration. "
             "Keep every black outline exactly where it is, same composition, same drawing. Fill each area with flat, "
             "bright, natural colors that make sense (skin tones for faces, white for beards and snow, realistic colors "
             "for animals and objects). No shading, no gradients, no texture, no new elements, white background, no text.")


def ai_reference(page: Path, out_dir: Path) -> Path | None:
    """Versao pintada pela IA (barata: 1k low). Usada so como fonte de cor por regiao."""
    import time
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from bookgen import run_job
    dest = out_dir / "ai" / page.name
    if dest.exists():
        return dest
    args = ["gpt_image_2_5", "--image", str(page), "--aspect_ratio", "3:4", "--resolution", "1k", "--quality", "low"]
    for _ in range(60):
        try:
            run_job(args, AI_PROMPT, dest)
            return dest
        except RuntimeError as e:
            if "rate_limit" in str(e).lower():
                time.sleep(20)
                continue
            print(f"  IA nao coloriu {page.name}: {e}")
            return None
    return None


def colorize(page: Path, palette: list[str], seed: int, ai: Path | None = None) -> tuple[Image.Image, list[tuple[np.ndarray, tuple]], np.ndarray]:
    """Retorna (imagem pintada, [(mascara, cor)] em ordem de pintura, mascara do traco)."""
    ink, lab, n = regions(page)
    rng = random.Random(seed)
    sizes = ndimage.sum(np.ones_like(lab), lab, index=range(1, n + 1))
    border = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    cols = [hex_rgb(c) for c in palette]
    fills = []
    min_area = lab.size * 0.00008  # ignora pontinhos (sobra de antialias)
    order = sorted(range(1, n + 1), key=lambda i: -sizes[i - 1])
    for i in order:
        if i in border or sizes[i - 1] < min_area:
            continue
        fills.append((lab == i, rng.choice(cols)))
    if ai is not None:
        # cor de cada regiao = mediana da versao da IA naquela area (robusta a pequeno desalinhamento)
        ref = np.asarray(Image.open(ai).convert("RGB").resize((lab.shape[1], lab.shape[0]), Image.LANCZOS))
        fills = [(m, tuple(int(v) for v in np.median(ref[m], axis=0))) for m, _ in fills]
    out = np.full(lab.shape + (3,), 255, np.uint8)
    for mask, c in fills:
        out[mask] = c
    out[ink] = 0
    return Image.fromarray(out), fills, ink


# ---------------------------------------------------------------- helpers de layout

def fit(img: Image.Image, w: int, h: int, bg=(255, 255, 255)) -> Image.Image:
    img = img.copy()
    img.thumbnail((w, h), Image.LANCZOS)
    canvas = Image.new("RGB", (w, h), bg)
    canvas.paste(img, ((w - img.width) // 2, (h - img.height) // 2))
    return canvas


def card(img: Image.Image, w: int, h: int, radius: int = 24) -> Image.Image:
    c = fit(img.convert("RGB"), w, h)
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, w, h], radius, fill=255)
    c.putalpha(mask)
    return c


def text_center(dr: ImageDraw.ImageDraw, xy, text: str, f, fill, max_w: int, line_gap: int = 8):
    words, lines, cur = text.split(), [], ""
    for wd in words:
        t = f"{cur} {wd}".strip()
        if dr.textlength(t, font=f) <= max_w:
            cur = t
        else:
            lines.append(cur)
            cur = wd
    lines.append(cur)
    x, y = xy
    for ln in lines:
        dr.text((x, y), ln, font=f, fill=fill, anchor="mt")
        y += f.size + line_gap
    return y


class Book:
    def __init__(self, slug: str):
        self.slug = slug
        self.dir = ROOT / "titles" / slug
        self.spec = json.loads((self.dir / "book.json").read_text(encoding="utf-8"))
        self.mk = self.spec.get("marketing", {})
        self.out = self.dir / "marketing"
        self.out.mkdir(exist_ok=True)
        self.pages = sorted((self.dir / "clean").glob("[0-9][0-9].png"))
        if not self.pages:
            sys.exit("Sem paginas limpas. Rode `bookgen.py clean` antes.")
        self.palette = self.mk.get("palette", PALETTES["default"])
        self.bg = hex_rgb(self.spec["cover"]["bg"])
        self.accent = hex_rgb(self.spec["cover"]["accent"])
        cover = self.dir / "raw" / "cover.png"
        self.cover = Image.open(cover).convert("RGB") if cover.exists() else None

    def showcase(self, k: int) -> list[Path]:
        """Paginas de vitrine: as do book.json (marketing.showcase) ou espalhadas pelo livro."""
        chosen = self.mk.get("showcase")
        if chosen:
            return [self.dir / "clean" / f"{i:02d}.png" for i in chosen[:k]]
        step = max(len(self.pages) // k, 1)
        return self.pages[::step][:k]

    def ai(self, page: Path) -> Path | None:
        return ai_reference(page, self.out) if self.mk.get("ai_color", True) else None

    @property
    def activity(self) -> bool:
        return not self.spec["pages"]

    def solution(self, page: Path) -> Path | None:
        sol = self.dir / "clean_solutions" / page.name
        return sol if sol.exists() else None

    def colored(self, page: Path) -> Image.Image:
        if self.activity:
            # livro de atividade: o "depois" e a solucao (labirinto) ou a propria pagina
            sol = self.solution(page)
            return Image.open(sol or page).convert("RGB")
        cache = self.out / "colored" / page.name
        if cache.exists():
            return Image.open(cache).convert("RGB")
        cache.parent.mkdir(exist_ok=True)
        img, _, _ = colorize(page, self.palette, seed=int(page.stem), ai=self.ai(page))
        img.save(cache)
        return img


# ---------------------------------------------------------------- A+ Content

def aplus(b: Book) -> None:
    title = b.spec["cover"]["front_title"]
    # 1. antes e depois (970x600)
    p = b.showcase(1)[0]
    before = Image.open(p).convert("RGB")
    after = b.colored(p)
    head, labels = "COLOR IT YOUR WAY!", ("BEFORE", "AFTER")
    if b.activity:
        if b.solution(p):
            head, labels = "CAN YOU SOLVE IT?", ("TRY IT", "SOLUTION")
        else:
            p2 = b.showcase(2)[1]
            after = Image.open(p2).convert("RGB")
            head, labels = "TRACE, COLOR AND WRITE!", ("WARM UP", "LETTERS")
    img = Image.new("RGB", (970, 600), b.bg)
    dr = ImageDraw.Draw(img)
    dr.text((485, 28), head, font=font("title", 52), fill="white", anchor="mt")
    for k, (im, lbl) in enumerate(((before, labels[0]), (after, labels[1]))):
        x = 55 + k * 450
        c = card(im, 410, 430)
        img.paste(c, (x, 110), c)
        dr.rounded_rectangle([x + 130, 520, x + 280, 570], 20, fill=b.accent)
        dr.text((x + 205, 545), lbl, font=font("title", 30), fill="white", anchor="mm")
    dr.polygon([(470, 300), (500, 325), (470, 350)], fill="white")
    img.save(b.out / "aplus_1_before_after.png")

    # 2. o que tem dentro (970x600): grade 5x2 com paginas pintadas e sem pintar alternadas
    img = Image.new("RGB", (970, 600), "white")
    dr = ImageDraw.Draw(img)
    dr.text((485, 22), f"{len(b.pages)} PAGES OF FUN INSIDE", font=font("title", 46), fill=b.bg, anchor="mt")
    picks = b.showcase(10)
    for k, pg in enumerate(picks):
        im = b.colored(pg) if k % 2 == 0 else Image.open(pg).convert("RGB")
        x, y = 25 + (k % 5) * 188, 90 + (k // 5) * 252
        c = card(im, 174, 238, 14)
        dr.rounded_rectangle([x - 3, y - 3, x + 177, y + 241], 16, fill=b.accent)
        img.paste(c, (x, y), c)
    img.save(b.out / "aplus_2_inside.png")

    # 3. faixa de beneficios (970x300)
    img = Image.new("RGB", (970, 300), b.accent)
    dr = ImageDraw.Draw(img)
    feats = b.mk.get("features", ["BIG BOLD LINES", "SINGLE SIDED PAGES", "8.5 x 11 INCH", "SCREEN FREE FUN"])
    for k, f in enumerate(feats):
        cx = 121 + k * 243
        dr.ellipse([cx - 60, 40, cx + 60, 160], fill="white")
        dr.text((cx, 100), str(k + 1), font=font("title", 64), fill=b.accent, anchor="mm")
        text_center(dr, (cx, 185), f, font("title", 28), "white", 220)
    img.save(b.out / "aplus_3_features.png")
    print(f"  A+: 3 imagens ({' '.join(title)})")


# ---------------------------------------------------------------- Pinterest

def pins(b: Book, n: int = 6) -> None:
    ages = " ".join(b.spec["cover"]["age_badge"]).title()
    for k, pg in enumerate(b.showcase(n), 1):
        img = Image.new("RGB", (1000, 1500), b.bg)
        dr = ImageDraw.Draw(img)
        subject = b.spec["pages"][int(pg.stem) - 1] if b.spec["pages"] else ""
        dr.text((500, 50), "FREE PRINTABLE PAGE" if b.activity else "FREE COLORING PAGE", font=font("title", 72), fill="white", anchor="mt")
        text_center(dr, (500, 140), subject.capitalize(), font("body", 40), "white", 900)
        # metade pintada, metade nao: mostra o resultado e da vontade de pintar
        bw, cw = Image.open(pg).convert("RGB"), b.colored(pg)
        bw = bw.resize(cw.size)
        if b.activity:
            half = bw
        else:
            half = Image.fromarray(np.where(np.arange(cw.width)[None, :, None] < cw.width // 2,
                                          np.asarray(cw), np.asarray(bw)).astype(np.uint8))
        c = card(half, 860, 1060, 30)
        img.paste(c, (70, 250), c)
        dr.rounded_rectangle([70, 1340, 930, 1460], 30, fill=b.accent)
        dr.text((500, 1400), f"GET ALL {len(b.pages)} IN THE BOOK  |  {ages.upper()}", font=font("title", 44),
                fill="white", anchor="mm")
        img.save(b.out / f"pin_{k:02d}.png")
    print(f"  Pinterest: {n} pins")


# ---------------------------------------------------------------- Shorts

def solve_order(puzzle: np.ndarray, solved: np.ndarray) -> np.ndarray:
    """Distancia de cada pixel do caminho da solucao ate a entrada (BFS no proprio caminho)."""
    from collections import deque
    path = (solved.astype(int) - puzzle.astype(int)) < -40  # onde a solucao escureceu a pagina
    path = ndimage.binary_dilation(path, iterations=6)  # costura onde a linha passa sob seta/parede
    path &= ~((puzzle < 100) & ~ndimage.binary_dilation((solved.astype(int) - puzzle.astype(int)) < -40, iterations=2))
    dist = np.full(path.shape, -1, np.int32)
    ys, xs = np.nonzero(path)
    start = int(np.argmin(ys))
    q = deque([(ys[start], xs[start])])
    dist[ys[start], xs[start]] = 0
    while q:
        y, x = q.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < path.shape[0] and 0 <= nx < path.shape[1] and path[ny, nx] and dist[ny, nx] < 0:
                dist[ny, nx] = dist[y, x] + 1
                q.append((ny, nx))
    return dist


def maze_short(b: Book, k: int, pg: Path, fps: int) -> None:
    W, H = 1080, 1920
    work = 900
    pz = Image.open(pg).convert("L")
    size = (work, int(work * pz.height / pz.width))
    puzzle = np.asarray(pz.resize(size, Image.LANCZOS))
    solved = np.asarray(Image.open(b.solution(pg)).convert("L").resize(size, Image.LANCZOS))
    dist = solve_order(puzzle, solved)
    hook = b.mk["hooks"][(k - 1) % len(b.mk["hooks"])]
    color = np.array(b.accent, np.uint8)

    def frame(limit: int | None, caption: str, cta: bool) -> np.ndarray:
        rgb = np.stack([puzzle] * 3, -1).copy()
        if limit is not None:
            sel = (dist >= 0) & (dist <= limit) & ((solved.astype(int) - puzzle.astype(int)) < -40)
            rgb[sel] = color
        f = Image.new("RGB", (W, H), b.bg)
        dr = ImageDraw.Draw(f)
        text_center(dr, (W // 2, 120), caption.upper(), font("title", 78), "white", 980)
        pc = card(Image.fromarray(rgb), work + 40, size[1] + 40, 36)
        f.paste(pc, ((W - pc.width) // 2, 400), pc)
        if cta:
            dr.rounded_rectangle([90, 1700, 990, 1840], 40, fill=b.accent)
            dr.text((W // 2, 1770), b.mk.get("cta", "LINK IN BIO"), font=font("title", 64), fill="white", anchor="mm")
        return np.asarray(f)

    frames = [frame(None, hook, False)] * int(fps * 2)
    top = int(dist.max())
    total = int(fps * 7)
    frames += [frame(int(top * (t + 1) / total), hook, False) for t in range(total)]
    frames += [frame(top, b.mk.get("end_caption", ""), True)] * int(fps * 3)
    path = b.out / f"short_{k:02d}.mp4"
    imageio.mimwrite(path, frames, fps=fps, codec="libx264", quality=7, macro_block_size=8)
    print(f"  Short: {path.name} ({len(frames) / fps:.0f}s)")


def shorts(b: Book, n: int = 3, fps: int = 30) -> None:
    W, H = 1080, 1920
    if b.activity:
        for k, pg in enumerate(b.showcase(n), 1):
            if b.solution(pg):
                maze_short(b, k, pg, fps)
        return
    hooks = b.mk.get("hooks", ["Watch this page come to life", "Which color would you pick?", "Kids love this one"])
    for k, pg in enumerate(b.showcase(n), 1):
        colored_img, fills, ink = colorize(pg, b.palette, seed=int(pg.stem), ai=b.ai(pg))
        h0, w0 = ink.shape
        scale = min(960 / w0, 1240 / h0)
        size = (int(w0 * scale), int(h0 * scale))
        state = np.full((h0, w0, 3), 255, np.uint8)
        state[ink] = 0

        def frame(arr: np.ndarray, caption: str, show_cta: bool) -> np.ndarray:
            f = Image.new("RGB", (W, H), b.bg)
            dr = ImageDraw.Draw(f)
            text_center(dr, (W // 2, 120), caption.upper(), font("title", 78), "white", 980)
            page = Image.fromarray(arr).resize(size, Image.LANCZOS)
            pc = card(page, size[0] + 40, size[1] + 40, 36)
            f.paste(pc, ((W - pc.width) // 2, 420), pc)
            if show_cta:
                dr.rounded_rectangle([90, 1700, 990, 1840], 40, fill=b.accent)
                dr.text((W // 2, 1770), b.mk.get("cta", "LINK IN BIO"), font=font("title", 64), fill="white", anchor="mm")
            return np.asarray(f)

        frames = [frame(state, hooks[(k - 1) % len(hooks)], False)] * int(fps * 1.2)
        # pinta regiao por regiao (maiores primeiro), ~8s no total
        total = int(fps * 8)
        per = max(len(fills) / total, 1e-9)
        done = 0.0
        for t in range(total):
            target = min(int((t + 1) * per + 0.999), len(fills))
            while done < target:
                mask, c = fills[int(done)]
                state[mask] = c
                done += 1
            state[ink] = 0
            frames.append(frame(state, hooks[(k - 1) % len(hooks)], False))
        final = frame(np.asarray(colored_img), b.mk.get("end_caption", " ".join(b.spec["cover"]["front_title"])), True)
        frames += [final] * int(fps * 3)
        path = b.out / f"short_{k:02d}.mp4"
        imageio.mimwrite(path, frames, fps=fps, codec="libx264", quality=7, macro_block_size=8)
        print(f"  Short: {path.name} ({len(frames) / fps:.0f}s)")


def posts(b: Book, start_day: int = 0) -> None:
    """Legendas + agenda sugerida (1 post por dia por canal), prontas pro Postiz ou postagem manual."""
    import datetime as dt
    name = " ".join(b.spec["cover"]["front_title"]).title()
    tags = b.mk.get("tags", ["#coloringpages", "#kidsactivities", "#coloringbook", "#screenfreekids", "#momlife"])
    site = f"https://rootboost.vercel.app/coloring/{b.slug}/"
    day0 = dt.date.today() + dt.timedelta(days=1 + start_day)
    items = []
    for k, pin in enumerate(sorted(b.out.glob("pin_*.png"))):
        items.append({
            "channel": "pinterest", "file": pin.name, "link": site,
            "title": f"Free {name} Page for Kids",
            "caption": f"Free printable page from our {name}. Print it, color it, and get all {len(b.pages)} pages in the full book. {' '.join(tags)}",
            "date": str(day0 + dt.timedelta(days=k)),
        })
    for k, vid in enumerate(sorted(b.out.glob("short_*.mp4"))):
        hook = b.mk.get("hooks", [name])[k % len(b.mk.get("hooks", [name]))]
        items.append({
            "channel": "youtube_shorts+tiktok+reels", "file": vid.name, "link": site,
            "title": f"{hook} #shorts",
            "caption": f"{hook}! Free printable pages at the link in bio. {' '.join(tags + ['#satisfying'])}",
            "date": str(day0 + dt.timedelta(days=2 * k)),
        })
    (b.out / "posts.json").write_text(json.dumps(items, indent=2, ensure_ascii=False), encoding="utf-8")
    md = [f"# Posts: {name}", "", f"Link de destino: {site}", ""]
    for it in items:
        md += [f"## {it['date']} · {it['channel']} · `{it['file']}`", f"**Titulo:** {it['title']}", "", it["caption"], ""]
    (b.out / "posts.md").write_text(chr(10).join(md), encoding="utf-8")
    print(f"  Posts: {len(items)} agendados em posts.json/posts.md")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("cmd", choices=["colorize", "aplus", "pins", "shorts", "posts", "all"])
    ap.add_argument("slug")
    a = ap.parse_args()
    b = Book(a.slug)
    if a.cmd == "colorize":
        for p in b.showcase(10):
            b.colored(p)
    if a.cmd in ("aplus", "all"):
        aplus(b)
    if a.cmd in ("pins", "all"):
        pins(b)
    if a.cmd in ("shorts", "all"):
        shorts(b)
    if a.cmd in ("posts", "all"):
        posts(b)


if __name__ == "__main__":
    main()
