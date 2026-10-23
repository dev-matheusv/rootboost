# Conector Meta Ads (RB-13) — desenho

Primeira marca a anunciar: **MedlyCare**, em português. Esta fase só **lê** métricas. Escrita nas campanhas
(pausar, mudar orçamento) fica pra Fase 5, atrás de `Traffic:Live=true` e com confirmação humana.

Pesquisado em 2026-10-23 na documentação oficial (fontes no fim). O que não foi confirmado está marcado
**[confirmar]** e é conferido contra a referência de métricas antes de entrar no código.

## Versão da API
- Atual: **Graph API e Marketing API v26.0** (lançada em 29/07/2026). A versão fica em config
  (`Meta__ApiVersion`, padrão `v26.0`), nunca escrita dentro da URL no código.
- Atenção: em **27/10/2026** algumas mudanças da v26.0 passam a valer pra **todas** as versões, inclusive
  chamadas sem versão. Por isso o padrão do CAPI também subiu de `v21.0` pra `v26.0` neste PR.
- Métricas de alcance e impressão orgânicas antigas (Page/Post Reach, Video/Story Impressions) foram
  descontinuadas em jun/2026 a favor de Media Views e Media Viewers. Isso afeta **insights de página**,
  não os de anúncio; não usamos as antigas.

## Endpoint
`GET https://graph.facebook.com/{versão}/act_{ad_account_id}/insights`
(o mesmo edge existe em campanha, conjunto e anúncio).

Parâmetros que vamos usar:
- `level`: `campaign`, `adset` e `ad` (uma chamada por nível, ou `ad` e agregamos) **[confirmar nomes]**
- `time_increment=1`: uma linha por dia, que é o snapshot diário **[confirmar]**
- `time_range` (`since`/`until`) ou `date_preset` (ex.: `last_7d`, confirmado)
- `fields`: `spend`, `impressions`, `clicks` (confirmados), mais `reach`, `frequency`, `ctr`, `cpc`, `cpm`,
  `actions`, `cost_per_action_type` **[confirmar na referência de métricas]**
- `breakdowns`: começar sem. `gender` confirmado; idade e plataforma depois.

Conversão do MedlyCare (cadastro de teste) chega em `actions` e `cost_per_action_type` pelo evento que o
Pixel e o CAPI mandarem. O nome exato do evento (`CompleteRegistration` ou personalizado) sai junto com a
configuração do Pixel do MedlyCare **[confirmar]**.

## Acesso
- Permissão **`ads_read`**. Pra app que só mexe na própria conta, o **acesso Standard** basta e é aprovado
  automaticamente pra app do tipo Business (sem App Review).
- Tier da Marketing API: começa em **Limited** (automático). Quota de insights por hora ≈ 600 + 400 × anúncios
  ativos. Pra uma conta pequena, sobra. **Full** exige 500 chamadas em 15 dias com menos de 15% de erro.
- Token: **usuário do sistema** do Gerenciador de Negócios (no tier Limited: 1 usuário do sistema + 1 admin).
  Vai em `Meta__AdsAccessToken` no Railway, nunca no código nem no chat.
- Verificação do negócio: exigida se o app acessar dado sensível. A doc não diz se insights conta como
  sensível **[confirmar ao criar o app]**.

## Limites e falhas (comportamento do conector)
- Ler os headers `X-FB-Ads-Insights-Throttle` (`app_id_util_pct`, `acc_id_util_pct`) e `X-Ad-Account-Usage`
  (`acc_id_util_pct`, `reset_time_duration`). Acima de 75% de uso, o job espera antes da próxima chamada.
- Erros de limite: código `4` (subcódigos `1504022` e `1504039` em insights), `17` (subcódigo `2446079`),
  `613` (subcódigo `1487742`), `80000`, `80003`, `80004`, `80014`. Resposta: backoff exponencial usando o
  tempo de reset dos headers. Erro de limite nunca derruba o job; o snapshot daquele dia fica pendente e
  tenta de novo.
- Consulta grande (muitos dias ou anúncios) usa relatório assíncrono, não síncrono.

## Arquitetura no RootBoost
- **`ICampaignInsightsSource`** (porta que já existe) ganha a implementação `MetaAdsInsightsSource`.
  `NotConfiguredInsightsSource` continua sendo o padrão: sem token, nada é chamado.
- **Snapshot diário** no banco (por marca, conta, nível, objeto e dia), gravado por um job agendado.
  Idempotente por (objeto, dia): rodar duas vezes não duplica.
- **Tela Campanhas** no Console: gasto, CTR, CPC, CPA e conversões por dia, e a **regra de parada** do
  orçamento visível (ex.: "R$ 200 gastos sem conversão: revisar criativo e público").
- Modo **Fake** determinístico pros testes e pro CI, igual ao `Supplier=Mock`.

## E a Ads CLI?
A Meta lançou em abril/2026 a **Ads CLI** (`meta ads <recurso> <ação>`), feita também pra agentes de IA:
lê insights, cria e edita campanhas, usa o mesmo token de usuário do sistema e devolve JSON.
- **No backend, não:** o job roda no Railway e chama a API direto do .NET, com os limites tratados no código.
- **Na operação, sim:** ela é a ferramenta natural pra eu (Claude) consultar e diagnosticar campanhas sob
  demanda, e pra montar a primeira campanha do MedlyCare com você aprovando cada passo.

## O que o Matheus prepara (ordem)
1. Gerenciador de Negócios (business.facebook.com) com a página do MedlyCare e o Instagram comercial.
2. Conta de anúncios com forma de pagamento e **limite de gasto da conta** (trava de segurança do próprio Meta).
3. Pixel (conjunto de dados) do MedlyCare e verificação do domínio `medlycare.com.br`.
4. App do tipo Business em developers.facebook.com com o produto Marketing API.
5. Usuário do sistema com a conta de anúncios atribuída e token com `ads_read`.

## Fontes
- [Introducing Graph API v25.0 and Marketing API v25.0](https://developers.facebook.com/blog/post/2026/02/18/introducing-graph-api-v25-and-marketing-api-v25)
- [Graph API v26.0 changelog](https://developers.facebook.com/docs/graph-api/changelog/version26.0/)
- [Graph API versions](https://developers.facebook.com/docs/graph-api/changelog/versions/)
- [Ads Insights API](https://developers.facebook.com/documentation/ads-commerce/marketing-api/insights)
- [Marketing API authorization](https://developers.facebook.com/documentation/ads-commerce/marketing-api/get-started/authorization.md)
- [Marketing API rate limiting](https://developers.facebook.com/documentation/ads-commerce/marketing-api/overview/rate-limiting.md)
- [Ads CLI overview](https://developers.facebook.com/documentation/ads-commerce/ads-ai-connectors/ads-cli/ads-cli-overview.md)
