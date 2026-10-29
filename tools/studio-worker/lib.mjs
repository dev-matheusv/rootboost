// Funções puras do worker do estúdio (RB-43): sem rede nem disco, pra testar com `node --test`.

/** Nome do projeto vira pasta e nome de arquivo: só letras minúsculas, números e hífen. */
export function projectName(roteiro, jobId) {
  const raw = typeof roteiro?.nome === "string" ? roteiro.nome : "";
  const slug = raw
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 60);
  return slug || `job-${String(jobId).slice(0, 8)}`;
}

/**
 * Semente da tentativa: a do job (ou 1) na primeira, +1 a cada nova tentativa. É o "tenta a próxima semente"
 * do portão de palavras sem par (pacote do marketing, etapa 5).
 */
export function seedFor(job) {
  const base = Number.isInteger(job?.seed) ? job.seed : 1;
  const attempt = Number.isInteger(job?.attempts) && job.attempts > 0 ? job.attempts : 1;
  return base + attempt - 1;
}

/** Troca {chave} pelos valores. Cada argumento é separado antes (sem shell), então espaço no valor não quebra nada. */
export function buildArgs(template, vars) {
  return template
    .trim()
    .split(/\s+/)
    .map((part) => part.replace(/\{(\w+)\}/g, (_, k) => (k in vars ? String(vars[k]) : `{${k}}`)));
}

/** Lê a linha do alinhar.py ("110 palavras, 0 sem par no Whisper"). Sem a linha: null (não dá pra afirmar nada). */
export function parseUnmatched(log) {
  const all = [...String(log).matchAll(/(\d+)\s+sem par no Whisper/gi)];
  return all.length ? Number(all[all.length - 1][1]) : null;
}

/** Portão de velocidade do narrado-calmo: outra semente não resolve, precisa mexer no roteiro. */
export function isScriptProblem(log) {
  return /port[aã]o:/i.test(String(log));
}

/**
 * Arquivos que o narrado-calmo deixa prontos, com o papel que a API espera:
 * finais/<nome>-narrado-instagram.mp4 (+ .jpg da capa), finais/<nome>-narrado.mp4 e a grade de revisão.
 * `exists` é injetado pra testar sem disco.
 */
export function expectedOutputs(finaisDir, projectDir, nome, exists) {
  const all = [
    { role: "video-instagram", path: `${finaisDir}/${nome}-narrado-instagram.mp4`, required: true },
    { role: "cover-instagram", path: `${finaisDir}/${nome}-narrado-instagram.jpg`, required: false },
    { role: "video", path: `${finaisDir}/${nome}-narrado.mp4`, required: false },
    { role: "grade", path: `${projectDir}/grade-revisao-calmo-narrado-instagram.png`, required: false },
  ];
  const found = all.filter((o) => exists(o.path));
  const missing = all.filter((o) => o.required && !exists(o.path)).map((o) => o.path);
  return { found, missing };
}

/** Avisos que vão pro Console: linhas do log que a pessoa precisa ver na revisão. */
export function warningsFrom(log) {
  return String(log)
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => /^(AVISO|aviso|QA:)/.test(l) || /fator .* fora/i.test(l))
    .slice(0, 20);
}

/** Última linha com conteúdo, curta: vira a nota de progresso no Console. */
export function lastLine(log, max = 280) {
  const lines = String(log).split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
  const l = lines[lines.length - 1] ?? "";
  return l.length > max ? `${l.slice(0, max - 1)}…` : l;
}

/**
 * Vozes particulares (clonadas de uma pessoa) só valem pra marca dona delas. A voz do Matheus fica no PC dele e
 * só narra o MedlyCare: nunca um vídeo de outro cliente, nem por padrão. Formato: "voz:marca,voz:marca".
 */
export function parsePrivateVoices(value) {
  const map = new Map();
  for (const pair of String(value ?? "").split(",")) {
    const [voice, brand] = pair.split(":").map((s) => s?.trim().toLowerCase());
    if (voice && brand) map.set(voice, brand);
  }
  return map;
}

/** Null quando pode; senão o motivo. Voz vazia sem padrão da marca também não pode (não há voz "neutra" aqui). */
export function voiceProblem(voice, brandKey, privateVoices) {
  if (!voice) return "esta marca ainda não tem voz pronta: crie uma na página Vozes (gravar a sua ou escolher do banco) e mande renderizar de novo";
  const owner = privateVoices.get(String(voice).toLowerCase());
  if (owner && owner !== String(brandKey).toLowerCase()) return `a voz ${voice} é particular de ${owner} e não pode narrar ${brandKey}`;
  return null;
}

// --- Gravação de tela (RB-50, docs/GRAVACAO.md) ---

/** Botões que a gravação nunca clica, mesmo que a IA peça: nada destrutivo, nada que saia ou mande algo de verdade. */
const DESTRUCTIVE = /\b(excluir|apagar|deletar|remover|cancelar assinatura|encerrar conta|pagar|comprar|finalizar compra|enviar mensagem|disparar|sair|logout|desconectar|redefinir|resetar|delete|remove|pay|buy|sign out|log out)\b/i;

export function isDestructive(label) {
  return DESTRUCTIVE.test(String(label ?? ""));
}

/** A gravação só anda dentro do site do produto (mesmo domínio ou subdomínio dele). */
export function sameSite(url, allowedUrl) {
  try {
    const host = new URL(url).hostname.toLowerCase();
    const allowed = new URL(allowedUrl).hostname.toLowerCase().replace(/^www\./, "");
    return host === allowed || host.endsWith(`.${allowed}`) || host === `www.${allowed}`;
  } catch {
    return false;
  }
}

/** Blocos do roteiro que a IA deixou pra gravar: [{ index, scene, source }]. */
export function scenesToRecord(roteiro) {
  return (roteiro?.blocos ?? [])
    .map((b, i) => ({ index: i, scene: typeof b.cena === "string" ? b.cena : "", source: b.trechos?.[0]?.[0] }))
    .filter((b) => typeof b.source === "string" && b.source.startsWith("gravar-bloco-") && b.scene.trim());
}

/**
 * Troca o trecho "a gravar" de cada bloco pelo bruto gravado: [fonte, início, fim, 1]. Corta meio segundo do começo
 * (página ainda montando) e mantém o resto; bloco sem gravação fica como estava (o pipeline acusa).
 */
export function applyRecordings(roteiro, recordings) {
  const out = JSON.parse(JSON.stringify(roteiro));
  for (const r of recordings) {
    const b = out.blocos?.[r.index];
    if (!b || !(r.duration > 1)) continue;
    b.trechos = [[r.source, 0.5, Math.round((r.duration - 0.2) * 10) / 10, 1]];
  }
  return out;
}

/** Blocos que a IA montou com mídia do acervo (RB-16): [{ index, id, seconds }]. O id vem da API e só aceita hex. */
export function libraryScenes(roteiro) {
  return (roteiro?.blocos ?? [])
    .map((b, i) => ({ index: i, source: b.trechos?.[0]?.[0], seconds: Number(b.trechos?.[0]?.[2]) || 5 }))
    .filter((b) => typeof b.source === "string" && /^acervo-[0-9a-f]{32}$/.test(b.source))
    .map((b) => ({ index: b.index, id: b.source.slice(7), seconds: b.seconds }));
}

/**
 * Recorte 4:5 em volta do foco que a IA marcou na descrição (fração da imagem), com 8% de folga: a foto enche a largura
 * do 9:16 em vez de entrar pequena no meio. Print largo vira a parte que interessa. Sem foco, centro da foto.
 */
export function focusCrop(width, height, focus, aspect = 0.8) {
  const f = focus && focus.w > 0 && focus.h > 0 ? focus : { x: 0, y: 0, w: 1, h: 1 };
  let cw = f.w * width * 1.08, ch = f.h * height * 1.08;
  if (cw / ch > aspect) ch = cw / aspect; else cw = ch * aspect;
  if (cw > width) { cw = width; ch = cw / aspect; }
  if (ch > height) { ch = height; cw = ch * aspect; }
  const even = (n) => Math.max(2, Math.floor(n / 2) * 2);
  cw = even(cw); ch = even(ch);
  const clamp = (v, max) => Math.min(Math.max(0, v), Math.max(0, max));
  const x = Math.floor(clamp((f.x + f.w / 2) * width - cw / 2, width - cw));
  const y = Math.floor(clamp((f.y + f.h / 2) * height - ch / 2, height - ch));
  return { x, y, w: cw, h: ch };
}

/** Área do foco com folga (sem forçar proporção), dentro da imagem. Base do modo "caber" pra print de tela. */
export function focusBox(width, height, focus, pad = 0.06) {
  const f = focus && focus.w > 0 && focus.h > 0 ? focus : { x: 0, y: 0, w: 1, h: 1 };
  const x0 = Math.max(0, (f.x - pad / 2) * width), y0 = Math.max(0, (f.y - pad / 2) * height);
  const x1 = Math.min(width, (f.x + f.w + pad / 2) * width), y1 = Math.min(height, (f.y + f.h + pad / 2) * height);
  const even = (n) => Math.max(2, Math.floor(n / 2) * 2);
  return { x: Math.floor(x0), y: Math.floor(y0), w: Math.min(even(x1 - x0), width - Math.floor(x0)), h: Math.min(even(y1 - y0), height - Math.floor(y0)) };
}

/**
 * Foto vira trecho 1080x1350 (4:5) com aproximação lenta (zoompan até 6%). Duas formas:
 * - foco alto (retrato, rosto, antes e depois): recorta 4:5 no foco e enche a tela;
 * - foco largo (print de tela, documento): NUNCA corta texto. A área do foco inteira cabe na largura, e o resto do
 *   quadro é a própria imagem desfocada no fundo (relato de 29/10: o recorte comia "Dias de Afastamento").
 * Duração com folga: o pipeline desacelera pra caber na fala, nunca acelera foto.
 */
export function photoClipArgs(input, output, width, height, seconds, focus) {
  const dur = Math.max(4, Math.ceil(seconds + 2));
  const frames = dur * 30;
  const zoom = `min(1+0.06*on/${frames},1.06)`;
  const pan = `zoompan=z='${zoom}':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=${frames}:s=1080x1350:fps=30,format=yuv420p`;
  const out = ["-t", String(dur), "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-movflags", "+faststart", output];
  const b = focusBox(width, height, focus);
  if (b.w / b.h <= 0.8) {
    const c = focusCrop(width, height, focus);
    return ["-y", "-loop", "1", "-i", input, "-vf", `crop=${c.w}:${c.h}:${c.x}:${c.y},scale=2160:2700,${pan}`, ...out];
  }
  return ["-y", "-loop", "1", "-i", input, "-filter_complex",
    `[0:v]crop=${b.w}:${b.h}:${b.x}:${b.y},split[a][b];` +
    `[a]scale=1080:1350:force_original_aspect_ratio=increase,crop=1080:1350,boxblur=24:2,eq=brightness=-0.08[bg];` +
    `[b]scale=1032:-2[fg];[bg][fg]overlay=(W-w)/2:(H-h)/2,scale=2160:2700,${pan}`, ...out];
}

/** Troca "acervo-<id>" pelo bruto pronto: [fonte, 0, fim, 1]. Vídeo usa no máximo o que tem; bloco sem clipe fica como estava. */
export function applyLibrary(roteiro, clips) {
  const out = JSON.parse(JSON.stringify(roteiro));
  for (const c of clips) {
    const b = out.blocos?.[c.index];
    if (!b || !(c.duration > 1)) continue;
    b.trechos = [[c.source, 0, Math.round((c.duration - 0.1) * 10) / 10, 1]];
  }
  return out;
}

/**
 * Lista de brutos do chat de marketing → [{ name, description }]. "- nome (10 s): texto" ou "- a e b (15 s cada): texto"
 * (os dois com a mesma descrição). Para em "NAO SUBIR"; linha com "(CONFERIR" só entra com includeToCheck.
 */
export function parseRawList(text, { includeToCheck = false } = {}) {
  const out = [];
  for (const line of String(text).split(/\r?\n/)) {
    if (/^\s*N[AÃ]O SUBIR/i.test(line)) break;
    const m = line.match(/^-\s+([a-z0-9][a-z0-9 -]*?)\s+\([^)]*\):\s*(.+)$/i);
    if (!m) continue;
    if (/\(CONFERIR/i.test(m[2]) && !includeToCheck) continue;
    const description = m[2].replace(/\s*\(CONFERIR[^)]*\)\s*/gi, " ").trim();
    for (const name of m[1].split(/\s+e\s+/)) if (/^[a-z0-9-]+$/i.test(name.trim())) out.push({ name: name.trim(), description });
  }
  return out;
}
