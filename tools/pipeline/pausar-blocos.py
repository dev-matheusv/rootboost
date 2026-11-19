# Alonga a pausa depois de ponto final e interrogacao dentro de cada fala de um spec comercial.
# Mesma ideia do pausar-voz.py dos reels: so mexe em silencio que ja existe, preenche com o fundo da propria
# gravacao e nunca corta palavra. Virgula fica como a voz gerou.
# Uso: python pausar-blocos.py <spec.json> [<spec.json> ...]
# Le comercial/audios/<hash>.wav (voz crua, sem marca) e grava comercial/audios-pausado/<hash>.wav.
# Depois rode acabar-blocos.py: com "pausas": true no spec ele le de audios-pausado e a marca d'agua continua
# sendo a ultima mudanca no audio.
import hashlib, json, os, sys
from pathlib import Path
import numpy as np, soundfile as sf
from faster_whisper import WhisperModel

BASE = Path(os.environ.get("STUDIO_BASE", "C:/Users/devma/Videos/MedlyCare Reels")) / "comercial"
SAI = BASE / "audios-pausado"
SAI.mkdir(exist_ok=True)
ALVO = {".": 0.42, "!": 0.42, "?": 0.55}
LIM = 0.008
XF = 0.03

modelo = WhisperModel("medium", device="cpu", compute_type="int8", cpu_threads=6)

def fundo_de(x, sr, mono):
    j = int(sr * 0.15)
    media = np.convolve(mono ** 2, np.ones(j) / j, "same")
    c0 = int(np.argmin(media[j:-j])) + j
    base = x[c0 - j // 2:c0 + j // 2]
    pp = np.concatenate([base, base[::-1]])
    return lambda n: np.tile(pp, (n // len(pp) + 2, 1))[:n]

import difflib, re, unicodedata
def norm(w):
    w = unicodedata.normalize("NFD", w.lower())
    return re.sub(r"[^a-z0-9]", "", "".join(c for c in w if not unicodedata.combining(c)))

def pontuacao_do_roteiro(texto, palavras):
    # A transcricao troca ponto por virgula as vezes; quem manda e o roteiro. Alinha palavra a palavra.
    orig = texto.split()
    a, b = [norm(w) for w in orig], [norm(w.word) for w in palavras]
    fins = {}
    for bl in difflib.SequenceMatcher(None, a, b, autojunk=False).get_matching_blocks():
        for k in range(bl.size):
            fim = orig[bl.a + k][-1:]
            if fim in ALVO:
                fins[bl.b + k] = fim
    return fins

def pausar(ent, sai, texto):
    x, sr = sf.read(str(ent), dtype="float32", always_2d=True)
    mono = np.abs(x).mean(axis=1)
    hop = int(sr * 0.01)
    env = np.sqrt(np.convolve(mono ** 2, np.ones(hop * 2) / (hop * 2), "same"))[::hop]
    segs, _ = modelo.transcribe(str(ent), language="pt", word_timestamps=True, beam_size=5, condition_on_previous_text=False)
    palavras = [w for s in segs for w in s.words]
    fins = pontuacao_do_roteiro(texto, palavras)
    ins = []
    for k in range(len(palavras) - 1):
        p, q = palavras[k], palavras[k + 1]
        fim = fins.get(k, "")
        if fim not in ALVO:
            continue
        i0, i1 = max(int((p.end - 0.2) * 100), 0), min(int((q.start + 0.2) * 100), len(env) - 1)
        if i1 <= i0:
            continue
        baixo = env[i0:i1] < LIM
        melhor, ini, j = 0, None, 0
        while j < len(baixo):
            if baixo[j]:
                m = j
                while m < len(baixo) and baixo[m]:
                    m += 1
                if m - j > melhor:
                    melhor, ini = m - j, j
                j = m
            else:
                j += 1
        if melhor < 5:  # menos de 50 ms de silencio real: emendar ali arrisca cortar som, deixa como esta
            continue
        extra = ALVO[fim] - melhor / 100
        if extra > 0.02:
            ins.append(((i0 + ini + melhor / 2) / 100, extra))
    fundo = fundo_de(x, sr, mono)
    xf = int(sr * XF)
    rampa = np.linspace(0, 1, xf, dtype="float32")[:, None]
    y, pos = x[:0], 0
    for t, e in ins:
        c = int(t * sr)
        a, f = x[pos:c], fundo(int(e * sr) + 2 * xf)
        y = np.concatenate([y, a[:-xf], a[-xf:] * (1 - rampa) + f[:xf] * rampa, f[xf:-xf]])
        y = np.concatenate([y, f[-xf:] * (1 - rampa) + x[c:c + xf] * rampa])
        pos = c + xf
    y = np.concatenate([y, x[pos:]])
    sf.write(str(sai), y, sr, subtype="PCM_16")
    return len(ins), sum(e for _, e in ins)

for arq in sys.argv[1:]:
    spec = json.loads(Path(arq).read_text(encoding="utf-8"))
    voz, seed = spec["voz"], spec.get("seed", 1)
    total = 0
    for b in spec["blocos"]:
        f = b.get("fala")
        if not f:
            continue
        nome = hashlib.sha1(f"{voz}|{seed}|{f}".encode("utf-8")).hexdigest()[:16] + ".wav"
        ent, sai = BASE / "audios" / nome, SAI / nome
        if sai.exists() or not ent.exists():
            continue
        n, s = pausar(ent, sai, f)
        total += s
        print(f"  {n} pausas +{s:.1f}s  {f[:50]}", flush=True)
    print(spec["nome"], f"+{total:.1f}s no total", flush=True)
