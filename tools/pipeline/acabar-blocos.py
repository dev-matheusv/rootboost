# Aplica o acabamento da voz a todos os blocos de um spec, num processo so. Roda com o Python do DMaker.
# Uso: python acabar-blocos.py <spec.json>. Le audios/<hash>.wav e grava audios-final/<hash>.wav.
# Receita aprovada em 05/11/2026 (amostra B): um tom abaixo por reamostragem, ritmo original, corte de grave, marca por ultimo.
import hashlib, json, os, subprocess, sys, wave
from pathlib import Path
import numpy as np
from dmaker.voice.watermark import default_watermarker

FF = os.environ.get("FFMPEG_PATH", os.environ.get("DMAKER_HOME", "C:/DMaker") + "/bin/ffmpeg/bin/ffmpeg.exe")
BASE = Path(os.environ.get("STUDIO_BASE", "C:/Users/devma/Videos/MedlyCare Reels")) / "comercial"
ACABAMENTOS = {"corpo-forte": ",equalizer=f=150:t=q:w=1:g=4,equalizer=f=420:t=q:w=1.2:g=-2.5,equalizer=f=3200:t=q:w=1.5:g=2,equalizer=f=9000:t=h:w=2000:g=-1.5,deesser=i=0.3,acompressor=threshold=0.1:ratio=3:attack=10:release=140:makeup=1.5"}
spec = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
voz, seed, semitons = spec["voz"], spec.get("seed", 1), spec.get("semitons", -1)
fator = 2 ** (semitons / 12)
FINAL = BASE / (("audios-final-" + spec["acabamento"] if spec.get("acabamento") else "audios-final") + ("-pausado" if spec.get("pausas") else ""))
ORIGEM = BASE / ("audios-pausado" if spec.get("pausas") else "audios")
FINAL.mkdir(exist_ok=True)
m = default_watermarker(enabled=True)
falas = [b["fala"] for b in spec["blocos"] if b.get("fala")]
ruins = 0
for i, f in enumerate(falas):
    nome = hashlib.sha1(f"{voz}|{seed}|{f}".encode("utf-8")).hexdigest()[:16] + ".wav"
    ent, sai = ORIGEM / nome, FINAL / nome
    if sai.exists():
        continue
    if not ent.exists():
        print(f"falta {i + 1}: {f[:40]}", flush=True)
        continue
    with wave.open(str(ent)) as w:
        taxa = w.getframerate()
    ritmo = "" if spec.get("mais_lenta") else f"atempo={1 / fator:.4f},"
    filtro = f"asetrate={int(taxa * fator)},aresample={taxa},{ritmo}highpass=f=70" + ACABAMENTOS.get(spec.get("acabamento"), "")
    cru = subprocess.run([FF, "-loglevel", "error", "-i", str(ent), "-af", filtro, "-ac", "1", "-ar", str(taxa), "-f", "f32le", "-"], capture_output=True, check=True).stdout
    a = np.clip(np.frombuffer(cru, dtype=np.float32), -1, 1)
    a = np.clip(np.asarray(m.embed(a, taxa), dtype=np.float32).reshape(-1), -1, 1)
    r = m.detect(a, taxa)
    if r.verdict != "found":
        ruins += 1
        print(f"marca fraca {i + 1}: {r.verdict} {r.probability} ({len(a) / taxa:.1f}s)", flush=True)
    with wave.open(str(sai), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(taxa); w.writeframes((a * 32767).astype("<i2").tobytes())
print("ok, marca fraca em", ruins)
