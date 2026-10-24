import type { BrandVoice, CopyFinding, ProofItem, ProofKind } from 'src/types/api';

export const PROOF_KINDS: ProofKind[] = ['Offer', 'Feature', 'Price', 'Metric', 'Testimonial', 'Credential', 'Scarcity', 'Forbidden'];

export type ProofState = 'active' | 'review' | 'forbidden' | 'retired';

/** Estado de um item de prova, na ordem que importa pra quem revisa. */
export function proofState(item: ProofItem): ProofState {
  if (!item.active) return 'retired';
  if (item.kind === 'Forbidden') return 'forbidden';
  return item.needsReview ? 'review' : 'active';
}

export function proofStateColor(state: ProofState): string {
  return { active: 'positive', review: 'warning', forbidden: 'negative', retired: 'grey-6' }[state];
}

/** Ativos primeiro, depois os que pedem revisão, os proibidos e por fim os retirados. */
export function sortProof(items: ProofItem[]): ProofItem[] {
  const order: Record<ProofState, number> = { review: 0, active: 1, forbidden: 2, retired: 3 };
  return [...items].sort((a, b) => order[proofState(a)] - order[proofState(b)] || b.verifiedAt.localeCompare(a.verifiedAt));
}

/**
 * Ordem de leitura: erro que bloqueia, erro, conferência humana obrigatória, conferência humana, sugestão.
 * "Bloqueante" numa conferência humana quer dizer "alguém precisa confirmar", não "está errado".
 */
export function findingRank(f: CopyFinding): number {
  if (f.status === 'Fail') return f.blocking ? 0 : 1;
  if (f.status === 'NeedsHuman') return f.blocking ? 2 : 3;
  return f.status === 'Suggestion' ? 4 : 5;
}

/** Pass some por padrão: a tela mostra só o que pede ação. */
export function sortFindings(findings: CopyFinding[], includePass = false): CopyFinding[] {
  return findings.filter((f) => includePass || f.status !== 'Pass').sort((a, b) => findingRank(a) - findingRank(b));
}

export function findingColor(f: CopyFinding): string {
  return { Fail: 'negative', NeedsHuman: 'warning', Suggestion: 'info', Pass: 'positive' }[f.status];
}

export function emptyVoice(): BrandVoice {
  return { audience: '', tone: [], wordSwaps: {}, bannedTerms: [], objections: [] };
}

/** Limpa a voz antes de salvar: tira espaços, vazios e repetidos. */
export function cleanVoice(v: BrandVoice): BrandVoice {
  const list = (xs: string[]) => [...new Set(xs.map((x) => x.trim()).filter(Boolean))];
  const swaps: Record<string, string> = {};
  for (const [from, to] of Object.entries(v.wordSwaps)) {
    if (from.trim() && to.trim()) swaps[from.trim()] = to.trim();
  }
  return {
    audience: v.audience.trim(),
    tone: list(v.tone),
    wordSwaps: swaps,
    bannedTerms: list(v.bannedTerms),
    objections: v.objections
      .map((o) => ({ objection: o.objection.trim(), answer: o.answer.trim() }))
      .filter((o) => o.objection && o.answer),
  };
}
