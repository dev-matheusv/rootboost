# BRIEF DE BUILD — Plataforma de Dropshipping Automatizado (colar no Claude Code)

> Cole este arquivo inteiro como prompt no Claude Code, dentro de uma pasta vazia
> (ou rode `claude` e cole). Desenvolva de forma incremental, commitando por etapa.

## Objetivo
Construir um sistema onde: **cliente compra numa landing page → o pedido é enviado
automaticamente ao fornecedor (CJ Dropshipping) via API, sem nenhum passo manual → o
rastreio volta por webhook e o cliente é notificado.** Começa com 1 produto ("rack de
tempero giratório"), mas a arquitetura precisa suportar **N produtos e vários idiomas
(EN/ES/PT)** só com configuração + uma nova landing. Vender em **USD/EUR**.

## Stack
- **.NET 8**, C#, minimal API. Clean Architecture (o dono do projeto trabalha assim).
- **EF Core + SQLite** pra persistência (zero custo, sobrevive a restart).
- **Docker** pra deploy. HttpClient tipado pras integrações.
- Landings: **HTML/CSS/JS estático** (sem framework pesado), uma por produto/idioma.

## Estrutura de solução (Clean Architecture)
```
/src
  /Domain          -> Order (entidade), Address (VO), OrderStatus (enum). Sem deps externas.
  /Application     -> casos de uso (PlaceOrderOnPayment, AttachTracking), interfaces
                      (ISupplierClient, IPaymentVerifier, IOrderRepository, INotifier).
  /Infrastructure  -> CjClient (ISupplierClient), PayPalVerifier/StripeVerifier
                      (IPaymentVerifier), EF repo (IOrderRepository), EmailNotifier (INotifier).
  /Api             -> host minimal API, endpoints de webhook, DI, config.
/landing
  /rack/en/index.html  /rack/es/index.html  /rack/pt/index.html   (mesmo template, strings por idioma)
/content
  /rack/scripts.md     (roteiros EN/ES/PT + copy da landing — placeholders, o dono preenche)
/tests
  Application.Tests    (unitários dos casos de uso)
Dockerfile
README.md
```

## Endpoints (Api)
- `POST /webhook/payment` — recebe evento do **PayPal** (`PAYMENT.CAPTURE.COMPLETED`).
  Fluxo: **verificar assinatura** → normalizar pra um `PaymentEvent` (pago?, paymentId,
  productKey via SKU/custom_id, email, endereço de entrega completo) → **idempotência**
  (ignorar se paymentId já processado) → resolver `productKey → cjVariantId` pelo catálogo
  → `ISupplierClient.CreateOrder(...)` → salvar Order(status=PLACED) → notificar cliente.
- `POST /webhook/stripe` — mesmo contrato, para quando houver LLC + Stripe
  (`checkout.session.completed`, verificar com signing secret). Deixar implementado atrás
  de feature flag.
- `POST /webhook/cj` — recebe status/rastreio da CJ → `AttachTracking` → notifica cliente.
- `GET /orders` — listagem simples (proteger com um header de API key). Vira dashboard depois.
- `GET /health`.

## Integração CJ Dropshipping (fornecedor)
- Base: `https://developers.cjdropshipping.com/api2.0/v1/`
- Auth: `POST authentication/getAccessToken` com `{ email, apiKey }` → `data.accessToken`.
  Cachear o token e **renovar quando expirar**. Header nas chamadas: `CJ-Access-Token`.
- Criar pedido: `POST shopping/order/createOrderV2` com o endereço do cliente + `products[{vid, quantity}]`.
- **Importante:** consultar a doc oficial da CJ pra montar o payload EXATO do createOrderV2
  e do webhook de rastreio. Onde a doc não estiver clara, deixar o client tipado com um
  `// TODO: confirmar campo na doc CJ` — **não inventar campos**.
- Escolher linha logística com **armazém US/EU** (entrega ~1 semana) quando disponível.

## Integração PayPal (pagamento)
- Checkout na landing com **PayPal Smart Buttons**; captura server-side ou via webhook.
- Habilitar **webhooks do PayPal** apontando pro `/webhook/payment`.
- **Verificar a assinatura** via `/v1/notifications/verify-webhook-signature` (ou SDK)
  ANTES de confiar no evento. Sem verificação = qualquer um dispara pedido grátis.

## Config de catálogo (o que torna multi-produto/idioma trivial)
Arquivo `catalog.json` (ou appsettings) tipo:
```json
{
  "products": {
    "rack":      { "price": "34.99", "currency": "USD", "cjVariantId": "TODO", "langs": ["en","es","pt"] },
    "chopper":   { "price": "29.99", "currency": "USD", "cjVariantId": "TODO", "langs": ["en","es"] },
    "petfeeder": { "price": "27.99", "currency": "USD", "cjVariantId": "TODO", "langs": ["en","es"] }
  }
}
```
Adicionar produto = 1 entrada aqui + 1 pasta de landing. Nada de código novo.

## Landing pages
- Template único, conversion-focused, responsivo, com **sticky CTA no mobile**,
  seções: hero (com vídeo do produto), problema, benefícios, como funciona (3 passos),
  provas sociais, garantia, FAQ, checkout PayPal.
- Strings por idioma (en/es/pt) em arquivos separados; a landing carrega o idioma da pasta.
- Placeholders claros pra: `PAYPAL_CLIENT_ID`, mídia do produto (imagens + vídeo do giro),
  preço/moeda (ler do catálogo). Passar `productKey` no pedido PayPal (custom_id) pro backend saber o que despachar.
- Já existe uma landing inicial (`rack-landing-en.html`) e um esqueleto de backend
  (`FulfillmentEngine_Program.cs`) — **usar como base se estiverem na pasta**.

## Segurança e robustez
- Verificação de assinatura em TODOS os webhooks. Idempotência por paymentId.
- Nunca commitar segredos: usar **variáveis de ambiente / user-secrets**.
- Retry + log estruturado se a chamada à CJ falhar (não perder pedido pago).
- Tratar erro de estoque/variante inválida sem derrubar o processo.

## Notificações
- `INotifier` com uma impl simples via SMTP ou API tipo Resend (placeholder de chave).
  E-mails: "pedido confirmado" e "seu rastreio".

## Deploy
- `Dockerfile` funcional. README com passo a passo pra **Render/Railway/Fly** (tier grátis),
  listando as env vars necessárias. SQLite em volume persistente.

## Testes
- Unitários do `PlaceOrderOnPayment`: idempotência, mapeamento productKey→variante,
  chamada ao `ISupplierClient` mockado, persistência. Mockar tudo que é externo.

## Critérios de aceite
1. `dotnet build` e `dotnet test` passam.
2. Subindo local, um POST de exemplo em `/webhook/payment` (payload de teste) cria uma
   Order e chama o `ISupplierClient` (mock em dev) — comprovável em `GET /orders`.
3. Trocar/adicionar produto é só editar `catalog.json` + criar pasta de landing.
4. Nenhum segredo hardcoded. Assinatura de webhook verificada. Idempotência funcionando.
5. README explica: env vars, como rodar, como fazer deploy, e o que falta o humano preencher.

## O que SÓ o humano fornece (deixe como TODO/env, não tente obter)
- `CJ_EMAIL`, `CJ_API_KEY` e o **VID** (id de variante) do rack (da conta CJ).
- `PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`, `PAYPAL_WEBHOOK_ID`.
- Mídia do produto (fotos + vídeo do giro) — vem da listagem CJ.
- Host escolhido pro deploy.

## Restrições (não faça)
- Não criar contas, não gerar chaves, não inventar payloads de API que você não confirmou.
- Não prometer que o pagamento é 100% automático sem os **webhooks do PayPal ligados**
  (documente isso no README).
- Comece pequeno e incremental: Domain+Application+testes → CjClient → endpoints →
  landing → deploy. Commit a cada etapa.
```
```
