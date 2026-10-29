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
  if (!voice) return "job sem voz e marca sem voz padrão: escolha uma voz no Estúdio";
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

/**
 * Foto vira trecho 1080x1350 (4:5) recortado no foco, com aproximação lenta (zoompan até 8%). Duração com folga: o
 * pipeline desacelera pra caber na fala, nunca acelera foto.
 */
export function photoClipArgs(input, output, width, height, seconds, focus) {
  const c = focusCrop(width, height, focus);
  const dur = Math.max(4, Math.ceil(seconds + 2));
  const frames = dur * 30;
  const zoom = `min(1+0.08*on/${frames},1.08)`;
  return ["-y", "-loop", "1", "-i", input, "-vf",
    `crop=${c.w}:${c.h}:${c.x}:${c.y},scale=2160:2700,zoompan=z='${zoom}':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=${frames}:s=1080x1350:fps=30,format=yuv420p`,
    "-t", String(dur), "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-movflags", "+faststart", output];
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
