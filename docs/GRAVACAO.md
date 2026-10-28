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
                                         ▼  resposta: clicar | digitar | rolar | esperar | pronto
                                         executa no Chrome, grava os quadros (screencast)
                                         ▼
          brutos/<nome>-bloco-N.mp4 + troca o trecho no roteiro por [bruto, 0, duração]
                                         ▼
                              pipeline de sempre (voz, alinhamento, montagem)
```

- **A chave da IA fica só no Railway.** O worker manda a foto e recebe o próximo passo; cada chamada grava custo em
  `AiUsage` (finalidade `screen-recording`).
- **Limites por cena:** no máximo 25 passos e 90 s; passou disso, o bloco falha e vai pra revisão humana.
- **Cursor desenhado na página** e marcas de tempo, como no gravador do MedlyCare.

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
