// Worker do estúdio (RB-43): roda no PC com DMaker, voz e FFmpeg. Só faz chamadas de saída pra API:
// pega o próximo job, grava roteiro.json e falas.json no projeto, roda o pipeline que já funciona
// (narrado-calmo.mjs por padrão), confere o portão de palavras sem par, sobe os arquivos pro bucket e entrega.
//
// Uso:  node worker.mjs            (fica rodando, uma rodada a cada STUDIO_POLL_SECONDS)
//       node worker.mjs --once     (uma rodada e sai)
// Config por variável de ambiente ou num worker.env ao lado (ver worker.env.example). A chave nunca vai pro repo.
import { spawn } from "node:child_process";
import { copyFileSync, existsSync, mkdirSync, openAsBlob, readFileSync, writeFileSync } from "node:fs";
import { basename, dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { buildArgs, expectedOutputs, isScriptProblem, lastLine, parseUnmatched, projectName, seedFor, warningsFrom } from "./lib.mjs";

const here = dirname(fileURLToPath(import.meta.url));
loadEnvFile(join(here, "worker.env"));

const cfg = {
  api: (process.env.ROOTBOOST_API ?? "").replace(/\/+$/, ""),
  key: process.env.ROOTBOOST_WORKER_KEY ?? "",
  workerId: process.env.STUDIO_WORKER_ID ?? `worker-${process.env.COMPUTERNAME ?? "pc"}`,
  base: process.env.STUDIO_BASE ?? "C:/Users/devma/Videos/MedlyCare Reels",
  tools: process.env.STUDIO_TOOLS ?? "C:/Users/devma/Documents/MedlyCare Marketing/ferramentas",
  // Executável e argumentos do pipeline. {nome} {voz} {seed} {tools} {base} são trocados por job.
  bin: process.env.STUDIO_PIPELINE_BIN ?? process.execPath,
  args: process.env.STUDIO_PIPELINE_ARGS ?? "{tools}/narrado-calmo.mjs {nome} --voz {voz} --semente {seed}",
  defaultVoice: process.env.STUDIO_DEFAULT_VOICE ?? "matheus2b",
  pollSeconds: Number(process.env.STUDIO_POLL_SECONDS ?? 60),
  heartbeatSeconds: Number(process.env.STUDIO_HEARTBEAT_SECONDS ?? 30),
};

if (!cfg.api || !cfg.key) {
  console.error("Falta ROOTBOOST_API ou ROOTBOOST_WORKER_KEY (no ambiente ou em worker.env).");
  process.exit(2);
}

const once = process.argv.includes("--once");
do {
  try {
    await round();
  } catch (e) {
    console.error(`[${now()}] rodada falhou:`, e.message);
  }
  if (!once) await sleep(cfg.pollSeconds * 1000);
} while (!once);

async function round() {
  const res = await call("POST", "/worker/jobs/claim", { workerId: cfg.workerId });
  if (res.status === 204) return;
  const job = res.data;
  const nome = projectName(job.roteiro, job.id);
  const seed = seedFor(job);
  console.log(`[${now()}] job ${job.id} (${job.theme}) como ${nome}, semente ${seed}, tentativa ${job.attempts}`);

  const projectDir = `${cfg.base}/projetos/${nome}`;
  writeScript(projectDir, job);
  await progress(job.id, 5, `roteiro gravado em projetos/${nome}`);

  const vars = { nome, voz: job.voiceKey ?? cfg.defaultVoice, seed, tools: cfg.tools, base: cfg.base };
  const { code, log } = await runPipeline(job.id, cfg.bin, buildArgs(cfg.args, vars));
  if (code !== 0) {
    const scriptProblem = isScriptProblem(log);
    return fail(job.id, `pipeline saiu com código ${code}: ${lastLine(log)}`, !scriptProblem);
  }

  // Portão (etapa 5 do pacote): palavra sem par = a síntese pode ter engolido texto. Outra semente costuma resolver.
  const unmatched = parseUnmatched(log);
  if (unmatched === null) return fail(job.id, "não achei a contagem de palavras sem par no log do pipeline", false);
  if (unmatched > 0) return fail(job.id, `${unmatched} palavra(s) sem par no alinhamento com a semente ${seed}`, true);

  const { found, missing } = expectedOutputs(`${cfg.base}/finais`, projectDir, nome, existsSync);
  if (missing.length) return fail(job.id, `arquivo esperado não apareceu: ${missing.join(", ")}`, false);

  await progress(job.id, 90, `subindo ${found.length} arquivos`);
  const outputs = [];
  for (const o of found) outputs.push({ role: o.role, url: await upload(job.brandKey, o.path) });
  const done = await call("POST", `/worker/jobs/${job.id}/deliver`, { workerId: cfg.workerId, outputs, warnings: warningsFrom(log), seed });
  console.log(`[${now()}] entregue: ${done.status}`);
}

/** Grava os contratos no projeto. Se já existia um roteiro diferente (reel aprovado com o mesmo nome), guarda cópia antes. */
function writeScript(projectDir, job) {
  mkdirSync(projectDir, { recursive: true });
  for (const [file, value] of [["roteiro.json", job.roteiro], ["falas.json", job.falas]]) {
    const path = `${projectDir}/${file}`;
    const text = JSON.stringify(value, null, 1);
    if (existsSync(path) && readFileSync(path, "utf8") !== text) copyFileSync(path, `${path}.antes-${Date.now()}`);
    writeFileSync(path, text);
  }
}

/** Roda o pipeline sem shell, guarda o log e manda a última linha como progresso (renova o prazo do job). */
function runPipeline(jobId, bin, args) {
  return new Promise((resolve) => {
    let log = "";
    const child = spawn(bin, args, { env: { ...process.env, PYTHONIOENCODING: "utf-8" }, windowsHide: true });
    const take = (chunk) => {
      const text = chunk.toString();
      log += text;
      process.stdout.write(text);
    };
    child.stdout.on("data", take);
    child.stderr.on("data", take);
    const beat = setInterval(() => void progress(jobId, 50, lastLine(log) || "renderizando").catch(() => {}), cfg.heartbeatSeconds * 1000);
    child.on("error", (e) => {
      log += `\n${e.message}`;
    });
    child.on("close", (code) => {
      clearInterval(beat);
      resolve({ code: code ?? 1, log });
    });
  });
}

async function upload(brandKey, path) {
  const form = new FormData();
  form.append("file", await openAsBlob(path), basename(path));
  const res = await fetch(`${cfg.api}/worker/media/${encodeURIComponent(brandKey)}`, { method: "POST", headers: { "X-Worker-Key": cfg.key }, body: form });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(`upload de ${basename(path)} falhou: ${res.status} ${data.error ?? ""}`);
  return data.url;
}

async function progress(jobId, percent, note) {
  await call("POST", `/worker/jobs/${jobId}/progress`, { workerId: cfg.workerId, percent, note });
}

async function fail(jobId, error, retry) {
  console.error(`[${now()}] falhou${retry ? " (volta pra fila)" : ""}: ${error}`);
  await call("POST", `/worker/jobs/${jobId}/fail`, { workerId: cfg.workerId, error, retry });
}

async function call(method, path, body) {
  const res = await fetch(cfg.api + path, {
    method,
    headers: { "Content-Type": "application/json", "X-Worker-Key": cfg.key },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  let data = null;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    data = text;
  }
  if (res.status >= 400) throw new Error(`${method} ${path}: ${res.status} ${typeof data === "object" ? data?.error ?? "" : data}`);
  return { status: res.status, data };
}

function loadEnvFile(path) {
  if (!existsSync(path)) return;
  for (const line of readFileSync(path, "utf8").split(/\r?\n/)) {
    const m = line.match(/^\s*([A-Z0-9_]+)\s*=\s*(.*)\s*$/);
    if (m && !(m[1] in process.env)) process.env[m[1]] = m[2].replace(/^"(.*)"$/, "$1");
  }
}

function now() {
  return new Date().toLocaleTimeString("pt-BR");
}

function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}
