# Guia de credenciais — RootBoost

Passo a passo pra você (Claude não cria conta nem digita senha por você). A ordem abaixo é a
mais rápida pra **testar uma venda hoje** sem gastar dinheiro nem depender da CJ.

---

## 1. PayPal Sandbox (prioridade — permite testar a venda inteira, sem dinheiro real)

1. Acesse **developer.paypal.com** e faça login com sua conta PayPal.
2. Menu **Apps & Credentials** → aba **Sandbox** → **Create App**.
   - Dê um nome (ex.: "RootBoost Sandbox") → Create.
3. Na tela do app você verá:
   - **Client ID** (público — vai na landing)
   - **Secret** (privado — vai só em env var no servidor)
   Guarde os dois.
4. Comprador de teste: menu **Testing Tools → Sandbox Accounts**. Já existe um comprador
   `sb-...@personal.example.com`. Clique nele → **View/Edit** pra ver/editar a senha. É com essa
   conta que você "compra" no teste (não usa dinheiro real).
5. (Opcional agora) Webhook: só é preciso pro `/webhook/payment` (rede de segurança). Pro teste
   local do checkout server-side **não precisa** — o fluxo create/capture não depende de webhook.
   Quando for configurar: **Apps & Credentials → seu app → Add Webhook**, URL
   `https://SEU-APP/webhook/payment`, evento `PAYMENT.CAPTURE.COMPLETED`, e copie o **Webhook ID**.

> Me mande o **Client ID** do sandbox (pode colar aqui, é público) e coloque o **Secret** você
> mesmo na env var `PayPal__ClientSecret`. Com isso a gente testa a compra local hoje.

## 2. CJ Dropshipping (pro fulfillment real — pode ficar pra depois do teste)

1. Crie conta em **cjdropshipping.com** e faça login.
2. Menu do perfil → **API** (ou **Authorization → API**) → gere/copiar a **API Key**.
   - `Cj__Email` = e-mail da conta CJ · `Cj__ApiKey` = a key gerada.
3. Ache o produto (rack giratório) no catálogo da CJ, escolha a **variante** e copie o **VID**
   (id da variante). Coloque no `catalog.json` em `products.rack.supplierVariantId`.
   - Prefira uma linha logística com **armazém US/EU** (entrega ~1 semana).
> Enquanto não tiver a CJ, testamos com `Supplier=Mock` (finge o pedido no fornecedor).

## 3. Resend (e-mails de confirmação/rastreio — opcional pro teste)

1. Conta em **resend.com** → **API Keys** → **Create API Key** → copie (`Resend__ApiKey`).
2. **Domains** → adicione seu domínio e configure os registros DNS (pra não cair em spam).
   `Resend__FromEmail` tem que ser desse domínio verificado.
> Enquanto não tiver, use `Notifier=Logging` (loga o e-mail em vez de enviar).

---

## Como testamos a venda (assim que você tiver o PayPal Sandbox)

Modo de teste: `Supplier=Mock`, `Notifier=Logging`, `Payments__Verifier=PayPal`, PayPal em Sandbox.

1. Você coloca o **Client ID (sandbox)** na landing (`PAYPAL_CLIENT_ID`) e `CONFIG.apiBase=http://localhost:5080`.
2. Você seta as env vars `PayPal__ClientId`, `PayPal__ClientSecret`, `PayPal__BaseUrl=https://api-m.sandbox.paypal.com`.
3. Subimos a API local; abrimos a landing; compramos com o comprador de teste do sandbox.
4. Conferimos em `GET /orders`: pedido `PlacedAtSupplier` (via Mock). Fluxo validado ponta a ponta.

Depois trocamos pro real: `Supplier=Cj` + credenciais CJ, PayPal em produção, Resend ligado, e deploy no Railway.
Ver também [.env.example](../.env.example) e o README.
