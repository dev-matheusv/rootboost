# Publicação e agendamento próprios (Fase 4)

Proposta do chat de marketing do MedlyCare, decidida pelo Matheus em 2026-10-23. Substitui o Postiz (US$ 29/mês
depois de 7 dias) por publicação direta nas APIs oficiais, gratuitas pras nossas próprias contas. A porta
`ISocialPublisher` continua, então o Postiz pode virar só mais um adaptador se um dia fizer sentido.

**Até isso existir:** o Matheus agenda à mão pelo Meta Business Suite (Instagram e Facebook) e pela página do
LinkedIn, com a planilha de postagem que o chat do MedlyCare prepara.

## O que cada API faz
Conferir a documentação oficial na hora de implementar (versões mudam). Nada aqui vira código sem essa conferência.

- **Instagram** (Graph API, conta Profissional ligada à página do Facebook)
  - Reels: `POST /{ig-user-id}/media` com `media_type=REELS`, `video_url` (URL **pública**), `caption`,
    `share_to_feed`, `cover_url` ou `thumb_offset`; esperar o container ficar `FINISHED`
    (`GET /{container-id}?fields=status_code`); depois `POST /{ig-user-id}/media_publish`.
  - **Sem agendamento nativo pela API**: o agendador é nosso (job que publica na hora marcada).
  - Limite de publicações por conta em 24 h: conferir o número atual na doc.
  - Permissões: `instagram_basic`, `instagram_content_publish`, `pages_show_list`, `pages_read_engagement`,
    `business_management`.
- **Facebook** (Página)
  - Vídeo e Reel: `POST /{page-id}/video_reels` (início, upload, fim) ou `/{page-id}/videos`.
    Aceita agendamento nativo (`published=false` + `scheduled_publish_time`).
  - Permissões: `pages_manage_posts`, `pages_read_engagement`.
- **LinkedIn** (página da empresa)
  - Posts API (`/rest/posts`) + Videos API (`initializeUpload`, upload em partes, `finalizeUpload`),
    autor `urn:li:organization:{id}`. Sem agendamento nativo: agendador nosso.
  - `w_organization_social` exige o produto **Community Management API** aprovado pelo LinkedIn (pode levar
    dias). **Pedir cedo.**

## Acesso sem App Review (pras nossas contas)
- App em developers.facebook.com, tipo Empresa, ligado ao portfólio MedlyCare.
- Em modo de desenvolvimento, quem tem papel no app (Matheus, Douglas) usa as permissões nas contas que administra,
  sem App Review. **App Review só pra publicar em conta de cliente** (Fase 6): acesso avançado, política de
  privacidade, vídeo de demonstração e verificação da empresa (CNPJ).
- Token de **usuário do sistema** do portfólio, de longa duração, como segredo (`ROOTBOOST_META_SYSTEM_TOKEN`).
  Nunca no repositório nem no chat.
- O mesmo app serve pra Fase 2 (Meta Ads, `ads_read`): pedir as permissões de anúncio e de página/Instagram juntas.

## Desenho
- **Domain:** `Publication { Id, BrandKey, Channel (Instagram|Facebook|LinkedIn), CreativeId, Caption,
  ScheduledFor, Status (Draft|Approved|Publishing|Published|Failed), ApprovedBy, ApprovedAt, ExternalId,
  PublishedUrl, Error, Attempts }`.
- **Application:** `ISocialPublisher` por canal (preparar vídeo, capa e legenda; publicar agora ou agendar).
  Caso de uso **AprovarPublicacao**: só grava com aprovação humana, e a aprovação é registro auditável (quem,
  quando, qual versão da legenda). Caso de uso **PublicarPendentes**: job a cada minuto pega as aprovadas com
  `ScheduledFor <= agora`.
- **Infrastructure:** `MetaInstagramPublisher`, `MetaPagePublisher`, `LinkedInPagePublisher` e `FakePublisher`
  (modo fake determinístico, padrão da casa). Vídeo precisa de URL pública: storage de objetos com URL assinada
  de curta duração.
- **Garantias:**
  - Idempotência pela `PublicationId`: nunca publicar duas vezes se o job reiniciar (grava o container ou o
    `ExternalId` antes do publish e confere antes de repetir).
  - Falha explícita: `Failed` com o erro da API, alerta no Console, retry com limite.
  - **Dry-run por padrão** (`Social:Live=false`), igual ao `Traffic:Live`.
  - Conferência automática antes de aprovar: a checklist de copy (RB-18), com limites, travessão e "nunca dizer".
- **Console:** calendário editorial (semana e mês) por canal, prévia do vídeo e da legenda, botão Aprovar por
  post, status e link depois de publicado.
- **Depois (ganho grátis):** insights orgânicos pela mesma API (alcance, plays, salvamentos) entram no ciclo de
  aprendizado; o post orgânico também ganha etiquetas (gancho, formato) e métricas.

## Primeiro uso real
Os 9 reels do MedlyCare (`Videos\MedlyCare Reels\finais\`, com a legenda por canal no `.txt` de cada vídeo),
3 por semana (segunda, quarta e sexta).

## Estado (RB-21, feito)
- **Base pronta e testada, em dry-run.** `Publication` (Domain), `ApprovePublication` e `PublishDuePublications`
  (Application), `EfPublicationStore` + migração `Publicacoes`, `FakeSocialPublisher` por canal e
  `PublicationScheduler` (a cada minuto; `Social:SchedulerEnabled=false` desliga).
- **Endpoints (Admin):** `POST/GET /admin/brands/{key}/publications`, `PUT .../{id}/caption`,
  `PUT .../{id}/schedule`, `POST .../{id}/approve` (roda a checklist; bloqueada devolve 422 com os achados;
  quem aprovou vem do JWT) e `POST /admin/publications/run` (dispara o job na hora).
- **Regras que os testes protegem:** legenda editada depois de aprovar volta pra rascunho (aprovação vale pra
  uma versão); rascunho nunca publica; só o que está no horário publica; container preparado uma vez só;
  retomada depois de queda não publica duas vezes; 3 tentativas e fica `Failed`; mídia só https.
- **Falta:** publicadores reais (RB-22 Instagram/Facebook, RB-24 LinkedIn), calendário no Console (RB-23).

## LinkedIn: adiado (2026-10-23)
- A Community Management API só libera pra empresa registrada, com **e-mail corporativo** (gmail é recusado), página
  da empresa verificando o app, site e política de privacidade. Hoje temos só o CNPJ do MEI e gmail.
- **Pré-requisitos pra destravar:** domínio próprio + e-mail no domínio, página da empresa no LinkedIn, página
  `/privacidade`. Passo a passo completo do pedido: docs oficiais `community-management-app-review` e `increasing-access`.
- **Por enquanto:** LinkedIn fica manual (a plataforma prepara mídia e legenda aprovada; alguém posta à mão).
  Avaliar depois o produto self-serve "Share on LinkedIn" (post no perfil pessoal) como ponte; confirmar na doc antes.
- Pedido recusado não pode ser refeito no mesmo app: só pedir com tudo pronto.
