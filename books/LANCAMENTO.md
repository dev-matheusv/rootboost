# Lançamento: o que fazer e em que ordem

Atualizado em 2026-10-15. No ar: **Christmas** (B0HMFG5R65) e **Axolotl** (B0HMFWV7PJ).

## 1. Corrigir o subtítulo do Christmas (sem gastar vaga de publicação)

O prazo de 72 h passou, mas o KDP corrige **erro de digitação** pelo suporte.
KDP > **Ajuda** (rodapé) > **Fale conosco** > *Book details* > *Title, subtitle, author*. Colar:

```
Hello KDP team,

My paperback ASIN B0HMFG5R65 (ISBN 979-8160623528) was published with a typo in the subtitle:
the number "40" lost its first digit and appears as "0".

Current subtitle:
0 Cute and Easy Holiday Pages with Santa, Reindeer, Snowmen, Elves and More. A Fun Gift for Boys and Girls

Correct subtitle:
40 Cute and Easy Holiday Pages with Santa, Reindeer, Snowmen, Elves and More. A Fun Gift for Boys and Girls

The interior and cover already show 40 pages; only the subtitle field has the typo. Could you please correct it?

Thank you!
```

## 2. A+ Content (Christmas e Axolotl, hoje)

KDP > **Marketing** > **A+ Content Manager** > *Start creating A+ content*. Idioma: English. Módulos, nesta ordem:

| Módulo do KDP | Imagem (`books/titles/<livro>/marketing/`) | Texto alternativo |
|---|---|---|
| Standard Image Header With Text | `aplus_1_before_after.png` | Before and after coloring example |
| Standard Image Header With Text | `aplus_2_inside.png` | Sample pages from inside the book |
| Standard Image & Light Text Overlay | `aplus_3_features.png` | Book features |

Aplicar ao ASIN do livro e **Submit**. A revisão do A+ leva até 7 dias. Pode deixar os campos de título/texto do módulo em branco (as imagens já falam).

## 3. Amazon Ads (começar com pouco e medir)

KDP > Marketing > **Amazon Ads** > Marketplace amazon.com > **Sponsored Products**.

**Campanha A: automática** (descobre os termos que convertem)
- Nome: `CHRISTMAS AUTO` (e `AXOLOTL AUTO`)
- Targeting: **Automatic**. Orçamento: **US$ 3/dia**. Lance: **Dynamic down only**, lance padrão **US$ 0,35**.
- Ligar.

**Campanha B: manual por palavra-chave** (só pro Christmas por enquanto, que tem janela curta)
- Nome: `CHRISTMAS KW`. Targeting: **Manual > Keyword**. Orçamento: **US$ 3/dia**. Lance **US$ 0,40**.
- Palavras (correspondência **Phrase**):
  christmas coloring book for kids · christmas coloring book for kids ages 4-8 · stocking stuffers for kids ·
  christmas activity book for kids · christmas gifts for kids · santa coloring book · kids christmas books ·
  coloring books for kids ages 4-8 · holiday coloring book for kids · advent activities for kids
- Negativas (exata): adult · adults · bluey · disney.

**Regras de ouro**
- Não mexer por **14 dias** (o algoritmo precisa aprender).
- Depois: em *Search terms*, termo com 10+ cliques e 0 venda → negativar. Termo que vendeu → copiar pra campanha manual.
- Gasto alvo: **ACoS** (gasto ÷ venda) até 70% no começo é aceitável, porque cada venda gera avaliação e posição.
  Royalty por livro ≈ US$ 2,50, então a campanha precisa de ~1 venda a cada US$ 3 a 4 gastos pra empatar.

## 4. Calendário de publicação (limite: 2 livros a cada ~3 dias)

| Lote | Livros | Por quê |
|---|---|---|
| 1 | **Christmas DE + Christmas ES** | Sazonal: Natal na Alemanha e na Espanha. Quanto antes, mais semanas de venda. |
| 2 | **Dinosaur EN + Toddler EN** (Color & Learn) | Os mais fortes da pesquisa, preço US$ 9,99. |
| 3 | **Tracing EN + Mazes EN** | Educativo: mercado maior do ano todo. |
| 4 | **Toddler DE + Toddler ES** | |
| 5 | **Dinosaur DE + Dinosaur ES** | |
| 6 | **Tracing DE + ES**, depois **Mazes DE + ES**, depois **Axolotl DE + ES** | |

Cada livro: abrir `out/listing.md`, conferir **título e subtítulo** no resumo antes de publicar (o erro do "0" veio de colar).
Edições DE/ES: marketplace principal Amazon.de / Amazon.es, preço em euro do `listing.md`.

## 5. Tráfego grátis (começar hoje, 10 minutos por dia)

- **Pinterest** (conta business "Crayon Cove"): 1 pin por dia de `marketing/pin_*.png`, link para
  `rootboost.vercel.app/coloring/christmas/` (já com botão pra Amazon). Legendas prontas em `marketing/posts.md`.
- **Shorts / TikTok / Reels**: 1 vídeo por dia de `marketing/short_*.mp4`.
- Pedir para 3 a 5 conhecidos comprarem e **avaliarem com sinceridade** (nunca trocar avaliação por brinde).

## 6. Como vamos medir (toda segunda)

KDP > **Relatórios** > *Royalties estimados* + Amazon Ads > *Campaign manager*. Me mandar um print ou a planilha exportada:
vendas por livro, impressões, cliques, gasto e ACoS. Com isso eu ajusto lances, palavras e decido o próximo livro.
