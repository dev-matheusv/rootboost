import { describe, expect, it } from 'vitest';
import { applySegmentEdits, estimateSeconds, jobStep, parseJson, sceneOf, scriptSegments } from 'src/utils/studio';

const roteiro = {
  nome: 'combo-8',
  abertura: ['Psicologia sem planilha', 'Agenda e evolução'],
  blocos: [{ titulo: 'Sessão toda semana', sub: 'Marca uma vez', trechos: [] }, { titulo: 'Evolução', sub: '', trechos: [] }],
};

describe('estúdio', () => {
  it('monta a ordem do vídeo: abertura, blocos e final, cada um com a sua fala', () => {
    const s = scriptSegments(roteiro, ['Abre.', 'Bloco um.', 'Bloco dois.', 'Fecha.']);
    expect(s.map((x) => x.label)).toEqual(['opening', 'block:1', 'block:2', 'closing']);
    expect(s[0]).toMatchObject({ title: 'Psicologia sem planilha', fala: 'Abre.' });
    expect(s[3]?.fala).toBe('Fecha.');
  });

  it('sem abertura (anúncio) a primeira fala é do bloco 1; fala que falta vira null', () => {
    const s = scriptSegments({ ...roteiro, abertura: null }, ['Bloco um.']);
    expect(s[0]).toMatchObject({ label: 'block:1', fala: 'Bloco um.' });
    expect(s[2]?.fala).toBeNull();
  });

  it('estima a duração a 2,4 palavras por segundo', () => {
    expect(estimateSeconds(['um dois três quatro cinco six', 'sete oito nove dez onze doze'])).toBe(5);
    expect(estimateSeconds(null)).toBe(0);
  });

  it('JSON inválido vira mensagem', () => {
    expect(parseJson('{"a":1}').value).toEqual({ a: 1 });
    expect(parseJson('{oops').error).toBeTruthy();
  });

  it('etapa da barra', () => {
    expect(jobStep('ScriptReady')).toBe(0);
    expect(jobStep('Rendering')).toBe(2);
    expect(jobStep('VoiceReview')).toBe(2);
    expect(jobStep('Rendering', true)).toBe(3);
    expect(jobStep('Done')).toBe(5);
  });
});

describe('edição sem JSON', () => {
  it('aplica título, subtítulo e fala de volta nos contratos e mantém os trechos', () => {
    const r = { ...roteiro, fecho: ['Teste grátis', 'Link na bio'] };
    const edits = scriptSegments(r, ['a', 'b', 'c', 'd']).map((s) => ({ label: s.label, title: s.title, sub: s.sub, fala: s.fala ?? '' }));
    edits[1]!.title = 'Sessão fixa';
    edits[1]!.fala = 'Nova fala do bloco um.';
    const out = applySegmentEdits(r, edits);
    expect(out.falas).toEqual(['a', 'Nova fala do bloco um.', 'c', 'd']);
    const blocos = out.roteiro.blocos as { titulo: string; trechos: unknown[] }[];
    expect(blocos[0]!.titulo).toBe('Sessão fixa');
    expect(blocos[0]!.trechos).toEqual([]);
    expect(out.roteiro.fecho).toEqual(['Teste grátis', 'Link na bio']);
  });

  it('cena do bloco', () => {
    expect(sceneOf({ blocos: [{ cena: 'abrir a agenda' }] }, 'block:1')).toBe('abrir a agenda');
    expect(sceneOf({ blocos: [] }, 'opening')).toBe('');
  });
});
