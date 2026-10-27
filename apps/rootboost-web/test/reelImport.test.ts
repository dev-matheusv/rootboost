import { describe, expect, it } from 'vitest';
import { edges, parseReelText } from 'src/utils/reelImport';

const RULE = '-'.repeat(66);
const SAMPLE = [
  '='.repeat(66),
  'INSTAGRAM (Reels)',
  RULE,
  'Ficha de harmonização sem papel. 💉',
  '',
  '#estetica #medlycare',
  RULE,
  '',
  'Texto alternativo:',
  'Gravação da tela do MedlyCare.',
  '',
  'Sugestão de enquete para os stories:',
  '"Onde você registra?"',
  '='.repeat(66),
  'FACEBOOK',
  RULE,
  'Texto do Facebook com link medlycare.com.br',
  RULE,
  'LINKEDIN',
  RULE,
  'Texto B2B.',
  '#gestao',
  RULE,
].join('\r\n');

describe('importar reel pronto', () => {
  it('separa a legenda de cada canal sem levar texto alternativo nem enquete', () => {
    const b = parseReelText(SAMPLE);
    expect(b.errors).toEqual([]);
    expect(b.instagram).toBe('Ficha de harmonização sem papel. 💉\n\n#estetica #medlycare');
    expect(b.facebook).toBe('Texto do Facebook com link medlycare.com.br');
    expect(b.linkedin).toBe('Texto B2B.\n#gestao');
    expect(b.instagram).not.toContain('Texto alternativo');
  });

  it('recusa bloco com conteúdo misturado em vez de adivinhar', () => {
    const broken = ['INSTAGRAM', RULE, 'legenda', 'Texto alternativo: x', RULE].join('\n');
    const b = parseReelText(broken);
    expect(b.instagram).toBeUndefined();
    expect(b.errors[0]).toContain('instagram');
  });

  it('avisa quando não há bloco nenhum ou falta a linha de fechamento', () => {
    expect(parseReelText('só texto solto').errors[0]).toContain('nenhum bloco');
    expect(parseReelText(['LINKEDIN', RULE, 'sem fim'].join('\n')).errors[0]).toContain('linkedin');
  });

  it('mostra começo e fim pra conferência', () => {
    expect(edges('Primeira\n\nmeio\n#tags')).toEqual({ first: 'Primeira', last: '#tags' });
  });
});
