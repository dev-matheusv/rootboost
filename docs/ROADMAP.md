# RootBoost — Roadmap

Documento vivo. Espelho no Notion: página **RootBoost — Roadmap** (dentro de "RootBoost — Documentação").
Cards no Jira: projeto **RB** (nativalabs). Cada item abaixo vira um card; o número entra aqui quando existir.

> **Mudança de rumo (2026-10-22):** antes valia "não construir plataforma antes de ter dado de venda".
> O dono decidiu construir a plataforma agora, porque ela já atende mais de uma frente (loja, livros,
> marketing do MedlyCare, conteúdo de terceiros) e o fluxo de desenvolvimento está rendendo. O registro
> completo da decisão está no `CLAUDE.md` (§0). A loja continua no ar e vendendo; nada aqui mexe nela
> sem flag.

## O que o RootBoost vira

Um **estúdio de marketing** com um ciclo só: **criar → publicar → medir → otimizar**, servindo várias
marcas (RootBoost loja, MedlyCare, livros, clientes). Cada etapa é um módulo com porta própria, modo
Fake pra rodar sem credencial e modo Desligado por padrão em produção.

```
            ┌──────────── Console (web) ─────────────┐
            │ visão geral · pedidos · produtos ·      │
            │ campanhas · criativos · agenda · IA     │
            └───────────────┬─────────────────────────┘
                            │ JWT
┌───────────────────────────▼──────────────────────────────────┐
│ RootBoost.Api (.NET 9, Railway)                               │
│  Loja (pedido, CJ, Stripe)  · Ads (Meta, Google)  · Estúdio   │
│  Publicação (Postiz)        · IA (Claude, advisor) · Insights │
└───────┬──────────────────────────┬───────────────────────────┘
        │ fila de jobs (pull)      │ APIs externas
┌───────▼─────────┐        Meta Marketing API · Google Ads API ·
│ Worker local    │        Postiz · Stripe · CJ · Resend
│ DMaker + voz    │
│ (PC do Matheus) │
└─────────────────┘
```

**Por que o render roda num worker local que puxa jobs:** o DMaker com voz clonada é pesado (CPU, modelos
de voz, FFmpeg) e hoje roda no PC. O worker **puxa** o trabalho da API, então não precisa abrir porta
nenhuma na rede de casa, e trocar o PC por uma máquina na nuvem depois é só mudar onde o worker roda.

## Fases

### Fase 0 — Fundação (em andamento)
- [x] Roadmap e registro da decisão (este arquivo, `CLAUDE.md` §0).
- [x] Fluxo de trabalho igual ao MedlyCare: `main` (produção), `develop` (integração), `feat/rb-XX-...`,
      PR com CI obrigatório, squash merge, conventional commits com o card no escopo.
- [x] CI no GitHub Actions: build e testes do backend, lint, tipos e build do front.
- [ ] Documentação no Notion e projeto RB no Jira.

### Fase 1 — Console (painel com login)
- [ ] **Postgres no lugar do SQLite** (padrão da casa; libera pgvector e analytics). Migração dos pedidos
      e visitas existentes, sem perder nada.
- [x] **Login com JWT** emitido pelo backend, no mesmo molde do RootFlow (`JwtTokenGenerator`, 8 h).
      Usuário dono criado por configuração. A API key continua valendo pra scripts.
- [ ] **Marca como conceito desde o início** (`Brand`): pedidos, produtos, campanhas e criativos
      pertencem a uma marca. Evita refazer o modelo quando entrar o marketing do MedlyCare.
- [x] **Front `apps/rootboost-web`** (esqueleto, login, Visão geral e Pedidos) no molde do front do MedlyCare (`C:/SFA_Sistema/apps/web`): Vue 3,
      Quasar 2, Pinia, vue-i18n, axios, vitest, PWA. Paleta própria do RootBoost. Deploy em projeto
      próprio na Vercel.
- [ ] Telas: Visão geral (visitas, pedidos, receita, conversão por produto), Pedidos, Produtos,
      Crescimento (o `GrowthPlanner` que já existe) e Tráfego (o plano em dry-run que já existe).

### Fase 2 — Anúncios, só leitura
- [ ] **Meta Ads**: `ICampaignInsightsSource` real (Marketing API, insights por campanha, conjunto e anúncio).
- [ ] **Google Ads**: mesma porta, implementação com GAQL. Pré-requisito do dono: developer token
      aprovado, conta MCC, OAuth e customer id.
- [ ] Snapshot diário das métricas no Postgres e telas de campanha com custo, ROAS, CPA e CTR.
- [ ] Nenhuma escrita nas contas de anúncio nesta fase.

### Fase 3 — Estúdio (criar)
- [ ] Fila de jobs de criativo na API e worker local que roda o DMaker (render, voz clonada, legendas).
- [ ] Voz em fala contínua (lição do MedlyCare: síntese frase a frase soa picotada). Pedido ao Douglas
      no Jira DMK; enquanto isso, o worker chama o OmniVoice direto.
- [ ] Regras de formato por canal (zona segura do Reels: margem 0.14, legenda em y=0.753).
- [ ] Biblioteca de criativos com proveniência (de onde veio, custo, qual hook, qual produto).
- [ ] Higgsfield como gerador de imagem atrás de porta (`IAssetGenerator`), com orçamento.

### Fase 4 — Publicação
- [ ] Postiz como agendador, atrás de porta (`ISocialPublisher`).
- [ ] Calendário editorial no Console. **Nada publica sem aprovação post a post.**

### Fase 5 — IA que otimiza
- [ ] Módulo `Ia` portado do RootFlow, no molde do MedlyCare v14 (e o painel do assistente, em Vue, reaproveitado no Console): `IModeloLinguagem` com tool use e
      streaming, implementações Claude e Fake, `Ia__Modo=Desligado` por padrão, uso e custo gravados.
- [ ] Advisor lê insights e propõe ações (escalar, pausar, trocar criativo, testar hook).
- [ ] **Ação só grava com confirmação humana**: proposta assinada, uso único, válida por 15 min
      (mesmo padrão do assistente do MedlyCare). Actuators de Meta e Google atrás de `Traffic:Live=true`.
- [ ] Autopilot agendado, começando em dry-run.

### Fase 6 — Produto
- [ ] Várias contas (workspaces) no molde do RootFlow, cobrança, onboarding.
- [ ] Estúdio aberto para pessoas comuns (ideia de `docs/VIDEO-DMAKER.md`), conversando com o Douglas.

## Fronteira com o DMaker

O DMaker é do Douglas e tem o próprio Jira (**DMK**). Regra: mudança no DMaker vira card no DMK e quem
mexe é ele; o RootBoost só consome por CLI ou MCP e guarda os specs do nosso lado
(`tools/video/`). Mudança no RootBoost vira card no **RB**.

## Credenciais que só o dono fornece (env var, nunca no chat)
Meta Business (app, token de sistema, ad account), Google Ads (developer token, OAuth client, refresh
token, customer id, login customer id), Postiz (API key), Claude (chave ou Bedrock), storage de mídia.
