// Worker do estúdio (RB-43): roda no PC com DMaker, voz e FFmpeg. Só faz chamadas de saída pra API:
// pega o próximo job, grava roteiro.json e falas.json no projeto, roda o pipeline que já funciona
// (narrado-calmo.mjs por padrão), confere o portão de palavras sem par, sobe os arquivos pro bucket e entrega.
//
// Uso:  node worker.mjs            (fica rodando, uma rodada a cada STUDIO_POLL_SECONDS)
//       node worker.mjs --once     (uma rodada e sai)
// Config por variável de ambiente ou num worker.env ao lado (ver worker.env.example). A chave nunca vai pro repo.
import { execFileSync, spawn } from "node:child_process";
import { copyFileSync, existsSync, mkdirSync, openAsBlob, readFileSync, writeFileSync } from "node:fs";
import { basename, dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import {
  applyLibrary, applyRecordings, buildArgs, libraryScenes, photoClipArgs, expectedOutputs, isScriptProblem, lastLine, parsePrivateVoices, parseUnmatched, projectName, scenesToRecord,
  seedFor, voiceProblem, warningsFrom,
} from "./lib.mjs";
import { recordScene } from "./recorder.mjs";

const here = dirname(fileURLToPath(import.meta.url));
loadEnvFile(join(here, "worker.env"));

const FFMPEG = process.env.FFMPEG_PATH ?? "C:/DMaker/bin/ffmpeg/bin/ffmpeg.exe";
const FFPROBE = process.env.FFPROBE_PATH ?? FFMPEG.replace(/ffmpeg(\.exe)?$/i, "ffprobe$1");

const DMAKER = process.env.DMAKER_PATH ?? "C:/DMaker/.venv/Scripts/dmaker.exe";
const PY = process.env.DMAKER_PYTHON ?? "C:/DMaker/.venv/Scripts/python.exe";
// Frase das amostras de voz: curta, neutra, com pergunta e pausa (mostra a entonação).
const SAMPLE_TEXT = "Oi! Esta é uma amostra da voz para os vídeos da marca. Dá pra ouvir com calma, e se gostar, é só escolher esta.";

const cfg = {
  api: (process.env.ROOTBOOST_API ?? "").replace(/\/+$/, ""),
  key: process.env.ROOTBOOST_WORKER_KEY ?? "",
  workerId: process.env.STUDIO_WORKER_ID ?? `worker-${process.env.COMPUTERNAME ?? "pc"}`,
  base: process.env.STUDIO_BASE ?? "C:/Users/devma/Videos/MedlyCare Reels",
  tools: process.env.STUDIO_TOOLS ?? "C:/Users/devma/Documents/MedlyCare Marketing/ferramentas",
  // Executável e argumentos do pipeline. {nome} {voz} {seed} {tools} {base} são trocados por job.
  bin: process.env.STUDIO_PIPELINE_BIN ?? process.execPath,
  args: process.env.STUDIO_PIPELINE_ARGS ?? "{tools}/narrado-calmo.mjs {nome} --voz {voz} --semente {seed}",
  // Voz quando o job não escolhe: só pra marca dona dela (ver privateVoices). Vazio = sem padrão.
  defaultVoice: process.env.STUDIO_DEFAULT_VOICE ?? "",
  // Vozes clonadas de pessoas: voz:marca. A do Matheus só narra o MedlyCare.
  privateVoices: parsePrivateVoices(process.env.STUDIO_PRIVATE_VOICES ?? "matheus2a:medlycare,matheus2b:medlycare,medlyfem:medlycare,medlyfem2:medlycare"),
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
    // Sinal de vida a cada rodada: o Console avisa "nenhum computador ligado" quando ele some.
    await call("POST", "/worker/heartbeat", { workerId: cfg.workerId, note: "livre" }).catch(() => {});
    await voiceRound();
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
  let seed = seedFor(job);
  console.log(`[${now()}] job ${job.id} (${job.theme}) como ${nome}, semente ${seed}, tentativa ${job.attempts}`);

  const voice = job.voiceKey ?? (cfg.privateVoices.get(cfg.defaultVoice.toLowerCase()) === job.brandKey ? cfg.defaultVoice : null);
  // Vozes cadastradas na plataforma também são particulares da marca dona (somadas às do worker.env).
  const owners = new Map(cfg.privateVoices);
  for (const v of (await call("GET", "/worker/voices/keys")).data ?? []) owners.set(String(v.key).toLowerCase(), String(v.brandKey).toLowerCase());
  const problem = voiceProblem(voice, job.brandKey, owners);
  if (problem) return fail(job.id, problem, false);

  const projectDir = `${cfg.base}/projetos/${nome}`;

  // 1. Pré-checagem barata: tudo que pode falhar sem gastar voz nem render (pedido do Matheus, 29/10).
  const scenes = scenesToRecord(job.roteiro);
  const account = scenes.length ? loadAccounts()[job.brandKey] : null;
  if (scenes.length) {
    if (!account?.url || !account?.usuario || !account?.senha)
      return fail(job.id, `sem conta de demonstração da marca ${job.brandKey} em tools/studio-worker/contas.json`, false);
    const up = await fetch(account.url, { signal: AbortSignal.timeout(10_000) }).then((r) => r.status < 500).catch(() => false);
    if (!up) return fail(job.id, `o sistema da gravação não respondeu (${account.url}); ligue o app e mande renderizar de novo`, false);
  }
  for (const s of libraryScenes(job.roteiro))
    if (!(job.media ?? []).some((m) => m.id === s.id))
      return fail(job.id, `mídia do acervo ${s.id} não veio no job: foi apagada do Acervo ou é de outra marca`, false);

  // 2. Voz primeiro: só a fala, com o portão de palavras sem par. Semente ruim troca na hora (até 3), sem montar nada.
  writeScript(projectDir, job);
  const voiceArgs = (sd) => [...buildArgs(cfg.args, { nome, voz: voice, seed: sd, tools: cfg.tools, base: cfg.base }), "--so-voz"];
  let voiceOk = false;
  for (let tries = 0; tries < 3; tries++, seed++) {
    await progress(job.id, 3, `gerando a voz (semente ${seed})`);
    const { code, log } = await runPipeline(job.id, cfg.bin, voiceArgs(seed));
    if (code !== 0) return fail(job.id, `voz saiu com código ${code}: ${lastLine(log)}`, !isScriptProblem(log));
    const unmatched = readUnmatched(projectDir, voice, seed) ?? parseUnmatched(log);
    if (unmatched === null) return fail(job.id, "não achei a contagem de palavras sem par da voz", false);
    if (unmatched === 0) { voiceOk = true; break; }
    console.log(`[${now()}] semente ${seed}: ${unmatched} palavra(s) sem par, tentando a próxima`);
  }
  if (!voiceOk) return fail(job.id, `a voz saiu com palavra sem par em 3 sementes seguidas (até a ${seed - 1}); revise o texto das falas`, false);

  // 3. Gravação de tela (RB-50): blocos que a IA escreveu com "cena" e trecho "gravar-bloco-N" são gravados agora.
  if (scenes.length) {
    const recordings = [];
    for (const [i, s] of scenes.entries()) {
      const source = `${nome}-bloco-${s.index + 1}`;
      await progress(job.id, 5 + Math.round((i / scenes.length) * 25), `gravando a tela: ${s.scene}`);
      try {
        const r = await recordScene({ brandKey: job.brandKey, scene: s.scene, account, outDir: `${cfg.base}/brutos/${source}`, name: source,
          ask: async (state) => (await call("POST", "/worker/ai/next-action", state)).data });
        recordings.push({ index: s.index, source, duration: r.duration });
      } catch (e) {
        return fail(job.id, `gravação do bloco ${s.index + 1}: ${e.message}`, !e.personalData);
      }
    }
    job.roteiro = applyRecordings(job.roteiro, recordings);
  }

  // Acervo (RB-16): bloco com foto ou vídeo da marca. Baixa o arquivo; foto vira trecho com aproximação lenta.
  const library = libraryScenes(job.roteiro);
  if (library.length) {
    await progress(job.id, 30, `preparando ${library.length} mídia(s) do acervo`);
    const clips = [];
    for (const s of library) {
      const media = job.media.find((m) => m.id === s.id);
      try {
        clips.push({ index: s.index, source: `acervo-${s.id}`, duration: await prepareMedia(media, s.seconds) });
      } catch (e) {
        return fail(job.id, `mídia do acervo do bloco ${s.index + 1}: ${e.message}`, true);
      }
    }
    job.roteiro = applyLibrary(job.roteiro, clips);
  }

  writeScript(projectDir, job, false); // mesma rodada: só trocou os trechos, não guarda cópia
  await progress(job.id, 32, `roteiro gravado em projetos/${nome}`);

  // 4. Montagem: o narrado-calmo reaproveita a voz e o alinhamento da semente aprovada (mesmo texto, léxico e semente).
  const vars = { nome, voz: voice, seed, tools: cfg.tools, base: cfg.base };
  const { code, log } = await runPipeline(job.id, cfg.bin, buildArgs(cfg.args, vars));
  if (code !== 0) {
    const scriptProblem = isScriptProblem(log);
    return fail(job.id, `pipeline saiu com código ${code}: ${lastLine(log)}`, !scriptProblem);
  }

  // Portão (etapa 5 do pacote): palavra sem par = a síntese pode ter engolido texto. Outra semente costuma resolver.
  const unmatched = readUnmatched(projectDir, voice, seed) ?? parseUnmatched(log);
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

/**
 * Voz da marca (RB-59): pega a próxima da fila, cria no DMaker com a marca dona gravada no perfil e gera duas amostras
 * (sementes 1 e 2) pra pessoa ouvir e escolher. A gravação vem pela API com a chave do worker, nunca por link público.
 */
async function voiceRound() {
  const res = await call("POST", "/worker/voices/claim", { workerId: cfg.workerId });
  if (res.status === 204) return;
  const v = res.data;
  if (!/^[a-z0-9-]{3,64}$/.test(v.key ?? "")) return voiceFail(v.id, `id de voz inválido: ${v.key}`, false);
  console.log(`[${now()}] voz ${v.key} (${v.kind}) da marca ${v.brandKey}, tentativa ${v.attempts}`);
  await call("POST", "/worker/heartbeat", { workerId: cfg.workerId, note: `criando a voz ${v.name}` }).catch(() => {});
  const dir = `${cfg.base}/vozes/${v.key}`;
  mkdirSync(dir, { recursive: true });
  try {
    let created;
    if (v.kind === "Clone") {
      const ref = await fetch(`${cfg.api}/worker/voices/${v.id}/reference?workerId=${encodeURIComponent(cfg.workerId)}`, { headers: { "X-Worker-Key": cfg.key } });
      if (!ref.ok) throw new Error(`gravação não veio: ${ref.status}`);
      const ext = ({ "audio/webm": ".webm", "audio/ogg": ".ogg", "audio/mp4": ".m4a", "audio/mpeg": ".mp3", "audio/wav": ".wav" })[ref.headers.get("content-type")?.split(";")[0] ?? ""] ?? ".webm";
      const file = `${dir}/referencia${ext}`;
      writeFileSync(file, Buffer.from(await ref.arrayBuffer()));
      // Sem --text: a gravação tem a autorização + a leitura (mais de 20 s); o DMaker transcreve e escolhe os 15 s com mais fala.
      created = dmaker(["voice", "clone", v.key, file, "--brand", v.brandKey]);
    } else {
      created = dmaker(["voice", "design", v.key, "--instruct", v.instruct, "--brand", v.brandKey]);
    }
    if (!created.ok && !/Já existe a voz/.test(created.out)) throw new Error(lastLine(created.out) || "dmaker voice falhou");

    writeFileSync(`${dir}/amostra.txt`, SAMPLE_TEXT);
    const form = new FormData();
    form.append("workerId", cfg.workerId);
    for (const seed of [1, 2]) {
      const wav = `${dir}/amostra-${seed}.wav`, m4a = `${dir}/amostra-${seed}.m4a`;
      execFileSync(PY, ["-X", "utf8", `${cfg.tools}/falar-continuo.py`, v.key, `${dir}/amostra.txt`, wav, "32", String(seed)], { stdio: "ignore" });
      execFileSync(FFMPEG, ["-y", "-i", wav, "-c:a", "aac", "-b:a", "96k", m4a], { stdio: "ignore" });
      form.append(`sample-${seed}`, await openAsBlob(m4a), `amostra-${seed}.m4a`);
    }
    const done = await fetch(`${cfg.api}/worker/voices/${v.id}/complete`, { method: "POST", headers: { "X-Worker-Key": cfg.key }, body: form });
    if (!done.ok) throw new Error(`entrega das amostras: ${done.status} ${await done.text()}`);
    console.log(`[${now()}] voz ${v.key} pronta`);
  } catch (e) {
    return voiceFail(v.id, e.message, true);
  }
}

function dmaker(args) {
  try {
    return { ok: true, out: execFileSync(DMAKER, args, { encoding: "utf8", env: { ...process.env, PYTHONIOENCODING: "utf-8" } }) };
  } catch (e) {
    return { ok: false, out: `${e.stdout ?? ""}${e.stderr ?? ""}` };
  }
}

async function voiceFail(id, error, retry) {
  console.error(`[${now()}] voz falhou${retry ? " (volta pra fila)" : ""}: ${error}`);
  await call("POST", `/worker/voices/${id}/fail`, { workerId: cfg.workerId, error, retry });
}

/** Baixa a mídia e deixa em brutos/acervo-<id>/acervo-<id>.mp4 (o pipeline lê dali). Devolve a duração em segundos. */
async function prepareMedia(media, seconds) {
  const source = `acervo-${media.id}`;
  const dir = `${cfg.base}/brutos/${source}`;
  mkdirSync(dir, { recursive: true });
  const res = await fetch(media.url);
  if (!res.ok) throw new Error(`download ${res.status}`);
  const raw = `${dir}/original${media.isVideo ? ".video" : ".jpg"}`;
  writeFileSync(raw, Buffer.from(await res.arrayBuffer()));
  const out = `${dir}/${source}.mp4`;
  if (media.isVideo) {
    // Normaliza (mov do celular, HDR, rotação) e tira o som: a narração é a trilha.
    execFileSync(FFMPEG, ["-y", "-i", raw, "-an", "-vf", "scale='min(1080,iw)':-2,format=yuv420p", "-r", "30", "-c:v", "libx264", "-preset", "medium",
      "-crf", "18", "-movflags", "+faststart", out], { stdio: "ignore" });
  } else {
    const [w, h] = execFileSync(FFPROBE, ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0", raw])
      .toString().trim().split(",").map(Number);
    execFileSync(FFMPEG, photoClipArgs(raw, out, w, h, seconds, media.focus), { stdio: "ignore" });
  }
  return Number(execFileSync(FFPROBE, ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", out]).toString().trim());
}

/** Palavras sem par gravadas pelo alinhamento da voz (o log não repete a contagem quando a voz vem do cache). */
function readUnmatched(projectDir, voice, seed) {
  try {
    const n = JSON.parse(readFileSync(`${projectDir}/voz-continua/${voice}-p32-s${seed}/alinhamento.json`, "utf8")).sem_par;
    return Number.isInteger(n) ? n : null;
  } catch {
    return null;
  }
}

/** Grava os contratos no projeto. Se já existia um roteiro diferente (reel aprovado com o mesmo nome), guarda cópia antes. */
function writeScript(projectDir, job, backup = true) {
  mkdirSync(projectDir, { recursive: true });
  for (const [file, value] of [["roteiro.json", job.roteiro], ["falas.json", job.falas]]) {
    const path = `${projectDir}/${file}`;
    const text = JSON.stringify(value, null, 1);
    if (backup && existsSync(path) && readFileSync(path, "utf8") !== text) copyFileSync(path, `${path}.antes-${Date.now()}`);
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

/** Contas de demonstração por marca, só no PC: { "<marca>": { "url", "usuario", "senha" } }. Nunca vão pra API. */
function loadAccounts() {
  const path = join(here, "contas.json");
  if (!existsSync(path)) return {};
  try {
    return JSON.parse(readFileSync(path, "utf8"));
  } catch {
    return {};
  }
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
