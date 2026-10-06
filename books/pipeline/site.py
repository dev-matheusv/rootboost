"""
Mini site "Crayon Cove" pra receber o trafego do Pinterest/Shorts:
  landing/coloring/index.html            vitrine com todos os livros
  landing/coloring/<slug>/index.html     3 paginas gratis pra imprimir (PDF) + botao pro livro na Amazon
O pin promete "free coloring page": a pagina entrega de verdade e oferece o livro completo.

Publica junto com as landings (Vercel serve landing/ como raiz): /coloring/<slug>/
Amazon: preencher "amazon_url" no book.json depois que o KDP aprovar; ate la o botao some.

Uso: python books/pipeline/site.py
"""

from __future__ import annotations

import html
import json
from pathlib import Path

from PIL import Image
from reportlab.lib.utils import ImageReader
from reportlab.pdfgen import canvas

ROOT = Path(__file__).resolve().parent.parent
SITE = ROOT.parent / "landing" / "coloring"
BRAND = "Crayon Cove"


def books() -> list[dict]:
    out = []
    for f in sorted((ROOT / "titles").glob("*/book.json")):
        spec = json.loads(f.read_text(encoding="utf-8"))
        d = f.parent
        if (d / "out" / "interior.pdf").exists():
            spec["_dir"] = d
            out.append(spec)
    return out


def free_pages(spec: dict) -> list[Path]:
    d = spec["_dir"]
    show = spec.get("marketing", {}).get("showcase") or [1, 2, 3]
    return [d / "clean" / f"{i:02d}.png" for i in show[:3]]


def write_pdf(pages: list[Path], dest: Path, footer: str) -> None:
    W, H = 8.5 * 72, 11 * 72
    c = canvas.Canvas(str(dest), pagesize=(W, H))
    for p in pages:
        c.drawImage(ImageReader(str(p)), 36, 60, W - 72, H - 96, preserveAspectRatio=True, anchor="c")
        c.setFont("Helvetica", 10)
        c.drawCentredString(W / 2, 30, footer)
        c.showPage()
    c.save()


def thumb(src: Path, dest: Path, w: int = 520) -> None:
    im = Image.open(src).convert("RGB")
    im.thumbnail((w, int(w * 1.4)))
    im.save(dest, "JPEG", quality=82, optimize=True)


CSS = """
:root{--bg:#FFF8EE;--ink:#1d1d1f;--muted:#5b5b66;--card:#fff;--brand:#E4572E}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font-family:Fredoka,system-ui,sans-serif}
.wrap{max-width:980px;margin:0 auto;padding:24px 16px 64px}
h1{font-family:'Luckiest Guy',Fredoka,sans-serif;font-weight:400;letter-spacing:.5px;font-size:clamp(32px,6vw,52px);margin:8px 0 4px;color:var(--accent,var(--brand))}
p.lead{font-size:20px;color:var(--muted);margin:0 0 24px}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:16px}
.card{background:var(--card);border-radius:20px;padding:12px;box-shadow:0 4px 18px rgba(0,0,0,.08);text-decoration:none;color:inherit}
.card img{width:100%;border-radius:12px;display:block}
.card h3{margin:10px 4px 4px;font-size:20px}
.btn{display:inline-block;padding:16px 28px;border-radius:999px;font-weight:600;font-size:20px;text-decoration:none;margin:8px 8px 8px 0}
.btn.main{background:var(--accent,var(--brand));color:#fff}.btn.alt{background:#fff;color:var(--ink);border:2px solid var(--ink)}
.hero{display:grid;grid-template-columns:1fr 1fr;gap:24px;align-items:center}
@media(max-width:720px){.hero{grid-template-columns:1fr}}
footer{margin-top:48px;color:var(--muted);font-size:14px}
"""

HEAD = """<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>{title}</title><meta name="description" content="{desc}">
<meta property="og:title" content="{title}"><meta property="og:description" content="{desc}"><meta property="og:image" content="{og}">
<link rel="preconnect" href="https://fonts.googleapis.com"><link href="https://fonts.googleapis.com/css2?family=Fredoka:wght@400;600&family=Luckiest+Guy&display=swap" rel="stylesheet">
<style>{css}</style></head><body style="--accent:{accent}"><div class="wrap">"""


def book_page(spec: dict) -> None:
    slug = spec["slug"]
    out = SITE / slug
    out.mkdir(parents=True, exist_ok=True)
    pages = free_pages(spec)
    write_pdf(pages, out / f"free-{slug}-coloring-pages.pdf", f"Free sample from {BRAND}. Get all the pages in the full book on Amazon.")
    for k, p in enumerate(pages, 1):
        thumb(p, out / f"page{k}.jpg")
    cover = spec["_dir"] / "raw" / "cover.png"
    if cover.exists():
        thumb(cover, out / "cover.jpg", 640)
    amazon = spec.get("amazon_url")
    title = f"Free {spec['cover']['front_title'][0].title()} Coloring Pages for Kids"
    desc = f"Download 3 free printable {spec['cover']['front_title'][0].lower()} coloring pages for kids. {spec['cover']['front_tagline'].capitalize()} in the full book."
    buy = (f'<a class="btn main" href="{html.escape(amazon)}" rel="nofollow">Get the full book on Amazon</a>'
           if amazon else '<span class="btn alt">Full book coming soon to Amazon</span>')
    bullets = "".join(f"<li>{html.escape(b)}</li>" for b in spec["cover"]["back_bullets"])
    thumbs = "".join(f'<div class="card"><img src="page{k}.jpg" alt="Free coloring page {k}" loading="lazy"></div>' for k in range(1, len(pages) + 1))
    body = f"""
<a href="../" style="color:inherit">&larr; All {BRAND} books</a>
<div class="hero">
  <div>
    <h1>{html.escape(title)}</h1>
    <p class="lead">Print them at home in seconds. Kids {html.escape(' '.join(spec['cover']['age_badge']).lower())} love them.</p>
    <a class="btn main" href="free-{slug}-coloring-pages.pdf" download>Download 3 free pages (PDF)</a>
  </div>
  <div class="card"><img src="cover.jpg" alt="{html.escape(spec['title'])}"></div>
</div>
<h2 style="margin-top:40px">Your free pages</h2>
<div class="grid">{thumbs}</div>
<h2 style="margin-top:40px">Want all {len(list((spec['_dir'] / 'clean').glob('[0-9][0-9].png')))} pages?</h2>
<ul style="font-size:18px;line-height:1.6">{bullets}</ul>
{buy}
<footer>&copy; 2026 {BRAND}. Free pages are for personal and classroom use.</footer>
"""
    head = HEAD.format(title=html.escape(title), desc=html.escape(desc), og="cover.jpg", css=CSS, accent=spec["cover"]["bg"])
    (out / "index.html").write_text(head + body + "</div></body></html>", encoding="utf-8")


def hub(all_books: list[dict]) -> None:
    cards = "".join(
        f'<a class="card" href="{b["slug"]}/"><img src="{b["slug"]}/cover.jpg" alt="{html.escape(b["title"])}" loading="lazy">'
        f'<h3>{html.escape(" ".join(b["cover"]["front_title"]).title())}</h3><p style="margin:0 4px 6px;color:#5b5b66">Free pages inside</p></a>'
        for b in all_books)
    head = HEAD.format(title=f"{BRAND}: Free Coloring Pages for Kids", desc="Free printable coloring pages and activity books for kids.",
                       og="", css=CSS, accent="#E4572E")
    body = f'<h1>{BRAND}</h1><p class="lead">Free printable coloring pages for kids. Pick a theme, print, and color!</p><div class="grid">{cards}</div><footer>&copy; 2026 {BRAND}</footer>'
    (SITE / "index.html").write_text(head + body + "</div></body></html>", encoding="utf-8")


def main() -> None:
    bs = books()
    for b in bs:
        book_page(b)
    hub(bs)
    print(f"site: {SITE} ({len(bs)} livros)")


if __name__ == "__main__":
    main()
