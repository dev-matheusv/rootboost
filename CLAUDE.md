# CLAUDE.md — RootBoost

Contexto mestre do projeto para o Claude Code. Leia isto primeiro em toda sessão.

> **Contexto da org** (padrões compartilhados entre RootFlow/RootBoost/MeclyCare) vive na memória
> global `~/.claude/CLAUDE.md` — carregada automaticamente. Aqui ficam só os detalhes do RootBoost.
> Alinhamento com o RootFlow (padrão da casa): mesmos .NET 9 / Clean Architecture / xUnit / Resend /
> Railway. **Decisões de convergência**: (a) migrar SQLite → **PostgreSQL** quando entrar a camada
> de dados/IA (RootFlow já usa Postgres+pgvector); (b) já adicionados `global.json`, `.editorconfig`,
> `.gitattributes` e `RootBoost.Api.http` pra bater com o RootFlow; (c) próximo alvo de teste: projeto
> `RootBoost.Api.IntegrationTests` (pipeline real + mocks), no estilo do RootFlow.

---

## 1. O que é o RootBoost

Plataforma própria de **dropshipping automatizado**, multi-produto e multi-idioma (EN/ES/PT),
vendendo em **USD/EUR**. Fluxo central, **sem passo manual**:

> Cliente compra numa landing → webhook de pagamento chega no nosso backend → verificamos a
> assinatura → criamos o pedido no fornecedor (CJ Dropshipping) com o endereço do comprador →
> a CJ devolve o rastreio por webhook → notificamos o cliente.

Começa com 1 produto ("rack de tempero giratório 360°"). A arquitetura suporta **N produtos e
idiomas só com config (`catalog.json`) + uma nova landing** — sem código novo.

### Visão de longo prazo (por que construímos custom, e não Shopify)
O dono quer **escalar com tráfego pago automatizado no Facebook/Meta Ads, controlado por IA**:
geração de criativos, análise de performance, otimização de campanhas e manutenção contínua do
tráfego. Por isso o backend é próprio: **somos donos do servidor**, o que permite:
- **Conversions API (CAPI) server-side** — o jeito mais confiável de enviar conversões ao Meta
  (à prova de bloqueio de cookie/iOS). Shopify prenderia essa camada.
- **Dados de pedido/venda próprios** para alimentar a IA de análise e otimização.
- **Sem mensalidade** e sem lock-in de plataforma na camada de automação.

---

## 2. Decisões já tomadas (não re-litigar sem motivo)

| Decisão | Escolha | Motivo |
|---|---|---|
| Build | **Backend .NET custom** (Clean Architecture) | Base para a automação de tráfego por IA; donos do código; sem mensalidade. |
| Pagamento (agora) | **PayPal Smart Buttons**, captura **server-side** | Custo zero pra começar. Sem empresa ainda. Ver §7 (risco). |
| Pagamento (futuro) | Stripe atrás de feature flag | Quando houver LLC US/EU. Backend já preparado para os dois. |
| Fornecedor | **CJ Dropshipping** (armazém US/EU, entrega ~1 semana) | Nunca AliExpress cru (20–40 dias = reembolso + conta congelada). |
| Persistência | **EF Core + SQLite** em volume persistente | Zero custo, sobrevive a restart. |
| Runtime | **.NET 9** (`net9.0`) | Só o SDK 9.0.308 está instalado. O brief citava .NET 8; migração é trivial se necessário. |
| Produto #1 | Rack de tempero giratório 360° (`rack`) | Devolução baixa (sem eletrônica), apelo #CleanTok, demo visual satisfatória. |
| Produto #2 | **SnackSpin** — bandeja giratória de aperitivos (`snackspin`) | Reaproveita 100% do playbook do rack: mesmo "girar=satisfatório", público e fábrica. |
| E-mail (INotifier) | **Resend** (API HTTP) | Tier grátis, boa entregabilidade, config simples via API key. |
| Deploy | **Railway** (Docker + volume persistente) | Deploy simples, volume pro SQLite, boa DX. |

---

## 3. Arquitetura e estrutura

Clean Architecture. Dependências apontam **para dentro**:
`Domain ← Application ← Infrastructure ← Api`.

```
/src
  /RootBoost.Domain          Entidades e VOs puros, sem deps externas.
                            Order (aggregate root), ShippingAddress (VO), OrderStatus (enum).
  /RootBoost.Application     Casos de uso + interfaces (portas). Depende só do Domain.
    /Abstractions           ISupplierClient, IPaymentVerifier, IOrderRepository, INotifier, IProductCatalog
    /Models                 PaymentEvent, CatalogProduct, SupplierOrderResult
    /UseCases               PlaceOrderOnPayment (coração), AttachTracking
  /RootBoost.Infrastructure  Implementações concretas (adapters). [A CONSTRUIR]
                            CjClient, PayPalVerifier/StripeVerifier, EF repo, EmailNotifier, JsonCatalog
  /RootBoost.Api             Host minimal API: endpoints de webhook, DI, config. [A CONSTRUIR]
/tests
  /RootBoost.Application.Tests   Unitários dos casos de uso (fakes in-memory, sem Moq).
/landing
  /rack/{en,es,pt}/index.html   Mesmo template, strings por idioma. [A CONSTRUIR a partir de rack-landing-en.html]
  /snackspin/{en,es}/index.html [FUTURO]
/content                    Roteiros e copy por produto/idioma (o dono preenche).
catalog.json                Config de produtos. [A CRIAR]
Dockerfile                  [A CRIAR]
```

### Regra de ouro do fluxo (invariantes que os testes protegem)
1. **Idempotência por `PaymentId`** — webhooks repetem; nunca processar o mesmo pagamento 2x.
2. **Verificar assinatura de TODO webhook** antes de confiar. Sem isso = pedido grátis pra qualquer um.
3. **Preço vem do servidor**, nunca do cliente. O `createOrder` do PayPal é server-side.
4. **Pedido pago que não dá pra faturar não some**: persiste como `Failed` + alerta humano.
5. **Falha de notificação nunca derruba o pedido** — loga e segue.

---

## 4. Estado atual (o que está feito × o que falta)

- [x] **Increment 1** — Domain + Application + testes. `dotnet build` e `dotnet test` verdes (11/11).
- [x] **Increment 2** — Infrastructure: `CjClient` (auth+cache/refresh de token+createOrderV2),
      `PayPalClient` + `PayPalWebhookVerifier` (verify-webhook-signature + fetch do order pra endereço),
      `TestPaymentVerifier` (dev), `JsonCatalog`, EF `OrderRepository`+`DbContext`, `ResendNotifier`,
      `LoggingNotifier` (dev), `MockSupplierClient` (dev), resiliência HTTP (retry) via `AddStandardResilienceHandler`.
- [x] **Increment 3** — Api: `/webhook/payment`, `/webhook/stripe` (flag), `/webhook/cj`,
      `GET /orders` (API key), `/health`; DI config-driven (Supplier/Notifier/Verifier). **Testado e2e**:
      pagamento→pedido→fornecedor→`PlacedAtSupplier`→webhook CJ→`Shipped`. Idempotência e escalonamento OK.
      Bugs achados e corrigidos no caminho: EF não mapeava auto-props `{ get; }` (agora `{ get; private set; }`);
      SQLite não ordena `DateTimeOffset` (agora via `DateTimeOffsetToBinaryConverter`).
- [x] **Increment 5** — `Dockerfile` (multi-stage) + `.dockerignore` + `README.md` de deploy no Railway.
- [x] **Increment 4** — **Checkout server-side no PayPal**: `ICheckoutGateway` (Application) +
      `PayPalCheckoutGateway` (Infra) que define o **preço a partir do catálogo no servidor**;
      `PayPalClient.CreateOrderAsync`/`CaptureOrderAsync`; endpoints `POST /paypal/create-order`
      e `POST /paypal/capture-order` (captura → `PlaceOrderOnPayment`); CORS pras landings.
      **Landings** `landing/rack/{en,es,pt}/index.html` são **geradas** por `landing/rack/build.mjs`
      a partir de `_template.html` + `strings.json` (fonte da copy). SEO-friendly: texto **embutido no
      HTML** (não via JS), `<head>` com title/description/canonical/hreflang/Open Graph/Twitter +
      JSON-LD Product/Offer. Editar copy = editar `strings.json` e rodar `node landing/rack/build.mjs`.
      Checkout chama os endpoints server-side (preço nunca vem do browser).
      Placeholders p/ publicar: `BASE_URL`/`OG_IMAGE` (no build.mjs), `PAYPAL_CLIENT_ID`, `CONFIG.apiBase`, mídia.
      Obs: `landing/rack/_local/` é teste local (gitignored); `rack-landing-en.html` na raiz é legado.
- [x] **Testes de integração** — `RootBoost.Api.IntegrationTests` (pipeline HTTP real + Mock/Test/Logging):
      health, `/webhook/payment` → Fulfilled + `/orders`, idempotência, `/webhook/cj` → Shipped. 16 testes no total.
- [x] **Padrão de linguagem** aplicado nas landings: copy sem hífen e sem travessão (regra no `~/.claude/CLAUDE.md`).
- [x] **Automação de tráfego (groundwork)** — Meta CAPI (server + Pixel deduplicado) + cérebro de
      otimização em dry-run (`CampaignOptimizer`/`CreativeSelector`/`TrafficAutopilot`, endpoint
      `/admin/traffic/plan`). Ver §8 e `docs/VISAO-GERAL.md`. Ligar tráfego real ainda depende de você.
- [ ] **Futuro** — Meta Ads real (insights+actuator), advisor de IA, pipeline de criativos, dashboard.

### CHECKPOINT (2026-09-12) — SOFT-LAUNCH NO AR ✅

**A loja está no ar, ponta a ponta, em produção (modo seguro):**
- **Backend (Railway):** `https://rootboost-production.up.railway.app` — `Supplier=Mock`, `Payments__Verifier=Test`,
  PayPal **sandbox** (ClientId público + Secret em env), volume em `/data`, porta 8080. `/health` ok.
- **Landing (Vercel):** `https://rootboost.vercel.app` (`/rack/{en,es,pt}/`; raiz redireciona por idioma).
  Gerada por `landing/rack/build.mjs` (API_BASE + PAYPAL_CLIENT_ID sandbox embutidos; BASE_URL = domínio Vercel).
- **Provado em produção:** compra sandbox na Vercel → captura no Railway → pedido em `/orders`.
  Caiu como `Failed` "no supplier variant configured" = **rede de segurança correta** (catalog com VID `TODO`);
  vira `PlacedAtSupplier` quando o VID real entrar.

**Repo:** `github.com/dev-matheusv/rootboost` (Railway e Vercel puxam de `main`; push = redeploy).

**Cutover pra LIVE (vender de verdade) — pendências do usuário:**
- **PayPal**: conta que receba no CPF (recuperando a PF antiga no suporte — inatividade). Trocar sandbox → LIVE
  (`PayPal__BaseUrl=https://api-m.paypal.com` + ClientId/Secret LIVE; ClientId LIVE também na landing via build.mjs).
- **CJ**: API Key criada (email `devmatheusoxs@gmail.com`, ID `CJ5814493`). Falta **escolher o produto** e pegar o **VID**
  → `catalog.json`. ⚠️ **Só ligar `Supplier=Cj` quando o PayPal for LIVE** — senão um pagamento sandbox (fake)
  dispararia um pedido REAL/pago na CJ. Em soft-launch, manter `Supplier=Mock`.
- **Mídia** do produto (vídeo do giro + fotos da CJ) → substituir placeholders na landing + `og-image.jpg`.
- **Sem CNPJ é OK** pra começar (CPF basta em PayPal e CJ).

**Pendências gerais:** `docs/VISAO-GERAL.md §6`. GitHub MCP a reautenticar; links Notion a colar.

> ✅ **Gap fechado**: o preço agora é **definido no servidor** (create-order lê o catálogo). O webhook
> `/webhook/payment` segue como rede de segurança/idempotência. Falta o humano preencher
> `PAYPAL_CLIENT_ID`, `CONFIG.apiBase` e a mídia antes de publicar. `rack-landing-en.html` (raiz) é
> legado — a versão viva é `landing/rack/en/index.html`.

> **Trabalho incremental, commit por etapa.** Comece pequeno; não faça tudo de uma vez.

---

## 5. Integrações (referência técnica)

### CJ Dropshipping
- Base: `https://developers.cjdropshipping.com/api2.0/v1/`
- Auth: `POST authentication/getAccessToken` `{ email, apiKey }` → `data.accessToken`.
  **Cachear o token e renovar ao expirar.** Header nas chamadas: `CJ-Access-Token`.
- Pedido: `POST shopping/order/createOrderV2` (endereço + `products:[{ vid, quantity }]`).
- **Consultar a doc oficial para o payload EXATO.** Onde a doc não estiver clara, deixar
  `// TODO: confirmar campo na doc CJ` — **nunca inventar campos de API**.
- Escolher linha logística com **armazém US/EU** quando disponível.

### PayPal
- Landing: Smart Buttons. **Captura/definição de preço server-side** (não confiar no client).
- Webhooks apontando para `/webhook/payment` (evento `PAYMENT.CAPTURE.COMPLETED`).
- **Verificar assinatura** via `/v1/notifications/verify-webhook-signature` antes de confiar.
- Endereço de entrega: `purchase_units[0].shipping.address`; email: `payer.email_address`.

### Stripe (atrás de feature flag, para quando houver LLC)
- Evento `checkout.session.completed`, verificar com signing secret.

---

## 6. Segredos e config (SÓ o humano fornece — deixar como TODO/env, não tentar obter)

Nunca commitar segredos. Usar variáveis de ambiente / user-secrets.

| Env var | O que é |
|---|---|
| `CJ_EMAIL`, `CJ_API_KEY` | Credenciais da conta CJ. |
| VID por produto | Id de variante da CJ (vai em `catalog.json`, campo `supplierVariantId`). |
| `PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`, `PAYPAL_WEBHOOK_ID` | App PayPal + id do webhook (para verificar assinatura). |
| `STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET` | Só quando ligar Stripe. |
| `ORDERS_API_KEY` | Protege o `GET /orders`. |
| SMTP/Resend key | Para o `INotifier`. |
| Mídia do produto | Fotos + vídeo do giro, vindos da listagem CJ. |

---

## 7. Riscos conhecidos (documentar, não esconder)

- **PayPal congela contas de dropshipping** com volume internacional novo, entrega longa e
  disputas. Mitigar: entrega rápida via armazém US/EU, política clara de reembolso, responder
  disputas rápido. Ter Stripe como plano B (via LLC) já no radar.
- **Preço client-side é fraude fácil** — por isso captura/definição server-side. Já é invariante.
- **Pedido pago sem fulfillment** = dinheiro em risco. Estado `Failed` + alerta ao operador cobre isso.
- Não prometer "100% automático" sem os **webhooks do PayPal ligados** (documentar no README).

---

## 8. Camada de automação de tráfego

Objetivo do dono: tráfego pago Meta Ads controlado por IA.

- [x] **Meta Conversions API (CAPI) — groundwork feito.** Porta `IConversionTracker` (Application),
  disparo de `Purchase` no `PlaceOrderOnPayment` só no caminho `Fulfilled` (best-effort, nunca
  derruba o pedido). Infra: `MetaConversionTracker` (POST em `graph.facebook.com/{ver}/{pixel}/events`,
  email em SHA-256, `event_id = PaymentId`) + `NullConversionTracker` (padrão). Liga com `Tracker=Meta`
  + `Meta__PixelId`/`Meta__AccessToken`. **Pixel no navegador** (no template da landing) dispara
  `Purchase` com o **mesmo `eventID = PaymentId`** → Meta deduplica server + browser. Desligado até
  setar `metaPixelId` na landing e `Tracker=Meta` no backend.
- [x] **Cérebro de otimização (dry-run) feito.** Namespace `RootBoost.Application.Traffic`:
  `CampaignOptimizer` (regras escalar/reduzir/pausar/manter por ROAS+política), `CreativeSelector`
  (elege criativo vencedor por CPA/CTR e pausa perdedores), `TrafficAutopilot` (puxa métricas →
  decide → aplica via actuator → relatório). Infra: `NotConfiguredInsightsSource` + `LoggingCampaignActuator`
  (**DRY-RUN**, só recomenda). Endpoint `GET /admin/traffic/plan` (API key) mostra o plano.
  Política ajustável na seção `Traffic` do config. 10 testes cobrindo as regras.
- [ ] **Meta Ads real** (próximo p/ ligar de verdade): `ICampaignInsightsSource` e `ICampaignActuator`
  reais (Marketing API: insights + set budget/pause), atrás de `Traffic:Live=true`.
- [ ] **Advisor de IA** (`IOptimizationAdvisor`): plugar Claude pra refinar/priorizar decisões.
- [x] **Match do CAPI enriquecido**: landing envia `fbp`/`fbc`/`sourceUrl` na captura; API deriva IP
  (X-Forwarded-For) e user-agent → `ConversionContext` → Meta CAPI (deduplicado com o Pixel).
- [ ] **Pipeline de criativos**: gerar variações com `higgsfield-generate`/`higgsfield-product-photoshoot`.

> ⚠️ **Não gastar em anúncio** antes do produto provar que vende. CAPI e otimizador estão prontos e
> **em dry-run/desligados**; ligar só quando for realmente rodar tráfego. Panorama completo: `docs/VISAO-GERAL.md`.

---

## 9. Convenções de código

- C# moderno: file-scoped namespaces, `record` para VOs/DTOs, nullable habilitado.
- Application **não** conhece HTTP/EF/PayPal — só interfaces. Concretos só na Infrastructure.
- Casos de uso não lançam exceção para falhas esperadas (sem estoque, sem mapeamento) — retornam
  resultado tipado (`PlaceOrderResult` etc.).
- Testes: fakes in-memory (`Fakes.cs`), sem dependência de mocking. Um teste por invariante.
- Nada de segredo hardcoded. Nada de payload de API inventado.

---

## 10. Comandos

```bash
dotnet build            # compila a solução
dotnet test             # roda os unitários
dotnet run --project src/RootBoost.Api   # sobe a API (quando existir)
```

---

## 11. Arquivos herdados (base, ainda não integrados)

- `PROMPT_CLAUDE_CODE.md` — brief original completo.
- `FulfillmentEngine_Program.cs` — esqueleto antigo em arquivo único (referência; será substituído
  pela Clean Architecture já em `/src`).
- `rack-landing-en.html` — landing inicial do rack em inglês (base para `/landing/rack/en`).
- `content/01_rack_giratorio_pacote_conteudo.md` — roteiros e copy do rack (EN/ES/PT).
