# RootBoost — Plataforma de Dropshipping Automatizado

Cliente compra numa landing → webhook de pagamento chega aqui → verificamos a assinatura →
criamos o pedido no fornecedor (CJ Dropshipping) com o endereço do comprador → a CJ devolve o
rastreio por webhook → notificamos o cliente. **Sem passo manual.** Multi-produto e multi-idioma
só com `catalog.json` + uma landing nova.

> Contexto completo do projeto (decisões, arquitetura, riscos, roadmap): veja [CLAUDE.md](CLAUDE.md).

## Stack
.NET 9 · Clean Architecture · EF Core + SQLite · minimal API · Docker.

```
src/RootBoost.Domain          entidades/VOs puros
src/RootBoost.Application      casos de uso + interfaces (portas)
src/RootBoost.Infrastructure   CJ, PayPal, EF, Resend, catálogo (adapters)
src/RootBoost.Api              host minimal API (webhooks, /orders, /health)
tests/                        unitários dos casos de uso
```

## Rodar local (tudo mockado, sem credencial)

O ambiente `Development` usa fornecedor Mock, notificador de log e verificador de pagamento de teste.

```bash
dotnet build
dotnet test
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/RootBoost.Api --urls http://localhost:5080
```

Testar o fluxo completo (cria pedido, chama fornecedor mock, aparece em `/orders`):

```bash
curl -X POST http://localhost:5080/webhook/payment -H "Content-Type: application/json" \
  -d '{"paymentId":"PAY-1","productKey":"rack","quantity":1,"email":"a@b.com","name":"Jane","line1":"123 St","city":"Austin","state":"TX","zip":"78701","countryCode":"US","amount":34.99,"currency":"USD"}'
curl http://localhost:5080/orders
```

> Nota: com o `catalog.json` real, o `rack` cai em `NeedsHuman` (`supplierVariantId` ainda é `TODO_*`).
> Isso é o comportamento correto — pedido pago sem variante configurada é escalado, não perdido.
> Preencha o VID da CJ pra ver o `Fulfilled`.

## Endpoints
| Método | Rota | O quê |
|---|---|---|
| POST | `/webhook/payment` | Evento de pagamento (PayPal em prod, Test em dev). Verifica assinatura → cria pedido. |
| POST | `/webhook/cj` | Status/rastreio da CJ → anexa tracking → notifica cliente. |
| POST | `/webhook/stripe` | Atrás de feature flag (`Features:Stripe=true`). Ainda não implementado. |
| GET | `/orders` | Dashboard simples. Protegido por header `X-Api-Key` (se `Orders:ApiKey` estiver setado). |
| GET | `/health` | Health check. |

## Variáveis de ambiente (prod)

Segredos **nunca** no código — via env vars. Nomes aninhados usam `__` (duplo sublinhado).

| Env var | O quê |
|---|---|
| `ConnectionStrings__Sqlite` | Ex.: `Data Source=/data/rootboost.db` (volume persistente). |
| `Supplier` | `Cj` (prod) ou `Mock` (dev). |
| `Notifier` | `Resend` (prod) ou `Logging` (dev). |
| `Payments__Verifier` | `PayPal` (prod) ou `Test` (dev). |
| `Cj__Email`, `Cj__ApiKey` | Credenciais da conta CJ. |
| `PayPal__ClientId`, `PayPal__ClientSecret`, `PayPal__WebhookId` | App + webhook do PayPal. `PayPal__BaseUrl` = `https://api-m.sandbox.paypal.com` pra sandbox. |
| `Resend__ApiKey`, `Resend__FromEmail`, `Resend__OperatorEmail` | E-mail transacional + inbox de alertas de falha. |
| `Orders__ApiKey` | Protege o `GET /orders`. |
| `Features__Stripe` | `true` liga o endpoint do Stripe (ainda não implementado). |

## Deploy no Railway

1. Suba o repositório no GitHub e crie um projeto no Railway a partir dele (detecta o `Dockerfile`).
2. **Adicione um Volume** montado em `/data` (o SQLite vive lá; sem isso, os pedidos somem a cada deploy).
3. Em **Variables**, configure as env vars da tabela acima (deixe `Supplier=Cj`, `Notifier=Resend`, `Payments__Verifier=PayPal`).
4. O Railway injeta `$PORT`; o container escuta em `0.0.0.0:8080` por padrão — ajuste `ASPNETCORE_URLS=http://0.0.0.0:$PORT` na variável se necessário.
5. Aponte os webhooks:
   - **PayPal** → `https://SEU-APP.up.railway.app/webhook/payment` (evento `PAYMENT.CAPTURE.COMPLETED`).
   - **CJ** → `https://SEU-APP.up.railway.app/webhook/cj`.

## O que só o humano fornece
- Conta CJ (`Cj__Email`, `Cj__ApiKey`) e o **VID** de cada produto (vai no `catalog.json`).
- App PayPal (`ClientId`, `ClientSecret`, `WebhookId`) com webhooks ligados.
- Chave do Resend + domínio verificado pro e-mail remetente.
- Mídia dos produtos (fotos + vídeo do giro), da listagem CJ.

## Aviso de honestidade
O pagamento **não** é 100% automático sem os **webhooks do PayPal ligados e verificados**. E o
checkout server-side (preço definido no backend, não no navegador) ainda será adicionado junto das
landings — enquanto isso não existir, não publique a landing com preço só no client. Ver [CLAUDE.md](CLAUDE.md) §7.
