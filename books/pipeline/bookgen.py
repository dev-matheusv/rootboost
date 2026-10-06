"""
RootBoost Books: pipeline de livros de colorir (Amazon KDP, impressao sob demanda).

Fluxo por livro (books/titles/<slug>/book.json):
  plan      -> lista prompts e custo estimado em creditos Higgsfield (nao gasta nada)
  generate  -> gera as paginas faltantes via CLI `higgsfield` (idempotente: pula o que ja existe)
  clean     -> converte cada pagina em line art pronta pra grafica (preto puro, 300 DPI, sem cinza)
  build     -> interior.pdf + cover.pdf no padrao KDP + listing.md (ficha do anuncio)
  all       -> generate + clean + build

Uso:
  python books/pipeline/bookgen.py plan christmas
  python books/pipeline/bookgen.py generate christmas --limit 3
  python books/pipeline/bookgen.py all christmas

Nada de segredo aqui: a autenticacao e a do `higgsfield auth login` da maquina.
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import threading
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter, ImageOps
from reportlab.lib.colors import HexColor, white
from reportlab.lib.utils import ImageReader
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas

ROOT = Path(__file__).resolve().parent.parent  # books/
TITLES = ROOT / "titles"
FONTS = Path(__file__).resolve().parent / "fonts"
DPI = 300
PT = 72  # pontos por polegada (reportlab)

# KDP: papel branco, tinta preta. Espessura da lombada por pagina (polegadas).
SPINE_PER_PAGE_WHITE = 0.002252
BLEED = 0.125
# Area que o KDP reserva pro codigo de barras na contracapa (polegadas).
BARCODE_W, BARCODE_H = 2.0, 1.2

pdfmetrics.registerFont(TTFont("Title", str(FONTS / "LuckiestGuy.ttf")))
pdfmetrics.registerFont(TTFont("Body", str(FONTS / "Fredoka.ttf")))


# ---------------------------------------------------------------- spec

def load_book(slug: str) -> tuple[dict, Path]:
    d = TITLES / slug
    spec = json.loads((d / "book.json").read_text(encoding="utf-8"))
    return spec, d


def page_prompt(spec: dict, subject: str) -> str:
    return f"{spec['style']['prefix']} {subject}. {spec['style']['suffix']}"


def gen_args(spec: dict, kind: str) -> list[str]:
    """Flags do modelo vindas do book.json (pages ou cover)."""
    g = spec["generation"][kind]
    args = [g["model"]]
    for k, v in g.get("params", {}).items():
        args += [f"--{k}", str(v)]
    return args


def higgsfield() -> str:
    exe = shutil.which("higgsfield")
    if not exe:
        sys.exit("CLI `higgsfield` nao encontrada no PATH.")
    return exe


# ---------------------------------------------------------------- plan

def cmd_plan(slug: str) -> None:
    spec, d = load_book(slug)
    raw = d / "raw"
    pages = spec["pages"]
    missing = [i for i, _ in enumerate(pages, 1) if not (raw / f"{i:02d}.png").exists()]
    cover_missing = not (raw / "cover.png").exists()

    def cost(kind: str) -> float:
        out = subprocess.run([higgsfield(), "generate", "cost", *gen_args(spec, kind), "--prompt", "x"],
                             capture_output=True, text=True)
        try:
            return float(out.stdout.split()[0])
        except (ValueError, IndexError):
            print(f"  (nao consegui estimar custo de {kind}: {out.stdout or out.stderr})")
            return 0.0

    pc, cc = cost("pages"), cost("cover")
    total = pc * len(missing) + (cc if cover_missing else 0)
    print(f"{spec['title']}\n  {len(pages)} paginas, faltam {len(missing)} | capa faltando: {cover_missing}")
    print(f"  custo: {pc} cr/pagina, {cc} cr/capa -> ~{total:.2f} creditos pra completar")
    for i in missing[:3]:
        print(f"  exemplo #{i:02d}: {page_prompt(spec, pages[i - 1])[:160]}...")


# ---------------------------------------------------------------- generate

def run_job(args: list[str], prompt: str, dest: Path) -> None:
    proc = subprocess.run([higgsfield(), "generate", "create", *args, "--prompt", prompt,
                           "--wait", "--wait-timeout", "15m", "--json"],
                          capture_output=True, text=True, encoding="utf-8")
    if proc.returncode != 0:
        raise RuntimeError((proc.stderr or proc.stdout).strip()[:500])
    jobs = json.loads(proc.stdout)
    job = jobs[0] if isinstance(jobs, list) else jobs
    url = job.get("result_url")
    if job.get("status") != "completed" or not url:
        raise RuntimeError(f"job sem resultado: status={job.get('status')}")
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(".part")
    urllib.request.urlretrieve(url, tmp)
    Image.open(tmp).convert("RGB").save(dest)
    tmp.unlink()
    # guarda a proveniencia (modelo, prompt, url) pra auditoria e regeracao
    meta = {"model": args[0], "prompt": prompt, "result_url": url, "job_id": job.get("id")}
    dest.with_suffix(".json").write_text(json.dumps(meta, indent=2), encoding="utf-8")


def cmd_generate(slug: str, limit: int | None, only: list[int] | None, workers: int = 4) -> None:
    spec, d = load_book(slug)
    raw = d / "raw"
    todo = []
    if not (raw / "cover.png").exists() and not only:
        todo.append(("cover", spec["cover"]["art_prompt"], gen_args(spec, "cover"), raw / "cover.png"))
    for i, subject in enumerate(spec["pages"], 1):
        if only and i not in only:
            continue
        dest = raw / f"{i:02d}.png"
        if only or not dest.exists():  # --only forca regerar
            todo.append((f"{i:02d}", page_prompt(spec, subject), gen_args(spec, "pages"), dest))
    if limit:
        todo = todo[:limit]
    print(f"{len(todo)} imagens pra gerar ({workers} em paralelo)", flush=True)
    stop = threading.Event()

    def work(item):
        label, prompt, args, dest = item
        if stop.is_set():
            return
        attempt = 0
        while attempt < 2:
            try:
                run_job(args, prompt, dest)
                print(f"  ok {label}", flush=True)
                return
            except RuntimeError as e:
                msg = str(e).lower()
                if "rate_limit" in msg:
                    # limite de jobs simultaneos do plano: espera e tenta de novo (nao conta tentativa)
                    time.sleep(20)
                    continue
                attempt += 1
                if "credit" in msg or "plan" in msg:
                    # sem credito / plano: para tudo (nao adianta tentar o resto)
                    stop.set()
                    print(f"  PARANDO em {label}: {e}", flush=True)
                    return
                print(f"  falhou {label} (tentativa {attempt}): {e}", flush=True)

    with ThreadPoolExecutor(max_workers=workers) as pool:
        list(pool.map(work, todo))
    if stop.is_set():
        sys.exit("Creditos ou plano insuficientes no Higgsfield.")


# ---------------------------------------------------------------- clean

def clean_page(src: Path, dest: Path, box_in: tuple[float, float], threshold: int) -> dict:
    """Line art pronta pra impressao: tira cinza, recorta sobra, sobe pra 300 DPI com borda lisa."""
    im = ImageOps.grayscale(Image.open(src))
    # binariza no tamanho original pra decidir o que e traco
    a = np.asarray(im)
    ink = a < threshold
    ys, xs = np.where(ink)
    if len(xs) == 0:
        raise ValueError(f"{src.name}: pagina vazia")
    pad = int(0.02 * max(a.shape))
    x0, x1 = max(xs.min() - pad, 0), min(xs.max() + pad, a.shape[1])
    y0, y1 = max(ys.min() - pad, 0), min(ys.max() + pad, a.shape[0])
    mask = Image.fromarray(np.where(ink, 0, 255).astype(np.uint8)).crop((x0, y0, x1, y1))

    # encaixa na caixa de arte (polegadas) a 300 DPI mantendo proporcao
    bw, bh = int(box_in[0] * DPI), int(box_in[1] * DPI)
    scale = min(bw / mask.width, bh / mask.height)
    w, h = int(mask.width * scale), int(mask.height * scale)
    # upscale + leve blur + re-threshold = contorno liso sem serrilhado
    up = mask.resize((w, h), Image.LANCZOS).filter(ImageFilter.GaussianBlur(radius=max(scale * 0.6, 0.8)))
    up = up.point(lambda v: 0 if v < 128 else 255).convert("L")
    canvas_img = Image.new("L", (bw, bh), 255)
    canvas_img.paste(up, ((bw - w) // 2, (bh - h) // 2))
    dest.parent.mkdir(parents=True, exist_ok=True)
    canvas_img.save(dest, dpi=(DPI, DPI), optimize=True)

    arr = np.asarray(canvas_img)
    black = float((arr < 128).mean())
    gray_ratio = float(((a > 60) & (a < 200)).mean())  # quanto cinza o original tinha
    return {"page": src.stem, "black_ratio": round(black, 3), "orig_gray_ratio": round(gray_ratio, 3),
            "flag": black > 0.22 or gray_ratio > 0.08}


def cmd_clean(slug: str) -> None:
    spec, d = load_book(slug)
    raw, out = d / "raw", d / "clean"
    lay = spec["layout"]
    box = (lay["trim"][0] - 2 * lay["margin"], lay["trim"][1] - 2 * lay["margin"])
    report = []
    for src in sorted(raw.glob("[0-9][0-9].png")):
        report.append(clean_page(src, out / src.name, box, spec["style"].get("threshold", 150)))
    (d / "qa.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    flagged = [r["page"] for r in report if r["flag"]]
    print(f"{len(report)} paginas limpas em {out}")
    if flagged:
        print(f"  revisar visualmente (muito preto ou muito cinza no original): {', '.join(flagged)}")


# ---------------------------------------------------------------- build

def fit_text(c: canvas.Canvas, text: str, font: str, max_w: float, size: float) -> float:
    while pdfmetrics.stringWidth(text, font, size) > max_w and size > 8:
        size -= 1
    return size


def wrap(text: str, font: str, size: float, max_w: float) -> list[str]:
    lines, cur = [], ""
    for word in text.split():
        trial = f"{cur} {word}".strip()
        if pdfmetrics.stringWidth(trial, font, size) <= max_w:
            cur = trial
        else:
            lines.append(cur)
            cur = word
    if cur:
        lines.append(cur)
    return lines


def build_interior(spec: dict, d: Path) -> int:
    lay = spec["layout"]
    tw, th = lay["trim"]
    W, H = tw * PT, th * PT
    m = lay["margin"] * PT
    pages = sorted((d / "clean").glob("[0-9][0-9].png"))
    if not pages:
        sys.exit("Sem paginas limpas. Rode `clean` antes.")
    c = canvas.Canvas(str(d / "out" / "interior.pdf"), pagesize=(W, H))
    c.setTitle(spec["title"])
    c.setAuthor(spec["author"])
    n = 0

    def blank():
        nonlocal n
        c.showPage()
        n += 1

    # 1. "Este livro pertence a"
    c.setFont("Title", 40)
    c.drawCentredString(W / 2, H * 0.62, spec["interior"]["belongs_to"])
    c.setLineWidth(2)
    c.line(W * 0.2, H * 0.5, W * 0.8, H * 0.5)
    blank()
    # 2. copyright (verso)
    c.setFont("Body", 10)
    y = H * 0.25
    for line in spec["interior"]["copyright"]:
        c.drawCentredString(W / 2, y, line)
        y -= 14
    blank()
    # 3. paginas de colorir, cada uma com verso em branco (marcador nao vaza)
    for p in pages:
        c.drawImage(ImageReader(str(p)), m, m, W - 2 * m, H - 2 * m, preserveAspectRatio=True, anchor="c")
        blank()
        if lay.get("single_sided", True):
            blank()
    # 3b. solucionario (livros de atividade): 4 por pagina
    sols = sorted((d / "clean_solutions").glob("[0-9][0-9].png"))
    if sols:
        if n % 2:
            blank()  # solucoes comecam em pagina impar (direita)
        c.setFont("Title", 60)
        c.drawCentredString(W / 2, H / 2, spec["interior"].get("solutions", "Solutions"))
        blank()
        cw, ch = (W - 2 * m - 24) / 2, (H - 2 * m - 24) / 2
        for k in range(0, len(sols), 4):
            for j, p in enumerate(sols[k:k + 4]):
                x = m + (j % 2) * (cw + 24)
                y = H - m - (j // 2 + 1) * ch - (j // 2) * 24
                c.drawImage(ImageReader(str(p)), x, y, cw, ch, preserveAspectRatio=True, anchor="c")
            blank()
    # 4. pagina de teste de cores
    if spec["interior"].get("color_test"):
        c.setFont("Title", 30)
        c.drawCentredString(W / 2, H - m - 40, spec["interior"]["color_test"])
        c.setLineWidth(2)
        cols, rows, s = 4, 6, 1.2 * PT
        gx = (W - cols * s - (cols - 1) * 18) / 2
        for r in range(rows):
            for k in range(cols):
                c.roundRect(gx + k * (s + 18), H - m - 110 - (r + 1) * (s + 14), s, s, 10)
        blank()
    if n % 2:
        blank()  # KDP pede numero par de paginas
    c.save()
    return n


def build_cover(spec: dict, d: Path, page_count: int) -> dict:
    lay = spec["layout"]
    tw, th = lay["trim"]
    spine = page_count * SPINE_PER_PAGE_WHITE
    W = (2 * BLEED + 2 * tw + spine) * PT
    H = (2 * BLEED + th) * PT
    front_x = (BLEED + tw + spine) * PT
    cv = spec["cover"]
    bg = HexColor(cv["bg"])
    accent = HexColor(cv["accent"])
    c = canvas.Canvas(str(d / "out" / "cover.pdf"), pagesize=(W, H))

    # fundo inteiro (contracapa + lombada + capa)
    c.setFillColor(bg)
    c.rect(0, 0, W, H, stroke=0, fill=1)

    # capa: arte ocupando o painel da frente ate o sangramento
    art = d / "raw" / "cover.png"
    fw, fh = (tw + BLEED) * PT, H
    if art.exists():
        im = Image.open(art).convert("RGB")
        target = fw / fh
        if im.width / im.height > target:
            nw = int(im.height * target)
            im = im.crop(((im.width - nw) // 2, 0, (im.width - nw) // 2 + nw, im.height))
        else:
            nh = int(im.width / target)
            im = im.crop((0, (im.height - nh) // 2, im.width, (im.height - nh) // 2 + nh))
        need = (int((tw + BLEED) * DPI), int((th + 2 * BLEED) * DPI))
        if im.width < need[0]:
            im = im.resize(need, Image.LANCZOS)  # TODO: trocar por upscale de IA (bytedance_image_upscale) quando houver credito
        c.drawImage(ImageReader(im), front_x, 0, fw, fh)
    else:
        print("  aviso: sem raw/cover.png, capa sai so com fundo e texto")

    safe = 0.375 * PT  # zona segura do KDP a partir do corte
    # faixa do titulo
    band_h = 2.9 * PT
    band_y = H - BLEED * PT - safe - band_h
    c.setFillColor(white)
    c.roundRect(front_x + safe, band_y, tw * PT - 2 * safe, band_h, 24, stroke=0, fill=1)
    c.setFillColor(accent)
    tmax = tw * PT - 2 * safe - 30
    title_lines = cv["front_title"]
    y = band_y + band_h - 20
    for line in title_lines:
        size = fit_text(c, line, "Title", tmax, 64)
        c.setFont("Title", size)
        y -= size
        c.drawCentredString(front_x + tw * PT / 2, y, line)
        y -= 6
    c.setFont("Body", 18)
    c.setFillColor(HexColor("#333333"))
    c.drawCentredString(front_x + tw * PT / 2, band_y + 14, cv["front_tagline"])
    # selo de idade (o que mais converte na capa infantil)
    c.setFillColor(accent)
    cx, cy, r = front_x + tw * PT - safe - 0.75 * PT, BLEED * PT + safe + 0.75 * PT, 0.75 * PT
    c.circle(cx, cy, r, stroke=0, fill=1)
    c.setFillColor(white)
    c.setFont("Title", 20)
    c.drawCentredString(cx, cy + 4, cv["age_badge"][0])
    c.setFont("Title", 26)
    c.drawCentredString(cx, cy - 22, cv["age_badge"][1])

    # contracapa: chamada + 4 amostras + area do codigo de barras livre
    bx0, bx1 = BLEED * PT + safe, (BLEED + tw) * PT - safe
    c.setFillColor(white)
    c.setFont("Title", 40)
    y = H - BLEED * PT - safe - 40
    for line in wrap(cv["back_headline"], "Title", 40, bx1 - bx0):
        c.drawCentredString((bx0 + bx1) / 2, y, line)
        y -= 46
    c.setFont("Body", 20)
    y -= 10
    for bullet in cv["back_bullets"]:
        for i, line in enumerate(wrap(bullet, "Body", 20, bx1 - bx0 - 24)):
            c.drawString(bx0 + (0 if i == 0 else 18), y, ("• " if i == 0 else "") + line)
            y -= 26
        y -= 6
    # 3 amostras grandes; se o kit de marketing ja pintou alguma, a do meio sai colorida
    samples = sorted((d / "clean").glob("[0-9][0-9].png"))
    show = spec.get("marketing", {}).get("showcase")
    if show:
        pick = [d / "clean" / f"{i:02d}.png" for i in show[:3]]
    else:
        pick = [samples[int(i * (len(samples) - 1) / 2)] for i in range(3)] if len(samples) >= 3 else samples
    gap = 16
    sw = (bx1 - bx0 - 2 * gap) / 3
    sh = sw * 1.3
    floor = BLEED * PT + 0.25 * PT + BARCODE_H * PT + 20  # acima do codigo de barras
    sy = max(floor, y - 24 - sh)
    for i, p in enumerate(pick):
        x = bx0 + i * (sw + gap)
        colored = d / "marketing" / "colored" / p.name
        src = colored if (i == 1 and colored.exists()) else p
        c.setFillColor(white)
        c.roundRect(x, sy, sw, sh, 12, stroke=0, fill=1)
        c.drawImage(ImageReader(str(src)), x + 6, sy + 6, sw - 12, sh - 12, preserveAspectRatio=True, anchor="c")
    c.setFillColor(white)
    c.setFont("Title", 22)
    c.drawString(bx0, BLEED * PT + 0.25 * PT + 20, spec["author"].upper())
    # area do codigo de barras: o KDP imprime ali, deixamos branco
    c.setFillColor(white)
    c.rect(bx1 - BARCODE_W * PT, BLEED * PT + 0.25 * PT, BARCODE_W * PT, BARCODE_H * PT, stroke=0, fill=1)

    # lombada: KDP so aceita texto na lombada com 79+ paginas, e recomenda 100+
    if page_count >= 100:
        c.saveState()
        c.translate((BLEED + tw + spine / 2) * PT, H / 2)
        c.rotate(-90)
        c.setFillColor(white)
        c.setFont("Title", min(spine * PT * 0.55, 18))
        c.drawCentredString(0, -min(spine * PT * 0.55, 18) / 3, cv["spine"])
        c.restoreState()
    c.save()
    return {"width_in": round(W / PT, 4), "height_in": round(H / PT, 4), "spine_in": round(spine, 4)}


def build_listing(spec: dict, d: Path, page_count: int, cover: dict) -> None:
    L = spec["listing"]
    designs = len(list((d / "clean").glob("[0-9][0-9].png")))
    md = [
        f"# Ficha KDP: {spec['title']}",
        "",
        "Copiar e colar no KDP (Bookshelf > Create > Paperback).",
        "",
        "## 1. Detalhes do livro",
        f"- **Idioma:** {L['language']}",
        f"- **Titulo:** {spec['title']}",
        f"- **Subtitulo:** {spec['subtitle']}",
        f"- **Autor (pen name):** {spec['author']}",
        f"- **Faixa etaria:** {L['age_min']} a {L['age_max']} anos",
        "- **Descricao (cole no editor do KDP):**",
        "",
        "```html",
        L["description_html"].strip(),
        "```",
        "",
        "- **Palavras chave (7 caixas):**",
        *[f"  {i}. {k}" for i, k in enumerate(L["keywords"], 1)],
        "- **Categorias (escolher no navegador de categorias do KDP):**",
        *[f"  - {cat}" for cat in L["categories"]],
        "- **Conteudo adulto:** Nao",
        "",
        "## 2. Conteudo",
        "- **ISBN:** usar o ISBN gratis do KDP",
        "- **Tinta e papel:** Black & white interior, white paper",
        f"- **Tamanho:** {spec['layout']['trim'][0]} x {spec['layout']['trim'][1]} in",
        "- **Sangramento (bleed):** No bleed",
        "- **Acabamento da capa:** Glossy",
        f"- **Miolo:** `out/interior.pdf` ({page_count} paginas, {designs} desenhos de pagina unica)",
        f"- **Capa:** `out/cover.pdf` ({cover['width_in']} x {cover['height_in']} in, lombada {cover['spine_in']} in)",
        "- **Conteudo gerado por IA:** **SIM**. Marcar imagens como geradas por IA (ferramenta: Higgsfield). "
        "Texto: escrito/editado por humano com ajuda de IA. Esconder isso viola a politica do KDP.",
        "",
        "## 3. Preco",
        f"- **Marketplace principal:** Amazon.com, **US$ {L['price_usd']}**",
        "- Conferir o royalty que o KDP calcula na tela (depende do custo de impressao das paginas).",
        "- Expanded Distribution: deixar desligado no inicio (royalty menor).",
    ]
    (d / "out" / "listing.md").write_text("\n".join(md) + "\n", encoding="utf-8")


def cmd_build(slug: str) -> None:
    spec, d = load_book(slug)
    (d / "out").mkdir(exist_ok=True)
    n = build_interior(spec, d)
    cover = build_cover(spec, d, n)
    build_listing(spec, d, n, cover)
    print(f"ok: {d / 'out'} (miolo {n} pags, capa {cover['width_in']}x{cover['height_in']} in)")


# ---------------------------------------------------------------- cli

def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("cmd", choices=["plan", "generate", "clean", "build", "all"])
    ap.add_argument("slug", help="pasta em books/titles/")
    ap.add_argument("--limit", type=int, help="gerar no maximo N imagens (teste)")
    ap.add_argument("--workers", type=int, default=4, help="geracoes em paralelo")
    ap.add_argument("--only", type=lambda s: [int(x) for x in s.split(",")], help="regerar paginas, ex: 3,7,12")
    a = ap.parse_args()
    if a.cmd == "plan":
        cmd_plan(a.slug)
    if a.cmd in ("generate", "all"):
        cmd_generate(a.slug, a.limit, a.only, a.workers)
    if a.cmd in ("clean", "all"):
        cmd_clean(a.slug)
    if a.cmd in ("build", "all"):
        cmd_build(a.slug)


if __name__ == "__main__":
    main()
