# Alinha o texto do roteiro (correto) com as palavras que o Whisper ouviu (com tempo), para a legenda
# sair com o texto certo e o tempo real da fala. Roda com o Python do DMaker.
# Uso: python alinhar.py <audio16k.wav> <falas.json> <saida.json>
#   falas.json: lista de strings, uma por bloco. saida: {"palavras": [{w, s, e, fala}], "falas": [{s, e}]}
import difflib
import json
import re
import sys
import unicodedata
import wave

import numpy as np
from faster_whisper import WhisperModel

NUMEROS = {"1": "um", "2": "dois", "3": "tres", "4": "quatro", "5": "cinco", "6": "seis", "7": "sete",
           "8": "oito", "9": "nove", "10": "dez", "11": "onze", "12": "doze", "13": "treze", "14": "quatorze",
           "15": "quinze", "16": "dezesseis", "17": "dezessete", "18": "dezoito", "19": "dezenove", "20": "vinte"}


def norm(p: str) -> str:
    p = unicodedata.normalize("NFD", p.lower())
    p = "".join(c for c in p if unicodedata.category(c) != "Mn")
    p = re.sub(r"[^a-z0-9]", "", p)
    return NUMEROS.get(p, p)


wav, falas_path, saida = sys.argv[1:4]
with wave.open(wav, "rb") as w:
    assert w.getframerate() == 16000
    dados = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768.0
falas = json.load(open(falas_path, encoding="utf-8"))
texto = " ".join(falas)
modelo = WhisperModel("medium", device="cpu", compute_type="int8")
def ouvir(dica):
    segs, _ = modelo.transcribe(dados, language="pt", word_timestamps=True, initial_prompt=dica)
    return [{"w": x.word.strip(), "s": x.start, "e": x.end} for s in segs for x in (s.words or [])]


def pares(ouv):
    a = [norm(w) for f in falas for w in f.split()]
    return sum(b.size for b in difflib.SequenceMatcher(None, a, [norm(o["w"]) for o in ouv], autojunk=False).get_matching_blocks())


# A dica com o texto ajuda nos nomes proprios, mas as vezes desalinha o comeco: fica com a melhor das duas.
ouvidas = ouvir(texto)
total = sum(len(f.split()) for f in falas)
if pares(ouvidas) < total:
    sem_dica = ouvir(None)
    if pares(sem_dica) > pares(ouvidas):
        ouvidas = sem_dica

roteiro = [(w, i) for i, f in enumerate(falas) for w in f.split()]
a = [norm(w) for w, _ in roteiro]
b = [norm(o["w"]) for o in ouvidas]
tempos: list[tuple[float, float] | None] = [None] * len(roteiro)
for bloco in difflib.SequenceMatcher(None, a, b, autojunk=False).get_matching_blocks():
    for k in range(bloco.size):
        o = ouvidas[bloco.b + k]
        tempos[bloco.a + k] = (o["s"], o["e"])
# Palavras sem par: interpola entre os vizinhos conhecidos.
conhecidos = [i for i, t in enumerate(tempos) if t]
faltando = len(roteiro) - len(conhecidos)
for i, t in enumerate(tempos):
    if t:
        continue
    ant = max((j for j in conhecidos if j < i), default=None)
    pos = min((j for j in conhecidos if j > i), default=None)
    ini = tempos[ant][1] if ant is not None else 0.0
    fim = tempos[pos][0] if pos is not None else ini + 0.4
    n = (pos if pos is not None else len(roteiro)) - (ant if ant is not None else -1)
    k = i - (ant if ant is not None else -1)
    passo = (fim - ini) / n
    tempos[i] = (ini + passo * (k - 1), ini + passo * k)
palavras = [{"w": w, "s": round(t[0], 3), "e": round(t[1], 3), "fala": f} for (w, f), t in zip(roteiro, tempos)]
limites = [{"s": min(p["s"] for p in palavras if p["fala"] == i), "e": max(p["e"] for p in palavras if p["fala"] == i)}
           for i in range(len(falas))]
json.dump({"palavras": palavras, "falas": limites, "sem_par": faltando}, open(saida, "w", encoding="utf-8"),
          ensure_ascii=False, indent=1)
print(f"{len(palavras)} palavras, {faltando} sem par no Whisper")
