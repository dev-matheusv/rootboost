# Nativa (RootBoost) — Roadmap

Refeito em 19/11/2026 a partir do pente fino pedido pelo Matheus. Substitui o roadmap por fases de 22/10 (marcado como
desatualizado) e junta o que sobrou do `PLANO.md`. Norte: **a plataforma faz o processo inteiro e o cliente só aprova**,
pra qualquer negócio (sistema, site, loja, serviço), não só o MedlyCare.

## 1. Pente fino (o que está aberto, com evidência)

### Estúdio
| # | Achado | Evidência |
|---|---|---|
| E1 | Não dá pra **parar** um vídeo que está sendo gerado, nem apagar: só esperar terminar ou falhar. | `CanBeDeleted` recusa em `Rendering`; não existe cancelar na API nem no worker. |
| E2 | O vídeo do relatório levou ~15 tentativas desde 07/11 e ainda não saiu. As falhas vieram da **conta de demonstração**, não do roteiro: dado vazio no período, CPF real na conta demo, tela que não muda, senha recusada (19/11). | Logs do worker de 07 a 19/11. |
| E3 | Não existe checagem da conta demo **antes** de gastar voz e IA: o erro só aparece no meio da gravação. | Idem. |
| E4 | **Vídeo com fotos do acervo sai fraco:** no teaser da IA (17/11), 12 dos 34 s são o logo ampliado e pixelado (faltou mídia), dois textos na tela ao mesmo tempo (título no meio e legenda embaixo), legenda cortando frase e legenda por cima da cartela final. | Folha de quadros do teaser. |
| E5 | **Ritmo da voz:** a nutrição (79 palavras em 30 s, pausa de 0,23 s entre frases) foi reprovada por atropelar. O R4 aprovado tem 0,5 a 0,6 s de pausa e 2,2 palavras por segundo no vídeo. Não há portão de densidade no roteiro nem de pausa na voz. | Alinhamentos de todos os reels (19/11). |
| E6 | A montagem depende de scripts **fora do repositório** (`Documents\MedlyCare Marketing\ferramentas`), do DMaker em `C:\DMaker` e de caminhos do PC do Matheus. Outra pessoa não consegue instalar. | `worker.mjs`: `STUDIO_TOOLS`, `FFMPEG`, `DMAKER`. |
| E7 | Instalar o worker é manual (Node, Chrome, ffmpeg, DMaker, `worker.env` com chave colada, `contas.json`). Não há tela de computadores no Console nem atualização guiada. | `docs/ESTUDIO.md`, `worker.env.example`. |
| E8 | Conferência existe só como aviso (folha de quadros e tela parada, 18/11). Ninguém **olha** os quadros antes de entregar; não há refação automática. | #274. |
| E9 | O explicativo ainda usa constantes visuais do MedlyCare; outra marca não tem kit (cores, logo, fonte) configurável no Console. | `PLANO.md`, RB-43. |

### Conhecimento da IA
| # | Achado |
|---|---|
| C1 | Não há onde subir **documentos da empresa** (PDF, Word, planilha, link) pra IA se basear. O acervo aceita foto e vídeo; o perfil da marca é texto curto. Depende da Root.IA (RAG do RootFlow), em desenvolvimento no outro chat. |
| C2 | Fontes que já temos e não alimentam o roteiro: perguntas que chegam na caixa de entrada, comentários dos posts, mapa do sistema explorado. |

### Uso por outras pessoas
| # | Achado |
|---|---|
| U1 | Um login por marca, sem convite de equipe nem papéis (dono, equipe, agência). Sem "esqueci a senha" (depende do domínio e e-mail). |
| U2 | Cliente novo não consegue conectar a própria conta Meta: tokens são variáveis no Railway e o app da Meta não está Live (RB-41). |
| U3 | Fila sem prioridade nem justiça entre marcas; um computador só; render na nuvem sem provedor decidido. |
| U4 | Sem cobrança, termos de uso e política de privacidade publicados; Vercel Hobby não serve pra uso comercial. |

### Anúncios e métricas
| # | Achado |
|---|---|
| A1 | Teste de R$ 50 (13 a 17/11): 97% do gasto na Audience Network, 287 cliques e só 65 visitas à página. **Corrigido** no #277 (só Instagram e Facebook, otimização por visualização da página). |
| A2 | **Sem evento de cadastro:** o MedlyCare não manda "cadastro feito" pro Pixel e pra Conversions API, então nenhuma campanha consegue otimizar por cliente, só por clique ou visita. |
| A3 | A plataforma não avisa sozinha quando o dinheiro vai pra um posicionamento ruim ou quando clique não vira visita. |
| A4 | O ranking orgânico não sabe a voz, o ritmo nem a duração de cada vídeo, então "qual voz funciona melhor" não tem resposta com número. |

## 2. Fases (ordem de entrega)

### Fase A — Estúdio confiável (agora)
1. **Controle do vídeo:** botão Parar (a API marca o pedido de parada, o worker confere a cada etapa e encerra o
   processo), Apagar a qualquer momento (para antes), Retomar do ponto salvo (voz aprovada e ensaio reaproveitados) e
   histórico de tentativas com o motivo em português claro. (E1)
2. **Checagem da conta demo antes de gastar:** login, período com dados, nenhum dado real, telas do roteiro existentes.
   Falhou, para na hora com o motivo e a correção. Tela "Conta de demonstração" no Console com o último resultado. (E2, E3)
3. **Portão de ritmo:** no roteiro, no máximo 2,2 palavras por segundo do vídeo; na voz, pausa mínima de 0,45 s entre
   frases (o worker estica o silêncio, nunca acelera a fala). Valores tirados do R4 aprovado. (E5)
4. **Vídeo com fotos e vídeos do cliente** (formato pessoal, acervo): resolução mínima por imagem (nunca ampliar logo
   ou foto pequena), um texto por vez na tela, legenda que nunca corta frase, cartela final limpa, e quando falta
   mídia a IA encurta o vídeo ou pede mais fotos em vez de encher. Usar os vídeos do cliente com corte nos melhores
   trechos (momentos, já existe). (E4)
5. **Conferência de verdade:** a IA olha a folha de quadros antes de entregar (texto ilegível, tela errada, dado real,
   texto duplo, logo pixelado) e refaz sozinha uma vez; o aviso só sobe se a refação também falhar. (E8)
6. **Pipeline dentro do repositório:** trazer `narrado-passo`, `narrado-calmo`, `acabar-voz` e `pausar-voz` pra
   `tools/pipeline` (com o acordo do marketing), tirar caminhos fixos, kit visual por marca no Console. (E6, E9)

### Fase B — Instalar e usar por outras pessoas
1. **Instalador do worker** em um comando (confere e baixa Node, Chrome, ffmpeg e DMaker) com **pareamento por código**
   mostrado no Console, sem colar chave. Tela "Computadores": ligado, versão, último vídeo, atualizar. (E7)
2. **Conta demo no servidor**, criptografada, em vez de `contas.json` no PC. (já decidido em 28/10 pra quando entrar cliente)
3. **Equipe:** convidar pessoa por e-mail, papéis por marca (dono, equipe, agência), esqueci a senha. (U1)
4. **Cliente conecta a própria Meta** com "Entrar com Facebook" (exige o app Live e a verificação do negócio, RB-41). (U2)
5. **Fila com prioridade** (data de postagem mais perto primeiro, refação na frente), justiça entre marcas, vários
   computadores, e decisão do **render na nuvem** (máquina com GPU pra voz; custo por vídeo calculado). (U3)

### Fase C — A IA conhece a empresa
1. **Documentos da empresa** no Console (PDF, Word, planilha, link do site), por marca, com a Root.IA fazendo a busca
   e o roteiro citando de onde tirou cada fato. Ponte enquanto a Root.IA não chega: extrair texto e guardar um resumo
   por documento. (C1)
2. **Fontes automáticas:** dúvidas da caixa de entrada e comentários viram pauta; site e Instagram relidos por semana;
   mapa do sistema refeito por semana, com tela nova virando sugestão de conteúdo. (C2)
3. Assistente geral no Console (molde do MedlyCare v14) usando documentos, métricas e pauta.

### Fase D — Métricas e Nativa Ads
1. **Evento de cadastro do MedlyCare** no Pixel e na Conversions API (pedido ao chat de desenvolvimento do MedlyCare)
   e campanha otimizando por cadastro. Sem isso não dá pra saber o custo por cliente. (A2)
2. **Alertas de campanha:** mais da metade do gasto num posicionamento, menos de 40% dos cliques virando visita,
   frequência alta. (A3)
3. **Ranking com atributos:** voz, palavras por segundo, duração, gancho e formato em cada vídeo, junto com retenção e
   tempo médio assistido (campos conferidos na doc oficial antes de codar). Responde "qual voz vende mais" com dado. (A4)
4. **Teste A/B pronto:** mesma peça em duas versões (voz ou gancho), R$ 20 cada, a plataforma monta e lê o resultado.
5. Orgânico do Facebook, relatório semanal por push ou e-mail.

### Fase E — Produto pra vender
Domínio e e-mail da Nativa, termos e privacidade, cobrança, Vercel Pro, white label pra agência, WhatsApp oficial
(caixa de entrada e aprovação), LinkedIn quando houver acesso de parceiro, marca registrada (INPI, nome composto).

## Andamento
- 19/11: **A1** botão Parar (#280, em produção) · **A2** conta demo conferida antes da voz (#282) · **A3** portão de
  ritmo e aviso de frases atropeladas (#282) · **A4** vídeo com fotos: imagem pequena inteira, um texto por vez, cena
  do tamanho da fala, legenda de frase, avisos e folha de quadros (#284). Anúncios só FB/IG com otimização por
  visualização da página (#277). Próximo: A5 conferência pela IA.

## Sugestões do Douglas (19/11, via Matheus; avaliar com ele)
| # | Sugestão | Situação |
|---|---|---|
| 1 | Instalador completo (dependências, worker e DMaker numa máquina sem o projeto; na dele faltava quase tudo e a capa saiu com o logo bugado) | Vira **B1**, sobe pra prioridade: é o que trava outra pessoa usar. |
| 2 | Todo erro diz qual ação corrige | Novo **A7**: mensagem de falha com "o que fazer" e botão quando der (reenviar, abrir contas, trocar mídia). |
| 3 | Cancelar o processo em andamento | Feito (**A1**, #280). |
| 4 | Excluir os itens com falha | Apagar um por um já existe fora do render; falta **apagar em lote** os que falharam (A7). |
| 5 | Pasta de identidade visual por projeto no acervo, aceitando manual escrito (.md, pasta, zip) | Novo **C4**, junto do kit visual por marca (E9) e dos documentos da empresa (C1). |
| 6 | Ajuste pontual não pode reescrever o roteiro inteiro | Confirmado: hoje a instrução manda reescrever tudo. Novo **A8**: ajuste por trecho, o resto do roteiro e das falas fica igual. |
| 7 | Renderizar pela placa de vídeo | Do DMaker (codificador da GPU); pedido no Jira DMK. Na nuvem entra na escolha da máquina (B5). |

## 3. Próximas duas semanas (ordem)
1. A1 Parar e apagar vídeo · A2 checagem da conta demo · A3 portão de ritmo.
2. A4 regras do vídeo com fotos · A5 conferência pela IA.
3. D1 pedido do evento de cadastro ao MedlyCare · D2 alertas de campanha.
4. A6 pipeline no repositório (combinar com o marketing) · B1 instalador e pareamento.
5. C1 documentos da empresa (alinhar com a Root.IA no RootFlow).

## 4. Depende do Matheus
- Senha da conta `demonstracao` (recusada em 19/11) e dados fictícios do período nela.
- Quantos cadastros entraram de 13 a 17/11 (custo por cadastro do teste).
- Ok do marketing pra trazer as ferramentas de montagem pro repositório.
- Decisões de custo: render na nuvem, Vercel Pro, domínio da Nativa.

## Fronteira com o DMaker
O DMaker (repo e Jira do Douglas) segue separado: voz, montagem e editor. A Nativa consome por CLI no worker e abre o
projeto no editor pra ajuste fino. Melhoria de montagem que é do DMaker vira pedido no Jira DMK.
