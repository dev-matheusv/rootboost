import { describe, expect, it } from 'vitest';
import { filterCommands, type Command } from 'src/utils/commands';

const cmd = (id: string, label: string, keywords?: string): Command => ({ id, label, group: 'Ir para', keywords, run: () => undefined });
const all = [cmd('pub', 'Publicações', 'agenda calendario'), cmd('camp', 'Campanhas'), cmd('reel', 'Importar reel pronto')];

describe('busca rápida', () => {
  it('ignora acento e maiúscula', () => {
    expect(filterCommands(all, 'PUBLICACOES').map((c) => c.id)).toEqual(['pub']);
  });

  it('acha pelas palavras-chave e exige todas as palavras', () => {
    expect(filterCommands(all, 'calendário').map((c) => c.id)).toEqual(['pub']);
    expect(filterCommands(all, 'reel pronto').map((c) => c.id)).toEqual(['reel']);
    expect(filterCommands(all, 'reel campanha')).toEqual([]);
  });

  it('vazio mostra tudo até o limite', () => {
    expect(filterCommands(all, '  ', 2)).toHaveLength(2);
  });
});
