import { describe, expect, it } from 'vitest';
import type { CopyFinding, ProofItem } from 'src/types/api';
import { cleanVoice, findingColor, proofState, sortFindings, sortProof } from 'src/utils/brandProfile';

const proof = (over: Partial<ProofItem>): ProofItem => ({
  id: crypto.randomUUID(), kind: 'Feature', statement: 'x', source: 'y', verifiedAt: '2026-10-01',
  reviewBy: null, active: true, needsReview: false, ...over,
});

const finding = (over: Partial<CopyFinding>): CopyFinding => ({
  id: 'f', group: 'g', status: 'Pass', blocking: false, message: 'm', fix: null, ...over,
});

describe('inventário de prova', () => {
  it('retirado vence qualquer outro estado', () => {
    expect(proofState(proof({ active: false, needsReview: true }))).toBe('retired');
  });

  it('proibido aparece como proibido, não como ativo', () => {
    expect(proofState(proof({ kind: 'Forbidden' }))).toBe('forbidden');
  });

  it('o que pede revisão vem primeiro e o retirado por último', () => {
    const sorted = sortProof([proof({ active: false }), proof({}), proof({ needsReview: true })]).map(proofState);
    expect(sorted).toEqual(['review', 'active', 'retired']);
  });
});

describe('checklist de copy', () => {
  it('erro primeiro, depois conferência humana obrigatória, e Pass escondido', () => {
    const sorted = sortFindings([
      finding({ id: 'ok' }),
      finding({ id: 'sug', status: 'Suggestion' }),
      finding({ id: 'human', status: 'NeedsHuman', blocking: true }),
      finding({ id: 'block', status: 'Fail', blocking: true }),
    ]);
    expect(sorted.map((f) => f.id)).toEqual(['block', 'human', 'sug']);
  });

  it('conferência humana obrigatória não aparece como erro', () => {
    expect(findingColor(finding({ status: 'NeedsHuman', blocking: true }))).toBe('warning');
    expect(findingColor(finding({ status: 'Fail', blocking: true }))).toBe('negative');
  });
});

describe('voz da marca', () => {
  it('limpa vazios, repetidos e pares incompletos antes de salvar', () => {
    const v = cleanVoice({
      audience: '  dono de clínica ',
      tone: ['frase curta', ' frase curta', ''],
      wordSwaps: { absenteísmo: 'falta', ' ': 'x', vazio: ' ' },
      bannedTerms: ['revolucione'],
      objections: [{ objection: 'e meus dados?', answer: 'migração com prévia' }, { objection: 'sem resposta', answer: '' }],
    });
    expect(v).toEqual({
      audience: 'dono de clínica',
      tone: ['frase curta'],
      wordSwaps: { absenteísmo: 'falta' },
      bannedTerms: ['revolucione'],
      objections: [{ objection: 'e meus dados?', answer: 'migração com prévia' }],
    });
  });
});
