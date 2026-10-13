// Coleta de mercado na Amazon (top orgânicos de cada busca: título, ASIN, nota, nº de avaliações, preço, BSR, páginas).
// A Amazon bloqueia acesso automatizado fora do navegador, então isto roda DENTRO de uma aba já aberta em amazon.com
// (console do navegador ou o browser do Claude). Leitura pública apenas, sem login.
//
// Uso: abrir https://www.amazon.com, colar no console, ajustar QUERIES, e ler window.__scan quando done=true.
// Resultado de 2026-10-08 resumido em books/PESQUISA.md.

const QUERIES = [
  'dinosaur coloring book for kids ages 4-8',
  'christmas coloring book for kids',
  'toddler coloring book ages 1-3',
];

window.__scan = { done: false, out: {} };
(async () => {
  const parse = (html) => new DOMParser().parseFromString(html, 'text/html');

  async function search(q) {
    const d = parse(await (await fetch('/s?k=' + encodeURIComponent(q))).text());
    return [...d.querySelectorAll('div[data-component-type="s-search-result"]')]
      .filter((e) => !/Sponsored/.test(e.innerText))
      .slice(0, 8)
      .map((e) => {
        const href = e.querySelector('a[href*="/dp/"]')?.getAttribute('href') || '';
        const t = e.innerText;
        return {
          title: (e.querySelector('h2')?.innerText || '').slice(0, 140),
          asin: (href.match(/\/dp\/([A-Z0-9]{10})/) || [])[1] || null,
          rating: (t.match(/(\d\.\d) out of 5/) || [])[1] || '',
          reviews: (t.match(/\(([\d.,]+K?)\)/) || [])[1] || '',
          price: (t.match(/\$\d+\.\d\d/) || [])[0] || '',
        };
      });
  }

  async function detail(asin) {
    const x = parse(await (await fetch('/dp/' + asin)).text()).body.innerText;
    return {
      bsr: (x.match(/#([\d,]+) in Books/) || [])[1] || '',
      pages: (x.match(/(\d+) pages/) || [])[1] || '',
    };
  }

  for (const q of QUERIES) {
    const items = await search(q);
    await Promise.all(items.filter((i) => i.asin).map(async (it) => {
      try { Object.assign(it, await detail(it.asin)); } catch { /* item sem página: segue */ }
    }));
    window.__scan.out[q] = items;
  }
  window.__scan.done = true;
})();
