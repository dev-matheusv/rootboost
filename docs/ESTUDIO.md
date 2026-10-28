# Estúdio: fila de jobs de criativo (RB-42)

A API guarda o pedido de vídeo e o worker local (o PC com DMaker, voz e FFmpeg) **puxa** o trabalho.
Nenhuma porta fica aberta em casa: o worker só faz chamadas de saída.

Base: pacote do marketing `Documents/MedlyCare Marketing/Pacote para o RootBoost - Reels/`,
`00 LEIA PRIMEIRO`, seções 3 (arquitetura) e 4 (contratos).

## Caminho do job

```
Draft ──PUT script──▶ ScriptReady ──POST render──▶ Queued ──worker claim──▶ Rendering
                         ▲                            ▲                         │
                         │ (editar roteiro)           │ fail com retry          │ deliver
                         │                            │ (até 3 tentativas)      ▼
                       Review ◀───────────────────────┴──────────────────── Review
                         │
                         └─ POST publications ──▶ Done   (posts em rascunho; aprovação continua no Console)
Failed: 3 tentativas sem sucesso ou falha sem retry. POST render manda de novo.
```

- Roteiro só muda fora do render. Mudar depois da revisão descarta os arquivos (não batem mais com o texto).
- O worker é dono do job até o prazo (`Studio:LeaseMinutes`, padrão 15). Cada progresso renova. Prazo vencido
  sem notícia: outro worker pode pegar (o PC pode ter desligado no meio).
- A escolha do próximo job usa `FOR UPDATE SKIP LOCKED`: dois workers nunca levam o mesmo.

## Contratos (validados em `ScriptContract`)

```jsonc
// roteiro.json
{ "nome": "combo-8-psicologia",
  "abertura": ["título", "subtítulo"],          // ou null (anúncio)
  "fecho": ["título", "cta"],                   // opcional
  "blocos": [{ "titulo": "...", "sub": "...", "trechos": [["fonte", 1, 4.3, 2]] }] }  // [fonte, início, fim, velocidade?]

// falas.json: uma frase por segmento
["fala da abertura?", "fala do bloco 1", "...", "fala final"]   // blocos + 1, +1 com abertura
```

Recusa (HTTP 422 com a lista `errors`): contagem de falas errada, trecho com fim antes do início, velocidade
zero, texto vazio, travessão ou meia-risca em título, subtítulo ou fala.

## Rotas do Console (login do Console)

| Rota | O que faz |
|---|---|
| `POST /admin/brands/{key}/creative-jobs` `{ kind: Organic\|Ad\|Prospecting, theme, voiceKey? }` | cria o job |
| `GET /admin/brands/{key}/creative-jobs[/{id}]` | lista ou detalhe (com roteiro, falas, avisos e arquivos) |
| `PUT .../{id}/script` `{ roteiro, falas }` | grava o roteiro (422 se fora do contrato) |
| `POST .../{id}/render` | manda pra fila |
| `POST .../{id}/publications` `{ scheduledFor, instagram?, facebook?, linkedIn? }` | cria os rascunhos com os arquivos |

Papéis dos arquivos: `video-instagram` (Instagram, com `cover-instagram` como capa do Reels) e `video`
(Facebook e LinkedIn; sem ele, usa o do Instagram). Outros papéis (ex.: `grade`) ficam só no job.

## Rotas do worker (cabeçalho `X-Worker-Key`)

Liga com a variável `Studio__WorkerKey`. Sem ela, `/worker/*` responde 404.

| Rota | Resposta |
|---|---|
| `POST /worker/jobs/claim` `{ workerId }` | 200 com `{ id, brandKey, kind, theme, voiceKey, seed, attempts, roteiro, falas }` ou 204 (nada na fila) |
| `POST /worker/jobs/{id}/progress` `{ workerId, percent, note? }` | 204; 409 se o job não é mais deste worker |
| `POST /worker/jobs/{id}/deliver` `{ workerId, outputs: [{ role, url }], warnings?, seed? }` | 200, job em `Review`; links precisam ser https |
| `POST /worker/jobs/{id}/fail` `{ workerId, error, retry }` | 200; com `retry` volta pra fila até 3 tentativas |
| `POST /worker/media/{brandKey}` (multipart `file`) | 201 com `url` pública do bucket, pra usar na entrega |

## Próximos cards

RB-43 worker (porta `falar-continuo.py`, `alinhar.py` e a montagem do `narrado-continuo.mjs`) → RB-44 portões
(0 palavra sem par, fator de velocidade, duração) → RB-51 tela do estúdio no Console.
