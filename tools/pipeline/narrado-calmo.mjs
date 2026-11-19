// Versao narrada em RITMO CALMO (padrao de 26/10/2026): a TELA manda no tempo e a fala acompanha.
// Diferenca para o narrado-continuo.mjs: cada bloco dura o que a sua tela precisa para ser vista
// (velocidade efetiva no maximo 1,25x) ou o que a sua fala precisa, o que for maior. A voz e gerada
// numa sintese continua e depois cada frase e posicionada no inicio do seu bloco, com silencio
// depois dela se a tela ainda estiver mostrando algo. Nada de acelerar a tela para caber na fala.
// Uso: node narrado-calmo.mjs <reel> [--voz matheus2b] [--passos 32] [--semente 1] [--so-voz] [--so-ig]
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
const FADE = 0.3;
const LEAD = 0.4;      // a frase entra 0,4 s depois do inicio do bloco (a pessoa ve a tela primeiro)
const TAIL = 0.7;      // respiro depois da frase antes do proximo bloco
const VMAX = 1.25;     // velocidade efetiva maxima da tela
const VMIN = 0.92;     // abaixo disso a imagem trava (camera lenta repete quadros): alongar o trecho no roteiro
const args = process.argv.slice(2);
const nome = args[0];
const opt = (k, d) => (args.includes(k) ? args[args.indexOf(k) + 1] : d);
const VOZ = opt("--voz", "matheus2b"), PASSOS = opt("--passos", "32"), SEMENTE = opt("--semente", "1");
const env = { ...process.env, PYTHONIOENCODING: "utf-8" };
const sh = (bin, a) => execFileSync(bin, a, { cwd: DMK, env, encoding: "utf8", maxBuffer: 64 << 20 });

const pastaProj = `${BASE}/projetos/${nome}`;
const falas = JSON.parse(readFileSync(`${pastaProj}/falas.json`, "utf8"));
const r = JSON.parse(readFileSync(`${pastaProj}/roteiro.json`, "utf8"));
const ABRE = !!r.abertura, DESL = ABRE ? 1 : 0;
// Marca do video (cores, logo e cartoes do kit do DMaker). Antes era MedlyCare fixo e video de outra marca saia com
// o logo e o CTA do MedlyCare. Marca sem kit no DMaker para aqui, em vez de sair com a identidade errada.
const MARCA = opt("--marca", r.marca ?? "medlycare");
if (!existsSync(`${DMK}/assets/brands/${MARCA}/brand.json`)) throw new Error(`marca ${MARCA} sem kit no DMaker (assets/brands/${MARCA}/brand.json)`);
const ehMedly = MARCA === "medlycare";
env.MARCA = MARCA; // o falar-continuo.py usa o dicionario de pronuncia desta marca
if (falas.length !== r.blocos.length + 1 + DESL) throw new Error(`${falas.length} falas para ${r.blocos.length} blocos`);

// 1. Fala continua + tratamento + alinhamento (mesmo cache do narrado-continuo).
// Tom da voz (05/11): reamostragem no acabar-voz.py, que refaz a marca d'agua depois de mudar as amostras.
const SEMITONS = Number(opt("--semitons", "0"));
const ACABAMENTO = opt("--acabamento", "");
const pv = `${pastaProj}/voz-continua/${VOZ}-p${PASSOS}-s${SEMENTE}${SEMITONS ? `-t${SEMITONS}` : ""}${ACABAMENTO ? `-${ACABAMENTO}` : ""}`;
mkdirSync(pv, { recursive: true });
const texto = falas.join(" ");
const txtPath = `${pv}/texto.txt`;
// O dicionario de pronuncia tambem entra no cache: mudar a pronuncia de uma palavra refaz a fala.
const lexPath = `${pv}/lexico.json`;
const lexico = sh(PY, ["-X", "utf8", "-c", "import json;from dmaker.voice.lexicon_store import LexiconStore;print(json.dumps(LexiconStore().for_brand('" + MARCA + "','pt').entries,ensure_ascii=False))"]).trim();
const mudou = !existsSync(txtPath) || readFileSync(txtPath, "utf8") !== texto || !existsSync(`${pv}/bruto.wav`)
  || !existsSync(lexPath) || readFileSync(lexPath, "utf8") !== lexico;
if (mudou) {
  writeFileSync(txtPath, texto);
  writeFileSync(lexPath, lexico);
  writeFileSync(`${pv}/falas.json`, JSON.stringify(falas));
  console.log(`sintetizando ${texto.length} caracteres com ${VOZ}, ${PASSOS} passos...`);
  // Com acabamento a sintese sai crua: a marca d'agua entra por ultimo, no acabar-voz.py.
  if (ACABAMENTO) env.DMAKER_WATERMARK = "0";
  console.log(sh(PY, [`${FERR}/falar-continuo.py`, VOZ, txtPath, `${pv}/bruto.wav`, PASSOS, SEMENTE, `${pv}/falas.json`]).trim().split("\n").pop());
  delete env.DMAKER_WATERMARK;
  if (SEMITONS || ACABAMENTO) console.log(sh(PY, ["-X", "utf8", `${FERR}/acabar-voz.py`, `${pv}/bruto.wav`, `${pv}/bruto.wav`, `--semitons=${SEMITONS}`, ...(args.includes("--mais-lenta") ? [] : ["--manter-ritmo"]), ...(ACABAMENTO ? [`--acabamento=${ACABAMENTO}`] : [])]).trim());
}
const tratado = `${pv}/tratado.wav`, w16 = `${pv}/16k.wav`, alin = `${pv}/alinhamento.json`;
if (mudou || !existsSync(tratado)) {
  sh(FF, ["-hide_banner", "-loglevel", "error", "-y", "-i", `${pv}/bruto.wav`, "-af",
    // Voz que passou pelo acabar-voz.py ja vem tratada: repetir deesser e compressor achata a fala e enfraquece a marca.
    ACABAMENTO ? "loudnorm=I=-14:TP=-1.5:LRA=11" : "highpass=f=70,deesser=i=0.25,acompressor=threshold=-20dB:ratio=2:attack=15:release=200,loudnorm=I=-14:TP=-1.5:LRA=9",
    "-ar", "48000", tratado]);
  sh(FF, ["-hide_banner", "-loglevel", "error", "-y", "-i", `${pv}/bruto.wav`, "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", w16]);
}
if (mudou || !existsSync(alin)) {
  writeFileSync(`${pv}/falas.json`, JSON.stringify(falas));
  console.log(sh(PY, [`${FERR}/alinhar.py`, w16, `${pv}/falas.json`, alin]).trim().split("\n").pop());
}
const A = JSON.parse(readFileSync(alin, "utf8"));
const durTotal = +sh(FFPROBE, ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", tratado]).trim();
if (args.includes("--so-voz")) { console.log("falas (s):", A.falas.map((f) => `${f.s.toFixed(1)}-${f.e.toFixed(1)}`).join(" / ")); process.exit(0); }

// Recorte de cada frase no audio continuo: do meio do silencio anterior ao meio do seguinte.
const cortes = A.falas.map((f, i) => {
  const ini = i === 0 ? Math.max(0, f.s - 0.08) : (A.falas[i - 1].e + f.s) / 2;
  const fim = i === A.falas.length - 1 ? Math.min(durTotal, f.e + 0.25) : (f.e + A.falas[i + 1].s) / 2;
  return { ini, fim, fala: f };
});

// 2. Duracao de cada segmento: max(o que a fala pede, o que a tela pede a <= VMAX).
const ultima = falas.length - 1;
const segs = []; // { tipo, dur, fala, bloco? }
if (ABRE) segs.push({ tipo: "abertura", fala: 0 });
r.blocos.forEach((b, i) => segs.push({ tipo: "bloco", fala: i + DESL, b }));
segs.push({ tipo: "fecho", fala: ultima });
let primeiroVideo = true;
for (const s of segs) {
  const f = A.falas[s.fala];
  const pedeFala = (f.e - f.s) + LEAD + TAIL;
  if (s.tipo === "bloco") {
    let nTrans = s.b.trechos.length - (primeiroVideo && !ABRE ? 1 : 0);
    primeiroVideo = false;
    const telaDe = () => s.b.trechos.reduce((t, [, a, z, v = 1]) => t + (z - a) / Math.min(v, VMAX), 0) - FADE * nTrans;
    // Fala mais longa que a tela: estende o ultimo trecho na propria gravacao (camera lenta trava a imagem).
    const deficit = pedeFala - telaDe();
    if (deficit > 0) {
      const ult = s.b.trechos.at(-1), v = Math.min(ult[3] ?? 1, VMAX);
      const dur = +sh(FFPROBE, ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", `${BASE}/brutos/${ult[0]}/${ult[0]}.mp4`]).trim();
      ult[2] = Math.min(dur - 0.05, ult[2] + deficit * v);
    }
    // Tela mais longa que a fala: corta o fim da gravacao (tela sem voz parece que o audio acabou antes).
    // Sobra no maximo 0,5 s depois da frase; trecho que ficar com menos de 1,5 s sai.
    // Frase que continua no bloco seguinte (termina em virgula) nao pode ganhar pausa no meio.
    const folga = /,\s*$/.test(falas[s.fala]) ? 0 : 0.5;
    let sobra = telaDe() - (pedeFala + folga);
    while (sobra > 0.05) {
      const ult = s.b.trechos.at(-1), v = Math.min(ult[3] ?? 1, VMAX);
      const cabe = (ult[2] - ult[1]) / v - 1.5;
      if (cabe >= sobra || s.b.trechos.length === 1) { ult[2] -= Math.min(sobra, Math.max(cabe, 0)) * v; break; }
      s.b.trechos.pop(); nTrans--; s.nTrans = nTrans; sobra = telaDe() - (pedeFala + folga);
    }
    const tela = telaDe();
    s.dur = Math.max(pedeFala, tela);
    s.nTrans = nTrans;
  } else s.dur = pedeFala + (s.tipo === "fecho" ? 0.8 : 0);
}
let t = 0;
for (const s of segs) { s.ini = t; t += s.dur; }
const total = t;

// 3. Audio: cada frase no inicio do seu segmento + LEAD.
const partes = segs.map((s) => ({ ...cortes[s.fala], em: s.ini + LEAD }));
const posicionado = `${pv}/calmo.wav`;
const fc = partes.map((p, i) => `[0:a]atrim=${p.ini.toFixed(3)}:${p.fim.toFixed(3)},asetpts=PTS-STARTPTS,afade=t=in:d=0.03,afade=t=out:st=${(p.fim - p.ini - 0.06).toFixed(3)}:d=0.06,adelay=${Math.round(p.em * 1000)}:all=1[a${i}]`).join(";")
  + ";" + partes.map((_, i) => `[a${i}]`).join("") + `amix=inputs=${partes.length}:normalize=0:dropout_transition=0,apad=whole_dur=${(total + 0.2).toFixed(2)}[out]`;
sh(FF, ["-hide_banner", "-loglevel", "error", "-y", "-i", tratado, "-filter_complex", fc, "-map", "[out]", "-ar", "48000", "-t", (total + 0.2).toFixed(2), posicionado]);

// 4. Legenda no tempo novo (sem a ultima fala, que esta no cartao).
const desloc = (fala) => { const p = partes[segs.findIndex((s) => s.fala === fala)]; return p.em - p.ini; };
const cues = A.falas.slice(0, ultima).map((_, i) => {
  const d = desloc(i);
  const ws = A.palavras.filter((p) => p.fala === i).map((p) => ({ start: +(p.s + d).toFixed(3), end: +(p.e + d).toFixed(3), text: p.w }));
  return { start: ws[0].start, end: ws.at(-1).end, text: ws.map((w) => w.text).join(" "), words: ws };
});
const legPath = `${pv}/legenda-calma.json`;
writeFileSync(legPath, JSON.stringify({ language: "pt", cues }, null, 1));

// 5. Portao de velocidade e relatorio.
let reprovado = false;
const relatorio = [];
const fechoTitulo = r.fecho?.[0] ?? falas[ultima].match(/^.*?[.?!](\s|$)/)?.[0].trim() ?? falas[ultima];
// Bloco unico para foto e print (fonte que nao e vertical): a imagem cresce ate encostar na legenda e o
// titulo e o subtitulo descem para logo acima dela, em vez de tres pecas soltas na tela. O brand do DMaker
// centraliza a fonte em 0,5 com margem igual nos dois eixos, entao a margem sai do tamanho desejado.
const dims = new Map();
const tamanho = (src) => {
  if (!dims.has(src)) {
    const [w, h] = sh(FFPROBE, ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0", `${BASE}/brutos/${src}/${src}.mp4`]).trim().split(",").map(Number);
    dims.set(src, { w, h });
  }
  return dims.get(src);
};
// Mesma lista do contrato da plataforma (todas existem no XFADE_TYPES do DMaker); outro valor vira fade.
const TRANSICOES = new Set(["fade", "dissolve", "slideleft", "slideup", "smoothleft", "wipeleft", "circleopen", "zoomin",
  "crosszoom", "whippan", "blurslide", "filmburn", "lightleak"]);
// As de movimento (DMaker v2) entram com curva ease; as xfade seguem lineares como sempre foram.
const MOVIMENTO = new Set(["crosszoom", "whippan", "blurslide", "filmburn", "lightleak"]);
const W = 1080, H = 1920, LARG_MAX = 0.92, FOLGA_LEG = 0.013, TOPO_MIN = 0.075;
const blocoFoto = (src, legY) => {
  const { w, h } = tamanho(src);
  // Gravacao de tela fica no layout de sempre, mesmo a de 1080x1200: crescer ate a legenda fazia a legenda comer a tela.
  if (!src.startsWith("acervo-") || w / h <= 0.7) return null;
  const cheio = Math.min(W / w, H / h);
  const k = Math.min(LARG_MAX * W / w, 2 * (legY - FOLGA_LEG - 0.5) * H / h);
  const topo = 0.5 - (h * k / H) / 2;
  return { margin: +((1 - k / cheio) / 2).toFixed(4), tituloY: +Math.max(TOPO_MIN, topo - 0.095).toFixed(3), subY: +Math.max(TOPO_MIN + 0.05, topo - 0.045).toFixed(3) };
};

const montar = (margin, legenda, cta) => {
  const timeline = [], overlays = [];
  for (const s of segs) {
    if (s.tipo === "abertura") timeline.push({ type: "card", title: r.abertura[0], subtitle: r.abertura[1], variant: "dark", duration: +s.dur.toFixed(2) });
    if (s.tipo === "fecho") timeline.push({ type: "card", title: fechoTitulo, subtitle: cta, variant: "dark", duration: +(s.dur + 0.5).toFixed(2), transition: { type: "fade", duration: 0.5 } });
    if (s.tipo !== "bloco") continue;
    const b = s.b;
    const bruto = b.trechos.reduce((x, [, a, z]) => x + (z - a), 0);
    const alvo = s.dur + FADE * s.nTrans;
    const k = b.trechos.reduce((x, [, a, z, v = 1]) => x + (z - a) / Math.min(v, VMAX), 0) / alvo; // <= 1
    const foto = blocoFoto(b.trechos[0][0], legenda.y);
    // Transicao escolhida pela IA do roteiro (bloco.transicao) so na entrada do bloco; dentro dele, fade.
    // A duracao fica em FADE para nao mexer na conta de tempo (a sobreposicao entra em s.dur + FADE * nTrans).
    const entrada = TRANSICOES.has(b.transicao) ? b.transicao : "fade";
    b.trechos.forEach(([src, a, z, v = 1], i) => {
      const vel = Math.min(v, VMAX) * k;
      if (vel > VMAX + 1e-6) reprovado = true;
      const m = blocoFoto(src, legenda.y)?.margin ?? margin;
      timeline.push({ type: "clip", src: `${BASE}/brutos/${src}/${src}.mp4`, start: a, end: z, speed: +vel.toFixed(4), reframe: { mode: "brand", margin: m }, ...(timeline.length ? { transition: { type: i === 0 ? entrada : "fade", duration: FADE, ...(i === 0 && MOVIMENTO.has(entrada) ? { curve: "ease" } : {}) } } : {}) });
    });
    const efetiva = bruto / (s.dur + FADE * s.nTrans);
    if (margin === 0.14) relatorio.push(`${b.titulo}: ${s.dur.toFixed(1)} s, tela a ${efetiva.toFixed(2)}x${efetiva < VMIN ? " (LENTA: alongar o trecho)" : ""}`);
    overlays.push({ type: "text", text: b.titulo, role: "title", y: foto?.tituloY ?? 0.137, start: +(s.ini + 0.3).toFixed(2), end: +(s.ini + s.dur - 0.1).toFixed(2), style: { size: 58, weight: "bold" } });
    if (b.sub) overlays.push({ type: "text", text: b.sub, role: "subtitle", y: foto?.subY ?? 0.188, start: +(s.ini + 0.45).toFixed(2), end: +(s.ini + s.dur - 0.1).toFixed(2), style: { size: 38, weight: "medium" } });
  }
  overlays.push({ type: "progress-bar" });
  return { timeline, overlays, captions: { source: legPath, style: { ...legenda, max_words: 4 } } };
};

const versoes = [
  { sufixo: "-narrado-instagram", margin: 0.14, legenda: { y: 0.753, size: 54, max_chars: 24 }, cta: r.ctaIg ?? (ehMedly ? "3 meses grátis, sem cartão. Link na bio" : r.fecho?.[1] ?? "") },
  { sufixo: "-narrado", margin: 0.04, legenda: { y: 0.865, size: 54, max_chars: 24 }, cta: r.ctaSite ?? (ehMedly ? "3 meses grátis, sem cartão. medlycare.com.br" : r.fecho?.[1] ?? "") },
].filter((v) => !(args.includes("--so-ig") && v.sufixo === "-narrado"));

for (const v of versoes) {
  const m = montar(v.margin, v.legenda, v.cta);
  if (reprovado) throw new Error("portao: trecho acima de 1,25x");
  const semAudio = `${pastaProj}/sem-audio-calmo${v.sufixo}.mp4`;
  const saida = `${BASE}/finais/${nome}${v.sufixo}.mp4`;
  const specPath = `${pastaProj}/spec-calmo${v.sufixo}.json`;
  writeFileSync(specPath, JSON.stringify({ name: `${nome}${v.sufixo}`, brand: MARCA, output: { preset: "instagram/reels", quality: "high", path: semAudio }, ...m, audio: { normalize: "off" } }, null, 2));
  for (const l of sh(DMAKER, ["render", specPath, "--no-ui"]).split("\n")) if (/QA:|aviso: overlays|erro/i.test(l)) console.log(l.trim());
  sh(FF, ["-hide_banner", "-loglevel", "error", "-y", "-i", semAudio, "-i", posicionado, "-map", "0:v", "-map", "1:a", "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-shortest", saida]);
  sh(DMAKER, ["thumbnail", saida, "--at", "1.5", "--out", saida.replace(/\.mp4$/, ".jpg")]);
  sh(DMAKER, ["sheet", saida]);
  const grade = saida.replace(/\.mp4$/, "_sheet.png");
  if (existsSync(grade)) renameSync(grade, `${pastaProj}/grade-revisao-calmo${v.sufixo}.png`);
  console.log("Pronto:", saida, `(~${total.toFixed(1)} s)`);
}
console.log(relatorio.join("\n"));
