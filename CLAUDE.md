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
      **Landings** `landing/rack/{en,es,pt}/index.html`: template único com dicionário i18n
      (markup escrito 1x; só `const LANG` muda por pasta), checkout chamando os endpoints
      server-side (preço nunca vem do browser). Placeholders: `CONFIG.apiBase`, `PAYPAL_CLIENT_ID`, mídia.
- [x] **Testes de integração** — `RootBoost.Api.IntegrationTests` (pipeline HTTP real + Mock/Test/Logging):
      health, `/webhook/payment` → Fulfilled + `/orders`, idempotência, `/webhook/cj` → Shipped. 16 testes no total.
- [x] **Padrão de linguagem** aplicado nas landings: copy sem hífen e sem travessão (regra no `~/.claude/CLAUDE.md`).
- [ ] **Futuro (automação)** — Meta CAPI server-side, pipeline de criativos (ver §8), dashboard de análise.

### Handoff — pendências do humano (o que depende de você)
1. **Reautenticar o GitHub MCP**: sessão interativa `claude` → `/mcp` (ou `claude mcp`) e reconectar `github`.
2. **Credenciais** (env vars, ver §6): `Cj__Email`/`Cj__ApiKey` + **VID** de cada produto no `catalog.json`;
   `PayPal__ClientId`/`ClientSecret`/`WebhookId`; `Resend__ApiKey` + domínio verificado.
3. **Landing**: preencher `PAYPAL_CLIENT_ID` e `CONFIG.apiBase` em `landing/rack/*/index.html`; trocar a mídia (foto/vídeo do giro).
4. **Links do Notion** (RootFlow/MedlyCare) na tabela do `~/.claude/CLAUDE.md`.

### Próximos passos de dev (quando voltarmos)
- Deploy no Railway em sandbox (com as credenciais) e teste do fluxo PayPal real ponta a ponta.
- SEO das landings: "assar" o texto estático por idioma (hoje é injetado via JS).
- 2º produto **SnackSpin**: entrada no `catalog.json` (já existe) + pasta de landing (reusa o template).
- Camada de automação (Meta CAPI) só depois do produto validar.

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

## 8. Camada de automação de tráfego (visão — ainda não construída)

Objetivo do dono: tráfego pago Meta Ads controlado por IA. Peças previstas:
- **Meta Conversions API (CAPI)**: disparar `Purchase`/`InitiateCheckout` server-side a partir
  dos webhooks (temos o dado do pedido no servidor — ideal para deduplicação com o Pixel).
- **Pipeline de criativos**: gerar imagens/vídeos de anúncio a partir da mídia do produto.
  Ferramentas disponíveis nesta sessão: skills `higgsfield-generate` (vídeo/UGC/ads, Marketing
  Studio, Virality Predictor) e `higgsfield-product-photoshoot` (foto de produto). Úteis para
  variações de criativo sem refilmar.
- **Análise/otimização**: ler performance de campanha, sugerir cortes/escala. (Definir métricas
  e fonte de dados antes de construir.)

> Só construir depois que o produto **provar que vende** com tráfego orgânico (Shorts) ou um
> teste pago pequeno. Não automatizar tráfego de um produto não validado.

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
