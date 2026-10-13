# RootBoost Studio: o que a linha de livros ensina pra plataforma

A linha de livros (`books/`) virou, na prática, um protótipo completo do ciclo que o RootBoost quer automatizar
pra qualquer produto: **pesquisar → criar → empacotar → divulgar → medir**. Este documento mapeia o que já existe
pra módulos reutilizáveis da plataforma, sem reescrever nada agora.

## Mapa: o que existe hoje → módulo do RootBoost

| Etapa | Hoje (livros) | Módulo da plataforma | Também serve pra |
|---|---|---|---|
| Pesquisa de mercado | `tools/market/amazon-scan.js` + `books/PESQUISA.md` (BSR, avaliações, preço, páginas, dores do público) | **Market Intel**: coleta por canal, guarda dataset, gera "o que vence e por quê" | sourcing de produtos de dropshipping (CJ), nichos de anúncio |
| Especificação | `book.json` (copy, keywords, tema de cada página, estilo, preço, idioma) | **Product Spec**: um manifesto por produto e variante (idioma/mercado) | landings (`products.json`) já seguem a mesma ideia |
| Geração de ativos | `bookgen.py generate` (Higgsfield, paralelo, retry, custo estimado antes) | **Asset Engine**: adapter de IA com fila, limite de concorrência, orçamento e proveniência | fotos de produto, criativos de anúncio, vídeos |
| Controle de qualidade | `qa.json` + folha de contato revisada página a página | **QA Gate**: métricas automáticas + aprovação humana antes de publicar | qualquer ativo gerado por IA |
| Empacotamento | PDFs no padrão KDP, edições DE/ES sem custo | **Packager por canal**: regras de cada marketplace (KDP, Amazon, Shopee, Meta) | anúncios, catálogos |
| Copy e SEO | `listing.md` (título, descrição, 7 keywords por mercado), padrão sem hífen | **Copy Engine**: copy por canal e idioma a partir da pesquisa | landings, e-mails, anúncios Meta |
| Criativos | `marketing.py` (A+, pins, Shorts, cor guiada por IA) | **Creative Engine**: pacote de criativos por produto | anúncios do dropshipping, DMaker |
| Distribuição | `site.py` (páginas grátis + QR no livro) + `posts.json` (Postiz) | **Distribution**: páginas de captura + agenda social | toda a loja |
| Medição | (falta) relatórios do KDP e Amazon Ads | **Insights → Optimizer**: alimenta o `CampaignOptimizer` que já existe | Meta Ads (já em dry-run) |

## Próximos passos (em ordem, quando fizer sentido)

1. **Medição primeiro**: importar o relatório de vendas do KDP (planilha) e o de Amazon Ads pro banco do RootBoost.
   Sem isso, nenhuma automação sabe o que funciona.
2. **Captura de e-mail no QR dos livros**: endpoint `POST /leads` no backend (Resend já existe) → audiência própria.
3. **Market Intel como serviço**: rodar a coleta periodicamente e registrar BSR dos nossos livros e dos concorrentes.
4. **Generalizar o Asset Engine**: tirar a lógica de fila/retry/custo do `bookgen.py` pra um adapter comum
   (Python hoje; porta `IAssetGenerator` no .NET quando entrar no backend).
5. Só então: **advisor de IA** (Claude) lendo Insights e propondo o próximo produto, criativo ou ajuste de anúncio.

Princípio: não construir plataforma antes de ter dado de venda. Os livros são o laboratório barato que gera esse dado.
