# Gravação de tela automática (RB-50)

Objetivo: o mesmo que fizemos à mão com o MedlyCare (`gravar-reel.mjs`), só que pra qualquer cliente e sem
roteiro de gravação escrito por gente. A IA escreve o roteiro com a **cena** de cada bloco (RB-45); aqui a IA
**executa** a cena no sistema do cliente e o worker grava.

## Como funciona

```
job com trechos "gravar-bloco-N"  ──►  worker abre o Chrome (sem janela, 540x600 a 2x)
                                         │  entra no sistema (conta de demonstração do PC)
                                         ▼
          para cada bloco: loop curto ── foto da tela + lista de elementos clicáveis
                                         │  POST /worker/ai/next-action  (a API chama a IA)
                                         ▼  resposta: clicar | digitar | colar-imagem | rolar | esperar | comecar | pronto
                                         executa no Chrome, grava os quadros (screencast)
                                         ▼
          brutos/<nome>-bloco-N.mp4 + troca o trecho no roteiro por [bruto, 0, duração]
                                         ▼
                              pipeline de sempre (voz, alinhamento, montagem)
```

- **A chave da IA fica só no Railway.** O worker manda a foto e recebe o próximo passo; cada chamada grava custo em
  `AiUsage` (finalidade `screen-recording`).
- **Limites por cena:** no máximo 40 passos e 240 s; passou disso, o bloco falha e vai pra revisão humana.
- **Preparo fora do vídeo:** cada cena abre uma sessão nova na tela inicial. A IA navega até o ponto de partida e responde
  `comecar`; os quadros anteriores são descartados. A espera pela IA também sai da linha do tempo.
- **Diagnóstico:** cada passo vai pra `passos.txt` na pasta da gravação; quando falha, o motivo vai pro erro do job e a
  tela final pra `falhou.jpg`.
- **Imagem e anexo:** `colar-imagem` cola `tools/studio-worker/demo/imagem-demo.png` no campo, e qualquer botão que abre
  a escolha de arquivo recebe essa mesma imagem. Nunca sai um arquivo de verdade do PC.
- **Cursor desenhado na página** e marcas de tempo, como no gravador do MedlyCare.

## Guia de uso do sistema

Em Perfil da marca, a pessoa cola a documentação do sistema e a IA propõe um guia (`POST /product/screen-guide/draft`). A pessoa confere e salva (`PUT /product/screen-guide`). O guia vem no job e vai junto de cada cena da gravação. Nada é salvo sem confirmação, e o texto colado entra como dado, nunca como instrução.

## Preparo da conta

`guias/<marca>.preparo.json` lista o que a conta de demonstração precisa ter antes da gravação. Cada item tem `quando` (expressão regular sobre as cenas do job) e `cena`. O worker roda esses itens fora do vídeo, com a mesma IA e as mesmas travas. Se falhar, o job para com o motivo e nada é gravado.

## Reel de passo a passo (06/11)

Quando toda cena do job é gravação de tela, o worker junta as gravações num bruto (`<nome>-completo`), escreve `tela.json` e monta com `narrado-passo.mjs --pausar`: voz inteira, pausas só em silêncio real, tela contínua, títulos numerados, legenda abaixo da tela e sem cartão de abertura. Job com acervo ou cena sem gravação segue no `narrado-calmo.mjs`. Regras em `Fluxo de producao de reels.txt`.

## Lições do vídeo do relatório (12/11)

O primeiro vídeo montado sozinho do começo ao fim foi reprovado: uma tela só, caminho não gravado, datas que a fala
não citou e voz travando. O que mudou:

- **Caminho no vídeo:** `comecar` na tela de onde a cena parte; depois dele, ir pelo endereço é recusado. O roteiro
  abre cada cena com "Começa em: <tela>" e lista cada toque com os valores exatos (datas, nomes, opções).
- **Ação de verdade:** `pronto` é recusado se nada foi feito depois do `comecar` numa cena que pede ação. Campo já
  preenchido com outro valor não conta: a IA troca pelo valor da cena.
- **Voz por bloco** (método do chat de marketing): `falar-blocos.py` → `pausar-blocos.py` (pausa só depois de ponto
  e interrogação, com a pontuação do roteiro e o fundo da própria gravação) → `acabar-blocos.py` (tom, corpo-forte,
  marca por último) → `juntar-voz.py` (falas unidas com o fundo mais quieto, nunca silêncio digital) → `alinhar.py`.
  O `narrado-passo` recebe essa pasta sem `--pausar`. `STUDIO_BLOCK_VOICE=0` volta pra voz contínua.
- **Preparo** só dispara quando a cena precisa dele e não derruba o vídeo; cartões de lista entram como clicáveis.

## Segurança

- **Conta de demonstração no PC** (`tools/studio-worker/contas.json`, fora do git; modelo em `contas.exemplo.json`): `{ "<marca>": { "url", "usuario",
  "senha" } }`. A senha nunca vai pra API nem pra IA: o worker digita ele mesmo quando a IA pede o passo `login`.
- **Só a conta de demonstração**, com dados fictícios. A IA recebe a instrução de nunca mostrar dado real e o quadro
  passa pela checagem de texto proibido da marca antes de virar vídeo.
- **Só o domínio do produto cadastrado** (`AppUrl` do RB-60): navegação pra fora é recusada pelo worker.
- **Nada destrutivo:** a IA não pode excluir, enviar mensagem real, pagar ou mudar configuração; o worker recusa
  cliques em botões com esses textos (lista no worker) além da instrução no prompt.
- Depois: conta no servidor, criptografada, cadastrada pelo cliente na conversa com a IA (quando entrarem clientes).

## Partes

Andamento (28/10): partes 1, 2 e 3 feitas. Testado no PC gravando o próprio Console local (Chrome sem janela,
login com a conta de demonstração, passos da IA em modo de teste, mp4 de saída). Falta testar com a IA de verdade
num sistema real e a parte 4.

1. **API:** `POST /worker/ai/next-action` (foto + elementos + cena + histórico curto → próximo passo), porta da IA
   aceitando imagem, custo registrado. Modo Fake pra teste.
2. **Worker:** gravador com CDP (Chrome do PC), loop por bloco, conta do `contas.json`, travas de domínio e de botão.
3. **Worker:** troca os trechos `gravar-bloco-N` pelos brutos gravados e segue pro pipeline.
4. **Console:** no Estúdio, prévia da gravação de cada cena e "gravar de novo" um bloco.
