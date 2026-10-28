import { describe, expect, it } from 'vitest';
import { estimateSeconds, jobStep, parseJson, scriptSegments } from 'src/utils/studio';

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
    expect(jobStep('Done')).toBe(4);
  });
});
