# Sintetiza o roteiro inteiro numa geracao so (sem picar por frase como o `dmaker voice say`),
# para a fala sair continua, sem emendas. Roda com o Python do DMaker.
# Uso: python falar-continuo.py <voz> <texto.txt> <saida.wav> [passos=32] [semente=1] [falas.json]
# Com falas.json e roteiro longo (mais de LIMITE caracteres), junta falas inteiras em grupos de ate
# LIMITE caracteres: uma geracao por grupo, emendadas com uma pausa curta na troca de bloco (onde a
# imagem tambem muda). Geracoes muito longas no OmniVoice perdem qualidade no fim.
import json
import sys
import os
import wave
from pathlib import Path

import numpy as np

from dmaker.voice.library import VoiceLibrary
from dmaker.voice.synth import VoiceRequest
from dmaker.pipeline.voices import build_synthesizer
from dmaker.voice.lexicon_store import LexiconStore
from dmaker.voice.pronunciation import spoken_text
from dmaker.voice.script import normalize_for_speech
from dmaker.voice.watermark import default_watermarker, watermark_enabled

LIMITE, PAUSA = 650, 0.45
voz, texto_path, saida = sys.argv[1], Path(sys.argv[2]), Path(sys.argv[3])
passos = int(sys.argv[4]) if len(sys.argv) > 4 else 32
semente = int(sys.argv[5]) if len(sys.argv) > 5 else 1
texto = " ".join(texto_path.read_text(encoding="utf-8").split())
grupos = [texto]
if len(sys.argv) > 6 and len(texto) > LIMITE:
    grupos = []
    for fala in json.loads(Path(sys.argv[6]).read_text(encoding="utf-8")):
        if grupos and len(grupos[-1]) + 1 + len(fala) <= LIMITE:
            grupos[-1] += " " + fala
        else:
            grupos.append(fala)

perfil = VoiceLibrary().get(voz)
# Dicionario de pronuncia do DMaker (global + marca do video, MARCA no ambiente): so a fala muda, a legenda usa o texto escrito.
LEX = LexiconStore().for_brand(os.environ.get("MARCA", "medlycare"), "pt")
sint = build_synthesizer()
partes, taxa = [], None
for i, g in enumerate(grupos):
    if len(grupos) > 1:
        print(f"grupo {i + 1} de {len(grupos)}: {len(g)} caracteres", flush=True)
    fala = sint.synthesize(VoiceRequest(text=normalize_for_speech(spoken_text(g, LEX), "pt"), language="pt", voice=perfil, steps=passos, seed=semente))
    taxa = fala.sample_rate
    if partes:
        partes.append(np.zeros(int(PAUSA * taxa), dtype=np.float32))
    partes.append(np.asarray(fala.samples, dtype=np.float32).reshape(-1))
amostras = np.clip(np.concatenate(partes), -1, 1)
# Marca d'agua inaudivel de voz gerada por IA (DMK-7), como o DMaker faz em toda fala.
if watermark_enabled():
    amostras = np.clip(np.asarray(default_watermarker(enabled=True).embed(amostras, taxa), dtype=np.float32).reshape(-1), -1, 1)
saida.parent.mkdir(parents=True, exist_ok=True)
with wave.open(str(saida), "wb") as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(taxa)
    w.writeframes((amostras * 32767).astype("<i2").tobytes())
print(f"{saida} {len(amostras) / taxa:.1f} s")
