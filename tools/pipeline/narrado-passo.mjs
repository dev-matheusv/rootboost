// Reel de PASSO A PASSO: o processo inteiro na tela, sem cartao de abertura, tela grande e a fala inteira.
// A tela vira um video continuo (tela.mp4), montado do bruto conforme o tempo de cada fala: trecho por trecho,
// com a imagem parada quando a fala pede mais tempo. Velocidade maxima 1,25x. Tela alta no quadro e legenda abaixo dela.
// Uso: node narrado-passo.mjs <reel> --voz-dir <pasta com tratado.wav e alinhamento.json>
//      node narrado-passo.mjs <reel> --audio <arquivo gravado por uma pessoa>   (trata e alinha antes)
// projetos/<reel>/tela.json: { "fonte": "ficha", "blocos": [ [ [a, b], [a, b, velocidade], { "hold": t, "peso": 1 } ], ... ] }
import { readFileSync, writeFileSync, mkdirSync, existsSync, renameSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { dirname } from "node:path";
import { fileURLToPath } from "node:url";

// Caminhos por variável (19/11, ferramentas trazidas do marketing pro repositório); o padrão é o PC do Matheus.
const BASE = process.env.STUDIO_BASE ?? "C:/Users/devma/Videos/MedlyCare Reels";
const FERR = dirname(fileURLToPath(import.meta.url));
const DMK = process.env.DMAKER_HOME ?? "C:/DMaker";
const DMAKER = process.env.DMAKER_PATH ?? `${DMK}/.venv/Scripts/dmaker.exe`, PY = process.env.DMAKER_PYTHON ?? `${DMK}/.venv/Scripts/python.exe`;
const FF = process.env.FFMPEG_PATH ?? `${DMK}/bin/ffmpeg/bin/ffmpeg.exe`, FFPROBE = process.env.FFPROBE_PATH ?? FF.replace(/ffmpeg(\.exe)?$/i, "ffprobe$1");
const OFF = 0.3, VMAX = 1.25, MARGEM = 0.05, FPS = 30;
const args = process.argv.slice(2);
const nome = args[0];
const opt = (k, d) => (args.includes(k) ? args[args.indexOf(k) + 1] : d);
// --tela-alta (13/11, Estúdio): gravação em tela de celular (430x760). A tela entra menor e mais alta no quadro, com o
// título acima e a legenda abaixo dela, fora da barra do Instagram. Sem a opção, fica o padrão (540x600).
const TELA_ALTA = process.argv.includes("--tela-alta");
const env = { ...process.env, PYTHONIOENCODING: "utf-8", ...(TELA_ALTA ? { TELA_SOBE: "130" } : {}) };
const sh = (bin, a) => execFileSync(bin, a, { cwd: DMK, env, encoding: "utf8", maxBuffer: 64 << 20 });

const proj = `${BASE}/projetos/${nome}`;
const ler = (p) => JSON.parse(readFileSync(p, "utf8"));
const falas = ler(`${proj}/falas.json`), r = ler(`${proj}/roteiro.json`);
// --legenda frase (13/11, pedido do marketing pros tutoriais do Estudio): frase inteira, cortada so na virgula, no ponto
// ou em pausa, ate 64 caracteres; o libass quebra em ate 2 linhas equilibradas. Sem a opcao, fica o padrao de 5 palavras.
const LEGENDA_FRASE = opt("--legenda") === "frase";
const SO_VOZ = args.includes("--so-voz"); // so a voz pausada, para a pessoa ouvir antes da gravacao
const tela = SO_VOZ ? null : ler(`${proj}/tela.json`);
if (!SO_VOZ && (falas.length !== r.blocos.length + 1 || tela.blocos.length !== r.blocos.length)) throw new Error("falas, blocos e tela nao batem");

// 1. Voz: pasta pronta (sintetizada) ou gravacao de uma pessoa, tratada de leve e alinhada ao roteiro.
let pv = opt("--voz-dir");
if (args.includes("--audio")) {
  pv = `${proj}/voz-gravada`;
  mkdirSync(pv, { recursive: true });
  sh(FF, ["-hide_banner", "-loglevel", "error", "-y", "-i", opt("--audio"), "-af", "highpass=f=70,deesser=i=0.2,acompressor=threshold=-20dB:ratio=2:attack=15:release=200,loudnorm=I=-14:TP=-1.5:LRA=9", "-ar", "48000", `${pv}/tratado.wav`]);
  sh(FF, ["-hide_banner", "-loglevel", "error", "-y", "-i", opt("--audio"), "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", `${pv}/16k.wav`]);
  writeFileSync(`${pv}/falas.json`, JSON.stringify(falas));
  console.log(sh(PY, [`${FERR}/alinhar.py`, `${pv}/16k.wav`, `${pv}/falas.json`, `${pv}/alinhamento.json`]).trim().split("\n").pop());
}
if (!pv) throw new Error("informe --voz-dir ou --audio");
// --pausar: pausas nos silencios reais da voz (pausar-voz.py) e marca d'agua refeita por ultimo. Usado pelo worker.
if (args.includes("--pausar")) {
  const fim = `${pv}-final`;
  console.log(sh(PY, ["-X", "utf8", `${FERR}/pausar-voz.py`, pv, fim]).trim().split("\n").pop());
  console.log(sh(PY, ["-X", "utf8", `${FERR}/acabar-voz.py`, `${fim}/tratado.wav`, `${fim}/t2.wav`, "--semitons=0", "--manter-ritmo"]).trim().split("\n").pop());
  renameSync(`${fim}/t2.wav`, `${fim}/tratado.wav`);
  pv = fim;
}
if (SO_VOZ) process.exit(0);
const A = ler(`${pv}/alinhamento.json`), tratado = `${pv}/tratado.wav`;
const durVoz = +sh(FFPROBE, ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", tratado]).trim();
const L = [0, ...A.falas.slice(1).map((f) => f.s + OFF - 0.15), durVoz + OFF + 1.2];
console.log("falas (s):", A.falas.map((f) => `${(f.s + OFF).toFixed(1)}-${(f.e + OFF).toFixed(1)}`).join(" / "));

// 2. Tela continua: cada bloco dura o que a sua fala dura.
const ultimoT = (segs) => { const s = segs.at(-1); return Array.isArray(s) ? s[1] : s.hold; };
const pecas = [];
tela.blocos.forEach((segs, i) => {
  const D = L[i + 1] - L[i];
  const mov = segs.filter(Array.isArray);
  const base = mov.reduce((t, [a, b, v = 1]) => t + (b - a) / v, 0);
  let holds = segs.filter((s) => !Array.isArray(s));
  const lista = holds.length ? segs : [...segs, { hold: ultimoT(segs) - 0.05, peso: 1 }];
  holds = lista.filter((s) => !Array.isArray(s));
  const k = Math.max(1, base / D);
  // Tela maior que a fala: corta o comeco do bloco e guarda o fim, que e o resultado da acao.
  if (k > VMAX) {
    const excesso = base - D * VMAX, s0 = mov[0];
    if (s0[1] - s0[0] > excesso + 0.5) { s0[0] = +(s0[0] + excesso).toFixed(2); console.warn(`aviso: bloco ${i + 1} cortou ${excesso.toFixed(1)} s do inicio da tela`); }
    else console.warn(`aviso: bloco ${i + 1} precisaria de ${k.toFixed(2)}x, encurte os trechos`);
  }
  const base2 = mov.reduce((t, [a, b, v = 1]) => t + (b - a) / v, 0);
  const vel = Math.min(Math.max(1, base2 / D), VMAX);
  const parada = Math.max(D - base2 / vel, 0.04 * holds.length);
  if (parada > 6) console.warn(`aviso: bloco ${i + 1} fica ${parada.toFixed(1)} s com a imagem parada; a cena tem pouca acao para essa fala`);
  const pesos = holds.reduce((t, h) => t + (h.peso ?? 1), 0);
  console.log(`bloco ${i + 1}: ${D.toFixed(1)} s, tela ${base2.toFixed(1)} s, ${vel.toFixed(2)}x, parada ${parada.toFixed(1)} s`);
  for (const s of lista) {
    if (Array.isArray(s)) pecas.push({ a: s[0], b: s[1], v: (s[2] ?? 1) * vel });
    else pecas.push({ hold: s.hold, d: (parada * (s.peso ?? 1)) / pesos });
  }
});
const telaMp4 = `${proj}/tela.mp4`;
const fonte = `${BASE}/brutos/${tela.fonte}/${tela.fonte}.mp4`;
const fontes = [...new Set([tela.fonte, ...pecas.map((p) => p.src).filter(Boolean)])];
const filtros = pecas.map((p, i) => (p.hold !== undefined
  ? `[0:v]fps=${FPS},trim=start=${p.hold.toFixed(3)}:duration=${(1.5 / FPS).toFixed(4)},setpts=PTS-STARTPTS,tpad=stop_mode=clone:stop_duration=${p.d.toFixed(3)},trim=duration=${p.d.toFixed(3)},setpts=PTS-STARTPTS[p${i}]`
  : `[0:v]fps=${FPS},trim=start=${p.a}:end=${p.b},setpts=(PTS-STARTPTS)/${p.v.toFixed(4)},fps=${FPS}[p${i}]`));
// Marcas: destaque na linha que a voz esta lendo. {x, y, w, h, de, ate}, em pixels do bruto e segundos da tela.
const marcas = (tela.marcas ?? []).flatMap((m) => { const e = `enable='between(t,${m.de},${m.ate})'`; return [`drawbox=x=${m.x}:y=${m.y}:w=${m.w}:h=${m.h}:color=0x12A594@0.16:t=fill:${e}`, `drawbox=x=${m.x}:y=${m.y}:w=${m.w}:h=${m.h}:color=0x12A594@0.95:t=5:${e}`]; }).join(",") || "null";
writeFileSync(`${proj}/tela.filtro.txt`, `${filtros.join(";\n")};\n${pecas.map((_, i) => `[p${i}]`).join("")}concat=n=${pecas.length}:v=1:a=0[v0];
[v0]${marcas}[v]`);
sh(FF, ["-hide_banner", "-loglevel", "error", "-y", "-i", fonte, "-/filter_complex", `${proj}/tela.filtro.txt`, "-map", "[v]", "-c:v", "libx264", "-crf", "16", "-preset", "fast", "-pix_fmt", "yuv420p", telaMp4]);
const durTela = +sh(FFPROBE, ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", telaMp4]).trim();
console.log(`tela ${durTela.toFixed(1)} s, previsto ${L.at(-2).toFixed(1)} s`);
if (fontes.length > 1) throw new Error("uma fonte por reel");

// 3. Legenda sem a ultima fala, que esta escrita no cartao final.
const ultima = falas.length - 1;
const cues = A.falas.slice(0, ultima).map((_, i) => {
  const ws = A.palavras.filter((p) => p.fala === i).map((p) => ({ start: +(p.s + OFF).toFixed(3), end: +(p.e + OFF).toFixed(3), text: p.w }));
  return { start: ws[0].start, end: ws.at(-1).end, text: ws.map((w) => w.text).join(" "), words: ws };
});
const legenda = `${proj}/legenda.json`;
writeFileSync(legenda, JSON.stringify({ language: "pt", cues }, null, 1));

// 4. Render: tela grande e continua, titulo do passo acima, legenda abaixo, cartao curto no fim.
const montar = (cta) => {
  const overlays = r.blocos.map((b, i) => ({ type: "text", text: b.titulo, role: "title", y: 0.092, start: +(L[i] + (i ? 0.1 : 0)).toFixed(2), end: +(L[i + 1] - 0.05).toFixed(2), style: { size: 58, weight: "bold" } }));
  overlays.push({ type: "progress-bar" });
  return {
    timeline: [
      { type: "clip", src: telaMp4, start: 0, end: +durTela.toFixed(3), reframe: { mode: "brand", margin: TELA_ALTA ? 0.214 : MARGEM } },
      { type: "card", title: r.fecho[0], subtitle: cta, variant: "dark", duration: +(L.at(-1) - L.at(-2) + 0.3).toFixed(2), transition: { type: "fade", duration: 0.4 } },
    ],
    overlays,
    captions: { source: legenda, style: LEGENDA_FRASE ? { y: TELA_ALTA ? 0.78 : 0.735, size: 50, max_chars: 64, max_words: 12 } : { y: 0.735, size: 54, max_chars: 30, max_words: 5 } },
  };
};
const versoes = [
  { sufixo: "-narrado-instagram", cta: `${r.fecho[1]}. Link na bio` },
  { sufixo: "-narrado", cta: `${r.fecho[1]}. medlycare.com.br` },
].filter((v) => !(args.includes("--so-ig") && v.sufixo === "-narrado"));
for (const v of versoes) {
  const semAudio = `${proj}/sem-audio${v.sufixo}.mp4`, saida = `${BASE}/finais/${nome}${v.sufixo}.mp4`, spec = `${proj}/spec${v.sufixo}.json`;
  writeFileSync(spec, JSON.stringify({ name: `${nome}${v.sufixo}`, brand: r.marca ?? "medlycare", output: { preset: "instagram/reels", quality: "high", path: semAudio }, ...montar(v.cta), audio: { normalize: "off" } }, null, 2));
  for (const l of sh(PY, ["-X", "utf8", `${FERR}/dmaker-tela-alta.py`, "render", spec, "--no-ui"]).split("\n")) if (/QA:|aviso: overlays|erro/i.test(l)) console.log(l.trim());
  sh(FF, ["-hide_banner", "-loglevel", "error", "-y", "-i", semAudio, "-i", tratado, "-filter_complex", `[1:a]adelay=${OFF * 1000}:all=1,apad[a]`, "-map", "0:v", "-map", "[a]", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-shortest", saida]);
  sh(DMAKER, ["thumbnail", saida, "--at", "3", "--out", saida.replace(/\.mp4$/, ".jpg")]);
  sh(DMAKER, ["sheet", saida]);
  const grade = saida.replace(/\.mp4$/, "_sheet.png");
  if (existsSync(grade)) renameSync(grade, `${proj}/grade-revisao-calmo${v.sufixo}.png`);
  console.log("Pronto:", saida, `(~${L.at(-1).toFixed(1)} s)`);
}
