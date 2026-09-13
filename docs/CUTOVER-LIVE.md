# Runbook — Cutover LIVE (soft-launch → vender de verdade)

Passo a passo pra virar de **sandbox + Mock** pra **LIVE** (PayPal Business real + fulfillment CJ real).
Fazer tudo de uma vez e testar com 1 pedido real de valor baixo. Rollback no fim.

> ⚠️ Regra de ouro: **nunca** deixar `Supplier=Cj` com PayPal em **sandbox** — pagamento fake dispararia
> pedido REAL/pago na CJ. LIVE = PayPal LIVE **e** `Supplier=Cj` **juntos**.

## Pré-requisitos (do humano)
- **CNPJ** ativo → **conta PayPal Business** (aba **Live** liberada).
- **CJ**: `Cj__Email` + `Cj__ApiKey` + o **VID** da variante do organizador **com estoque US**.
- (Opcional no lançamento) **Resend** (chave + domínio verificado) e **Meta** (Pixel + token CAPI).

## 1. VID da CJ → catalog.json
- Opção A: no CJ, **Connect / Adicionar aos meus produtos** o organizador; o VID da variante fica em **My Products**.
- Opção B (comigo): puxamos via API com a `Cj__ApiKey` (rodo o comando local no dia, confirmando o endpoint na doc CJ).
- Escolher a **cor com estoque US** (a Bege estava 0). Depois editar `catalog.json`:
  `"carorganizer".supplierVariantId = "<VID>"` → `git commit` + `git push` (Railway redeploya).

## 2. App PayPal LIVE (conta Business)
- developer.paypal.com (logado na Business) → **Apps & Credentials → aba Live → Create App**.
- Copiar **Client ID (Live)** e **Secret (Live)**.
- **Webhooks**: no app Live, **Add Webhook** → URL `https://rootboost-production.up.railway.app/webhook/payment`,
  evento `PAYMENT.CAPTURE.COMPLETED` → copiar o **Webhook ID**.

## 3. Variáveis no Railway (backend) — modo LIVE
```
Supplier=Cj
Notifier=Resend
Payments__Verifier=PayPal
PayPal__BaseUrl=https://api-m.paypal.com
PayPal__ClientId=<LIVE Client ID>
PayPal__ClientSecret=<LIVE Secret>
PayPal__WebhookId=<LIVE Webhook ID>
Cj__Email=<email CJ>
Cj__ApiKey=<API key CJ>
Resend__ApiKey=<chave>   Resend__FromEmail=pedidos@seu-dominio   Resend__OperatorEmail=voce@...
Orders__ApiKey=<chave forte>
Cors__AllowedOrigins=https://rootboost.vercel.app
```
(Volume `/data` já configurado.) Salvar → Railway redeploya. Testar `GET /health` = ok.

## 4. Landing (Vercel) — Client ID LIVE
- Em `landing/build.mjs`: `PAYPAL_CLIENT_ID = "<LIVE Client ID>"` (o Secret **não** vai na landing).
- `node landing/build.mjs` → `git commit` + `git push` (Vercel republica).

## 5. CJ webhook de rastreio
- No painel CJ, apontar o webhook de status/rastreio para `https://rootboost-production.up.railway.app/webhook/cj`.

## 6. (Opcional) Ligar Meta Pixel + CAPI
- Railway: `Tracker=Meta`, `Meta__PixelId=...`, `Meta__AccessToken=...`.
- `landing/build.mjs`: `META_PIXEL_ID = "<Pixel ID>"` → `node landing/build.mjs` + push.

## 7. Teste real (1 pedido de valor baixo)
- Abrir `https://rootboost.vercel.app/car/pt/` → comprar de verdade (valor baixo, seu cartão).
- Conferir `GET /orders` (header `X-Api-Key`): status `PlacedAtSupplier` + `supplierOrderId` real da CJ.
- Confirmar o pedido no painel da CJ (pago) e o e-mail de confirmação (Resend).
- Fazer reembolso do teste pra você mesmo.

## 8. Rollback (se algo der errado)
- Railway: `Supplier=Mock`, `Payments__Verifier=Test`, `PayPal__BaseUrl=https://api-m.sandbox.paypal.com`.
- Isso volta pro modo seguro na hora (sem tocar em CJ/dinheiro real).

## Checklist de segurança
- [ ] Assinatura do webhook PayPal verificada (WebhookId setado).
- [ ] Idempotência ok (pedido não duplica).
- [ ] Política de reembolso/contato visível na landing (reduz disputa).
- [ ] Acompanhar os primeiros pedidos manualmente (`/orders`) até confiar no fluxo.
