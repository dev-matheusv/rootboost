# Ferramentas de montagem do Estúdio

Trazidas de `Documents\MedlyCare Marketing\ferramentas` em 19/11/2026 (roadmap A6, com o ok do Matheus). O worker
(`tools/studio-worker`) usa esta pasta por padrão; `STUDIO_TOOLS` aponta pra outra se precisar.

| Arquivo | O que faz |
|---|---|
| `narrado-passo.mjs` | Passo a passo com tela contínua e voz inteira (método do R4). |
| `narrado-calmo.mjs` | Explicativo com trechos e voz contínua (formato antigo, ainda usado com acervo). |
| `falar-continuo.py`, `falar-blocos.py` | Voz do DMaker: fala contínua ou um áudio por bloco. |
| `acabar-voz.py`, `acabar-blocos.py` | Tom e acabamento da voz, com a marca d'água refeita depois. |
| `pausar-voz.py`, `pausar-blocos.py` | Pausas só nos silêncios reais da voz (nenhuma palavra cortada). |
| `alinhar.py` | Alinhamento palavra a palavra e contagem de palavras sem par. |
| `dmaker-tela-alta.py` | Render do DMaker com a tela alta do celular. |

Caminhos por variável (o padrão é o PC do Matheus): `STUDIO_BASE` (pasta de vídeos), `DMAKER_HOME` (instalação do
DMaker), `DMAKER_PATH`, `DMAKER_PYTHON`, `FFMPEG_PATH`, `FFPROBE_PATH`.

Mudança aqui vale pro Estúdio. O marketing continua com a cópia dele pra peças feitas à mão; melhoria que nasce lá
entra aqui por PR.
