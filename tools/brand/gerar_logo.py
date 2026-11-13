"""
Gera o logo da Nativa Labs em SVG limpo (13/11/2026), redesenhado a partir da imagem do Matheus: quatro fios que se
cruzam no centro, cada ponta com um nó redondo, em azul e branco. Letras convertidas em traço (Montserrat, OFL, a
mesma fonte do Console), então o arquivo não depende de fonte instalada.

Uso: python tools/brand/gerar_logo.py   (grava em apps/rootboost-web/public/brand/)
"""
import math
import pathlib

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "apps" / "rootboost-web" / "public" / "brand"
FONT = ROOT / "apps" / "rootboost-web" / "src" / "assets" / "fonts" / "montserrat-regular.woff2"

AZUL = "#3D72E0"
BRANCO = "#F2F5FA"
MARINHO = "#1D2742"
FUNDO = "#26282E"

# --- Símbolo: viewBox 0..200, centro 100 ---
C = 100.0
R_NO = 74.0      # distância do centro até o nó
R_ANEL = 11.5    # raio do nó (meio do traço)
TRACO = 11.0     # espessura do fio e do anel
FOLGA = 5.5      # vão entre um fio e o que passa por cima


def ponto(angulo_graus, raio):
    a = math.radians(angulo_graus - 90)
    return C + raio * math.cos(a), C + raio * math.sin(a)


def fios():
    """Quatro fios, girados de 90 em 90 graus. Cada um sai de um nó e chega a outro 135 graus adiante, curvando pelo centro."""
    out = []
    for k in range(4):
        a0 = 90 * k
        a1 = a0 + 135
        x0, y0 = ponto(a0, R_NO - R_ANEL)
        x1, y1 = ponto(a1, R_NO - R_ANEL)
        # controle puxado pro lado de dentro da curva: passa perto do centro sem cruzar exatamente nele
        cx, cy = ponto(a0 + 67.5 + 180, 30)
        d = f"M{x0:.2f},{y0:.2f} Q{cx:.2f},{cy:.2f} {x1:.2f},{y1:.2f}"
        out.append((d, ponto(a0, R_NO), ponto(a1, R_NO), k % 2 == 0))
    return out


def simbolo(cor_clara, prefixo, grosso=False):
    """Fios com vão de verdade (máscara), então fica certo sobre qualquer fundo."""
    fs = fios()
    traco, folga, anel = (15.0, 6.5, 12.5) if grosso else (TRACO, FOLGA, R_ANEL)
    defs, corpo = [], []
    for i, (d, n0, n1, claro) in enumerate(fs):
        cor = cor_clara if claro else AZUL
        depois = fs[i + 1:]
        if depois:
            cortes = "".join(
                f'<path d="{dd}" stroke="#000" stroke-width="{traco + 2 * folga}" fill="none" stroke-linecap="round"/>'
                + "".join(f'<circle cx="{x:.2f}" cy="{y:.2f}" r="{anel + traco / 2 + folga}" fill="#000"/>' for x, y in (m0, m1))
                for dd, m0, m1, _ in depois)
            defs.append(f'<mask id="{prefixo}m{i}" maskUnits="userSpaceOnUse" x="0" y="0" width="200" height="200">'
                        f'<rect width="200" height="200" fill="#fff"/>{cortes}</mask>')
            corpo.append(f'<g mask="url(#{prefixo}m{i})">')
        corpo.append(f'<path d="{d}" stroke="{cor}" stroke-width="{traco}" fill="none"/>')
        for x, y in (n0, n1):
            corpo.append(f'<circle cx="{x:.2f}" cy="{y:.2f}" r="{anel}" stroke="{cor}" stroke-width="{traco}" fill="none"/>')
        if depois:
            corpo.append("</g>")
    return f"<defs>{''.join(defs)}</defs>{''.join(corpo)}"


# --- Palavra: NATIVA fina + LABS grossa (mesma fonte com contorno, como na imagem original) ---
_font = TTFont(str(FONT))
_glyphs = _font.getGlyphSet()
_cmap = _font.getBestCmap()
_upm = _font["head"].unitsPerEm
_kern = {}


def texto(s, x, y_base, tamanho, cor, peso_extra=0.0, espaco=0.0):
    """Devolve (svg, largura). peso_extra em fração do tamanho engrossa a letra com contorno da mesma cor."""
    esc = tamanho / _upm
    partes, cx = [], x
    for ch in s:
        nome = _cmap[ord(ch)]
        pen = SVGPathPen(_glyphs)
        _glyphs[nome].draw(TransformPen(pen, (esc, 0, 0, -esc, cx, y_base)))
        d = pen.getCommands()
        if d:
            partes.append(d)
        cx += _glyphs[nome].width * esc + espaco
    largura = cx - x - espaco
    contorno = f' stroke="{cor}" stroke-width="{peso_extra * tamanho:.2f}" stroke-linejoin="round"' if peso_extra else ""
    return f'<path d="{" ".join(partes)}" fill="{cor}"{contorno}/>', largura


def palavra(x, y_base, tamanho, cor_nativa, cor_labs):
    a, w1 = texto("NATIVA", x, y_base, tamanho, cor_nativa, espaco=tamanho * 0.02)
    b, w2 = texto("LABS", x + w1 + tamanho * 0.06, y_base, tamanho, cor_labs, peso_extra=0.07, espaco=tamanho * 0.04)
    return a + b, w1 + tamanho * 0.06 + w2


def svg(w, h, conteudo, fundo=None):
    bg = f'<rect width="{w}" height="{h}" fill="{fundo}"/>' if fundo else ""
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w:.0f} {h:.0f}" width="{w:.0f}" height="{h:.0f}">'
            f'<title>Nativa Labs</title>{bg}{conteudo}</svg>\n')


def gerar():
    OUT.mkdir(parents=True, exist_ok=True)
    arquivos = {}
    for tema, clara, texto_cor in (("escuro", BRANCO, BRANCO), ("claro", MARINHO, MARINHO)):
        sim = simbolo(clara, f"{tema}s")
        arquivos[f"nativa-simbolo-{tema}.svg"] = svg(200, 200, sim)

        # horizontal: símbolo 200 + palavra ao lado
        tam = 92
        pal, larg = palavra(0, 0, tam, texto_cor, texto_cor)
        largura = 200 + 40 + larg + 20
        arquivos[f"nativa-labs-horizontal-{tema}.svg"] = svg(
            largura, 200, simbolo(clara, f"{tema}h") + f'<g transform="translate(240 133)">{pal}</g>')

        # vertical: símbolo em cima, palavra embaixo, centralizada
        tam = 70
        pal, larg = palavra(0, 0, tam, texto_cor, texto_cor)
        largura = max(200, larg) + 40
        arquivos[f"nativa-labs-vertical-{tema}.svg"] = svg(
            largura, 300, f'<g transform="translate({(largura - 200) / 2:.1f} 0)">{simbolo(clara, f"{tema}v")}</g>'
            f'<g transform="translate({(largura - larg) / 2:.1f} 282)">{pal}</g>')

    # ícone de app (Meta, PWA, favicon): símbolo claro sobre quadrado escuro com canto arredondado
    arquivos["nativa-icone.svg"] = svg(200, 200,
        f'<rect width="200" height="200" rx="44" fill="{FUNDO}"/><g transform="translate(22 22) scale(0.78)">{simbolo(BRANCO, "ic", grosso=True)}</g>')
    arquivos["nativa-icone-quadrado.svg"] = svg(200, 200,
        f'<rect width="200" height="200" fill="{FUNDO}"/><g transform="translate(22 22) scale(0.78)">{simbolo(BRANCO, "iq", grosso=True)}</g>')

    arquivos["nativa-icone-maskable.svg"] = svg(200, 200,
        f'<rect width="200" height="200" fill="{FUNDO}"/><g transform="translate(40 40) scale(0.6)">{simbolo(BRANCO, "im", grosso=True)}</g>')
    for nome, conteudo in arquivos.items():
        (OUT / nome).write_text(conteudo, encoding="utf-8")
    print("\n".join(sorted(arquivos)))


if __name__ == "__main__":
    gerar()
