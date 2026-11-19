# Acabamento da voz: tom por reamostragem (sem vocoder de fase), corte de grave e marca d'agua por ultimo.
# Roda com o Python do DMaker. A marca tem de ser a ultima transformacao das amostras (dmaker/voice/watermark.py).
# Uso: python acabar-voz.py <entrada.wav> <saida.wav> [--semitons=-1] [--manter-ritmo] [--sem-marca] [--acabamento=corpo|corpo-forte]
import os, subprocess, sys, wave
import numpy as np
from dmaker.voice.watermark import default_watermarker

FF = os.environ.get("FFMPEG_PATH", os.environ.get("DMAKER_HOME", "C:/DMaker") + "/bin/ffmpeg/bin/ffmpeg.exe")
args = [a for a in sys.argv[1:] if not a.startswith("--")]
entrada, saida = args[0], args[1]
semitons = next((float(a.split("=")[1]) for a in sys.argv if a.startswith("--semitons=")), -1.0)
fator = 2 ** (semitons / 12)
with wave.open(entrada) as w:
    taxa = w.getframerate()
filtro = f"asetrate={int(taxa * fator)},aresample={taxa},"
if "--manter-ritmo" in sys.argv:
    filtro += f"atempo={1 / fator:.4f},"
filtro += "highpass=f=70"
ACABAMENTOS = {
    "corpo": ",equalizer=f=170:t=q:w=1:g=2.5,equalizer=f=420:t=q:w=1.2:g=-1.5,equalizer=f=3200:t=q:w=1.5:g=1.5,deesser=i=0.25,acompressor=threshold=0.12:ratio=2.2:attack=12:release=160:makeup=1.3",
    "corpo-forte": ",equalizer=f=150:t=q:w=1:g=4,equalizer=f=420:t=q:w=1.2:g=-2.5,equalizer=f=3200:t=q:w=1.5:g=2,equalizer=f=9000:t=h:w=2000:g=-1.5,deesser=i=0.3,acompressor=threshold=0.1:ratio=3:attack=10:release=140:makeup=1.5",
}
filtro += next((ACABAMENTOS[a.split("=")[1]] for a in sys.argv if a.startswith("--acabamento=")), "")
cru = subprocess.run([FF, "-loglevel", "error", "-i", entrada, "-af", filtro, "-ac", "1", "-ar", str(taxa), "-f", "f32le", "-"], capture_output=True, check=True).stdout
a = np.clip(np.frombuffer(cru, dtype=np.float32), -1, 1)
if "--sem-marca" not in sys.argv:
    m = default_watermarker(enabled=True)
    a = np.clip(np.asarray(m.embed(a, taxa), dtype=np.float32).reshape(-1), -1, 1)
    r = m.detect(a, taxa)
    print(saida, "marca:", r, flush=True)
with wave.open(saida, "wb") as w:
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(taxa); w.writeframes((a * 32767).astype("<i2").tobytes())
print(saida, f"{len(a) / taxa:.1f}s")
