# Criação de vídeo por comando (DMaker) — uso interno e caminho de produto

## O que é e por que entrou

[DMaker](https://github.com/DougFSA/DMaker) (do Douglas, MIT) é um editor de vídeos curtos por
comando: recebe um **spec JSON** e renderiza com FFmpeg. Instalado em `C:\DMaker`.

Ele fecha a lacuna que tínhamos: a gente escrevia o roteiro e **você** montava no CapCut. Agora o
roteiro **vira o vídeo**. Provado em 2026-10-01: `projects/prepmate-roteiro-a.json` gerou um
1080x1920, 30fps, 18.5s só com as fotos do PrepMate.

O que importa pra nós:
- `image` com `duration` + `motion` (Ken Burns sub-pixel, sem tremer) = nosso slideshow com movimento
- `overlays` de texto com `role` (`hook`, `title`, `cta`) = o texto na tela do pacote de conteúdo
- preset `instagram/reels` = 9:16 pronto pra TikTok/Shorts/Reels
- **templates com `params`, `$for` e `$if`** = a mesma receita serve pra qualquer produto
- **servidor MCP** = dá pra operar por agente, sem humano no meio
- `lint`/`doctor` que avisam de nitidez, legibilidade e tempo

## Limitação descoberta na prática

As fotos da CJ têm ~790px. A saída vertical é 1080x1920, então sobe 2.4x e **perde nitidez**.
Caminhos: (a) gerar imagens em 2k (Higgsfield), (b) filmar o produto de verdade, (c) aceitar a
perda no começo. Ordem de qualidade: b > a > c.

---

## Arquitetura: três camadas, repositórios separados

**Decisão: NÃO forkar o DMaker pra dentro do RootBoost.** Ele é projeto do Douglas, em Python,
com ciclo de vida próprio. Fork nos prende a uma versão e nos faz herdar manutenção alheia.
Consumimos como dependência e continuamos puxando as melhorias dele.

```
DMaker (upstream, do Douglas)     motor de render. Não mexemos. git pull traz melhoria.
      ^ consumido via CLI/MCP
RootBoost Studio (NOSSO, repo novo)   camada opinativa: fotos + receita -> spec JSON -> MP4s
      ^ consumido via API
RootBoost (existente)             pede criativos por produto. Encaixa no item ja planejado
                                  de "Pipeline de criativos" (CLAUDE.md 8).
```

### Por que camada separada e não dentro do RootBoost
- Stacks diferentes: RootBoost é .NET no Railway, DMaker é Python + FFmpeg + modelos. Juntar
  inflaria a imagem do backend e acoplaria o deploy da loja ao de render.
- Render é **pesado e em lote**; a loja é leve e sob demanda. Escalam diferente.
- Studio separado pode virar produto próprio sem desmontar a loja.

---

## O insight de produto (e a boa notícia)

Você quer algo que **pessoa comum** use pra automatizar vídeo. A parte difícil disso **já está
pronta no DMaker**: ele tem template parametrizado (`params`, `$for`, `$if`), marcas, e até uma
interface web (`dmaker ui`).

Ou seja, o produto que você imagina **não é tecnologia de render nova**. É:

1. **Curadoria de templates** que funcionam (reel de produto, antes e depois, depoimento, unboxing)
2. **Geração da copy** (hook, textos de tela, legenda) — que é justamente o que um LLM faz bem
3. **Front-end amigável**: pessoa sobe 4 fotos, escolhe um clima, e recebe 5 variações de vídeo

Isso é um build **bem menor** do que parece. O motor já existe e é bom.

### Fluxo do produto imaginado
```
usuário sobe fotos do produto
   -> escolhe uma receita ("reel de produto", "antes e depois")
   -> IA escreve hook + textos + legenda (vários ângulos)
   -> Studio monta N specs variando hook, ordem e movimento
   -> DMaker renderiza em lote
   -> usuário baixa 5 a 10 vídeos prontos pra postar
```
O diferencial não é o render: é **sair com 10 variações pra testar**, não com 1 vídeo.
Isso casa com o `CreativeSelector` que já existe no RootBoost (elege vencedor por CPA/CTR).

---

## Meu conselho de sequência (importante)

**Agora: uso interno apenas.** Fazer nossos vídeos, conseguir a primeira venda. A loja foi ao ar
hoje e ainda não vendeu. Construir um segundo produto antes do primeiro provar é o erro clássico
de dividir foco antes de validar.

**Depois da primeira venda:** montar os templates do RootBoost e automatizar nossa própria
produção em lote. Isso é alavanca direta no que já estamos fazendo.

**Só depois, se fizer sentido:** o Studio como produto pra terceiros.

A arquitetura acima **mantém a porta aberta** sem custo nenhum hoje: usar DMaker como ferramenta
interna já é exatamente o primeiro passo do produto. Nada se perde.

## Ponto humano, não técnico

O DMaker é do Douglas e é **bem construído** (SOLID, testes numéricos de qualidade visual, lint
próprio). A licença MIT permite uso comercial, mas se isso virar produto, **converse com ele**.
Além de ser o certo a fazer, alguém que escreve um código desses é melhor como parceiro do que
como autor de uma dependência.

## Como usar hoje

```powershell
cd C:\DMaker
.\.venv\Scripts\dmaker.exe render .\projects\<spec>.json
```
Specs nossos ficam em `C:\DMaker\projects\`. Saída em `C:\DMaker\output\`.
Interface web: `.\.venv\Scripts\dmaker.exe ui` (localhost:8765).
