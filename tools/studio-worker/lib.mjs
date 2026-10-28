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
