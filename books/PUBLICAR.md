# Como publicar e vender (passo a passo)

Tudo que é arquivo já está pronto em `books/titles/<livro>/out/` e `.../marketing/`.
O que fica com você: as contas (KDP, Pinterest, TikTok/YouTube) e os cliques de "publicar".

## 0. Ordem de lançamento

| # | Livro | Por quê agora |
|---|---|---|
| 1 | `christmas` | Janela do Natal. Precisa estar aprovado até ~25/out pra pegar novembro inteiro. |
| 2 | `axolotl` | Tema em alta, concorrência menor. |
| 3 | `dinosaur` | Perene, alto volume. |
| 4 | `mazes` | Atividade (não é colorir): outro público, mesma máquina. |
| 5 | `tracing` | Pré escola: pais compram o ano todo (volta às aulas é pico). |
| 6 | `toddler` | 1 a 3 anos, faixa pouco atendida. |

Publicar 1 ou 2 por dia (o KDP limita títulos novos por dia).

## 0b. Edições em alemão e espanhol (12 livros extras)

Pastas `titles/<livro>-de/` e `titles/<livro>-es/`: mesmos desenhos, tudo traduzido (título, capa, miolo, descrição,
palavras chave locais). Cada uma é um **livro novo** no KDP (Create > Paperback), com **Language: German / Spanish**.

- Publicar **depois** que a versão em inglês do mesmo livro for aprovada (evita o KDP achar que é duplicado).
- Preço: o `listing.md` de cada edição traz o marketplace principal e o preço em euro (€ 8,99 colorir, € 7,99 atividade).
  Na tela de Pricing, escolher **Amazon.de** (alemão) ou **Amazon.es** (espanhol) como marketplace principal.
- Espanhol vende também na Amazon.com (público hispânico dos EUA): conferir que o preço em dólar ficou US$ 8,99 / 7,99.

## 1. Conta KDP (uma vez)

1. kdp.amazon.com, entrar com a conta Amazon.
2. **Conta > Informações do autor/editor**: PF com seu CPF, endereço.
3. **Pagamento**: banco no Brasil (Nubank). Depósito direto em BRL, sem mínimo.
4. **Informações fiscais**: entrevista fiscal como pessoa física brasileira (gera o W 8BEN). Retenção de 30% nos EUA é normal (sem tratado).

## 2. Subir cada livro (≈10 min)

Bookshelf > **Create** > **Paperback**. Abrir `out/listing.md` do livro e copiar campo a campo.

- **Details**: título, subtítulo, autor "Crayon Cove Press", descrição (colar o HTML do listing), 7 palavras chave, categorias, faixa etária.
- **Content**: ISBN grátis do KDP; *Black & white interior, white paper*; **8.5 x 11 in**; **No bleed**; capa **Glossy**.
  Upload: `out/interior.pdf` e `out/cover.pdf` (opção "Upload a cover you already have").
  **AI-generated content: Yes** (imagens geradas com Higgsfield; nos livros de labirinto e caligrafia, só a capa).
  Clicar **Launch Previewer** e conferir 2 ou 3 páginas.
- **Pricing**: amazon.com no preço do listing (US$ 8,99 colorir, US$ 7,99 atividade). Os outros marketplaces o KDP converte sozinho.
- **Publish**. Aprovação em até 72 h.

## 3. Depois de aprovado (no mesmo dia)

1. **Link da Amazon**: copiar a URL do livro e colar em `book.json` no campo `"amazon_url"`.
   Rodar `python books/pipeline/site.py` e dar push: o botão "Get the full book on Amazon" aparece no site.
2. **A+ Content** (KDP > Marketing > A+ Content Manager): criar com os 3 módulos de `marketing/`:
   - Standard Image Header with Text → `aplus_1_before_after.png`
   - Standard Image Header with Text → `aplus_2_inside.png`
   - Standard Image & Light Text Overlay → `aplus_3_features.png`
3. **Amazon Ads** (KDP > Marketing > Amazon Ads): Sponsored Products, **targeting automático**, **US$ 3 a 5/dia** por livro,
   lance padrão. Rodar 14 dias sem mexer. Depois: pausar termos com cliques e sem venda, subir lance nos que vendem.
4. **Primeiras avaliações**: pedir pra conhecidos que comprarem deixarem review honesta. Nunca trocar review por brinde (proibido).

## 4. Tráfego orgânico (grátis)

Arquivos em `marketing/` de cada livro; legendas e datas em `marketing/posts.md`.

- **Pinterest** (maior busca de "coloring pages"): conta business "Crayon Cove", uma pasta por tema.
  Postar 1 pin por dia com o link do site (`/coloring/<livro>/`), que entrega 3 páginas grátis e leva pra Amazon.
- **YouTube Shorts, TikTok, Reels**: os `short_*.mp4` (página sendo colorida / labirinto sendo resolvido). 1 por dia.
- Automação: dá pra agendar tudo via **Postiz** (`posts.json` já está no formato de agenda). Precisa criar conta
  no postiz.com e conectar Pinterest/TikTok/YouTube; depois disso eu agendo o mês inteiro de uma vez.

## 5. Métrica de decisão (meados de dezembro)

- Por livro: vendas/dia, ACoS dos anúncios (gasto ÷ venda), cliques do Pinterest no site.
- Livro que vende: fazer a "continuação" (volume 2) e variantes do tema.
- Livro que não vende com anúncio rodando: trocar capa/título antes de desistir.

## 6. Próximos livros (mesma máquina)

- Sazonais: Valentine's (publicar até início de janeiro), Easter (até fevereiro), Halloween (até agosto).
- Volume 2 dos que venderem. Labirintos/caligrafia por tema (labirintos de dinossauro, números 1 a 20).
- Versão em português pra UICLAP (Brasil impresso): só trocar textos da capa e do miolo, páginas são sem texto.
