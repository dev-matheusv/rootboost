# Junta a voz por bloco (método do chat de marketing, 12/11/2026) numa faixa só pro narrado-passo.
# Cada fala vem pronta de falar-blocos.py -> pausar-blocos.py -> acabar-blocos.py (pausa só depois de ponto e
# interrogação, acabamento e marca d'água por último). Entre uma fala e outra entra uma pausa curta feita do fundo
# mais quieto da própria gravação (nunca silêncio digital, que soa como corte). A marca d'água fica intacta:
# juntar não muda tom nem andamento.
# Uso (Python do DMaker): python juntar-voz.py <spec.json> <pasta de saída> [pausa_entre_falas_s=0.45]
# Saída: tratado.wav, 16k.wav e falas.json na pasta (o alinhamento sai depois, com alinhar.py).
import hashlib, json, subprocess, sys, wave
from pathlib import Path
import numpy as np

FF = "C:/DMaker/bin/ffmpeg/bin/ffmpeg.exe"
BASE = Path("C:/Users/devma/Videos/MedlyCare Reels/comercial")
spec = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
out = Path(sys.argv[2])
gap_s = float(sys.argv[3]) if len(sys.argv) > 3 else 0.45
out.mkdir(parents=True, exist_ok=True)
voz, seed = spec["voz"], spec.get("seed", 1)
pasta = BASE / (("audios-final-" + spec["acabamento"] if spec.get("acabamento") else "audios-final") + ("-pausado" if spec.get("pausas") else ""))
falas = [b["fala"] for b in spec["blocos"] if b.get("fala")]



UNIDADES = {"zero": 0, "um": 1, "uma": 1, "dois": 2, "duas": 2, "três": 3, "tres": 3, "quatro": 4, "cinco": 5, "seis": 6,
            "sete": 7, "oito": 8, "nove": 9, "dez": 10, "onze": 11, "doze": 12, "treze": 13, "catorze": 14, "quatorze": 14,
            "quinze": 15, "dezesseis": 16, "dezessete": 17, "dezoito": 18, "dezenove": 19, "vinte": 20, "trinta": 30,
            "quarenta": 40, "cinquenta": 50, "sessenta": 60, "setenta": 70, "oitenta": 80, "noventa": 90, "cem": 100,
            "cento": 100, "duzentos": 200, "trezentos": 300, "quatrocentos": 400, "quinhentos": 500, "seiscentos": 600,
            "setecentos": 700, "oitocentos": 800, "novecentos": 900}


def numerais(texto):
    """
    Número falado por extenso vira algarismo só pro alinhamento e pra legenda (13/11): o Whisper escreve "2026" e "30",
    e "dois mil e vinte e seis" contava seis palavras sem par, reprovando voz boa. A voz continua falando por extenso.
    Só converte sequência com pelo menos duas palavras de número ("trinta" sozinho fica) ou com "mil".
    """
    palavras = texto.split(" ")
    out, i = [], 0
    while i < len(palavras):
        j, total, atual, usadas, fim = i, 0, 0, 0, ""
        while j < len(palavras):
            w = palavras[j]
            nucleo = w.strip(".,!?;:").lower()
            if nucleo == "e" and usadas and j + 1 < len(palavras) and palavras[j + 1].strip(".,!?;:").lower() in UNIDADES | {"mil": 0}:
                j += 1
                continue
            if nucleo == "mil":
                total += (atual or 1) * 1000
                atual = 0
            elif nucleo in UNIDADES:
                atual += UNIDADES[nucleo]
            else:
                break
            usadas += 1
            fim = w[len(w.rstrip(".,!?;:")):]
            j += 1
            if fim:
                break
        if usadas >= 2 or (usadas == 1 and palavras[i].strip(".,!?;:").lower() == "mil"):
            out.append(str(total + atual) + fim)
            i = j
        else:
            out.append(palavras[i])
            i += 1
    return " ".join(out)

def ler(p):
    with wave.open(str(p)) as w:
        return w.getframerate(), np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float32) / 32768


def fundo(a, taxa, janela=0.2):
    """Trecho mais quieto (janela de 200 ms) da gravação: o 'silêncio' natural daquela fala."""
    n = int(taxa * janela)
    if len(a) <= n:
        return np.zeros(n, dtype=np.float32)
    passo = max(1, n // 4)
    rms = [float(np.sqrt(np.mean(a[i:i + n] ** 2))) for i in range(0, len(a) - n, passo)]
    i = int(np.argmin(rms)) * passo
    return a[i:i + n].copy()


def pausa(tom, taxa, dur):
    """Repete o fundo até a duração, com cruzamento de 30 ms entre as cópias pra não estalar."""
    total, xf = int(taxa * dur), int(taxa * 0.03)
    res = tom.copy()
    rampa = np.linspace(0, 1, xf, dtype=np.float32)
    while len(res) < total:
        res = np.concatenate([res[:-xf], res[-xf:] * (1 - rampa) + tom[:xf] * rampa, tom[xf:]])
    return res[:total]


partes, taxa = [], None
for i, f in enumerate(falas):
    arq = pasta / (hashlib.sha1(f"{voz}|{seed}|{f}".encode("utf-8")).hexdigest()[:16] + ".wav")
    if not arq.exists():
        sys.exit(f"falta a voz do bloco {i + 1} ({arq.name}): rode falar-blocos, pausar-blocos e acabar-blocos")
    t, a = ler(arq)
    if taxa is None:
        taxa = t
    if t != taxa:
        sys.exit(f"bloco {i + 1} com taxa {t}, esperado {taxa}")
    if partes:
        partes.append(pausa(fundo(a, taxa), taxa, gap_s))
    partes.append(a)

faixa = np.clip(np.concatenate(partes), -1, 1)
with wave.open(str(out / "tratado.wav"), "wb") as w:
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(taxa); w.writeframes((faixa * 32767).astype("<i2").tobytes())
subprocess.run([FF, "-hide_banner", "-loglevel", "error", "-y", "-i", str(out / "tratado.wav"), "-ar", "16000", "-ac", "1",
                "-c:a", "pcm_s16le", str(out / "16k.wav")], check=True)
(out / "falas.json").write_text(json.dumps([numerais(f) for f in falas], ensure_ascii=False), encoding="utf-8")
print(f"voz por bloco: {len(falas)} falas, {len(faixa) / taxa:.1f} s")
