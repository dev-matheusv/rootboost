# Como trabalhar no RootBoost

Resumo rápido. O detalhe completo (comandos, release, hotfix, protocolo do Jira) segue o mesmo fluxo do
MedlyCare, documentado no Notion em "RootBoost — Documentação > Fluxo de trabalho".

## Branches
- `main` = produção. Railway (API) e Vercel (landings e Console) deployam sozinhos a partir dela.
- `develop` = integração. Não faz deploy.
- `feat/rb-XX-descricao`, `fix/rb-XX-descricao`, `chore/...`, `docs/...` saem de `develop` e voltam por PR.
- Hotfix sai de `main`, volta pra `main` e depois `develop` recebe `main`.

## Commits
Conventional commits em pt-BR, com o card no escopo:
`feat(RB-12): login com JWT no Console`

## Antes do PR (o mesmo que o CI roda)
```bash
dotnet build && dotnet test
cd apps/rootboost-web && npm ci && npm run lint && npm run build   # se mexeu no front
```

## Merge
Squash merge em `develop`. Release é um PR `develop -> main` chamado `release: ...`.

## Regras da casa
Sem segredo no código, sem payload de API inventado, webhook sempre verificado, copy sem hífen e sem
travessão, decisões registradas no `CLAUDE.md` §0 (o quê e por quê).
