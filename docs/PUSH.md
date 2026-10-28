# Avisos no celular (push da PWA, RB-37)

O Console instalado no celular (PWA) recebe avisos mesmo fechado. No iPhone, só com o Console adicionado à tela
inicial (Safari, Compartilhar, Adicionar à Tela de Início).

## O que manda aviso hoje

| Quando | Aviso | Toque abre |
|---|---|---|
| Aviso de operação **novo** (post que falhou, sync de anúncio, conexão da Meta) | "Aviso no Console" + a mensagem | Console com a lista de avisos |
| Worker entregou um vídeo do estúdio | "Vídeo pronto pra revisar" + o tema | Estúdio |
| Botão de teste (ao ligar os avisos) | "Teste do Console" | Console |

O mesmo problema repetindo não manda de novo (só o aviso novo). Nada é aprovado nem publicado pelo aviso: ele só
leva pra tela certa.

## Como ligar

1. Gerar o par de chaves VAPID **no seu computador** (não colar no chat nem no repositório):
   ```
   npx web-push generate-vapid-keys
   ```
2. No Railway, serviço da API, criar:
   - `Push__PublicKey` = chave pública
   - `Push__PrivateKey` = chave privada
   - `Push__Subject` = `mailto:` + o seu e-mail (o serviço de push usa pra contato)
3. No celular, abrir o Console instalado, menu, **Avisos no celular**. O navegador pede permissão e chega um teste.

Sem as três variáveis o push fica desligado: o item nem aparece no menu e nada é enviado.

## Como funciona

- `GET /admin/push/config` diz se está ligado e entrega a chave pública.
- `POST /admin/push/devices` inscreve o aparelho (endereço e chaves que o navegador gera); `POST /admin/push/devices/remove` tira.
- `PushNotifier` manda pra todos os aparelhos; nunca derruba quem chamou. Aparelho que o serviço diz que acabou
  (404/410) sai da lista; 5 falhas seguidas também.
- O service worker (`public/sw.js`) mostra o aviso e, ao tocar, volta pra aba do Console já aberta ou abre uma nova.
  Continua sem cache de tela.

## Próximos

Lembrete de post que sai em menos de 24 h ainda sem aprovação; preferência de canal por usuário; WhatsApp (RB-32)
e e-mail (RB-28) como outros canais.
