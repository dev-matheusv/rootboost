// Gravador de tela guiado pela IA (RB-50, desenho em docs/GRAVACAO.md). Abre o Chrome do PC sem janela, entra no
// sistema do cliente com a conta de demonstração (contas.json, fora do git), e a cada passo manda a foto e os
// elementos pra API (/worker/ai/next-action), que devolve o que fazer. Grava os quadros e monta o mp4 com FFmpeg.
// Mesmo molde do gravar-reel.mjs do MedlyCare (CDP, cursor desenhado, 540x600 a 2x), sem passos escritos à mão.
import { spawn, spawnSync } from "node:child_process";
import { appendFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { isDestructive, sameSite } from "./lib.mjs";

// Verbos de cena que pedem ação na tela (não só mostrar o que já está lá).
const ACTION_WORDS = /\b(toque|toca|clique|clica|escolha|escolhe|selecione|digite|preencha|abra|gere|marque|troque|mude)\b/i;
const CHROME = process.env.CHROME_PATH ?? "C:/Program Files/Google/Chrome/Application/chrome.exe";
const FFMPEG = process.env.FFMPEG_PATH ?? "C:/DMaker/bin/ffmpeg/bin/ffmpeg.exe";
// Imagem fictícia que a gravação cola ou anexa: nunca um arquivo de verdade do PC.
const DEMO_IMAGE = join(dirname(fileURLToPath(import.meta.url)), "demo", "imagem-demo.png");
const DEMO_FACE = join(dirname(fileURLToPath(import.meta.url)), "demo", "rosto-referencia.jpg");
// Janela estreita de Reels se apresenta como celular: sistema que escolhe o menu pelo aparelho (MedlyCare) mostra a barra
// de baixo; com Chrome de computador nessa largura a tela fica sem menu nenhum e a IA não tem por onde navegar.
const MOBILE_UA = "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36";
const VIEW = { width: 540, height: 600, scale: 2 };
// Tempo total da cena, contando a espera pela IA (essa espera não entra no vídeo).
const MAX_SECONDS = 240;
const MIN_SCENE_SECONDS = 8; // abaixo disso não dá pra cobrir a fala com ação
const MAX_DEAD_CLICKS = 4; // cliques seguidos sem mudar nada na tela: para antes de gastar mais IA
const CDP_TIMEOUT_MS = 30_000; // comando sem resposta do Chrome vira erro, nunca espera infinita
const wait = (ms) => new Promise((r) => setTimeout(r, ms));

class Cdp {
  constructor(url) {
    this.ws = new WebSocket(url);
    this.seq = 0;
    this.pending = new Map();
    this.listeners = new Map();
    this.open = new Promise((ok, fail) => { this.ws.onopen = ok; this.ws.onerror = fail; });
    this.ws.onclose = () => {
      for (const { fail } of this.pending.values()) fail(new Error("o Chrome da gravação fechou no meio"));
      this.pending.clear();
    };
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
    return new Promise((ok, fail) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        fail(new Error(`o Chrome não respondeu a ${method} em ${CDP_TIMEOUT_MS / 1000} s`));
      }, CDP_TIMEOUT_MS);
      this.pending.set(id, { ok: (r) => { clearTimeout(timer); ok(r); }, fail: (e) => { clearTimeout(timer); fail(e); } });
      this.ws.send(JSON.stringify({ id, method, params }));
    });
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
  const sel = 'a,button,input,select,textarea,[role=button],[role=link],[role=tab],[role=menuitem],[onclick],[tabindex]:not([tabindex="-1"]),[contenteditable="true"]';
  const out = []; let n = 0;
  // Número de listas anteriores sai antes: em 11/11 o "e12" velho continuava no botão SOAP e o clique no "Preencher
  // ficha" (e12 da lista nova) caía nele. Só o elemento que entra na lista recebe número.
  for (const old of document.querySelectorAll('[data-rb-id]')) old.removeAttribute('data-rb-id');
  // Cartão clicável costuma ser só um div com cursor de mão; pega o de fora, não cada filho dele.
  const nodes = new Set(document.querySelectorAll(sel));
  for (const el of document.querySelectorAll('body *'))
    if (getComputedStyle(el).cursor === 'pointer' && getComputedStyle(el.parentElement).cursor !== 'pointer') nodes.add(el);
  // Imagem grande entra na lista: é onde a IA marca um ponto exato (clicar com texto "x,y").
  for (const el of document.querySelectorAll('img')) if (el.getBoundingClientRect().width >= 150) nodes.add(el);
  // Cartão de lista (q-card, q-item) costuma abrir com clique sem ter cursor de mão nem papel de botão: em 12/11 o
  // modelo da ficha estava na tela e não na lista, e a IA esperou 17 passos. Cartão grande demais (painel) fica de fora.
  for (const el of document.querySelectorAll('.q-card,.q-item')) if (el.getBoundingClientRect().height <= 240) nodes.add(el);
  for (const el of nodes) {
    const r = el.getBoundingClientRect();
    if (r.width < 4 || r.height < 4 || r.bottom < 0 || r.top > innerHeight || getComputedStyle(el).visibility === 'hidden') continue;
    const tag = el.tagName.toLowerCase();
    // Seletor (q-select, combobox) mostra só o valor atual, mas abre opções. Em 12/11 a IA viu "botão Semana" na Agenda
    // e não percebeu que ali troca pra Mês.
    const isSelect = tag === 'select' || el.getAttribute('role') === 'combobox' || !!el.closest('.q-select');
    const role = tag === 'img' ? 'imagem' : isSelect ? 'seletor (valor atual; clique abre as opções)' : tag === 'input' ? 'campo ' + (el.type || 'texto') : tag === 'textarea' ? 'campo texto' : tag === 'a' ? 'link' : el.isContentEditable ? 'campo editor' : 'botão';
    const x = Math.round(r.left + r.width / 2), y = Math.round(r.top + r.height / 2);
    // Elemento que não recebe o clique no centro (escondido atrás de outro, painel fechado com opacidade zero) fica de
    // fora: em 07/11 a IA clicou 4 vezes num "Preencher ficha" fantasma que estava embaixo do seletor SOAP.
    if (tag !== 'img' && y >= 0 && y < innerHeight && x >= 0 && x < innerWidth) {
      const hit = document.elementFromPoint(x, y);
      if (!hit || !(hit === el || el.contains(hit) || hit.contains(el))) continue;
    }
    const id = 'e' + (++n); el.setAttribute('data-rb-id', id);
    // Ícone sem texto (barra de baixo): a posição deixa a IA casar o id com o que ela vê na foto de 540x600.
    const text = (el.getAttribute('aria-label') || el.innerText || el.placeholder || el.value || el.title || '').trim().replace(/\\s+/g, ' ').slice(0, 80) || (tag === 'img' ? 'imagem de x=' + Math.round(r.left) + ' a ' + Math.round(r.right) + ', y=' + Math.round(r.top) + ' a ' + Math.round(r.bottom) : 'ícone sem texto em x=' + x + ' y=' + y);
    out.push({ id, role, text, x, y });
    if (n >= 150) break;
  }
  return out;
})()`;

async function openChrome() {
  const profile = mkdtempSync(join(tmpdir(), "rb-rec-"));
  // Sem gerenciador de senhas: depois do login o Chrome oferece salvar a senha, e o que ele abre pode segurar os cliques.
  mkdirSync(join(profile, "Default"), { recursive: true });
  writeFileSync(join(profile, "Default", "Preferences"), JSON.stringify({ credentials_enable_service: false, profile: { password_manager_enabled: false, password_manager_leak_detection: false } }));
  const proc = spawn(CHROME, ["--headless=new", "--remote-debugging-port=0", `--user-data-dir=${profile}`, "--no-first-run",
    "--no-default-browser-check", "--password-store=basic", "--disable-save-password-bubble", "--disable-notifications", "--hide-scrollbars", "--mute-audio", "--lang=pt-BR", `--window-size=${VIEW.width},${VIEW.height}`, "about:blank"],
  { stdio: "ignore" });
  // Porta livre a cada gravação (o Chrome escolhe e grava no perfil): porta fixa pegava um Chrome velho ainda fechando.
  const close = () => {
    if (proc.pid) spawnSync("taskkill", ["/pid", String(proc.pid), "/t", "/f"], { stdio: "ignore" });
    try { rmSync(profile, { recursive: true, force: true }); } catch { /* arquivo preso, o Windows limpa depois */ }
  };
  const portFile = join(profile, "DevToolsActivePort");
  let target = null;
  for (let i = 0; i < 80 && !target; i++) {
    await wait(250);
    try {
      if (!existsSync(portFile)) continue;
      const port = readFileSync(portFile, "utf8").split("\n")[0].trim();
      target = (await (await fetch(`http://localhost:${port}/json/list`)).json()).find((t) => t.type === "page");
    } catch { /* subindo */ }
  }
  if (!target) { close(); throw new Error("o Chrome da gravação não abriu em 20 s"); }
  const cdp = new Cdp(target.webSocketDebuggerUrl);
  await cdp.open;
  await cdp.send("Page.enable");
  await cdp.send("Runtime.enable");
  await cdp.send("DOM.enable");
  // Janela de escolher arquivo não existe sem tela: quem abre recebe a imagem de demonstração.
  await cdp.send("Page.setInterceptFileChooserDialog", { enabled: true });
  cdp.on("Page.fileChooserOpened", (p) => {
    // No cadastro de modelo de ficha a imagem é o rosto de referência; no resto, o print de demonstração.
    evaluate(cdp, "location.pathname")
      .then((path) => cdp.send("DOM.setFileInputFiles", { files: [path.includes("modelos-formulario") ? DEMO_FACE : DEMO_IMAGE], backendNodeId: p.backendNodeId }))
      .catch(() => {});
  });
  await cdp.send("Emulation.setDeviceMetricsOverride", { width: VIEW.width, height: VIEW.height, deviceScaleFactor: VIEW.scale, mobile: false });
  await cdp.send("Emulation.setUserAgentOverride", { userAgent: MOBILE_UA, platform: "Android" });
  await cdp.send("Emulation.setFocusEmulationEnabled", { enabled: true });
  await cdp.send("Page.addScriptToEvaluateOnNewDocument", { source: CURSOR });
  await cdp.send("Page.addScriptToEvaluateOnNewDocument", { source: WATCH_CLICKS });
  return { cdp, close };
}

async function evaluate(cdp, expression) {
  const r = await cdp.send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description ?? "erro na página");
  return r.result.value;
}

// Conta o que chega na página em cada clique, pra saber se ele parou no caminho ou se a página ignorou.
const WATCH_CLICKS = `(() => {
  if (window.__rbClicks) return; window.__rbClicks = [];
  for (const k of ['pointerdown', 'click']) window.addEventListener(k, (e) => {
    const t = e.target;
    window.__rbClicks.push(k + ' em ' + String(t?.tagName).toLowerCase() + '.' + String(t?.className?.baseVal ?? t?.className ?? '').slice(0, 40) + (e.isTrusted ? '' : ' (script)'));
  }, true);
})()`;
const SCREEN_SIGN = `location.href + '|' + document.body.innerText.length + '|' + document.querySelectorAll('body *').length`;

// O que ajuda a explicar um clique que não chegou: foco, camadas abertas e bloqueios na página.
const PAGE_STATE = `JSON.stringify({ visivel: document.visibilityState, foco: document.hasFocus(), ativo: document.activeElement?.tagName, corpo: document.body.className.slice(0, 80), ponteiro: getComputedStyle(document.body).pointerEvents, camadas: document.querySelectorAll('.q-dialog, .q-menu, .driver-popover, .q-loading, dialog[open]').length })`;

// Testa as rotas direto, sem depender de clique: mostra se a navegação do sistema anda, falha ou fica pendurada.
const ROUTER_TEST = `(async () => {
  try {
    const router = document.querySelector('#q-app')?.__vue_app__?.config.globalProperties.$router;
    if (!router) return 'sem acesso às rotas';
    const from = router.currentRoute.value.fullPath;
    const result = await Promise.race([router.push('/me').then((f) => (f ? 'recusada: ' + f.message : 'ok')), new Promise((ok) => setTimeout(() => ok('pendurada'), 4000))]);
    const to = router.currentRoute.value.fullPath;
    if (to !== from) await router.replace(from);
    return from + ' para ' + to + ': ' + result;
  } catch (e) { return 'erro: ' + e.message; }
})()`;

/** Clique feito pela própria página, pra quando o clique do navegador não chega nela. */
const scriptClick = (x, y) => `(() => {
  const e = document.elementFromPoint(${x}, ${y});
  if (!e) return false;
  const at = { bubbles: true, cancelable: true, composed: true, view: window, clientX: ${x}, clientY: ${y}, button: 0 };
  e.dispatchEvent(new PointerEvent('pointerdown', { ...at, buttons: 1, pointerId: 1, pointerType: 'mouse', isPrimary: true }));
  e.dispatchEvent(new MouseEvent('mousedown', { ...at, buttons: 1 }));
  e.closest('input, textarea, [contenteditable]')?.focus();
  e.dispatchEvent(new PointerEvent('pointerup', { ...at, pointerId: 1, pointerType: 'mouse', isPrimary: true }));
  e.dispatchEvent(new MouseEvent('mouseup', at));
  e.dispatchEvent(new MouseEvent('click', at));
  return true;
})()`;

async function click(cdp, el, byScript = false) {
  await evaluate(cdp, `window.__moveCursor && window.__moveCursor(${el.x}, ${el.y})`);
  await wait(650);
  await evaluate(cdp, `window.__ripple && window.__ripple(${el.x}, ${el.y})`);
  if (byScript) return evaluate(cdp, scriptClick(el.x, el.y));
  for (const type of ["mouseMoved", "mousePressed", "mouseReleased"])
    await cdp.send("Input.dispatchMouseEvent", { type, x: el.x, y: el.y, button: "left", clickCount: type === "mouseMoved" ? 0 : 1 });
}

async function type(cdp, text, delay = 60) {
  for (const ch of String(text)) {
    await cdp.send("Input.insertText", { text: ch });
    await wait(delay);
  }
}

/** Cola a imagem de demonstração no campo com foco, como um print vindo da área de transferência. */
async function pasteImage(cdp) {
  const base64 = readFileSync(DEMO_IMAGE).toString("base64");
  return evaluate(cdp, `(() => {
    const bytes = Uint8Array.from(atob(${JSON.stringify(base64)}), (c) => c.charCodeAt(0));
    const data = new DataTransfer();
    data.items.add(new File([bytes], "imagem-demo.png", { type: "image/png" }));
    return !(document.activeElement ?? document.body).dispatchEvent(new ClipboardEvent("paste", { clipboardData: data, bubbles: true, cancelable: true }));
  })()`);
}

async function pressEnter(cdp) {
  await cdp.send("Input.dispatchKeyEvent", { type: "keyDown", key: "Enter", code: "Enter", windowsVirtualKeyCode: 13, text: "\r" });
  await cdp.send("Input.dispatchKeyEvent", { type: "keyUp", key: "Enter", code: "Enter", windowsVirtualKeyCode: 13 });
}

// Campo de senha em qualquer lugar da página, não só na parte visível: na janela de 540x600 a logo grande empurra o
// formulário pra baixo da dobra. Rola até ele pra que usuário e senha entrem na lista de elementos.
const SHOW_PASSWORD = `(() => {
  const p = [...document.querySelectorAll('input[type=password]')].find((e) => e.offsetParent !== null);
  if (!p) return false;
  p.scrollIntoView({ block: 'center' });
  return true;
})()`;

/** Texto de erro visível na tela de login (alerta, mensagem do campo), pra dizer por que não entrou. */
const LOGIN_ERROR = `(() => {
  const sel = '[role=alert],.q-field__messages,.q-notification__message,.error,.alert,.text-negative,.invalid-feedback';
  return [...document.querySelectorAll(sel)].map((e) => e.innerText.trim()).filter(Boolean).join(' | ').slice(0, 200);
})()`;

/**
 * Entra com a conta de demonstração: usuário no campo antes da senha, senha no campo de senha. A IA nunca vê.
 * Confere se entrou de verdade (o campo de senha some); senão tenta o botão Entrar; devolve o erro da tela quando não entra.
 */
async function login(cdp, elements, account) {
  const pass = elements.find((e) => e.role === "campo password");
  const user = [...elements].reverse().find((e) => /^campo (texto|text|email|tel)/.test(e.role) && elements.indexOf(e) < elements.indexOf(pass));
  if (!pass || !user) return { ok: false, error: "não achei os campos de usuário e senha" };
  await click(cdp, user);
  await type(cdp, account.usuario, 25);
  await click(cdp, pass);
  await type(cdp, account.senha, 25);
  await pressEnter(cdp);
  for (let i = 0; i < 2; i++) {
    await wait(3000);
    const now = await evaluate(cdp, LIST_ELEMENTS);
    if (!(await evaluate(cdp, SHOW_PASSWORD))) return { ok: true };
    // Enter não enviou: clica no botão de entrar.
    const submit = now.find((e) => e.role === "botão" && /\b(entrar|login|acessar|continuar|sign in)\b/i.test(e.text));
    if (i === 0 && submit) await click(cdp, submit);
  }
  return { ok: false, error: (await evaluate(cdp, LOGIN_ERROR)) || "a tela de login continuou aberta" };
}

/** Campos da tela de login, esperando a página terminar de carregar (até ~6 s). Nulo se esta tela não pede senha. */
async function loginFields(cdp) {
  for (let i = 0; i < 6; i++) {
    if (await evaluate(cdp, SHOW_PASSWORD)) {
      await wait(300);
      return evaluate(cdp, LIST_ELEMENTS);
    }
    await wait(1000);
  }
  return null;
}

/**
 * Grava uma cena: abre o sistema, deixa a IA conduzir até "pronto" e devolve o mp4. Recusa navegar pra fora do site do
 * produto e clicar em botão destrutivo (vira passo perdido no histórico, e a IA escolhe outro caminho).
 * @param ask (state) => Promise<{ action, target, text, reason }>, a chamada à API.
 */
/** prepare: passo fora do vídeo (preparo da conta). Vale o mesmo controle da IA, sem tempo mínimo e sem gerar mp4. */
export async function recordScene({ brandKey, scene, account, outDir, name, ask, prepare = false, log = console.log }) {
  mkdirSync(join(outDir, "quadros"), { recursive: true });
  const { cdp, close } = await openChrome();
  const frames = [];
  const problems = []; // erro de script e chamada recusada desde o último passo
  await cdp.send("Network.enable");
  cdp.on("Runtime.exceptionThrown", (p) => problems.push(`erro na página: ${p.exceptionDetails.exception?.description?.split("\n")[0] ?? p.exceptionDetails.text}`));
  cdp.on("Runtime.consoleAPICalled", (p) => { if (p.type === "error") problems.push(`console: ${p.args.map((a) => a.value ?? a.description ?? "").join(" ").slice(0, 160)}`); });
  cdp.on("Network.responseReceived", (p) => { if (p.response.status >= 400) problems.push(`HTTP ${p.response.status} ${new URL(p.response.url).pathname}`); });
  cdp.on("Page.frameNavigated", (p) => { if (!p.frame.parentId) note(`página carregada: ${p.frame.url}`); });
  cdp.on("Network.loadingFailed", (p) => { if (!p.canceled) problems.push(`rede falhou: ${p.errorText}`); });
  let recording = false;
  let skipped = 0; // segundos em que a gravação esperou a IA: saem da linha do tempo do vídeo
  // Registro de cada passo, junto dos quadros: é o que explica uma gravação que falhou.
  const trail = join(outDir, "passos.txt");
  writeFileSync(trail, `Cena: ${scene}\n`);
  const note = (line) => { log(line); appendFileSync(trail, `${new Date().toISOString()} ${line}\n`); };
  // Tela parada não gera quadro: foto na hora, pra o começo da cena e o "segurar" no fim existirem no vídeo.
  const grab = async () => {
    const shot = await cdp.send("Page.captureScreenshot", { format: "jpeg", quality: 90 });
    const file = `q${String(frames.length).padStart(5, "0")}.jpg`;
    writeFileSync(join(outDir, "quadros", file), Buffer.from(shot.data, "base64"));
    frames.push({ file, t: Date.now() / 1000 - skipped });
  };
  cdp.on("Page.screencastFrame", (p) => {
    cdp.send("Page.screencastFrameAck", { sessionId: p.sessionId }).catch(() => {});
    if (!recording) return;
    const file = `q${String(frames.length).padStart(5, "0")}.jpg`;
    writeFileSync(join(outDir, "quadros", file), Buffer.from(p.data, "base64"));
    frames.push({ file, t: p.metadata.timestamp - skipped });
  });
  try {
    const css = join(dirname(fileURLToPath(import.meta.url)), "guias", `${brandKey}.css`);
    if (existsSync(css)) await cdp.send("Page.addScriptToEvaluateOnNewDocument", { source: `addEventListener('DOMContentLoaded', () => { const s = document.createElement('style'); s.textContent = ${JSON.stringify(readFileSync(css, "utf8"))}; document.head.append(s); });` });
    await cdp.send("Page.navigate", { url: account.url });
    await wait(2500);
    const history = [];
    let loggedIn = false;
    // Login antes de gravar, se a primeira tela pedir: o vídeo começa já dentro do sistema.
    let elements = await loginFields(cdp);
    note(elements ? "tela de login encontrada" : `sem tela de login em ${await evaluate(cdp, "location.href")}`);
    if (elements) {
      const r = await login(cdp, elements, account);
      note(r.ok ? "entrou com a conta de demonstração" : `não entrou: ${r.error}`);
      if (!r.ok) {
        // Print da tela de login pra conferência (fica só no PC; o campo de senha aparece mascarado).
        const shot = await cdp.send("Page.captureScreenshot", { format: "jpeg", quality: 70 });
        writeFileSync(join(outDir, "login-falhou.jpg"), Buffer.from(shot.data, "base64"));
        const err = new Error(`não entrou no sistema com a conta de demonstração (${r.error}); confira usuário, senha e endereço em contas.json. Print em ${join(outDir, "login-falhou.jpg")}`);
        err.personalData = true; // não adianta tentar de novo sem corrigir a conta
        throw err;
      }
      loggedIn = true;
    }

    recording = true;
    await cdp.send("Page.startScreencast", { format: "jpeg", quality: 90, maxWidth: VIEW.width * VIEW.scale, maxHeight: VIEW.height * VIEW.scale });
    const started = Date.now();
    let outcome = "desistir";
    let stopReason = `passou de ${MAX_SECONDS} s sem terminar`;
    let deadClicks = 0;
    let started_scene = false;
    let acted = 0;
    let personalDetours = 0; // dado real visto no preparo: troca de paciente até 2 vezes
    let byScript = false; // o clique do navegador não chegou na página: segue clicando por dentro dela
    await evaluate(cdp, WATCH_CLICKS);
    for (let step = 0; Date.now() - started < (prepare ? 2 * MAX_SECONDS : MAX_SECONDS) * 1000; step++) {
      elements = await evaluate(cdp, LIST_ELEMENTS);
      // Foto em 1x (540x600) pra IA: um quarto dos pixels da gravação, bem menos tokens por passo (custo).
      // O recorte conta a partir do topo do documento: sem somar a rolagem, a foto saía em branco depois de rolar.
      const scroll = await evaluate(cdp, "({ x: scrollX, y: scrollY })");
      const shot = await cdp.send("Page.captureScreenshot", { format: "jpeg", quality: 55, clip: { ...scroll, width: VIEW.width, height: VIEW.height, scale: 1 / VIEW.scale } });
      const url = await evaluate(cdp, "location.href");
      recording = false;
      const asked = Date.now();
      const next = await ask({ brandKey, scene, url, elements: elements.map(({ id, role, text }) => ({ id, role, text })), history, step, image: shot.data });
      skipped += (Date.now() - asked) / 1000;
      recording = true;
      const chosen = elements.find((e) => e.id === next.target);
      note(`passo ${step + 1} em ${url}: ${next.action} ${chosen ? `"${chosen.text}"` : next.target ?? ""} ${next.text ?? ""} (${next.reason})`);
      // Cena de dois segundos vira imagem parada no vídeo: manda mostrar a ação antes de encerrar.
      const shown = frames.length ? frames.at(-1).t - frames[0].t : 0;
      if (!prepare && next.action === "pronto" && shown < MIN_SCENE_SECONDS && step < 20) {
        note(`  pronto recusado: só ${shown.toFixed(1)} s de cena`);
        history.push(`recusado: a cena tem só ${shown.toFixed(1)} s; mostre a ação acontecendo antes de encerrar`);
        continue;
      }
      // Cena que pede ação (tocar, escolher, digitar) só termina depois de a ação acontecer na tela (12/11: a IA dizia
      // "a data já está preenchida" e encerrava; o vídeo ficou com datas que a fala não citou e tela parada).
      if (!prepare && next.action === "pronto" && started_scene && acted === 0 && ACTION_WORDS.test(scene) && step < 30) {
        note("  pronto recusado: nenhuma ação feita depois do comecar");
        history.push("recusado: a cena pede ação e nada foi feito depois do comecar. Faça na tela cada passo que a cena descreve, mesmo que o campo já tenha valor (troque pelo valor da cena)");
        continue;
      }
      if (next.action === "pronto" || next.action === "desistir") { outcome = next.action; stopReason = next.reason; break; }
      if (next.action === "comecar") {
        // Só o primeiro vale: repetir apagava a ação já gravada e o vídeo saía parado.
        if (started_scene) { history.push("recusado: a cena já começou, siga mostrando a ação"); continue; }
        started_scene = true;
        // Preparo terminou: o que veio antes (navegar, abrir, ajustar) fica fora do vídeo.
        frames.length = 0;
        await grab();
        history.push("comecar: gravação da cena valendo a partir daqui");
        continue;
      }
      if (next.action === "dado-pessoal" && !started_scene && ++personalDetours <= 2) {
        // Ainda no preparo (nada disso entra no vídeo): apaga os quadros e manda trocar de paciente, em vez de perder o
        // vídeo inteiro (pedido do Matheus, 11/11). Depois de comecar, ou na terceira vez, para como sempre.
        frames.length = 0;
        rmSync(join(outDir, "quadros"), { recursive: true, force: true });
        mkdirSync(join(outDir, "quadros"), { recursive: true });
        note(`  dado que não parece de demonstração no preparo: quadros apagados, trocando de paciente (${next.reason})`);
        history.push("recusado: este paciente mostra dado que não parece de demonstração; volte, não abra este paciente de novo e use outro atendimento ou outro paciente da fila");
        continue;
      }
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
      // Texto começando com "/": vai direto pra esse endereço do sistema (tela que não cabe na barra do celular).
      // Depois do comecar o caminho tem de aparecer: pular pelo endereço deixaria o vídeo sem o passo a passo (12/11).
      if (!prepare && started_scene && next.action === "clicar" && /^\/[\w/-]*$/.test(next.text ?? "")) {
        history.push("recusado: a cena já começou; chegue na tela tocando no menu, como a pessoa faria");
        continue;
      }
      if (next.action === "clicar" && /^\/[\w/-]*$/.test(next.text ?? "")) {
        await evaluate(cdp, `location.assign(${JSON.stringify(next.text)})`);
        await wait(3000);
        history.push(`abriu ${next.text}`);
        continue;
      }
      if (next.action === "clicar" && el) {
        if (isDestructive(el.text)) { history.push(`recusado: ${el.text} (botão proibido)`); continue; }
        const before = await evaluate(cdp, SCREEN_SIGN);
        // Texto "x,y" no clique: ponto exato da foto da tela, pra marcar um lugar dentro de uma imagem.
        const spot = /^(\d+)\s*,\s*(\d+)$/.exec(next.text ?? "");
        // Opção de lista pode estar cortada no fim da janela: traz pra vista antes, senão o clique cai no fundo e fecha a lista.
        const visible = spot ? null : await evaluate(cdp, `(() => {
          const n = document.querySelector('[data-rb-id="${el.id}"]');
          const hit = document.elementFromPoint(${el.x}, ${el.y});
          if (!n || (hit && (n.contains(hit) || hit.contains(n)))) return null;
          n.scrollIntoView({ block: 'center' });
          const r = n.getBoundingClientRect();
          return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) };
        })()`);
        const at = spot ? { ...el, x: Number(spot[1]), y: Number(spot[2]) } : { ...el, ...visible };
        const under = await evaluate(cdp, `(() => { const e = document.elementFromPoint(${at.x}, ${at.y}); return e ? e.tagName.toLowerCase() + '.' + String(e.className.baseVal ?? e.className).slice(0, 50) : 'nada'; })()`);
        await click(cdp, at, byScript);
        await wait(1500);
        let seen = await evaluate(cdp, "(window.__rbClicks ?? []).splice(0).join(', ')");
        let changed = (await evaluate(cdp, SCREEN_SIGN)) !== before;
        note(`  clique em ${at.x},${at.y} sobre ${under}; a página viu: ${seen || "nada"}; tela ${changed ? "mudou" : "igual"}${problems.length ? `; problemas: ${problems.splice(0).slice(0, 6).join(" | ")}` : ""}`);
        // Clique que não mudou nada: registra o alvo e o que está no ponto, com o caminho no HTML (11/11: o "Preencher
        // ficha" da lista ficava no mesmo ponto do seletor SOAP e só o log mostra qual dos dois é).
        if (!changed) note(`  alvo: ${await evaluate(cdp, `(() => {
          const path = (e) => { const p = []; for (let n = e; n && n !== document.body && p.length < 6; n = n.parentElement) p.unshift(n.tagName.toLowerCase() + (n.id ? '#' + n.id : '') + '.' + String(n.className.baseVal ?? n.className).split(' ').slice(0, 2).join('.')); return p.join(' > '); };
          const t = document.querySelector('[data-rb-id="${el.id}"]'), h = document.elementFromPoint(${at.x}, ${at.y});
          const r = t?.getBoundingClientRect();
          return 'lista=' + (t ? path(t) + ' [' + Math.round(r.left) + ',' + Math.round(r.top) + ' ' + Math.round(r.width) + 'x' + Math.round(r.height) + '] texto="' + (t.innerText || '').trim().slice(0, 40) + '"' : 'sumiu') + ' | no ponto=' + (h ? path(h) + ' texto="' + (h.innerText || '').trim().slice(0, 40) + '"' : 'nada');
        })()`)}`);
        if (!byScript && !seen && !changed) {
          note(`  o clique não chegou na página; estado: ${await evaluate(cdp, PAGE_STATE)}`);
          byScript = true;
          await evaluate(cdp, scriptClick(at.x, at.y));
          await wait(1500);
          seen = await evaluate(cdp, "(window.__rbClicks ?? []).splice(0).join(', ')");
          changed = (await evaluate(cdp, SCREEN_SIGN)) !== before;
          note(`  clique por dentro da página; ela viu: ${seen || "nada"}; tela ${changed ? "mudou" : "igual"}${problems.length ? `; problemas: ${problems.splice(0).slice(0, 6).join(" | ")}` : ""}`);
          if (!changed) note(`  teste das rotas: ${await evaluate(cdp, ROUTER_TEST)}`);
        }
        deadClicks = changed ? 0 : deadClicks + 1;
        if (deadClicks >= MAX_DEAD_CLICKS) {
          stopReason = `${deadClicks} cliques seguidos sem mudar a tela`;
          break;
        }
      } else if (next.action === "digitar" && el) {
        // Campo já com foco mantém o cursor onde a IA clicou: num editor de várias linhas o clique no centro cairia na seção errada.
        const focused = await evaluate(cdp, `(() => { const n = document.querySelector('[data-rb-id="${el.id}"]'), a = document.activeElement; return !!(n && a && a !== document.body && (n === a || n.contains(a) || a.contains(n))); })()`);
        if (!focused) await click(cdp, el, byScript);
        await type(cdp, next.text ?? "");
        deadClicks = 0;
      } else if (next.action === "colar-imagem" && el) {
        await click(cdp, el);
        await wait(500);
        if (!(await pasteImage(cdp))) history.push("colar-imagem: este campo não recebeu a imagem; procure o botão de inserir imagem");
        await wait(2000); // tempo do envio e de a imagem aparecer no vídeo
      } else if (next.action === "rolar") {
        const delta = next.text === "cima" ? -350 : 350;
        // Rola a PÁGINA, não o que está embaixo do cursor: em 11/11 o editor de texto do MedlyCare ocupava a tela e a roda
        // do mouse só rolava o conteúdo dele, então a IA nunca chegava na seção Ficha. Animado, pra ficar natural no vídeo.
        const pageScrolled = await evaluate(cdp, `(() => {
          const ok = (e) => e && e.scrollHeight > e.clientHeight + 40 && e.clientHeight > innerHeight * 0.6 && (e === document.scrollingElement || /auto|scroll/.test(getComputedStyle(e).overflowY));
          const list = [document.scrollingElement, ...document.querySelectorAll('body *')].filter(ok);
          const page = list.sort((a, b) => b.clientHeight - a.clientHeight)[0];
          if (!page) return false;
          const from = page.scrollTop, to = Math.max(0, Math.min(from + ${delta}, page.scrollHeight - page.clientHeight)), t0 = performance.now();
          if (to === from) return false;
          return new Promise((done) => { const step = () => { const k = Math.min((performance.now() - t0) / 600, 1), q = k < .5 ? 2 * k * k : 1 - Math.pow(-2 * k + 2, 2) / 2; page.scrollTop = from + (to - from) * q; k < 1 ? setTimeout(step, 16) : done(true); }; step(); });
        })()`);
        if (pageScrolled) await wait(300);
        else if (byScript) await evaluate(cdp, `(() => { let e = document.elementFromPoint(270, 300); while (e && e.scrollHeight <= e.clientHeight + 1) e = e.parentElement; (e ?? document.scrollingElement).scrollBy({ top: ${delta}, behavior: 'smooth' }); })()`);
        else await cdp.send("Input.dispatchMouseEvent", { type: "mouseWheel", x: 270, y: 300, deltaX: 0, deltaY: delta });
      } else if (next.action === "login") {
        // Sessão caiu no meio (ou o primeiro login não pegou): tenta de novo; se não entrar, para com o motivo.
        // A IA pediu login mas não há campo de senha (tela carregando, sessão ativa, botão Entrar antes do formulário):
        // não é conta errada, então não derruba o vídeo; ela vê o aviso no histórico e segue a cena.
        const fields = await loginFields(cdp);
        if (!fields) {
          history.push("login: esta tela não tem campo de senha; a sessão pode já estar ativa, siga a cena ou abra a tela de entrar");
          continue;
        }
        const r = await login(cdp, fields, account);
        if (!r.ok) throw Object.assign(new Error(`não entrou no sistema com a conta de demonstração (${r.error}); confira contas.json`), { personalData: true });
        loggedIn = true;
      }
      history.push(`${next.action} ${el?.text ?? next.text ?? ""}`.trim());
      if (started_scene && ["clicar", "digitar", "colar-imagem", "rolar"].includes(next.action)) acted++;
      await wait(900);
      const now = await evaluate(cdp, "location.href");
      if (!sameSite(now, account.url)) { outcome = "desistir"; history.push(`saiu do site: ${now}`); break; }
    }
    await wait(2000); // segura o resultado na tela
    recording = false;
    await grab();
    await cdp.send("Page.stopScreencast");
    if (outcome !== "pronto") {
      const shot = await cdp.send("Page.captureScreenshot", { format: "jpeg", quality: 70 });
      writeFileSync(join(outDir, "falhou.jpg"), Buffer.from(shot.data, "base64"));
      note(`parou: ${stopReason}`);
      throw new Error(`a IA parou: ${stopReason}. Tela final em ${await evaluate(cdp, "location.href")}. Passos em ${trail}`);
    }
    // Caminho que deu certo, pra próxima gravação da mesma tarefa seguir sem tentativa e erro.
    const steps = history.filter((h) => !/^(recusado|colar-imagem: este|login: esta|saiu do site)/.test(h));
    if (prepare) return { path: null, duration: 0, steps };
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
    return { path: out, duration: (frames.at(-1).t - frames[0].t) + 0.04, steps };
  } finally {
    close();
  }
}

// --- Exploração do sistema (11/11): a IA aprende o sistema do cliente sozinha. ---------------------------------------
// Opções de cada seletor da tela: abre, lê o menu e fecha com Esc, sem escolher nada (12/11: o "Mês" da Agenda só
// aparece dentro do seletor). Só o texto das opções, no máximo 5 seletores e 12 opções cada.
const SELECT_OPTIONS = `(async () => {
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  const out = [];
  for (const sel of [...document.querySelectorAll('.q-select')].filter((e) => e.getBoundingClientRect().width > 0).slice(0, 5)) {
    const label = (sel.querySelector('.q-field__label')?.innerText || '').trim();
    const value = (sel.querySelector('.q-field__native')?.innerText || '').trim().replace(/\\s+/g, ' ');
    (sel.querySelector('.q-field__control') || sel).click();
    await wait(500);
    const opts = [...document.querySelectorAll('.q-menu .q-item')].map((i) => i.innerText.trim().replace(/\\s+/g, ' ')).filter(Boolean).slice(0, 12);
    document.activeElement?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', keyCode: 27, bubbles: true }));
    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', keyCode: 27, bubbles: true }));
    await wait(300);
    if (opts.length) out.push((label || value || 'seletor') + (value && label ? ' (agora: ' + value + ')' : '') + ': ' + opts.join(', '));
  }
  return out;
})()`;

// Só NAVEGA: abre telas pelo endereço e toca em abas. Não digita, não salva, não envia, não exclui. Anota o que cada tela
// tem (títulos, abas, campos, botões), nunca o conteúdo de listas, e tira texto com cara de nome ou número de pessoa.
const PAGE_MAP = `(() => {
  const seen = (els, max) => [...new Set([...els].map((e) => (e.getAttribute?.('aria-label') || e.innerText || e.placeholder || e.textContent || '').trim().replace(/\\s+/g, ' '))
    .filter((t) => t && t.length <= 40 && !/\d{3}|@/.test(t) && !/^[A-ZÀ-Ú][a-zà-ú]+( [A-ZÀ-Ú][a-zà-ú]+)+$/.test(t)))].slice(0, max);
  const visible = (e) => { const r = e.getBoundingClientRect(); return r.width > 2 && r.height > 2 && getComputedStyle(e).visibility !== 'hidden'; };
  const q = (sel) => [...document.querySelectorAll(sel)].filter(visible);
  const route = location.pathname.split('/').map((s) => /[0-9a-f]{8}-|^\d+$/i.test(s) ? ':id' : s).join('/') || '/';
  const detail = route.includes(':id');
  const links = [...document.querySelectorAll('a[href]')].map((a) => { try { return new URL(a.href, location.href); } catch { return null; } })
    .filter((u) => u && u.origin === location.origin && !/sair|logout|excluir|delete|remover/i.test(u.pathname)).map((u) => u.pathname);
  return {
    route,
    titulos: detail ? [] : seen(q('h1,h2,h3,.text-h5,.text-h6,.q-toolbar__title'), 8),
    abas: seen(q('[role=tab]'), 15),
    campos: seen(q('.q-field__label,label,[placeholder]'), 30),
    botoes: seen(q('button,[role=button],a.q-btn'), 30),
    menu: seen(q('.q-footer [aria-label],.q-footer .q-tab,.q-drawer .q-item'), 20),
    links: [...new Set(links)].slice(0, 60),
  };
})()`;

/** Passa pelas telas do sistema com a conta de demonstração e devolve o mapa (uma entrada por tipo de tela). */
export async function exploreSystem({ account, maxPages = 40, log = console.log }) {
  const { cdp, close } = await openChrome();
  const pages = [];
  try {
    await cdp.send("Page.navigate", { url: account.url });
    await wait(2500);
    const fields = await loginFields(cdp);
    if (fields) {
      const r = await login(cdp, fields, account);
      if (!r.ok) throw Object.assign(new Error(`exploração: não entrou com a conta de demonstração (${r.error})`), { personalData: true });
    }
    const origin = new URL(await evaluate(cdp, "location.href")).origin;
    const queue = [new URL(await evaluate(cdp, "location.href")).pathname];
    // Rotas do app: SPA (Vue, React) quase não usa link de verdade. No Vue, o roteador lista as telas; no resto, os
    // itens da barra de baixo e do menu lateral são tocados um a um e o endereço que abre entra na fila.
    const routerPaths = await evaluate(cdp, `(() => { try { const app = document.querySelector('#q-app, #app, [data-v-app]')?.__vue_app__;
      const r = app?.config?.globalProperties?.$router; return r ? r.getRoutes().map((x) => x.path).filter((p) => !p.includes(':') && !p.includes('*')) : []; } catch { return []; } })()`);
    // Fora: login, cadastro e telas públicas (termos, portal do paciente, página de busca): não são do uso do dia a dia.
    const skip = /login|entrar|sair|logout|reset|senha|password|cadastro|signup|registrar|termo|politica|privacidade|paciente\/|encontre|suspended|convite|confirmar/i;
    for (const p of routerPaths ?? []) if (!skip.test(p)) queue.push(p);
    const navCount = await evaluate(cdp, `document.querySelectorAll('.q-footer .q-tab, .q-footer .q-btn, .q-footer a, .q-drawer .q-item, nav a, nav button').length`);
    for (let i = 0; i < Math.min(navCount ?? 0, 15); i++) {
      const before = await evaluate(cdp, "location.pathname");
      await evaluate(cdp, `document.querySelectorAll('.q-footer .q-tab, .q-footer .q-btn, .q-footer a, .q-drawer .q-item, nav a, nav button')[${i}]?.click()`);
      await wait(1200);
      const after = await evaluate(cdp, "location.pathname");
      if (after !== before) { queue.push(after); await cdp.send("Page.navigate", { url: origin + before }); await wait(1800); }
    }
    const routes = new Set();
    const visited = new Set();
    while (queue.length && pages.length < maxPages) {
      const path = queue.shift();
      if (visited.has(path) || (visited.size > 0 && skip.test(path))) continue;
      visited.add(path);
      await cdp.send("Page.navigate", { url: origin + path });
      await wait(2500);
      const now = await evaluate(cdp, "location.href");
      if (!sameSite(now, account.url)) continue;
      const map = await evaluate(cdp, PAGE_MAP);
      // Detalhe (/x/:id) entra uma vez só por tipo de tela: o que importa é a estrutura, não cada registro.
      if (routes.has(map.route)) continue;
      routes.add(map.route);
      // Abas mudam o que aparece: toca em cada uma (aba só navega) e soma campos e botões.
      const tabs = [];
      for (const tab of map.abas.slice(0, 8)) {
        const ok = await evaluate(cdp, `(() => { const t = [...document.querySelectorAll('[role=tab]')].find((e) => (e.getAttribute('aria-label') || e.innerText || '').trim().replace(/\\s+/g, ' ') === ${JSON.stringify(tab)}); if (!t) return false; t.click(); return true; })()`);
        if (!ok) continue;
        await wait(1200);
        const inner = await evaluate(cdp, PAGE_MAP);
        const seletores = await evaluate(cdp, SELECT_OPTIONS).catch(() => []);
        tabs.push({ aba: tab, campos: inner.campos.filter((c) => !map.campos.includes(c)), botoes: inner.botoes.filter((b) => !map.botoes.includes(b)), seletores });
        for (const l of inner.links) if (!visited.has(l)) queue.push(l);
      }
      const { links, ...rest } = map;
      const seletores = tabs.length ? [] : await evaluate(cdp, SELECT_OPTIONS).catch(() => []);
      pages.push({ ...rest, seletores, abas: tabs.length ? tabs : map.abas });
      log(`exploração: ${map.route} (${map.campos.length} campos, ${map.botoes.length} botões, ${map.abas.length} abas)`);
      for (const l of links) if (!visited.has(l)) queue.push(l);
    }
    return { origin, pages };
  } finally {
    close();
  }
}
