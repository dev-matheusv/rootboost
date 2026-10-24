import { describe, expect, it } from 'vitest';
import { formatDate, formatInt, formatMoney, formatPercent, sumByCurrency } from 'src/utils/format';
import { statusColor } from 'src/utils/orderStatus';

describe('format', () => {
  it('formata dinheiro na moeda do pedido', () => {
    expect(formatMoney(29.99, 'USD', 'en-US')).toBe('$29.99');
  });

  it('formata porcentagem com uma casa', () => {
    expect(formatPercent(0.0234, 'en-US')).toBe('2.3%');
  });

  it('formata inteiro com separador de milhar', () => {
    expect(formatInt(12345, 'en-US')).toBe('12,345');
  });

  it('nunca soma moedas diferentes', () => {
    const totals = sumByCurrency(
      [{ v: 10, c: 'USD' }, { v: 5, c: 'EUR' }, { v: 2.5, c: 'USD' }],
      (x) => x.v,
      (x) => x.c,
    );
    expect(totals).toEqual({ USD: 12.5, EUR: 5 });
  });
});

describe('statusColor', () => {
  it('pedido que falhou chama atenção em vermelho', () => {
    expect(statusColor('Failed')).toBe('negative');
  });

  it('status desconhecido cai no neutro', () => {
    expect(statusColor('Qualquer')).toBe('grey-6');
  });
});

import { brandParams } from 'src/stores/brandStore';

describe('brandParams', () => {
  it('sem marca escolhida não filtra', () => {
    expect(brandParams('')).toEqual({});
  });

  it('com marca manda o filtro pra API', () => {
    expect(brandParams('medlycare')).toEqual({ brand: 'medlycare' });
  });
});

describe('formatDate', () => {
  it('não volta um dia por causa do fuso', () => {
    expect(formatDate('2026-10-30', 'pt-BR')).toBe('30/10/2026');
  });
});
