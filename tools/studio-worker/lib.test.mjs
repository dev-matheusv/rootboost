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
