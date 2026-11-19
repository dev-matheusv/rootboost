// O que fazer quando um vídeo falha (roadmap A7, pedido do Douglas em 19/11): todo erro conhecido vem com a ação que
// corrige. A ordem importa: o primeiro padrão que bate decide.
const rules: [RegExp, string][] = [
  [/conta de demonstração não entrou|não entrou no sistema|tela de login continuou/i, 'demoLogin'],
  [/dado pessoal/i, 'personalData'],
  [/não respondeu/i, 'systemDown'],
  [/palavras? sem par/i, 'voiceWords'],
  [/crédito da IA|teto (mensal )?da IA|credit balance/i, 'aiCredit'],
  [/parado a pedido/i, 'stopped'],
  [/cliques seguidos sem mudar a tela|a IA parou/i, 'stuckScreen'],
  [/conexão|ENOTFOUND|fetch failed|timeout/i, 'network'],
];

/** Chave de i18n (studio.fix.*) com a ação que corrige o erro, ou null quando não há dica conhecida. */
export function failHint(error: string | null | undefined): string | null {
  if (!error) return null;
  return rules.find(([pattern]) => pattern.test(error))?.[1] ?? null;
}
