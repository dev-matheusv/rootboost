# Plano de desenvolvimento (reavaliado em 27/10/2026)

Norte (RB-53): **o RootBoost faz o processo inteiro e o cliente só aprova.** Toda prioridade abaixo
é medida por quanto trabalho manual ela tira do cliente, sem abrir mão de qualidade e segurança.
Cards no Jira RB; o roadmap por fases segue em `ROADMAP.md`.

## O que já está pronto (base)
Console com login, marcas, perfil da marca com prova, checklist de copy, base de conhecimento,
leitura de anúncios da Meta com regra de parada, publicação real no Instagram e foto no Facebook,
agenda com aprovação, upload de mídia, trocar mídia e apagar, importar reel pronto (um post por
canal, LinkedIn manual), capa do Reels, Reels automático na Página do Facebook, Postgres em todo lugar.

## Ondas (ordem de entrega)

### Onda 1: fechar o ciclo do MedlyCare com qualidade (concluída em 27/10)
| Card | O quê | Por quê agora |
|---|---|---|
| ~~RB-48~~ | ~~Capa do Reels e "compartilhar no feed"~~ (feito 27/10) | |
| ~~RB-57~~ | ~~Reels automático na Página do Facebook~~ (feito 27/10; 1º post real é o teste) | |
| ~~RB-58~~ | ~~Avisos: post que falhou, sync de anúncios e saúde diária da conexão Meta (log + tela; push no RB-37)~~ (feito 27/10) | |
| ~~Limpeza~~ | ~~mp4 provisórios da landing saíram; os 3 rascunhos antigos passaram pro bucket~~ (feito 27/10) | |

### Onda 2: o estúdio (coração da visão: a plataforma gera o conteúdo)
Ordem por dependência: **RB-42** fila de jobs → **RB-43** worker (voz contínua, alinhamento,
montagem com a lógica do `narrado-calmo.mjs`) → **RB-44** portões (ritmo máx. 1,25x, sem câmera lenta
abaixo de ~0,95x, máx. 2 cortes por bloco, fade de áudio) → **RB-50** gravação de tela do produto do
cliente → **RB-47** regressão com os reels aprovados → **RB-51** tela do estúdio → **RB-46** calendário
que propõe. Worker no PC do Matheus agora; **render na nuvem** é o passo seguinte (cliente não pode
depender de máquina local).

### Onda 3: a IA da plataforma, a Root AI (o cérebro, junto com o DMaker)
**Decidido (27/10):** a IA do RootFlow vira produto próprio, a **Root AI**, usada no RootFlow, no
MedlyCare e no RootBoost (RB-17, RB-56). Base: o RAG do RootFlow (documentos da empresa, embeddings,
pgvector, resposta fundamentada) + o molde do assistente do MedlyCare v14 (streaming, tool use,
cartão de confirmação). Fontes: documentos do cliente, conhecimento de marketing acumulado
(`knowledge/`), conectores e internet. **Autonomia por marca:** Copiloto (propõe e espera aprovação,
padrão) ou Piloto automático (aplica dentro de limites e avisa, com teto de gasto, só ações
reversíveis, registro e desfazer). Publicar conteúdo novo sempre com aprovação. Depois, nesta ordem:
- **RB-16** acervo da marca (o cliente joga fotos, vídeos, ideias, documentos; a IA usa como fonte).
- **RB-45** roteiro e textos pela IA (usa playbook + prova da marca).
- **RB-55** assistente por texto e **áudio** ("3 reels por semana, seg/qua/sex 12h") que planeja, gera e agenda.
- **RB-54** importar calendário (txt livre via IA, csv, planilha).
- **RB-34** relatórios automáticos e **RB-35** intervalo de análise configurável.

### Onda 4: anúncios com IA
**RB-31** campanha montada pela IA (tudo pausado, Matheus ativa) → **RB-52** métricas por anúncio e
aprendizado → **RB-25** insights orgânicos. Google Ads depois.

### Onda 5: experiência e avisos
**RB-30/RB-14** PWA e visual no molde do MedlyCare (roxo próprio) → **RB-37** notificação de pendência
(push da PWA primeiro) → **RB-32** aprovação pelo WhatsApp (extensão do Chrome agora, API oficial com
retorno) → **RB-26** "esqueci a senha" por e-mail.

### Onda 6: produto pra clientes e agências
**RB-40** implantação automática (agências com várias marcas, equipe, white label) → **RB-39**
captação pelo WhatsApp (amadurecer antes) → LinkedIn automático → vídeo na Página do Facebook.

## Caminho crítico fora do código (começar já, demora semanas)
- **Nome e domínio:** destravam e-mail transacional (**RB-28**, bloqueia a 1ª venda real), LinkedIn,
  "esqueci a senha" e a política de privacidade.
- **Meta pra clientes (RB-41):** verificação da empresa, revisão do app (vídeo das telas), app em
  modo Live. Depende do domínio (política de privacidade).
- ~~Segurança: 2FA no Facebook, trocar `Orders__ApiKey`, limpar variáveis antigas~~ (feito pelo Matheus em 27/10).

## Qualidade e desempenho (vale em toda onda)
- Toda entrega: testes (unitário + integração em Postgres), CI verde, conferência em produção.
- Integração externa só com campo conferido na doc oficial; segredo só em variável.
- Observabilidade antes de escala: alertas da Onda 1, depois métricas de tempo de resposta e fila.
- Backups do Postgres: conferir a recuperação por ponto no tempo do Railway antes de ter clientes.
- Desempenho: mídia servida pela API está ok pro volume atual; com clientes, avaliar CDN na frente
  do `/media` e render na nuvem com fila.

## Dependências em uma linha
domínio → e-mail (RB-28), LinkedIn, privacidade → revisão Meta (RB-41) → clientes (RB-40) ·
motor de IA (RB-17) → RB-16, RB-45, RB-55, RB-54, RB-34 · worker (RB-43) → RB-44, RB-47, RB-50, RB-51 ·
PWA (RB-30) → push (RB-37).
