// Sobe gravações brutas de tela pro Acervo de uma marca, em lote, com a descrição de cada uma (a IA escolhe a cena só
// por ela). Usa a chave do worker (worker.env), nunca senha do Console. Vídeo sobe como BRUTO (não "Reel pronto").
//
// Uso:  node subir-acervo.mjs <marca> <lista.txt> [--enviar] [--incluir-conferir] [--momentos]
//   --momentos: lista no formato "- nome [inicio-fim] o que a tela mostra" (índice de momentos). Bruto já enviado tem a
//   descrição e os momentos trocados; bruto novo sobe com eles.
//   Sem --enviar só mostra o que faria. A lista é o formato do chat de marketing:
//   "- nome (10 s): descrição"  ou  "- nomeA e nomeB (15 s cada): descrição"; para em "NAO SUBIR".
//   Linha com "(CONFERIR" fica de fora, a menos que passe --incluir-conferir.
//   Já enviados ficam em acervo-enviados.json (ao lado) e não sobem de novo.
import { existsSync, openAsBlob, readFileSync, writeFileSync } from "node:fs";
import { basename, dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { parseMomentsList, parseRawList } from "./lib.mjs";

const here = dirname(fileURLToPath(import.meta.url));
for (const line of existsSync(join(here, "worker.env")) ? readFileSync(join(here, "worker.env"), "utf8").split(/\r?\n/) : []) {
  const m = line.match(/^\s*([A-Z0-9_]+)\s*=\s*(.*)\s*$/);
  if (m && !process.env[m[1]]) process.env[m[1]] = m[2];
}
const api = (process.env.ROOTBOOST_API ?? "").replace(/\/+$/, "");
const key = process.env.ROOTBOOST_WORKER_KEY ?? "";
const base = process.env.STUDIO_BASE ?? "C:/Users/devma/Videos/MedlyCare Reels";
const [brand, listPath] = process.argv.slice(2).filter((a) => !a.startsWith("--"));
const send = process.argv.includes("--enviar");
const withCheck = process.argv.includes("--incluir-conferir");
if (!brand || !listPath) {
  console.error("uso: node subir-acervo.mjs <marca> <lista.txt> [--enviar] [--incluir-conferir]");
  process.exit(2);
}
if (send && (!api || !key)) {
  console.error("Falta ROOTBOOST_API ou ROOTBOOST_WORKER_KEY em worker.env.");
  process.exit(2);
}

const statePath = join(here, "acervo-enviados.json");
const sent = new Set(existsSync(statePath) ? JSON.parse(readFileSync(statePath, "utf8")) : []);
const withMoments = process.argv.includes("--momentos");
const items = withMoments ? parseMomentsList(readFileSync(listPath, "utf8")) : parseRawList(readFileSync(listPath, "utf8"), { includeToCheck: withCheck });
let ok = 0;
for (const it of items) {
  const file = `${base}/brutos/${it.name}/${it.name}.mp4`;
  const id = `${brand}/${it.name}`;
  if (sent.has(id) && withMoments) {
    if (!send) { console.log(`atualizaria ${it.name}: ${it.moments.length} momento(s)`); continue; }
    const res = await fetch(`${api}/worker/media/${encodeURIComponent(brand)}/details`, {
      method: "PUT", headers: { "X-Worker-Key": key, "Content-Type": "application/json" },
      body: JSON.stringify({ fileName: `${it.name}.mp4`, description: it.description, moments: it.moments }),
    });
    console.log(res.ok ? `momentos ok ${it.name} (${it.moments.length})` : `FALHOU ${it.name}: ${res.status} ${await res.text()}`);
    if (res.ok) ok++;
    continue;
  }
  if (sent.has(id)) { console.log(`já enviado: ${it.name}`); continue; }
  if (!existsSync(file)) { console.log(`NÃO ACHEI: ${file}`); continue; }
  if (!send) { console.log(`subiria ${it.name}: ${it.description}`); continue; }
  const form = new FormData();
  form.append("file", await openAsBlob(file), basename(file));
  form.append("raw", "true");
  form.append("description", it.description);
  if (it.moments) form.append("moments", JSON.stringify(it.moments));
  const res = await fetch(`${api}/worker/media/${encodeURIComponent(brand)}`, { method: "POST", headers: { "X-Worker-Key": key }, body: form });
  if (!res.ok) { console.log(`FALHOU ${it.name}: ${res.status} ${await res.text()}`); continue; }
  sent.add(id);
  writeFileSync(statePath, JSON.stringify([...sent], null, 1));
  console.log(`ok ${it.name}`);
  ok++;
}
console.log(send ? `${ok} enviados.` : `${items.length} na lista. Rode com --enviar pra subir.`);
