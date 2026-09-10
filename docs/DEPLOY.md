# Deploy — RootBoost (ir ao ar)

Duas peças sobem separadas: **backend** (API .NET, no Railway) e **landing** (estática, em Vercel/Netlify/
qualquer host estático). O banco é SQLite num volume persistente.

> Distinção importante: **go-live técnico** (a loja no ar, alcançável) dá pra fazer hoje. **Vender de
> verdade** exige, além disso: conta PayPal que **receba**, fornecedor **CJ** (ou fulfillment manual dos
> primeiros pedidos) e **mídia** do produto na landing. Não receba dinheiro sem conseguir entregar.

---

## 1. Backend no Railway

1. **Suba o repo no GitHub** (a partir da sua conta):
   ```bash
   git remote add origin https://github.com/dev-matheusv/rootboost.git   # crie o repo antes
   git push -u origin main
   ```
2. No **railway.app** → New Project → **Deploy from GitHub repo** → escolha o repo. Ele detecta o `Dockerfile`.
3. **Volume**: adicione um Volume montado em **`/data`** (sem isso o SQLite some a cada deploy).
4. **Variables** (Settings → Variables) — mínimo pra produção:
   ```
   Supplier=Cj
   Notifier=Resend
   Payments__Verifier=PayPal
   ConnectionStrings__Sqlite=Data Source=/data/rootboost.db
   PayPal__BaseUrl=https://api-m.paypal.com
   PayPal__ClientId=...            (LIVE, não sandbox)
   PayPal__ClientSecret=...        (LIVE)
   PayPal__WebhookId=...           (LIVE, ver passo 6)
   Cj__Email=...    Cj__ApiKey=...
   Resend__ApiKey=...  Resend__FromEmail=pedidos@seu-dominio  Resend__OperatorEmail=voce@...
   Orders__ApiKey=uma-chave-forte
   Cors__AllowedOrigins=https://SUA-LANDING     (o domínio da landing)
   ```
   > Pra um go-live de teste sem CJ/Resend: `Supplier=Mock` e `Notifier=Logging` (não entrega/nao envia email).
5. Deploy. A URL fica tipo `https://rootboost-production.up.railway.app`. Teste `GET /health`.
6. **Webhooks PayPal** (painel do app LIVE): adicione `https://SEU-BACKEND/webhook/payment` (evento
   `PAYMENT.CAPTURE.COMPLETED`) e copie o **Webhook ID** pra `PayPal__WebhookId`. (Rede de segurança;
   o checkout já funciona sem ele via create/capture.)

## 2. Landing (estática)

1. No `landing/rack/build.mjs`, defina `BASE_URL` (domínio da landing) e coloque `og-image.jpg` na pasta.
2. No `_template.html` (ou direto nos arquivos gerados): `CONFIG.apiBase = "https://SEU-BACKEND"`,
   `PAYPAL_CLIENT_ID` (LIVE) no `<script src=...>`, e a **mídia** (vídeo do giro + fotos) no lugar dos placeholders.
3. Rode `node landing/rack/build.mjs`.
4. Publique a pasta `landing/rack` (ou cada idioma) num host estático:
   - **Vercel/Netlify**: aponte pra pasta `landing`, sem build command (é HTML puro). Rota por idioma: `/rack/en`, `/rack/es`, `/rack/pt`.
5. Garanta que `Cors__AllowedOrigins` no backend inclui o domínio da landing.

## 3. Checklist de "vender de verdade"
- [ ] PayPal que **recebe** (conta verificada o suficiente pro seu volume inicial).
- [ ] `catalog.json` com o **VID** real da CJ (senão pedido pago cai em `NeedsHuman`).
- [ ] Landing com **mídia real** (placeholder não converte).
- [ ] Testar **1 pedido real de valor baixo** ponta a ponta antes de mandar tráfego.
- [ ] Política de reembolso/contato visível (reduz disputa no PayPal).

## 4. Alternativa pra hoje: soft-launch
Subir com `Supplier=Mock`/PayPal **sandbox** só pra validar a loja no ar e o funil, sem cobrar ninguém;
trocar pra LIVE quando a conta PayPal receber e a CJ estiver configurada. Assim você "sobe hoje" sem risco.
