# Gravação de voz pra clonagem (DMaker / OmniVoice)

Você grava **uma vez**, uns 13 segundos, e a partir daí o DMaker narra qualquer roteiro com a sua
voz, e as legendas saem da própria narração.

## O texto (leia este)

Os vídeos do RootBoost são pro público dos EUA, então a referência principal é **em inglês**.
O clone copia timbre **e** jeito de falar, por isso leia no tom de quem conta uma dica pra um
amigo, com energia, igual você quer que a narração soe.

> Hey, quick tip before you scroll. I tested this for a whole week, and honestly? It saved me so
> much time every single day. Here's exactly how it works, and why I'd buy it again.

São 35 palavras, ~13 s em ritmo natural. O limite do motor é **3 a 15 s**.

### Opcional: versão em português
Só se for fazer conteúdo em PT (ou pra Laura). Cada voz clonada guarda um idioma.

> Oi, espera aí antes de passar. Eu testei isso por uma semana inteira e, sinceramente? Facilitou
> muito o meu dia. Vou te mostrar exatamente como funciona e por que eu compraria de novo.

## Como gravar (é o que decide a qualidade)

- **Lugar silencioso e "morto"**: quarto com cama, cortina, roupa. Nada de banheiro ou sala vazia (eco).
- **Celular a um palmo da boca**, um pouco de lado pra não estourar o "p" e o "b".
- **Sem música, ventilador, ar ligado** ou TV ao fundo. O clone aprende o ruído junto.
- **Uma tomada só**, do começo ao fim, sem cortar no meio. Se errar, grave tudo de novo.
- Meio segundo de silêncio antes de começar e depois de terminar (o DMaker apara o resto).
- Formato: WAV se o app permitir; senão o m4a/mp3 do gravador do celular serve.

## Onde salvar

`C:\DMaker\projects\voz\matheus-en.wav` (ou `.m4a`). A pasta é ignorada pelo git, então o áudio
da sua voz não vai pro GitHub.

## O que eu rodo depois

```powershell
cd C:\DMaker
.\.venv\Scripts\dmaker.exe voice clone matheus-en .\projects\voz\matheus-en.wav --language en `
  --text "Hey, quick tip before you scroll. I tested this for a whole week, and honestly? It saved me so much time every single day. Here's exactly how it works, and why I'd buy it again."
```

Passo o texto exato com `--text`: assim o motor não depende do Whisper adivinhar o que foi dito e
alinha melhor a voz. Se você improvisar alguma palavra na leitura, me avise qual.

## Custo de tempo (importante)

O OmniVoice roda na CPU (não temos GPU NVIDIA): **~50 s de processamento por segundo de narração**.
Um vídeo de 18 s leva ~15 min pra narrar. O lote de 8 vídeos, ~2 h, então roda em segundo plano.
O cache por fala evita reprocessar: trocar só o visual não regera a voz.
