import { test } from "node:test";
import assert from "node:assert/strict";
import { buildArgs, expectedOutputs, isScriptProblem, lastLine, parseUnmatched, projectName, seedFor, warningsFrom } from "./lib.mjs";

test("nome do projeto vira pasta segura", () => {
  assert.equal(projectName({ nome: "Combo 8: Psicologia!" }, "x"), "combo-8-psicologia");
  assert.equal(projectName({ nome: "../../etc" }, "x"), "etc");
  assert.equal(projectName({}, "1986596b-29df"), "job-1986596b");
});

test("cada tentativa usa a semente seguinte", () => {
  assert.equal(seedFor({ seed: null, attempts: 1 }), 1);
  assert.equal(seedFor({ seed: 42, attempts: 1 }), 42);
  assert.equal(seedFor({ seed: 42, attempts: 3 }), 44);
});

test("modelo do comando vira argumentos sem passar por shell", () => {
  assert.deepEqual(buildArgs("{tools}/narrado-calmo.mjs {nome} --voz {voz} --semente {seed}", { tools: "C:/x y", nome: "a", voz: "m2b", seed: 3 }),
    ["C:/x y/narrado-calmo.mjs", "a", "--voz", "m2b", "--semente", "3"]);
});

test("portão de palavras sem par lê a última contagem", () => {
  assert.equal(parseUnmatched("110 palavras, 2 sem par no Whisper\n...\n110 palavras, 0 sem par no Whisper"), 0);
  assert.equal(parseUnmatched("nada aqui"), null);
});

test("problema de roteiro não se resolve com outra semente", () => {
  assert.equal(isScriptProblem("Error: portao: trecho acima de 1,25x"), true);
  assert.equal(isScriptProblem("ECONNRESET"), false);
});

test("arquivos esperados com papel; sem o vídeo do Instagram falta o principal", () => {
  const have = new Set(["F/a-narrado-instagram.mp4", "F/a-narrado-instagram.jpg", "F/a-narrado.mp4"]);
  const { found, missing } = expectedOutputs("F", "P", "a", (p) => have.has(p));
  assert.deepEqual(found.map((o) => o.role), ["video-instagram", "cover-instagram", "video"]);
  assert.deepEqual(missing, []);
  assert.deepEqual(expectedOutputs("F", "P", "b", () => false).missing, ["F/b-narrado-instagram.mp4"]);
});

test("avisos e nota de progresso", () => {
  assert.deepEqual(warningsFrom("ok\nAVISO bloco 2 lento\nQA: legenda perto da borda\nPronto"), ["AVISO bloco 2 lento", "QA: legenda perto da borda"]);
  assert.equal(lastLine("a\n\nsintetizando 600 caracteres\n"), "sintetizando 600 caracteres");
});

test("voz particular só narra a marca dona", async () => {
  const { parsePrivateVoices, voiceProblem } = await import("./lib.mjs");
  const priv = parsePrivateVoices("matheus2a:medlycare, matheus2b:medlycare");
  assert.equal(voiceProblem("matheus2b", "medlycare", priv), null);
  assert.match(voiceProblem("matheus2b", "clinica-x", priv), /particular de medlycare/);
  assert.equal(voiceProblem("banco-ana", "clinica-x", priv), null);
  assert.match(voiceProblem(null, "clinica-x", priv), /sem voz/);
});

test("gravação: travas e troca dos trechos", async () => {
  const { isDestructive, sameSite, scenesToRecord, applyRecordings } = await import("./lib.mjs");
  assert.equal(isDestructive("Excluir paciente"), true);
  assert.equal(isDestructive("Sair"), true);
  assert.equal(isDestructive("Nova sessão"), false);
  assert.equal(sameSite("https://app.medlycare.com.br/agenda", "https://medlycare.com.br"), true);
  assert.equal(sameSite("https://evil.com/?medlycare.com.br", "https://medlycare.com.br"), false);
  const roteiro = { blocos: [
    { cena: "abrir a agenda", trechos: [["gravar-bloco-1", 0, 5]] },
    { cena: "", trechos: [["gravar-bloco-2", 0, 4]] },
    { cena: "ver prontuário", trechos: [["psico2", 1, 9]] },
  ] };
  assert.deepEqual(scenesToRecord(roteiro).map((s) => s.index), [0]);
  const out = applyRecordings(roteiro, [{ index: 0, source: "x-bloco-1", duration: 12.34 }]);
  assert.deepEqual(out.blocos[0].trechos, [["x-bloco-1", 0.5, 12.1, 1]]);
  assert.deepEqual(roteiro.blocos[0].trechos, [["gravar-bloco-1", 0, 5]]); // original intacto
});

test("acervo: blocos com mídia, foto vira trecho e troca dos trechos", async () => {
  const { libraryScenes, photoClipArgs, applyLibrary } = await import("./lib.mjs");
  const id = "d063b8ee53e2455f899ff9752759f2ee";
  const roteiro = { blocos: [
    { trechos: [[`acervo-${id}`, 0, 6]] },
    { trechos: [["acervo-../../x", 0, 6]] },
    { cena: "agenda", trechos: [["gravar-bloco-3", 0, 5]] },
  ] };
  assert.deepEqual(libraryScenes(roteiro), [{ index: 0, id, seconds: 6 }]);
  const args = photoClipArgs("in.jpg", "out.mp4", 3000, 4000, 6);
  assert.equal(args[args.indexOf("-t") + 1], "8");
  assert.match(args[args.indexOf("-vf") + 1], /s=1080x1350/);
  const out = applyLibrary(roteiro, [{ index: 0, source: `acervo-${id}`, duration: 8 }]);
  assert.deepEqual(out.blocos[0].trechos, [[`acervo-${id}`, 0, 7.9, 1]]);
  assert.deepEqual(roteiro.blocos[0].trechos, [[`acervo-${id}`, 0, 6]]);
});

test("acervo: recorte 4:5 no foco enche a tela, sem sair da foto", async () => {
  const { focusCrop } = await import("./lib.mjs");
  // Print largo (1920x1080) com foco no documento à direita: corta a parte certa, 4:5, dentro da imagem.
  const c = focusCrop(1920, 1080, { x: 0.6, y: 0.1, w: 0.3, h: 0.8 });
  assert.ok(Math.abs(c.w / c.h - 0.8) < 0.01, `${c.w}x${c.h}`);
  assert.ok(c.y >= 0 && c.y + c.h <= 1080);
  assert.ok(c.x + c.w <= 1920 && c.x > 900, `x=${c.x}`);
  // Sem foco: foto inteira cabendo em 4:5.
  const whole = focusCrop(1000, 2000, null);
  assert.deepEqual([whole.w, whole.h], [1000, 1250]);
  assert.ok(whole.y + whole.h <= 2000);
});
