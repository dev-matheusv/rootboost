import { describe, expect, it } from 'vitest';
import { failHint } from 'src/utils/studioErrors';

describe('failHint', () => {
  it('liga os erros reais dos logs à ação que corrige', () => {
    expect(failHint('gravação contínua: não entrou no sistema com a conta de demonstração (a tela de login continuou aberta)')).toBe('demoLogin');
    expect(failHint('gravação do bloco 1: a tela mostrou dado pessoal real (CPF)')).toBe('personalData');
    expect(failHint('a voz saiu com mais de 3 palavras sem par em 4 sementes')).toBe('voiceWords');
    expect(failHint('gravação contínua: a IA parou: 4 cliques seguidos sem mudar a tela.')).toBe('stuckScreen');
    expect(failHint('Parado a pedido. Roteiro e voz escolhida continuam')).toBe('stopped');
    expect(failHint('o sistema da gravação não respondeu (https://app)')).toBe('systemDown');
    expect(failHint('algo novo que ninguém viu')).toBeNull();
    expect(failHint(null)).toBeNull();
  });
});
