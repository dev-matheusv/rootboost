// Gravador de tela guiado pela IA (RB-50, desenho em docs/GRAVACAO.md). Abre o Chrome do PC sem janela, entra no
// sistema do cliente com a conta de demonstração (contas.json, fora do git), e a cada passo manda a foto e os
// elementos pra API (/worker/ai/next-action), que devolve o que fazer. Grava os quadros e monta o mp4 com FFmpeg.
// Mesmo molde do gravar-reel.mjs do MedlyCare (CDP, cursor desenhado, 540x600 a 2x), sem passos escritos à mão.
import { spawn } from "node:child_process";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { isDestructive, sameSite } from "./lib.mjs";

const CHROME = process.env.CHROME_PATH ?? "C:/Program Files/Google/Chrome/Application/chrome.exe";
const FFMPEG = process.env.FFMPEG_PATH ?? "C:/DMaker/bin/ffmpeg/bin/ffmpeg.exe";
const VIEW = { width: 540, height: 600, scale: 2 };
const MAX_SECONDS = 90;
const wait = (ms) => new Promise((r) => setTimeout(r, ms));

class Cdp {
  constructor(url) {
    this.ws = new WebSocket(url);
    this.seq = 0;
    this.pending = new Map();
    this.listeners = new Map();
    this.open = new Promise((ok, fail) => { this.ws.onopen = ok; this.ws.onerror = fail; });
    this.ws.onmessage = (ev) => {
      const msg = JSON.parse(ev.data);
      if (msg.id && this.pending.has(msg.id)) {
        const { ok, fail } = this.pending.get(msg.id);
        this.pending.delete(msg.id);
        msg.error ? fail(new Error(msg.error.message)) : ok(msg.result);
      } else if (msg.method) (this.listeners.get(msg.method) ?? []).forEach((fn) => fn(msg.params));
    };
  }
  send(method, params = {}) {
    const id = ++this.seq;
    this.ws.send(JSON.stringify({ id, method, params }));
    return new Promise((ok, fail) => this.pending.set(id, { ok, fail }));
  }
  on(method, fn) { this.listeners.set(method, [...(this.listeners.get(method) ?? []), fn]); }
}

// Cursor desenhado na página (roxo da plataforma) com deslocamento suave e onda no clique.
const CURSOR = `(() => {
  if (window.__rbCursor) return; window.__rbCursor = true;
  const add = () => {
    const c = document.createElement('div');
    c.style.cssText = 'position:fixed;left:0;top:0;width:30px;height:30px;z-index:2147483647;pointer-events:none;transition:transform .6s cubic-bezier(.45,0,.2,1);transform:translate(270px,520px)';
    c.innerHTML = '<svg viewBox="0 0 24 24" width="30" height="30"><path d="M4 2l15 9-6.5 1.6L16 20l-2.6 1.3-3.4-7.2L4 19z" fill="#7A4CE0" stroke="#fff" stroke-width="1.6" stroke-linejoin="round"/></svg>';
    document.documentElement.appendChild(c);
    window.__moveCursor = (x, y) => { c.style.transform = 'translate(' + (x - 4) + 'px,' + (y - 2) + 'px)'; };
    window.__ripple = (x, y) => { const o = document.createElement('div'); o.style.cssText = 'position:fixed;left:' + (x - 18) + 'px;top:' + (y - 18) + 'px;width:36px;height:36px;border-radius:50%;border:3px solid #7A4CE0;z-index:2147483646;pointer-events:none;transition:all .5s ease-out;opacity:.9'; document.documentElement.appendChild(o); requestAnimationFrame(() => { o.style.transform = 'scale(1.8)'; o.style.opacity = '0'; }); setTimeout(() => o.remove(), 600); };
  };
  document.readyState === 'loading' ? document.addEventListener('DOMContentLoaded', add) : add();
})();`;

// Numera os elementos visíveis de clicar ou digitar (data-rb-id) e devolve id, tipo, texto e o centro de cada um.
const LIST_ELEMENTS = `(() => {
  const sel = 'a,button,input,select,textarea,[role=button],[role=link],[role=tab],[role=menuitem],[onclick],[tabindex]:not([tabindex="-1"])';
  const out = []; let n = 0;
  for (const el of document.querySelectorAll(sel)) {
    const r = el.getBoundingClientRect();
    if (r.width < 4 || r.height < 4 || r.bottom < 0 || r.top > innerHeight || getComputedStyle(el).visibility === 'hidden') continue;
    const id = 'e' + (++n); el.setAttribute('data-rb-id', id);
    const tag = el.tagName.toLowerCase();
    const role = tag === 'input' ? 'campo ' + (el.type || 'texto') : tag === 'textarea' ? 'campo texto' : tag === 'select' ? 'lista' : tag === 'a' ? 'link' : 'botão';
    const text = (el.getAttribute('aria-label') || el.innerText || el.placeholder || el.value || el.title || '').trim().replace(/\\s+/g, ' ').slice(0, 80);
    out.push({ id, role, text, x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) });
    if (n >= 150) break;
  }
  return out;
})()`;

async function openChrome(port) {
  const profile = mkdtempSync(join(tmpdir(), "rb-rec-"));
  const proc = spawn(CHROME, ["--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`, "--no-first-run",
    "--no-default-browser-check", "--hide-scrollbars", "--mute-audio", "--lang=pt-BR", `--window-size=${VIEW.width},${VIEW.height}`, "about:blank"],
  { stdio: "ignore" });
  let target = null;
  for (let i = 0; i < 60 && !target; i++) {
    await wait(250);
    try { target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find((t) => t.type === "page"); } catch { /* subindo */ }
  }
  if (!target) throw new Error("o Chrome não respondeu");
  const cdp = new Cdp(target.webSocketDebuggerUrl);
  await cdp.open;
  await cdp.send("Page.enable");
  await cdp.send("Runtime.enable");
  await cdp.send("Emulation.setDeviceMetricsOverride", { width: VIEW.width, height: VIEW.height, deviceScaleFactor: VIEW.scale, mobile: false });
  await cdp.send("Emulation.setFocusEmulationEnabled", { enabled: true });
  await cdp.send("Page.addScriptToEvaluateOnNewDocument", { source: CURSOR });
  return { cdp, close: () => { try { proc.kill(); } catch {} try { rmSync(profile, { recursive: true, force: true }); } catch {} } };
}

async function evaluate(cdp, expression) {
  const r = await cdp.send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description ?? "erro na página");
  return r.result.value;
}

async function click(cdp, el) {
  await evaluate(cdp, `window.__moveCursor && window.__moveCursor(${el.x}, ${el.y})`);
  await wait(650);
  await evaluate(cdp, `window.__ripple && window.__ripple(${el.x}, ${el.y})`);
  for (const type of ["mouseMoved", "mousePressed", "mouseReleased"])
    await cdp.send("Input.dispatchMouseEvent", { type, x: el.x, y: el.y, button: "left", clickCount: type === "mouseMoved" ? 0 : 1 });
}

async function type(cdp, text, delay = 60) {
  for (const ch of String(text)) {
    await cdp.send("Input.insertText", { text: ch });
    await wait(delay);
  }
}

async function pressEnter(cdp) {
  await cdp.send("Input.dispatchKeyEvent", { type: "keyDown", key: "Enter", code: "Enter", windowsVirtualKeyCode: 13, text: "\r" });
  await cdp.send("Input.dispatchKeyEvent", { type: "keyUp", key: "Enter", code: "Enter", windowsVirtualKeyCode: 13 });
}

/** Entra com a conta de demonstração: o usuário no primeiro campo antes da senha, a senha no campo de senha. A IA nunca vê. */
async function login(cdp, elements, account) {
  const pass = elements.find((e) => e.role === "campo password");
  const user = [...elements].reverse().find((e) => e.role.startsWith("campo") && e !== pass && elements.indexOf(e) < elements.indexOf(pass));
  if (!pass || !user) return false;
  await click(cdp, user);
  await type(cdp, account.usuario, 25);
  await click(cdp, pass);
  await type(cdp, account.senha, 25);
  await pressEnter(cdp);
  await wait(2500);
  return true;
}

/**
 * Grava uma cena: abre o sistema, deixa a IA conduzir até "pronto" e devolve o mp4. Recusa navegar pra fora do site do
 * produto e clicar em botão destrutivo (vira passo perdido no histórico, e a IA escolhe outro caminho).
 * @param ask (state) => Promise<{ action, target, text, reason }>, a chamada à API.
 */
export async function recordScene({ brandKey, scene, account, outDir, name, ask, port = 9333, log = console.log }) {
  mkdirSync(join(outDir, "quadros"), { recursive: true });
  const { cdp, close } = await openChrome(port);
  const frames = [];
  let recording = false;
  cdp.on("Page.screencastFrame", (p) => {
    cdp.send("Page.screencastFrameAck", { sessionId: p.sessionId }).catch(() => {});
    if (!recording) return;
    const file = `q${String(frames.length).padStart(5, "0")}.jpg`;
    writeFileSync(join(outDir, "quadros", file), Buffer.from(p.data, "base64"));
    frames.push({ file, t: p.metadata.timestamp });
  });
  try {
    await cdp.send("Page.navigate", { url: account.url });
    await wait(2500);
    const history = [];
    let loggedIn = false;
    // Login antes de gravar, se a primeira tela pedir: o vídeo começa já dentro do sistema.
    let elements = await evaluate(cdp, LIST_ELEMENTS);
    if (elements.some((e) => e.role === "campo password")) loggedIn = await login(cdp, elements, account);

    recording = true;
    await cdp.send("Page.startScreencast", { format: "jpeg", quality: 90, maxWidth: VIEW.width * VIEW.scale, maxHeight: VIEW.height * VIEW.scale });
    const started = Date.now();
    let outcome = "desistir";
    for (let step = 0; Date.now() - started < MAX_SECONDS * 1000; step++) {
      elements = await evaluate(cdp, LIST_ELEMENTS);
      // Foto em 1x (540x600) pra IA: um quarto dos pixels da gravação, bem menos tokens por passo (custo).
      const shot = await cdp.send("Page.captureScreenshot", { format: "jpeg", quality: 55, clip: { x: 0, y: 0, width: VIEW.width, height: VIEW.height, scale: 1 / VIEW.scale } });
      const url = await evaluate(cdp, "location.href");
      const next = await ask({ brandKey, scene, url, elements: elements.map(({ id, role, text }) => ({ id, role, text })), history, step, image: shot.data });
      log(`  passo ${step + 1}: ${next.action} ${next.target ?? ""} (${next.reason})`);
      if (next.action === "pronto" || next.action === "desistir") { outcome = next.action; break; }
      if (next.action === "dado-pessoal") {
        // Dado de pessoa real na tela: apaga os quadros já gravados e para sem tentar de novo.
        recording = false;
        await cdp.send("Page.stopScreencast");
        rmSync(join(outDir, "quadros"), { recursive: true, force: true });
        const err = new Error(`a tela mostrou dado pessoal real (${next.reason}); grave com a conta de demonstração`);
        err.personalData = true;
        throw err;
      }
      const el = elements.find((e) => e.id === next.target);
      if (next.action === "clicar" && el) {
        if (isDestructive(el.text)) { history.push(`recusado: ${el.text} (botão proibido)`); continue; }
        await click(cdp, el);
      } else if (next.action === "digitar" && el) {
        await click(cdp, el);
        await type(cdp, next.text ?? "");
      } else if (next.action === "rolar") {
        await cdp.send("Input.dispatchMouseEvent", { type: "mouseWheel", x: 270, y: 300, deltaX: 0, deltaY: next.text === "cima" ? -350 : 350 });
      } else if (next.action === "login" && !loggedIn) {
        loggedIn = await login(cdp, elements, account);
      }
      history.push(`${next.action} ${el?.text ?? next.text ?? ""}`.trim());
      await wait(900);
      const now = await evaluate(cdp, "location.href");
      if (!sameSite(now, account.url)) { outcome = "desistir"; history.push(`saiu do site: ${now}`); break; }
    }
    await wait(1200); // segura o resultado na tela
    recording = false;
    await cdp.send("Page.stopScreencast");
    if (outcome !== "pronto") throw new Error(`a IA não conseguiu mostrar a cena: ${scene}`);
    if (frames.length < 2) throw new Error("nenhum quadro gravado");

    const lines = [];
    frames.forEach((f, i) => {
      const next = i + 1 < frames.length ? frames[i + 1].t : f.t + 0.04;
      lines.push(`file 'quadros/${f.file}'`, `duration ${Math.max(next - f.t, 0.001).toFixed(4)}`);
    });
    lines.push(`file 'quadros/${frames.at(-1).file}'`);
    writeFileSync(join(outDir, "quadros.txt"), lines.join("\n"));
    const out = join(outDir, `${name}.mp4`);
    await new Promise((ok, fail) => {
      const ff = spawn(FFMPEG, ["-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", "quadros.txt", "-vf", "fps=30,format=yuv420p",
        "-c:v", "libx264", "-preset", "medium", "-crf", "16", "-movflags", "+faststart", out], { cwd: outDir, stdio: "inherit" });
      ff.on("exit", (c) => (c === 0 ? ok() : fail(new Error(`ffmpeg saiu com ${c}`))));
    });
    rmSync(join(outDir, "quadros"), { recursive: true, force: true });
    return { path: out, duration: (frames.at(-1).t - frames[0].t) + 0.04 };
  } finally {
    close();
  }
}
