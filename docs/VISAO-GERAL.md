# RootBoost — Visão Geral (parâmetro de tudo)

Documento-mapa da plataforma. Estado em 2026-09-09. Para o contexto operacional do dia a dia,
ver `CLAUDE.md` (raiz) e o `~/.claude/CLAUDE.md` (padrões da família Root).

---

## 1. O que é

Plataforma própria de **dropshipping automatizado** com meta de **tráfego pago (Meta Ads) controlado
por IA**. Fluxo central, sem passo manual:

> Cliente compra na landing → checkout PayPal **server-side** (preço definido no servidor) →
> pedido criado no fornecedor **CJ Dropshipping** com o endereço do comprador → rastreio volta por
> webhook → cliente é notificado. Em paralelo, a **conversão** é reportada à Meta (CAPI + Pixel) e o
> **otimizador** ajusta orçamento/criativos.

Multi-produto e multi-idioma (EN/ES/PT) só com `catalog.json` + uma pasta de landing.

---

## 2. Arquitetura

Clean Architecture, monólito modular (.NET 9). Dependências apontam para dentro:
`Api → Application → Domain`, com `Infrastructure` implementando as portas.

Três contextos dentro da Application:
- **Fulfillment** (`Abstractions`, `UseCases`, `Models`): pedido pago → fornecedor → rastreio.
- **Tracking** (`IConversionTracker`): conversões pra Meta (server-side).
- **Traffic** (`Traffic/`): o "cérebro" — otimizador, seletor de criativos, piloto automático.

```
src/RootBoost.Domain          Order, ShippingAddress, OrderStatus
src/RootBoost.Application      portas + casos de uso + Traffic (otimização)
src/RootBoost.Infrastructure   CJ, PayPal, EF/SQLite, Resend, Meta CAPI, Traffic adapters
src/RootBoost.Api              minimal API (webhooks, checkout, /orders, /admin, /health)
tests/RootBoost.Application.Tests        23 unit
tests/RootBoost.Api.IntegrationTests     5 e2e (pipeline HTTP real + mocks)
landing/rack/{en,es,pt}        landings geradas por build.mjs (SEO)
catalog.json                   produtos (preço/variante/idiomas)
```

Invariantes protegidas por teste: idempotência por `PaymentId`; assinatura de webhook verificada;
**preço server-side**; pedido pago que falha vira `Failed` + alerta humano; notificação/conversão
nunca derrubam o pedido.

---

## 3. O que está construído (checklist)

**Backend / fulfillment**
- [x] Domain + casos de uso `PlaceOrderOnPayment` (idempotência, mapeamento, escalonamento) e `AttachTracking`.
- [x] `CjClient` (auth + cache de token + createOrderV2), EF Core/SQLite, `ResendNotifier`, mocks de dev.
- [x] Endpoints: `/webhook/payment`, `/webhook/cj`, `/webhook/stripe` (flag), `GET /orders` (API key), `/health`.

**Checkout (pagamento)**
- [x] `PayPalCheckoutGateway` + `/paypal/create-order` (preço do catálogo no servidor) e `/paypal/capture-order`.
- [x] `PayPalWebhookVerifier` (verify-webhook-signature) como rede de segurança/idempotência.
- [x] Validado no PayPal **Sandbox**: `create-order` retornou pedido real (HTTP 200). Código provado.

**Landings (marketing)**
- [x] EN/ES/PT geradas por `landing/rack/build.mjs` (`_template.html` + `strings.json`).
- [x] SEO: texto no HTML (não via JS), title/description/canonical/hreflang/OG/Twitter + JSON-LD Product.
- [x] Copy no padrão de linguagem (sem hífen, sem travessão).

**Conversões (Meta CAPI)**
- [x] `IConversionTracker` + `MetaConversionTracker` (server-side, email SHA-256, `event_id = PaymentId`).
- [x] Pixel no navegador com o mesmo `eventID` → dedup server + browser. **Desligado** por padrão.

**Automação de tráfego (o cérebro) — dry-run**
- [x] `CampaignOptimizer` (escalar/reduzir/pausar/manter por ROAS + política com travas).
- [x] `CreativeSelector` (elege vencedor por CPA/CTR, pausa perdedores).
- [x] `TrafficAutopilot` (métricas → decisões → aplica via actuator → relatório) + `GET /admin/traffic/plan`.
- [x] Adapters em **DRY-RUN** (`NotConfiguredInsightsSource`, `LoggingCampaignActuator`): só recomenda.

**Infra / casa**
- [x] Dockerfile (bind em `$PORT`), `.env.example`, `.editorconfig`/`.gitattributes`/`global.json`, docs.
- [x] 28 testes verdes. Alinhado aos padrões do RootFlow.

---

## 4. Como rodar (local, tudo mockado)

```bash
dotnet build
dotnet test
# API em modo dev (Mock/Logging/Test), sem credencial:
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/RootBoost.Api --urls http://localhost:5080
```
Gerar landings: `node landing/rack/build.mjs`. Ver plano da automação: `GET /admin/traffic/plan` (dry-run).

---

## 5. Como a automação de tráfego por IA funciona

1. **Sinal de conversão** (já pronto): toda venda faturada dispara `Purchase` no servidor (CAPI) e no
   navegador (Pixel), deduplicados pelo `PaymentId`. É o dado que o algoritmo da Meta e o nosso otimizador usam.
2. **Coleta de métricas** (a ligar): `ICampaignInsightsSource` puxa spend/receita/vendas/CTR por campanha e criativo.
3. **Decisão** (pronto, testado): `CampaignOptimizer` aplica regras por ROAS (escala vencedor, corta perdedor,
   reduz morno) dentro de travas (teto de orçamento, gasto mínimo antes de decidir). `CreativeSelector` faz o A/B.
4. **Ação** (a ligar): `ICampaignActuator` aplica budget/pause na Meta. Hoje em **dry-run** (só recomenda).
5. **IA** (a ligar): `IOptimizationAdvisor` permite um conselheiro (Claude) refinar/priorizar as decisões.
6. **Criativos** (a ligar): gerar variações com Higgsfield e alimentar o teste A/B.

Loop pretendido: um schedule chama `TrafficAutopilot.RunAsync` a cada X → ajustes contínuos.
Segurança: nada gasta anúncio sozinho; travas de orçamento; dry-run por padrão.

---

## 6. O que depende de você (handoff)

| Item | Pra quê | Como |
|---|---|---|
| Conta **PayPal pessoal (CPF)** | Receber de verdade sem esperar CNPJ | Login em developer.paypal.com; credenciais reais no lugar do sandbox |
| Conta **CJ** + **VID** por produto | Fulfillment real | `catalog.json` + env `Cj__*` |
| **Resend** (key + domínio) | E-mails de confirmação/rastreio | env `Resend__*` |
| **Meta**: Pixel + token CAPI + ad account | Ligar conversão e tráfego | env `Tracker=Meta`, `Meta__*`; `metaPixelId` na landing |
| **BASE_URL** + `og-image.jpg` | SEO/compartilhamento das landings | `build.mjs` + rodar o build |
| Reautenticar **GitHub MCP** | Integração git | `claude mcp` / `/mcp` em sessão interativa |
| Links **Notion** (RootFlow/MedlyCare) | Base de conhecimento | colar em `~/.claude/CLAUDE.md` |

---

## 7. Roadmap (ordem sugerida)

1. Abrir/confirmar PayPal pessoal → **primeira venda real** (valor baixo) end to end.
2. CJ real (VID) → fulfillment de verdade; Resend ligado.
3. Tráfego orgânico (Shorts) pra **validar** o produto antes de pagar mídia.
4. Ligar Meta (Pixel + CAPI) e, com dados, `Traffic:Live=true` com actuator real da Meta Ads.
5. Advisor de IA + pipeline de criativos (Higgsfield) + schedule do autopilot.
6. 2º produto (SnackSpin): entrada no catálogo + pasta de landing.
7. Quando escalar/precisar: migrar SQLite → PostgreSQL; Stripe via LLC/CNPJ.
