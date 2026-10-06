# RootBoost Books: livros de colorir infantis (Amazon KDP)

Linha de produto de **impressão sob demanda**: a Amazon imprime e envia cada livro quando alguém compra.
Sem estoque, sem frete, sem atendimento de entrega. A gente só cria os arquivos e publica.

## Como funciona

```
books/titles/<slug>/book.json   especificação: título, copy, keywords, 40 temas de página, capa
books/pipeline/bookgen.py       plan | generate | clean | build | all
  raw/      imagens geradas pelo Higgsfield (+ .json com modelo/prompt/url de cada uma)
  clean/    line art pronta pra gráfica: preto puro, sem cinza, 2250x3000 px @ 300 DPI
  qa.json   páginas pra revisar no olho (muito preto ou muito cinza no original)
  out/      interior.pdf + cover.pdf + listing.md (ficha pronta pra colar no KDP)
```

```bash
python books/pipeline/bookgen.py plan christmas          # custo em créditos, não gasta nada
python books/pipeline/bookgen.py generate christmas --limit 3   # amostra antes do lote
python books/pipeline/bookgen.py all christmas           # gera o que falta, limpa, monta PDFs
python books/pipeline/bookgen.py generate christmas --only 7,12 # refaz páginas ruins
```

Dependências: `pip install pillow numpy reportlab pymupdf` e o CLI `higgsfield` logado.
`generate` é idempotente (pula o que já existe), então pode rodar de novo sem gastar à toa.

## Catálogo inicial (por que esses)

| slug | Livro | Por quê |
|---|---|---|
| `christmas` | Christmas Coloring Book, 4 a 8 anos | Sazonal com pico em nov/dez. **Precisa estar no ar até o fim de outubro.** |
| `axolotl` | Axolotl Coloring Book, 4 a 8 | Tema em alta entre crianças; concorrência menor que dinossauro. |
| `dinosaur` | Dinosaur Coloring Book, 4 a 8 | Perene, demanda muito alta (e concorrência alta). |
| `toddler` | My First Big Coloring Book, 1 a 3 | Faixa de 1 a 3 anos é pouco atendida; páginas super simples. |

Padrão de todos: 8.5 x 11 in, 40 desenhos, página única (verso em branco, marcador não vaza),
selo de idade na capa (aumenta conversão), preço US$ 8,99. Sem personagens licenciados
(Bluey, Pokémon, Disney etc.): isso derruba o livro e pode travar a conta.

## Regras do KDP que importam

- **Declarar IA**: no upload, marcar que as imagens foram geradas por IA. Não é público, mas omitir viola a política.
- **Revisar cada página no olho** antes de publicar (dedo a mais, traço quebrado, cinza). O `qa.json` ajuda a priorizar.
- Miolo sem sangramento ("No bleed"), papel branco, tinta preta. Capa com bleed de 0,125 in (o script já calcula a lombada).
- O KDP leva até 72 h pra aprovar cada título.

## Onde vender (resumo)

- **Amazon KDP (amazon.com e Europa)**: o canal principal. Atenção: o KDP **não vende livro impresso na amazon.com.br**;
  o público aqui é EUA/Europa (copy já em inglês).
- **Brasil impresso**: UICLAP faz impressão sob demanda e vende na loja dela (e anuncia parceria com Amazon).
  Exige versão em português (trocar copy e capa no `book.json`).
- **Mercado Livre / Shopee**: não imprimem nem enviam. Só valem com estoque próprio ou PDF digital, então ficam pra depois.
- **Etsy (PDF imprimível)**: mesmo arquivo `interior.pdf` vendido como download. Canal secundário barato de testar.

## Integração com a Amazon

O KDP **não tem API pública** de publicação. A SP-API da Amazon é pra Seller Central (produto físico), não pra livros KDP.
Por isso o pipeline entrega tudo pronto pra upload manual (uns 10 min por livro com o `listing.md`).
Relatórios de vendas do KDP podem ser exportados em planilha e analisados depois.
