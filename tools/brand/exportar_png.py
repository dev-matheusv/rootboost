"""
Exporta os PNGs do logo a partir dos SVGs (Chrome sem janela, fundo transparente). Rodar depois do gerar_logo.py.
Uso: python tools/brand/exportar_png.py
"""
import pathlib
import subprocess
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
PUB = ROOT / "apps" / "rootboost-web" / "public"
BRAND = PUB / "brand"
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"

# (svg, png de saída, largura, altura)
SAIDAS = [
    ("nativa-icone.svg", "brand/png/nativa-icone-1024.png", 1024, 1024),        # ícone do app na Meta e lojas
    ("nativa-icone-quadrado.svg", "brand/png/nativa-icone-quadrado-1024.png", 1024, 1024),  # foto de perfil (rede corta em círculo)
    ("nativa-simbolo-escuro.svg", "brand/png/nativa-simbolo-escuro-1024.png", 1024, 1024),
    ("nativa-simbolo-claro.svg", "brand/png/nativa-simbolo-claro-1024.png", 1024, 1024),
    ("nativa-labs-horizontal-escuro.svg", "brand/png/nativa-labs-horizontal-escuro.png", None, 400),
    ("nativa-labs-horizontal-claro.svg", "brand/png/nativa-labs-horizontal-claro.png", None, 400),
    ("nativa-labs-vertical-escuro.svg", "brand/png/nativa-labs-vertical-escuro.png", None, 900),
    ("nativa-labs-vertical-claro.svg", "brand/png/nativa-labs-vertical-claro.png", None, 900),
    ("nativa-icone.svg", "icons/icon-192.png", 192, 192),
    ("nativa-icone.svg", "icons/icon-512.png", 512, 512),
    ("nativa-icone-maskable.svg", "icons/maskable-512.png", 512, 512),
    ("nativa-icone-quadrado.svg", "icons/apple-touch-icon.png", 180, 180),
]


def tamanho(svg_path):
    import re
    vb = re.search(r'viewBox="0 0 ([\d.]+) ([\d.]+)"', svg_path.read_text(encoding="utf-8"))
    return float(vb.group(1)), float(vb.group(2))


def main():
    (BRAND / "png").mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        for svg, out, w, h in SAIDAS:
            src = BRAND / svg
            vw, vh = tamanho(src)
            if w is None:
                w = round(h * vw / vh)
            html = pathlib.Path(tmp) / "p.html"
            html.write_text(f'<!doctype html><html><body style="margin:0;background:transparent">'
                            f'<img src="{src.as_uri()}" style="display:block;width:{w}px;height:{h}px"></body></html>', encoding="utf-8")
            dest = PUB / out
            subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1",
                            "--default-background-color=00000000", f"--window-size={w},{h}", f"--screenshot={dest}",
                            "--allow-file-access-from-files", html.as_uri()],
                           check=True, capture_output=True, timeout=60)
            print(out, f"{w}x{h}")


if __name__ == "__main__":
    main()
