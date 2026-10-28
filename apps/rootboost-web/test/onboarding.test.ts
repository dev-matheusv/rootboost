import { describe, expect, it } from 'vitest';
import { emptyProduct, mergeProposal, readyToSave } from 'src/utils/onboarding';

const proposal = (over = {}) => ({
  name: null, kind: null, url: null, appUrl: null, summary: null, audience: null, features: [], differentials: [], offer: null, ...over,
});

describe('cadastro por conversa', () => {
  it('proposta preenche o que sabe e não apaga o que já estava', () => {
    const form = { ...emptyProduct(), name: 'MedlyCare', audience: 'clínicas' };
    const out = mergeProposal(form, proposal({ summary: 'Sistema pra clínicas', features: ['Agenda'], audience: '  ' }));
    expect(out.name).toBe('MedlyCare');
    expect(out.audience).toBe('clínicas');
    expect(out.summary).toBe('Sistema pra clínicas');
    expect(out.features).toEqual(['Agenda']);
  });

  it('pronto pra salvar com nome, resumo, público e um recurso', () => {
    expect(readyToSave(emptyProduct())).toBe(false);
    expect(readyToSave({ ...emptyProduct(), name: 'a', summary: 'b', audience: 'c', features: ['d'] })).toBe(true);
  });
});
