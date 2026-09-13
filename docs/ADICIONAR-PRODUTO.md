# Playbook — subir vários produtos e escolher o vencedor

O sistema é multi-produto por design: adicionar um produto é **config, não código**. Este doc
liga as duas peças que já existem: o **gerador multi-produto** (subir vários) e o
**relatório de conversão** (`/admin/products/performance`, saber qual converte mais).

## A ideia (teste de vencedor, custo ~$0)

Suba 3 a 5 produtos candidatos como landings, jogue tráfego orgânico (Shorts, ver
`CONTEUDO-ORGANICO.md`) apontando cada vídeo pro link do seu produto, e deixe o **dado** decidir:
o que tiver a melhor **receita por visitante** e conversão você escala; o resto você corta.
Só um produto precisa estar faturável (VID da CJ) pra vender; os outros podem rodar em
soft-launch coletando sinal de topo de funil (visitas + interesse) antes de você investir no VID.

## Subir um produto novo (5 passos)

1. **Catálogo** — adicione uma entrada em `catalog.json`:
   ```json
   "meuproduto": {
     "displayName": "Marca Nome do Produto",
     "price": "29.99", "currency": "USD",
     "supplierVariantId": "TODO_CJ_VID_MEUPRODUTO",
     "langs": ["en", "es", "pt"], "logisticName": null
   }
   ```
   Deixe `supplierVariantId` como `TODO_...` até ter o VID real da CJ. Enquanto for TODO o produto
   **não fatura** (um pedido pago cai em `Failed` + alerta, rede de segurança). Dá pra testar orgânico.

2. **Copy** — crie `landing/<path>/strings.json`. Copie `landing/_scaffold/strings.example.json`,
   preencha o bloco `en` e traduza pra `es` e `pt` (mesmas chaves). Regra: **sem hífen e sem travessão**.
   Reviews (`r1..r3`): use avaliações **reais** depois das primeiras vendas, não invente clientes.

3. **Mídia** — coloque em `landing/<path>/media/`: `hero.jpg`, `lifestyle.jpg` e 3 fotos de galeria.
   Baixe da listagem CJ (fotos full res, não os thumbnails webp). Nomes você escolhe no manifesto.

4. **Manifesto** — adicione uma entrada em `landing/products.json`:
   ```json
   { "path": "meuproduto", "productKey": "meuproduto", "brand": "Marca",
     "media": { "hero": "hero.jpg", "lifestyle": "lifestyle.jpg", "gallery": ["g1.jpg","g2.jpg","g3.jpg"] } }
   ```

5. **Gerar + publicar** — `node landing/build.mjs` (gera todas) e `git push` (Vercel republica).
   A landing sobe em `rootboost.vercel.app/<path>/<lang>/` com o beacon de visita já embutido.

> Preço e moeda vêm do `catalog.json` (fonte única). O `build.mjs` avisa se o `productKey` do
> manifesto não existir no catálogo. Nenhum código novo em nenhum passo.

## Medir: qual converte mais

- `GET /admin/products/performance` (header `X-Api-Key`). Opcional `?days=7` pra janela.
- Devolve, por produto, rankeado do melhor pro pior:
  - **views** (visitas da landing), **orders** (pedidos pagos), **revenue**
  - **conversionRate** = pedidos / visitas
  - **revenuePerVisitor** = receita / visitas (a métrica mestre: quanto cada visita rende)
  - **fulfillable** (false = VID ainda TODO), **enoughData** (true a partir de 100+ visitas)
- Conversão vem de: beacon `POST /track/view` na landing (denominador) + pedidos pagos (numerador).
  Deduplicado com o Meta Pixel/CAPI quando ligado (mesmo evento por PaymentId).

## Decidir: escalar ou cortar

- **Amostra primeiro.** Só confie quando `enoughData=true` (100+ visitas). Abaixo disso é ruído.
- **Vencedor** = maior `revenuePerVisitor` com `enoughData`. É o que paga tráfego e sobra lucro.
  Cruze com o `GrowthPlanner` (`/admin/growth/plan`) pra ver se o pago fecha a conta nesse produto.
- **Corte rápido** o que tem muita visita e conversão perto de zero. Tráfego é seu tempo, não gaste nele.
- **Faturar o vencedor:** pegue o VID da CJ (com estoque US), troque o `TODO_...` no `catalog.json`,
  e siga o `CUTOVER-LIVE.md` (PayPal LIVE + `Supplier=Cj`). Aí escala.

## Onde cada peça vive

- Catálogo: `catalog.json` · Manifesto: `landing/products.json` · Gerador: `landing/build.mjs`
- Template + copy: `landing/_template.html` + `landing/<path>/strings.json` (+ `_scaffold/` de modelo)
- Conversão: `POST /track/view` (beacon) + `GET /admin/products/performance`
- Estratégia de tráfego: `CONTEUDO-ORGANICO.md` · Cutover pra vender: `CUTOVER-LIVE.md`
