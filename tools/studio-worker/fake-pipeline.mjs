// Pipeline de mentira pra testar o worker sem DMaker: cria os arquivos que o narrado-calmo criaria e imprime
// a linha do alinhamento. FAKE_UNMATCHED=n simula palavras sem par; FAKE_FAIL=portao simula o portão de velocidade.
// Uso (no worker.env): STUDIO_PIPELINE_ARGS={tools}/fake-pipeline.mjs {base} {nome} {seed}
import { mkdirSync, writeFileSync } from "node:fs";

const [base, nome, seed] = process.argv.slice(2);
if (process.env.FAKE_FAIL === "portao") {
  console.error("Error: portao: trecho acima de 1,25x");
  process.exit(1);
}
const unmatched = Number(process.env.FAKE_UNMATCHED ?? 0);
console.log(`sintetizando com a semente ${seed}...`);
console.log(`42 palavras, ${unmatched} sem par no Whisper`);
mkdirSync(`${base}/finais`, { recursive: true });
mkdirSync(`${base}/projetos/${nome}`, { recursive: true });
writeFileSync(`${base}/finais/${nome}-narrado-instagram.mp4`, Buffer.from([0, 0, 0, 24]));
writeFileSync(`${base}/finais/${nome}-narrado-instagram.jpg`, Buffer.from([0xff, 0xd8, 0xff]));
writeFileSync(`${base}/finais/${nome}-narrado.mp4`, Buffer.from([0, 0, 0, 24]));
console.log("AVISO bloco 2 com fator 1,9");
console.log(`Pronto: ${base}/finais/${nome}-narrado-instagram.mp4`);
