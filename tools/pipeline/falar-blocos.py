# Gera, num processo so, a fala de cada bloco de um spec comercial. Roda com o Python do DMaker.
# Uso: python falar-blocos.py <spec.json> [passos=64]. O ajuste de tom fica no spec ("semitons") e entra na montagem.
# Audio em Videos/MedlyCare Reels/comercial/audios/<hash>.wav. Bloco ja gerado nao e refeito.
import hashlib, json, os, sys, wave
from pathlib import Path
import numpy as np
from dmaker.voice.library import VoiceLibrary
from dmaker.voice.synth import VoiceRequest
from dmaker.pipeline.voices import build_synthesizer
from dmaker.voice.lexicon_store import LexiconStore
from dmaker.voice.pronunciation import spoken_text
from dmaker.voice.script import normalize_for_speech
from dmaker.voice.watermark import default_watermarker, watermark_enabled

spec = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
passos = int(sys.argv[2]) if len(sys.argv) > 2 else 64
voz, seed = spec["voz"], spec.get("seed", 1)
AUD = Path(os.environ.get("STUDIO_BASE", "C:/Users/devma/Videos/MedlyCare Reels")) / "comercial" / "audios"
AUD.mkdir(parents=True, exist_ok=True)
perfil = VoiceLibrary().get(voz)
LEX = LexiconStore().for_brand(os.environ.get("MARCA", "medlycare"), "pt")
sint = build_synthesizer()
falas = [b["fala"] for b in spec["blocos"] if b.get("fala")]
for i, f in enumerate(falas):
    arq = AUD / (hashlib.sha1(f"{voz}|{seed}|{f}".encode("utf-8")).hexdigest()[:16] + ".wav")
    if arq.exists():
        continue
    r = sint.synthesize(VoiceRequest(text=normalize_for_speech(spoken_text(f, LEX), "pt"), language="pt", voice=perfil, steps=passos, seed=seed))
    a = np.clip(np.asarray(r.samples, dtype=np.float32).reshape(-1), -1, 1)
    if watermark_enabled():
        a = np.clip(np.asarray(default_watermarker(enabled=True).embed(a, r.sample_rate), dtype=np.float32).reshape(-1), -1, 1)
    with wave.open(str(arq), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(r.sample_rate); w.writeframes((a * 32767).astype("<i2").tobytes())
    print(f"{i + 1}/{len(falas)} {len(a) / r.sample_rate:.1f}s", flush=True)
print("ok")
