// Gerador compartilhado das landings (multi-produto). SEO friendly (texto no HTML).
// Subir produto novo = 1 entrada em products.json + pasta <path>/ (strings.json + media/).
// Nenhum build.mjs por produto. Preco e moeda vem do catalog.json (fonte unica da verdade).
//
// Uso:  node landing/build.mjs            (constroi todos)
//       node landing/build.mjs car home   (constroi so esses paths)
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const dir = path.dirname(fileURLToPath(import.meta.url));
const root = path.join(dir, "..");

// --- Config de publicacao (compartilhada por todas as landings) ---
const BASE_URL = "https://rootboost.vercel.app";
const API_BASE = "https://rootboost-production.up.railway.app";
// Client ID PayPal (publico). Sandbox agora; trocar p/ LIVE no cutover (ver docs/CUTOVER-LIVE.md).
const PAYPAL_CLIENT_ID = "BAAPIdIdPmgfASjk5sCCT_if30LytHS8XN4GzZcIO9p2D_rkgiBrRoZCS_c73MgnUL6tGiNjeaJ1mK3pKs";
// Pixel do Meta (browser). Deixe assim = desligado. No cutover, troque pelo Pixel ID real.
const META_PIXEL_ID = "YOUR_META_PIXEL_ID";

const template = fs.readFileSync(path.join(dir, "_template.html"), "utf8");
const manifest = JSON.parse(fs.readFileSync(path.join(dir, "products.json"), "utf8"));
const catalog = JSON.parse(fs.readFileSync(path.join(root, "catalog.json"), "utf8")).products;

const only = process.argv.slice(2); // paths especificos, ou vazio = todos
const entries = manifest.products.filter(p => only.length === 0 || only.includes(p.path));
if (entries.length === 0) { console.error("[build] nenhum produto pra construir."); process.exit(1); }

let total = 0;
for (const entry of entries) {
  const { path: productPath, productKey, brand, media } = entry;
  const cat = catalog[productKey];
  if (!cat) { console.error(`[build] ERRO: productKey "${productKey}" nao existe em catalog.json. Pulando ${productPath}.`); continue; }

  const stringsPath = path.join(dir, productPath, "strings.json");
  if (!fs.existsSync(stringsPath)) { console.error(`[build] ERRO: falta ${productPath}/strings.json. Pulando.`); continue; }
  const strings = JSON.parse(fs.readFileSync(stringsPath, "utf8"));
  const langs = Object.keys(strings);

  const mediaBase = `/${productPath}/media`;
  const gallery = (media?.gallery ?? []).slice(0, 3);
  while (gallery.length < 3) gallery.push(media?.hero ?? "hero.jpg"); // fallback: repete o hero
  const heroSrc = `${mediaBase}/${media?.hero ?? "hero.jpg"}`;
  const lifestyleSrc = `${mediaBase}/${media?.lifestyle ?? "lifestyle.jpg"}`;
  const ogImage = `${BASE_URL}/${productPath}/og-image.jpg`;

  const canonical = (lang) => `${BASE_URL}/${productPath}/${lang}/`;
  const alternates = () =>
    [...langs.map(l => `<link rel="alternate" hreflang="${l}" href="${canonical(l)}">`),
     `<link rel="alternate" hreflang="x-default" href="${canonical(langs[0])}">`].join("\n");

  for (const lang of langs) {
    const s = strings[lang];
    const vars = {
      ...s, lang, canonical: canonical(lang), alternates: alternates(), ogImage,
      priceNum: String(cat.price), currencyCode: cat.currency,
      apiBase: API_BASE, paypalClientId: PAYPAL_CLIENT_ID, metaPixelId: META_PIXEL_ID,
      productKey, brand,
      heroSrc, lifestyleSrc,
      g1src: `${mediaBase}/${gallery[0]}`, g2src: `${mediaBase}/${gallery[1]}`, g3src: `${mediaBase}/${gallery[2]}`
    };
    const html = template.replace(/\{\{(\w+)\}\}/g, (_, key) => {
      if (!(key in vars)) { console.warn(`[build] chave ausente em ${productPath}/${lang}: ${key}`); return ""; }
      return String(vars[key]);
    });
    const outDir = path.join(dir, productPath, lang);
    fs.mkdirSync(outDir, { recursive: true });
    fs.writeFileSync(path.join(outDir, "index.html"), html, "utf8");
    total++;
    console.log(`[build] ${productPath}/${lang}/index.html  (${productKey}, ${cat.currency} ${cat.price})`);
  }
}
console.log(`[build] OK: ${total} landings geradas de ${entries.length} produto(s).`);
