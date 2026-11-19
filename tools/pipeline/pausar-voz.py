# Alonga as pausas da voz sintetizada nos silencios que ela ja tem: nenhuma palavra e cortada.
# Uso: pausar-voz.py <pasta de origem> <pasta de destino>  (tratado.wav + alinhamento.json + falas.json)
import json, sys, shutil, os
import numpy as np, soundfile as sf
ori, des = sys.argv[1], sys.argv[2]
os.makedirs(des, exist_ok=True)
x, sr = sf.read(f"{ori}/tratado.wav", dtype="float32", always_2d=True)
a = json.load(open(f"{ori}/alinhamento.json", encoding="utf-8"))
P = a["palavras"]
mono = np.abs(x).mean(axis=1)
hop = int(sr * 0.01)
env = np.sqrt(np.convolve(mono ** 2, np.ones(hop * 2) / (hop * 2), "same"))[::hop]  # 10 ms
# Vírgula fica como a voz fez: esticar uma queda curta no meio da frase soa como corte (06/11).
ALVO = {".": 0.42, "!": 0.42, "?": 0.55}
TROCA = 0.8   # troca de bloco
LIM = 0.008
# Pausa maior na troca de um bloco, para a tela terminar: "4=2.4" e a pausa depois da fala 4 (conta do zero).
EXTRA = {int(a.split("=")[0]): float(a.split("=")[1]) for a in sys.argv[3:]}
ins = []  # (tempo, extra)
for k in range(len(P) - 1):
    p, q = P[k], P[k + 1]
    fim = p["w"][-1]
    if fim not in ALVO: continue
    alvo = EXTRA.get(p["fala"], TROCA) if q["fala"] != p["fala"] else ALVO[fim]
    i0, i1 = max(int((p["e"] - 0.2) * 100), 0), min(int((q["s"] + 0.2) * 100), len(env) - 1)
    if i1 <= i0: continue
    seg = env[i0:i1]
    baixo = seg < LIM
    # maior silencio do intervalo
    melhor, ini, j = 0, None, 0
    while j < len(baixo):
        if baixo[j]:
            m = j
            while m < len(baixo) and baixo[m]: m += 1
            if m - j > melhor: melhor, ini = m - j, j
            j = m
        else: j += 1
    if melhor < (3 if q["fala"] != p["fala"] else 10):  # menos de 100 ms de silencio real: emenda arriscada, deixa como esta
        print("sem silencio apos", p["w"]); continue
    atual = melhor / 100
    extra = alvo - atual
    if extra > 0.02: ins.append(((i0 + ini + melhor / 2) / 100, extra, p["fala"] if q["fala"] != p["fala"] else None))
out, pos = [], 0
# A pausa e preenchida com o proprio fundo do silencio, em vai e vem, com transicao suave:
# silencio digital zerado no meio do ar da gravacao soa como picote.
XF = int(sr * 0.03)
# O fundo vem do trecho mais quieto da gravacao inteira. Usar o entorno do ponto de emenda repetiu resto de
# consoante em laco no R3 ("tttt tatata" por 2,6 s, 06/11).
J = int(sr * 0.15)
media = np.convolve(mono ** 2, np.ones(J) / J, "same")
c0 = int(np.argmin(media[J:-J])) + J
BASE = x[c0 - J // 2:c0 + J // 2]
def fundo(c, n):
    pp = np.concatenate([BASE, BASE[::-1]])
    return np.tile(pp, (n // len(pp) + 2, 1))[:n]
rampa = np.linspace(0, 1, XF, dtype="float32")[:, None]
y = x[:0]
for t, e, _ in ins:
    c = int(t * sr)
    a_, f = x[pos:c], fundo(c, int(e * sr) + 2 * XF)
    y = np.concatenate([y, a_[:-XF], a_[-XF:] * (1 - rampa) + f[:XF] * rampa, f[XF:-XF]])
    nx = x[c:c + XF]
    y = np.concatenate([y, f[-XF:] * (1 - rampa) + nx * rampa])
    pos = c + XF
y = np.concatenate([y, x[pos:]])
sf.write(f"{des}/tratado.wav", y, sr, subtype="PCM_24")
# Pausa de troca de bloco vale pelo numero da fala: o alinhamento pode marcar a palavra antes do silencio.
desloc = lambda t, n: t + sum(e for tt, e, b in ins if (tt < t if b is None else n > b))
for p in P: p["s"], p["e"] = round(desloc(p["s"], p["fala"]), 3), round(desloc(p["e"], p["fala"]), 3)
for n, f in enumerate(a["falas"]): f["s"], f["e"] = round(desloc(f["s"], n), 3), round(desloc(f["e"], n), 3)
json.dump(a, open(f"{des}/alinhamento.json", "w", encoding="utf-8"), ensure_ascii=False)
shutil.copy(f"{ori}/falas.json", f"{des}/falas.json")
print(f"{len(ins)} pausas, +{sum(i[1] for i in ins):.1f} s, total {len(y) / sr:.1f} s")
