// Descoberta de produtos na CJ via API (sem passar pelo muro anti-bot do site).
// Credencial: tools/cj/creds.json (GITIGNORADO, voce cria local) OU env CJ_EMAIL/CJ_API_KEY.
// Token cacheado em tools/cj/.token.json (a CJ limita o getAccessToken ~1 a cada 5 min).
//
// Uso:
//   node tools/cj/explore.mjs search "car trunk organizer" US       # busca por palavra + pais
//   node tools/cj/explore.mjs product 1386170811295076352           # detalhe por pid (variants + vid + estoque)
//   node tools/cj/explore.mjs product CJMT109776403CX               # detalhe por SKU
//   node tools/cj/explore.mjs raw product/listV2 "keyWord=car&pageSize=5"  # chamada crua (debug)
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const dir = path.dirname(fileURLToPath(import.meta.url));
const BASE = "https://developers.cjdropshipping.com/api2.0/v1/";
const TOKEN_FILE = path.join(dir, ".token.json");

function loadCreds() {
  const credsPath = path.join(dir, "creds.json");
  if (fs.existsSync(credsPath)) {
    const c = JSON.parse(fs.readFileSync(credsPath, "utf8"));
    if (c.email && c.apiKey) return { email: c.email, apiKey: c.apiKey };
  }
  const email = process.env.CJ_EMAIL, apiKey = process.env.CJ_API_KEY;
  if (email && apiKey) return { email, apiKey };
  console.error("ERRO: sem credencial. Crie tools/cj/creds.json com {\"email\":\"...\",\"apiKey\":\"...\"} (gitignorado) ou sete CJ_EMAIL/CJ_API_KEY.");
  process.exit(1);
}

async function getToken() {
  // reaproveita token cacheado se ainda valido (margem de 5 min)
  if (fs.existsSync(TOKEN_FILE)) {
    try {
      const t = JSON.parse(fs.readFileSync(TOKEN_FILE, "utf8"));
      if (t.token && t.expiresAt && Date.now() < t.expiresAt - 5 * 60 * 1000) return t.token;
    } catch { /* ignora cache corrompido */ }
  }
  const { email, apiKey } = loadCreds();
  const res = await fetch(BASE + "authentication/getAccessToken", {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, apiKey })
  });
  const json = await res.json();
  if (!json?.data?.accessToken) {
    console.error("ERRO ao autenticar na CJ:", JSON.stringify(json));
    process.exit(1);
  }
  const token = json.data.accessToken;
  const exp = Date.parse(json.data.accessTokenExpiryDate) || (Date.now() + 12 * 3600 * 1000);
  fs.writeFileSync(TOKEN_FILE, JSON.stringify({ token, expiresAt: exp }), "utf8");
  return token;
}

async function cjGet(pathAndQuery) {
  const token = await getToken();
  const res = await fetch(BASE + pathAndQuery, { headers: { "CJ-Access-Token": token } });
  const text = await res.text();
  let json; try { json = JSON.parse(text); } catch { json = { _raw: text }; }
  return { status: res.status, json };
}

const sleep = (ms) => new Promise(r => setTimeout(r, ms));
// Codigos de armazem LOCAL (entrega ~1 semana). CN_xx = origem China, nao conta como local.
const US_LOCAL = new Set(["US"]);
const EU_LOCAL = new Set(["DE", "FR", "ES", "IT", "CZ", "PL", "NL", "BE", "GB", "UK", "EU"]);
const regionSet = (r) => (r === "EU" ? EU_LOCAL : US_LOCAL);
const hasRegion = (codes, set) => Array.isArray(codes) && codes.some(c => set.has(c));
// menor preco de "a -- b" ou "a"
const minPrice = (p) => { const n = String(p ?? "").split("--").map(s => parseFloat(s)).filter(x => !isNaN(x)); return n.length ? Math.min(...n) : NaN; };

// Estoque real por armazem de um VID. Retorna [{countryCode, areaEn, qty}].
async function stockByVid(vid) {
  const { json } = await cjGet("product/stock/queryByVid?" + new URLSearchParams({ vid }));
  const data = json?.data ?? [];
  return Array.isArray(data)
    ? data.map(d => ({ country: d.countryCode, area: d.areaEn, qty: d.storageNum ?? d.totalInventoryNum ?? 0 }))
    : [];
}

function fmtMoney(v) { return v == null ? "?" : v; }

async function search(keyword, country) {
  // Endpoint de busca (V2, elasticsearch). Parametros confirmados em runtime pelo dump abaixo.
  const q = new URLSearchParams({ pageNum: "1", pageSize: "20", productNameEn: keyword });
  if (country) q.set("countryCode", country);
  let { status, json } = await cjGet("product/list?" + q.toString());
  // fallback pro listV2 se o list classico nao vier
  if (json?.result !== true && json?.code !== 200) {
    ({ status, json } = await cjGet("product/listV2?" + new URLSearchParams({ page: "1", size: "20", keyWord: keyword, ...(country ? { countryCode: country } : {}) })));
  }
  const list = json?.data?.list ?? json?.data?.content ?? json?.data ?? [];
  console.log(`\n[search] "${keyword}"${country ? " (" + country + ")" : ""} — HTTP ${status}, result=${json?.result ?? json?.code}`);
  if (!Array.isArray(list) || list.length === 0) {
    console.log("  (sem lista reconhecida — dump cru pra eu ver os campos reais:)");
    console.log(JSON.stringify(json, null, 2).slice(0, 4000));
    return;
  }
  for (const p of list.slice(0, 20)) {
    console.log(`  - ${p.productNameEn ?? p.nameEn ?? p.name ?? "?"}`);
    console.log(`      pid=${p.pid ?? p.id ?? "?"}  sku=${p.productSku ?? p.sku ?? "?"}  preco=${fmtMoney(p.sellPrice ?? p.price)}  cat=${p.categoryName ?? p.categoryId ?? "?"}`);
    if (p.productImage ?? p.bigImage) console.log(`      img=${p.productImage ?? p.bigImage}`);
  }
  console.log(`  (${list.length} itens; use: node tools/cj/explore.mjs product <pid> pra ver variantes/estoque)`);
}

async function product(idOrSku) {
  const key = /^\d{6,}$/.test(idOrSku) ? "pid" : "productSku";
  const { status, json } = await cjGet("product/query?" + new URLSearchParams({ [key]: idOrSku }));
  console.log(`\n[product] ${key}=${idOrSku} — HTTP ${status}, result=${json?.result ?? json?.code}`);
  const d = json?.data;
  if (!d) { console.log(JSON.stringify(json, null, 2).slice(0, 4000)); return; }
  console.log(`  nome: ${d.productNameEn ?? d.nameEn ?? "?"}`);
  console.log(`  pid:  ${d.pid ?? d.id ?? "?"}   sku: ${d.productSku ?? d.sku ?? "?"}`);
  const imgs = d.productImageSet ?? d.productImages ?? (d.productImage ? [d.productImage] : []);
  if (Array.isArray(imgs) && imgs.length) {
    console.log(`  fotos (${imgs.length}):`);
    imgs.slice(0, 12).forEach((u, i) => console.log(`     [${i}] ${u}`));
  }
  const variants = d.variants ?? d.variantList ?? [];
  console.log(`  variantes (${variants.length}):`);
  for (const v of variants) {
    const inv = v.inventories ?? v.inventory ?? v.inventoryList ?? [];
    const us = Array.isArray(inv) ? inv.find(x => (x.countryCode ?? x.country) === "US") : null;
    const usStock = us ? (us.totalInventory ?? us.storageNum ?? us.stock ?? "?") : "0/na";
    console.log(`     vid=${v.vid ?? v.variantId ?? "?"}  ${v.variantNameEn ?? v.variantKey ?? ""}  preco=${fmtMoney(v.variantSellPrice ?? v.sellPrice)}  estoqueUS=${usStock}`);
  }
  console.log("  (se algum campo veio '?', me avisa que eu ajusto o parser pro shape real — dump abaixo:)");
  console.log(JSON.stringify(d, null, 2).slice(0, 1500));
}

// Caca completa: busca -> filtra por regiao (shippingCountryCodes) -> rankeia por validacao
// (listedNum) -> confirma estoque local do armazem via stock/queryByVid nos top N.
async function hunt(keyword, region = "US", deepN = 6, priceCap = 25) {
  const set = regionSet(region);
  let list = [];
  for (const page of ["1", "2"]) {
    const q = new URLSearchParams({ pageNum: page, pageSize: "90", productNameEn: keyword });
    const { json } = await cjGet("product/list?" + q.toString());
    list = list.concat(json?.data?.list ?? json?.data?.content ?? []);
    await sleep(400);
  }
  console.log(`\n[hunt] "${keyword}" regiao=${region} capPreco=$${priceCap} — ${list.length} resultados brutos`);

  const cand = list
    .filter(p => hasRegion(p.shippingCountryCodes, set))       // armazem local de verdade
    .filter(p => { const m = minPrice(p.sellPrice); return isNaN(m) || m <= priceCap; })
    .map(p => ({
      name: p.productNameEn, pid: p.pid, sku: p.productSku,
      price: p.sellPrice, listed: Number(p.listedNum || p.listingCount || 0),
      video: !!p.isVideo, codes: p.shippingCountryCodes, img: p.productImage
    }))
    .sort((x, y) => y.listed - x.listed || (y.video - x.video));

  console.log(`[hunt] ${cand.length} com armazem ${region} (por shippingCountryCodes). Top por validacao:\n`);
  const top = cand.slice(0, deepN);
  for (const c of top) {
    // pega 1 variante e confere estoque local real
    let localQty = "?";
    try {
      await sleep(400);
      const { json: pj } = await cjGet("product/query?" + new URLSearchParams({ pid: c.pid }));
      const v0 = (pj?.data?.variants ?? pj?.data?.variantList ?? [])[0];
      if (v0?.vid) {
        await sleep(400);
        const wh = await stockByVid(v0.vid);
        const local = wh.filter(w => set.has(w.country));
        localQty = local.length ? local.map(w => `${w.country}:${w.qty}`).join(",") : "0(so " + (wh.map(w=>w.country).join("/")||"?") + ")";
      }
    } catch { localQty = "erro"; }
    console.log(`  * ${c.name}`);
    console.log(`      pid=${c.pid} sku=${c.sku} preco=${c.price} listados=${c.listed} video=${c.video ? "sim" : "nao"}`);
    console.log(`      envia=${(c.codes||[]).join("/")}  estoque_local=${localQty}`);
    console.log(`      img=${c.img}`);
  }
  console.log(`\n[hunt] estoque_local com numero real (ex US:1234) = armazem local de verdade -> entrega ~1 semana. "0(so CN)" = so China, descartar.`);
}

const [cmd, a, b, c, d] = process.argv.slice(2);
if (cmd === "search") await search(a, b);
else if (cmd === "product") await product(a);
else if (cmd === "hunt") await hunt(a, (b || "US").toUpperCase(), Number(c) || 6, Number(d) || 25);
else if (cmd === "stock") { console.log(JSON.stringify(await stockByVid(a), null, 2)); }
else if (cmd === "raw") { const r = await cjGet(a + (b ? "?" + b : "")); console.log(JSON.stringify(r, null, 2).slice(0, 6000)); }
else { console.error("uso: hunt \"palavra\" [US|EU] [N] | search \"palavra\" [US] | product <pid|sku> | stock <vid> | raw <path> \"qs\""); process.exit(1); }
