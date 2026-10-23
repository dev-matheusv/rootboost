# Base de conhecimento de marketing que aprende

Proposta do Matheus com o chat de marketing do MedlyCare (2026-10-23), registrada como direção do produto.
Card no Jira: **RB-15** (v1). Encaixa nas Fases 3 a 5 do `docs/ROADMAP.md`.

## O pedido
Tudo o que aprendermos fazendo marketing (copy, persuasão, formato de vídeo, voz, teste de criativo) mora
**dentro** do RootBoost. Todo cliente usa esse conhecimento desde o primeiro dia, e cada teste de cada
cliente deixa a plataforma melhor pra todos.

## Fontes pra importar
- `Documents\RootBoost Conhecimento\`: **já separado em geral** (vale pra qualquer marca):
  `copy-metodo`, `persuasao`, `conferencia`, `video`, `anuncios`, `aprendizados`. É a base do `knowledge/`.
- `Documents\MedlyCare Marketing\` (cliente zero, com o que é específico dele):
  `Playbook de copy MedlyCare.txt` (níveis de consciência de Schwartz, PAS, AIDA, 4U, 6 tipos de gancho,
  Cialdini, vieses, Fogg, equação de valor, pirâmide B2B da Bain, objeções, regras de verdade e prova,
  checklist com nota), `Fluxo de producao de reels.txt`, `Anuncios\` (estrutura de teste e regra de pausa e
  escala no Meta; grupos, limites de caractere e negativas no Google), `ferramentas\`
  (`narrado-continuo.mjs`, `falar-continuo.py`, `alinhar.py`, `conferir-fala.py`, `gerar-google.mjs`).

## Desenho

### 1. Conhecimento geral versionado (igual pra todos)
Pasta `knowledge/` no repositório, legível por gente e por máquina:
- `copy/playbook.md`: método, fórmulas e estilo (entra no prompt da IA)
- `copy/levers.json`: cada gatilho de persuasão, com quando usar, quando não usar, nível de evidência e regra
  legal (CDC no Brasil)
- `copy/checklist.json`: itens de conferência com peso (vira nota automática)
- `channels/limits.json`: limites de caractere e zonas seguras por canal, com a data em que foram conferidos
- `channels/policies.json`: o que Meta e Google recusam (saúde de quem lê, promessa etc.)
- `video/formats.json`: margens, posição da legenda e duração ideal por formato

Mudar conhecimento é **PR com o porquê**, no mesmo fluxo do código. Assim ele tem histórico e revisão.

### 2. Conhecimento de cada marca (por `Brand`)
- Voz da marca: tom, palavras proibidas, como o fundador fala
- Banco de voz do cliente: frases reais, com fonte e data
- **Inventário de prova:** o que é verdade hoje (recursos em produção, oferta, preço, números medidos). **A IA
  só escreve afirmação que esteja no inventário.** É o que impede depoimento, número ou urgência inventados,
  e é o maior diferencial de confiança do produto
- Ofertas e objeções respondidas

### 3. Ciclo de aprendizado
- Cada criativo nasce com **etiquetas**: tipo de gancho, nível de consciência, gatilho, recurso, formato, voz,
  duração e nicho. A Fase 2 traz as métricas (CTR, retenção de 3 s, CPA).
- **Por marca:** o `CreativeSelector` (já existe) elege o vencedor, e o resultado volta pro registro de
  aprendizados da marca.
- **Entre marcas:** agrega **só etiqueta e métrica por nicho** (ex.: "em clínica de fisio, gancho de pergunta
  teve CTR melhor que dor"). Isso vira a probabilidade inicial de cada etiqueta pro próximo cliente do nicho
  (bandit, ex.: amostragem de Thompson, decide o que testar).
- **Privacidade:** texto e vídeo do cliente **nunca** saem da marca dele. Só etiqueta e número, agregados, com
  consentimento explícito no termo de uso (LGPD), e com um mínimo de N marcas por nicho antes de mostrar
  qualquer agregado.

### 4. A IA usa isso em toda geração
Prompt = conhecimento geral (playbook, gatilhos, checklist) + marca (voz, inventário de prova, voz do cliente)
+ aprendizados (o que venceu no nicho). A saída passa pela **checklist automática** (limites, política,
verdade contra o inventário, sem travessão) **antes** de chegar ao humano. Nada publica sem aprovação.

### 5. No Console
- Ao criar uma peça: "por que este gancho", com o princípio e o dado por trás
- Nota da checklist em cada criativo, com o que corrigir
- Tela "O que funciona no seu nicho" (os agregados do item 3)
- No fim de cada teste: o aprendizado em uma frase, gravado no registro da marca

### 6. O MedlyCare é o cliente zero
Primeira `Brand`, com o playbook e o inventário de prova dele importados. O teste dos 9 criativos de Meta
(`Videos\MedlyCare Reels\anuncios\`) roda pelo próprio RootBoost quando a Fase 2 estiver de pé. O que
aprendermos ali é o primeiro dado do cérebro.

### 7. Acervo da marca (RAG, no molde do RootFlow)
Ideia do Matheus (2026-10-23). O cliente sobe **tudo o que tem da empresa**: documentação, apresentações,
planilhas, criativos antigos, imagens, logo, vídeos. A IA usa esse acervo pra gerenciar a campanha (subir
anúncio, ler métricas, fazer criativo e copy) com base nos dados **da própria empresa**.
- **Motor:** o do RootFlow (já portado no MedlyCare v14): extrai texto de PDF, Word, Excel, PowerPoint, CSV,
  TXT e Markdown, quebra em trechos, gera embeddings e busca no **pgvector**. O RootBoost já está no Postgres.
  Alvo de longo prazo: um pacote comum `Root.IA` pra família Root, em vez de três cópias.
- **Mídia:** arquivo no storage de objetos (bucket do Railway, US$ 0,015 por GB/mês), com descrição gerada por
  IA de visão (o que aparece, cores, texto na imagem) e transcrição dos vídeos (Whisper, que o DMaker já usa).
  É essa descrição que entra na busca. Logo e cores alimentam a marca do DMaker (`brand.json`).
- **Resposta com fonte:** toda copy ou recomendação baseada no acervo diz de onde veio (documento e trecho),
  como o chat do RootFlow faz.
- **Acervo não é prova:** o acervo pode **sugerir** itens pro inventário de prova, mas só entram depois que um
  humano confirma. Documento velho com número desatualizado não pode virar anúncio.
- **Isolamento:** o acervo de uma marca nunca é buscado por outra, e nada dele entra no agregado entre marcas.

## Ordem
1. **RB-15, Base de conhecimento v1:** `knowledge/` com os arquivos do item 1 a partir das fontes, mais o modelo
   de `Brand` com inventário de prova (item 2). Depende do `Brand` do RB-11.
2. Checklist automática como função pura e testada (limites, política, verdade, travessão).
3. Acervo da marca (item 7, RB-16): ingestão de documentos primeiro, mídia com descrição por IA depois.
4. Ciclo de aprendizado (item 3), depois da Fase 2 (métricas).
