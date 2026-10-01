// Gera specs do DMaker (um por variacao de hook) a partir da config de cada produto.
// Esta e a camada "Studio": config enxuta -> spec JSON pronto pro motor de render.
//
// Uso:  node tools/video/make-specs.mjs            (gera todos)
//       node tools/video/make-specs.mjs posture    (so um produto)
//
// Depois:  cd C:\DMaker && .\.venv\Scripts\dmaker.exe render <spec>
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const dir = path.dirname(fileURLToPath(import.meta.url));
const repo = path.join(dir, "..", "..");
const OUT_DIR = "C:/DMaker/projects";
const MEDIA = (p, f) => `C:/RootMark/landing/${p}/media/${f}`;

// --- Produtos -------------------------------------------------------------
// `shots` e a sequencia visual (a mesma em todas as variacoes); `hooks` sao os
// ganchos testados. Uma variacao = shots + 1 hook. Testar hook e o que mais muda
// resultado, por isso variamos so ele e mantemos o resto constante.
const PRODUCTS = {
  kitchen: {
    key: "prepmate",
    shots: [
      { img: "g2.jpg", dur: 3.0, motion: "zoom-in" },
      { img: "g1.jpg", dur: 4.5, motion: "zoom-out", transition: "fade" },
      { img: "g3.jpg", dur: 5.0, motion: "pan-left", transition: "dissolve" },
      { img: "hero.jpg", dur: 4.0, motion: "zoom-in", transition: "fade" },
      { img: "lifestyle.jpg", dur: 3.0, motion: "zoom-out", transition: "fade" }
    ],
    // texto que acompanha cada shot a partir do segundo (o primeiro leva o hook)
    beats: ["7 blades. one tool.", "dice. slice. julienne. grate.", "and it catches everything", "real results"],
    cta: "link in bio",
    hooks: [
      "This replaced 9 tools in my kitchen",
      "POV: you still chop onions by hand",
      "Meal prep went from 40 minutes to 10",
      "Why did nobody tell me about this sooner"
    ]
  },
  posture: {
    key: "standtall",
    shots: [
      { img: "hero.jpg", dur: 3.0, motion: "zoom-in" },
      { img: "g1.jpg", dur: 4.5, motion: "zoom-out", transition: "fade" },
      { img: "g2.jpg", dur: 5.0, motion: "pan-left", transition: "dissolve" },
      { img: "lifestyle.jpg", dur: 4.0, motion: "zoom-in", transition: "fade" },
      { img: "g3.jpg", dur: 3.0, motion: "zoom-out", transition: "fade" }
    ],
    beats: ["it pulls your shoulders back, gently", "takes a minute to put on", "and it hides under a shirt", "sizes S to XXL"],
    cta: "link in bio",
    hooks: [
      "POV: you just caught yourself slouching again",
      "Desk job posture check",
      "The gentle reminder my back needed",
      "Nobody can tell I am wearing this"
    ]
  }
};

const TRANSITION_DUR = 0.4;

/**
 * Monta o spec. O ponto delicado: transicao SOBREPOE os trechos, entao o tempo
 * total e a soma das duracoes MENOS a soma das transicoes. Se o texto for
 * posicionado ignorando isso, ele termina depois do video (o lint do DMaker
 * reclamou disso na primeira tentativa).
 */
function buildSpec(landingPath, cfg, hook, variantIndex) {
  const timeline = cfg.shots.map((s, i) => {
    const seg = { type: "image", src: MEDIA(landingPath, s.img), duration: s.dur, motion: s.motion };
    // As fotos da CJ tem ~790px. Preencher 1080x1920 por corte exigiria ampliar 2.4x e borrar
    // (o lint do DMaker acusa). Com fundo desfocado a foto fica perto do tamanho nativo e
    // nitida, e o 9:16 e preenchido por uma versao borrada dela mesma.
    seg.reframe = { mode: "blur" };
    if (s.transition) seg.transition = { type: s.transition, duration: TRANSITION_DUR };
    return seg;
  });

  // tempo de inicio real de cada trecho, descontando a sobreposicao das transicoes
  const starts = [];
  let t = 0;
  cfg.shots.forEach((s, i) => {
    if (i > 0 && cfg.shots[i].transition) t -= TRANSITION_DUR;
    starts.push(t);
    t += s.dur;
  });
  const total = t;

  const overlays = [];
  // gancho no primeiro trecho: e o que decide se a pessoa fica
  overlays.push({ type: "text", text: hook, role: "hook", start: 0.2, end: Math.min(starts[1] + 0.2, total) });
  // um texto por trecho seguinte
  cfg.beats.forEach((text, i) => {
    const idx = i + 1;
    if (idx >= cfg.shots.length) return;
    const start = starts[idx] + 0.3;
    const end = Math.min(starts[idx] + cfg.shots[idx].dur - 0.2, total - 0.1);
    if (end > start) overlays.push({ type: "text", text, role: "title", start: +start.toFixed(2), end: +end.toFixed(2) });
  });
  // CTA so no fim, uma vez (CTA cedo faz a pessoa sair antes de criar desejo)
  overlays.push({ type: "text", text: cfg.cta, role: "cta", start: +(total - 2.5).toFixed(2), end: +(total - 0.1).toFixed(2) });

  return {
    name: `${cfg.key}-v${variantIndex + 1}`,
    output: { preset: "instagram/reels", quality: "high" },
    brand: "default",
    timeline,
    overlays,
    audio: { normalize: "two-pass" }
  };
}

const only = process.argv.slice(2);
fs.mkdirSync(OUT_DIR, { recursive: true });

let n = 0;
for (const [landingPath, cfg] of Object.entries(PRODUCTS)) {
  if (only.length && !only.includes(landingPath) && !only.includes(cfg.key)) continue;
  cfg.hooks.forEach((hook, i) => {
    const spec = buildSpec(landingPath, cfg, hook, i);
    const file = path.join(OUT_DIR, `${spec.name}.json`);
    fs.writeFileSync(file, JSON.stringify(spec, null, 2) + "\n", "utf8");
    const dur = spec.overlays[spec.overlays.length - 1].end + 0.1;
    console.log(`[spec] ${spec.name.padEnd(14)} ~${dur.toFixed(1)}s  hook: "${hook}"`);
    n++;
  });
}
console.log(`\n[spec] ${n} specs em ${OUT_DIR}`);
