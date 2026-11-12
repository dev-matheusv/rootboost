# Ligar o Nativa Ads de verdade (checklist do Matheus)

O código está pronto e testado em modo Fake. O que falta é fora do código. Ordem pensada pra nada gastar sem você ver.

## 1. Na Meta (Gerenciador de Negócios, business.facebook.com)
1. **Configurações do negócio → Usuários do sistema →** o usuário `rootboost`.
2. **Atribuir ativos:** a conta de anúncios do MedlyCare (controle total), a Página do Facebook e a conta do Instagram.
3. **Gerar novo token** pro app RootBoost com as permissões:
   `ads_management`, `ads_read`, `pages_read_engagement`, `pages_show_list`, `business_management`.
   Validade: "nunca expira" (token de usuário do sistema).
4. Copie o token só pro Railway (passo 2). Nunca cole no chat.
5. Confira se a conta de anúncios tem **forma de pagamento** válida (a campanha nasce pausada, mas a Meta exige).

## 2. No Railway (serviço da API)
| Variável | Valor |
|---|---|
| `Meta__AdsAccessToken` | o token novo do passo 1 |
| `Ads__Creator` | `Meta` |
| `Ads__Reader` | `Meta` (provavelmente já está) |
| `Ads__Accounts__0__Brand` | `medlycare` (provavelmente já está) |
| `Ads__Accounts__0__AccountId` | `act_...` da conta (provavelmente já está) |
| `Ads__Accounts__0__PixelId` | `3321676744699393` (só se for usar objetivo "Cadastros") |

A Página e o Instagram vêm das variáveis `Social__Accounts__*`, que já existem pra publicação.

## 3. Release
Dar o ok pra eu levar `develop` pra `main` (Nativa Ads: PRs #184 e #186). O Railway e a Vercel publicam sozinhos.

## 4. Primeiro teste (sem gastar)
1. No Console: **Campanhas → Campanha com a IA**, escolha um vídeo pronto e clique em **Montar campanha**.
2. Confira texto, público e orçamento. **Aprovar e criar pausada**.
3. Abra o Gerenciador de Anúncios: devem aparecer campanha, conjunto e anúncio **pausados**. O anúncio passa pela
   revisão da Meta (pode levar algumas horas).
4. Só ative quando quiser começar a gastar. Daí em diante o Console mostra o que fazer (aguardar, manter, subir
   orçamento, trocar o vídeo, pausar) e tem o botão de pausar.

## Se der erro
A campanha fica "Falhou" com a mensagem da própria Meta (permissão faltando, conta sem pagamento, vídeo recusado).
Corrija e clique em **Aprovar** de novo: ela continua de onde parou, sem duplicar.
