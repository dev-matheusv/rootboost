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

## IA escreve o roteiro (RB-45)

Na tela, **Novo vídeo** já pede o roteiro à IA: ela usa o perfil da marca (público, tom, termos proibidos, trocas de
palavra), as provas ativas (só afirma o que está ali), frases de clientes, o playbook de copy e as regras do formato.
Devolve 3 aberturas alternativas e, por bloco, a **cena** (o que gravar). A resposta passa pelos mesmos portões; se não
passar, a IA tenta de novo uma vez com os erros. A pessoa ajusta em texto, trecho por trecho; os arquivos JSON ficam
em "Avançado". Cada chamada grava modelo, tokens e custo em `AiUsage`.

Ligar em produção (Railway): `Ai__Mode=Claude` e `Anthropic__ApiKey` (chave da API da Anthropic, só no Railway).
Opcional: `Ai__Model` (padrão `claude-opus-5-5`) e `Ai__Effort` (padrão `medium`). **Modelo por finalidade**
(controle de custo): `Ai__Models__studio-script`, `Ai__Models__onboarding-chat`, `Ai__Models__screen-recording`
(ex.: `claude-haiku-5-5`). O custo dos últimos 30 dias aparece na Visão geral e em `GET /admin/ai/usage`. Sem isso fica `Off` e a tela
avisa. `Ai__Mode=Fake` dá um roteiro fixo pra testar sem custo.

Enquanto a gravação automática da tela (RB-50) não existe, cada bloco escrito pela IA vem com um trecho
`gravar-bloco-N` e a duração estimada da fala: é a lista do que gravar.

## Ordem do worker: falhar cedo e barato (29/10)

1. **Pré-checagem** (segundos): conta de demonstração e app da gravação respondendo; mídias do acervo no job (a API já
   falha no claim se alguma foto foi barrada pela proteção de dado pessoal).
2. **Voz primeiro** (`narrado-calmo --so-voz`): sintetiza e alinha; lê `sem_par` em `voz-continua/<voz>-p32-s<semente>/alinhamento.json`.
   Palavra sem par troca a semente na hora (até 3) sem montar nada.
3. Só com a voz aprovada: gravação de tela, mídia do acervo e montagem (o narrado-calmo reaproveita a voz do cache).
Próximo passo (pedido): gerar 2 sementes e parar em "Revisão da voz" no Console antes de montar.

## Vídeo a partir do acervo (RB-16)

A IA do roteiro recebe a lista do acervo da marca (vídeos e fotos com descrição, até 60) e escolhe, por bloco, a mídia
que mostra a cena (campo `midia`). O trecho vira `["acervo-<id>", 0, segundos]`; sem mídia fica `gravar-bloco-N`
(gravação de tela). No claim, a API manda `media: [{ id, url, isVideo }]` só das mídias da própria marca citadas no roteiro.
O worker baixa cada uma para `brutos/acervo-<id>/acervo-<id>.mp4`: vídeo é normalizado (H.264, 30 fps, sem som, até 1080 de
largura); foto vira trecho com aproximação lenta (zoompan, 10%) com 2 s de folga, porque o pipeline só desacelera foto pra
caber na fala. Foto sem descrição não entra na lista (a IA não enxerga a imagem no roteiro, só a descrição).
Custo: zero de IA além do roteiro; a descrição da foto é feita uma vez no upload (`media-describe`, ~US$ 0,01). Qualquer foto (JPG, PNG, WebP, até 40 MB, de pé conforme o EXIF) é convertida no servidor pra JPEG de até 1568 px antes de ir pra IA (SkiaSharp, MIT): o limite de 5 MB da API vale pra imagem em base64, e foto de celular passava disso. Erro do provedor vira 502 com mensagem clara e o motivo vai pro log.

## Dado pessoal no acervo e na gravação (29/10)

- **Descrever com a IA** também marca dado pessoal (nome, CPF, RG, telefone, e-mail, endereço, nascimento, prontuário,
  assinatura, rosto de cliente): `nenhum`, `ficticio` (bate com os dados de demonstração da marca), `real` ou `incerto`,
  com as áreas em fração da imagem. O item ganha o selo "Contém dado pessoal".
- **Versão borrada automática** (mosaico grosso + desfoque, sem volta) em `<chave>-borrada.jpg`. A pessoa ajusta em
  "Ajustar borrado" (arrasta na foto, vê o original com login). Real ou dúvida: o **link público só entrega a borrada**
  (sem ela, 404) e o original só sai com login (`/media/{id}/original`).
- **Trava**: a IA do roteiro não vê mídia bloqueada; o render recusa roteiro com ela e diz o motivo; o worker só recebe
  mídia liberada. Foto ainda não checada (descrita antes disto) fica fora até passar pela IA.
- **Regra por marca** (`Brands.Privacy`): `Blur` (padrão: real entra só borrado) ou `FictionalOnly` (real não entra
  nem borrado). MedlyCare = `FictionalOnly`, com os dados fictícios conhecidos em `Brands.FictionalMarkers`.
- **Gravação de tela**: a IA de cada passo também diz se a tela mostra dado de pessoa real; se sim, a gravação para,
  os quadros são apagados e o job falha sem tentar de novo ("grave com a conta de demonstração").
- Limite: vídeo do acervo não é checado (a IA não assiste vídeo); vale a descrição da pessoa.

## Vozes da marca (RB-59)

Página **Vozes** no Console: a pessoa grava a própria voz (no celular ou no computador, pelo navegador) ou cria uma voz do
banco (gênero, idade e tom). A gravação começa com a **autorização falada** ("Eu, Fulana, autorizo a Marca a criar e usar
uma voz com IA a partir desta gravação, somente nos vídeos e posts da marca") e segue com um texto de leitura (~30 s no
total). Quem autorizou, quando e o texto ficam em `NarrationVoices`. Gravação e amostras ficam **privadas** no bucket
(`voices/<marca>/...`): só saem pela API com login (Console) ou com a chave do worker. Fila igual à do estúdio:
o worker pega (`/worker/voices/claim`), baixa a gravação, roda `dmaker voice clone <key> <arquivo> --brand <marca>` (ou
`voice design --instruct`), gera duas amostras com `falar-continuo.py` (sementes 1 e 2) e entrega. A pessoa ouve e
escolhe; a semente escolhida vira a padrão dos jobs com essa voz. **Trava de marca:** job com voz de outra marca é
recusado na API, e o worker soma as vozes da plataforma às particulares do `worker.env` antes de narrar. O id da voz
leva a marca (`<marca>-<nome>-xxxx`). Custo: zero de IA paga (DMaker local).

## Rotas do Console (login do Console)

| Rota | O que faz |
|---|---|
| `POST /admin/brands/{key}/creative-jobs` `{ kind: Organic\|Ad\|Prospecting, theme, voiceKey? }` | cria o job |
| `GET /admin/brands/{key}/creative-jobs[/{id}]` | lista ou detalhe (com roteiro, falas, avisos e arquivos) |
| `PUT .../{id}/script` `{ roteiro, falas }` | grava o roteiro (422 se fora do contrato) |
| `POST .../{id}/write-script` `{ instructions? }` | a IA escreve roteiro e falas (409 com a IA desligada) |
| `GET /admin/brands/{key}/ai/status` | se a IA está ligada |
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

## Worker local (RB-43): `tools/studio-worker/`

Orquestra o pipeline que já funciona (`narrado-calmo.mjs`, com `falar-continuo.py` e `alinhar.py`), sem reescrever
voz nem montagem. A cada rodada:

1. pega o próximo job (`claim`);
2. grava `roteiro.json` e `falas.json` em `projetos/<nome>/` (se já existia um roteiro diferente, guarda cópia
   `.antes-<data>`);
3. roda o pipeline sem shell, mandando a última linha do log como progresso a cada 30 s (renova o prazo);
4. **portão de palavras sem par** (etapa 5 do pacote): lê a linha "N sem par no Whisper"; acima de 0 devolve pra
   fila e a próxima tentativa usa a **semente seguinte** (até 3); sem a linha, falha pra um humano olhar;
5. **portão de velocidade** do `narrado-calmo` ("portao: trecho acima de 1,25x"): falha sem nova tentativa,
   porque outra semente não resolve (precisa mexer nos trechos do roteiro);
6. sobe `finais/<nome>-narrado-instagram.mp4` (+ `.jpg` da capa), `<nome>-narrado.mp4` e a grade de revisão,
   e entrega com os papéis `video-instagram`, `cover-instagram`, `video` e `grade`, mais os avisos do log.

Pré-requisitos no PC: o mesmo ambiente do `narrado-calmo.mjs` (DMaker com o extra de voz, `pip install -e .[voice]`,
que inclui o `audioseal` da marca d'água nas falas; FFmpeg; faster-whisper). O worker chama a cópia viva das
ferramentas (`STUDIO_TOOLS`), então mudanças do marketing no pipeline valem na hora. Desde 28/10 o cache da voz
também considera o dicionário de pronúncia da marca (`voz-continua/<voz>/lexico.json`): mudar uma pronúncia
ressintetiza mesmo com o texto igual.

Como ligar no PC:

```
cd tools/studio-worker
copy worker.env.example worker.env   # preencher ROOTBOOST_WORKER_KEY (a mesma do Railway)
node worker.mjs                      # fica rodando; --once faz uma rodada só
```

No Railway: criar `Studio__WorkerKey` com um valor longo e aleatório (só o Matheus define; nunca no repo nem no chat).

`fake-pipeline.mjs` simula o pipeline (`FAKE_UNMATCHED`, `FAKE_FAIL=portao`) pra testar o worker sem DMaker.

## Próximos cards

RB-44 portões que faltam (duração por formato, checklist de copy nas falas) → RB-47 regressão com os reels
aprovados → RB-50 gravação de tela do produto do cliente → RB-45 roteiro pela IA.
