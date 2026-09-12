// Gerador estático das landings do rack (SEO friendly: texto embutido no HTML).
// Uso:  node landing/rack/build.mjs
// Fonte: _template.html + strings.json  ->  <lang>/index.html
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const dir = path.dirname(fileURLToPath(import.meta.url));

// EDITAR antes de publicar:
const BASE_URL = "https://rootboost.vercel.app"; // domínio da landing (Vercel)
const PRODUCT_PATH = "rack";                    // vira {BASE}/rack/<lang>/
const OG_IMAGE = `${BASE_URL}/${PRODUCT_PATH}/og-image.jpg`;
const PRICE_NUMBER = "34.99";                   // numérico pro JSON-LD (bater com catalog.json)
const CURRENCY_CODE = "USD";
// Checkout: origem da API (Railway) + Client ID do PayPal (público). Sandbox agora; trocar p/ LIVE depois.
const API_BASE = "https://rootboost-production.up.railway.app";
const PAYPAL_CLIENT_ID = "BAAPIdIdPmgfASjk5sCCT_if30LytHS8XN4GzZcIO9p2D_rkgiBrRoZCS_c73MgnUL6tGiNjeaJ1mK3pKs";

const template = fs.readFileSync(path.join(dir, "_template.html"), "utf8");
const strings = JSON.parse(fs.readFileSync(path.join(dir, "strings.json"), "utf8"));
const langs = Object.keys(strings);

const canonical = (lang) => `${BASE_URL}/${PRODUCT_PATH}/${lang}/`;
const alternates = () =>
  [...langs.map(l => `<link rel="alternate" hreflang="${l}" href="${canonical(l)}">`),
   `<link rel="alternate" hreflang="x-default" href="${canonical(langs[0])}">`].join("\n");

let count = 0;
for (const lang of langs) {
  const s = strings[lang];
  const vars = { ...s, lang, canonical: canonical(lang), alternates: alternates(), ogImage: OG_IMAGE,
                 priceNum: PRICE_NUMBER, currencyCode: CURRENCY_CODE,
                 apiBase: API_BASE, paypalClientId: PAYPAL_CLIENT_ID };
  // troca todos os {{key}} pelos valores (valor ausente vira string vazia, com aviso)
  const html = template.replace(/\{\{(\w+)\}\}/g, (_, key) => {
    if (!(key in vars)) { console.warn(`[build] chave ausente em ${lang}: ${key}`); return ""; }
    return String(vars[key]);
  });
  const outDir = path.join(dir, lang);
  fs.mkdirSync(outDir, { recursive: true });
  fs.writeFileSync(path.join(outDir, "index.html"), html, "utf8");
  count++;
  console.log(`[build] gerado ${lang}/index.html`);
}
console.log(`[build] OK: ${count} landings geradas. Lembre de setar BASE_URL, PAYPAL_CLIENT_ID e CONFIG.apiBase antes de publicar.`);
