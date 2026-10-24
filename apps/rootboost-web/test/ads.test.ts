import { describe, expect, it } from 'vitest';
import type { AdsSummary } from 'src/types/api';
import { connectionState, singleCurrency, spendBars } from 'src/utils/ads';

const m = (spend: number, conversions = 0) => ({
  spend, impressions: 0, clicks: 0, reach: 0, conversions, ctr: null, cpc: null, cpm: null, cpa: null,
});

const summary = (currencies: string[], daily: number[]): AdsSummary => ({
  brandKey: 'medlycare', since: '2026-10-18', until: '2026-10-24', currencies,
  totals: m(0), campaigns: [], alerts: [],
  daily: daily.map((s, i) => ({ date: `2026-10-${18 + i}`, metrics: m(s) })),
});

describe('anúncios', () => {
  it('moeda misturada não tem total', () => {
    expect(singleCurrency(summary(['BRL', 'USD'], []))).toBeNull();
    expect(singleCurrency(summary(['USD'], []))).toBe('USD');
  });

  it('barras proporcionais ao maior gasto, sem dividir por zero', () => {
    expect(spendBars(summary(['BRL'], [50, 100])).map((b) => b.height)).toEqual([0.5, 1]);
    expect(spendBars(summary(['BRL'], [0, 0])).map((b) => b.height)).toEqual([0, 0]);
  });

  it('estado da conexão', () => {
    expect(connectionState('None', 0)).toBe('off');
    expect(connectionState('Fake', 0)).toBe('noAccount');
    expect(connectionState('Fake', 1)).toBe('fake');
    expect(connectionState('Meta', 1)).toBe('live');
  });
});
