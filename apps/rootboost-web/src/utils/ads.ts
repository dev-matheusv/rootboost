import type { AdsSummary } from 'src/types/api';

/** Moeda única do resumo; com mais de uma, os totais não valem e a tela mostra só por campanha. */
export function singleCurrency(summary: AdsSummary | null): string | null {
  if (!summary) return null;
  if (summary.currencies.length === 1) return summary.currencies[0] ?? null;
  return summary.currencies.length === 0 ? 'BRL' : null;
}

/** Altura relativa (0 a 1) de cada dia no gráfico de gasto. */
export function spendBars(summary: AdsSummary | null): { date: string; spend: number; conversions: number; height: number }[] {
  const days = summary?.daily ?? [];
  const max = Math.max(0, ...days.map((d) => d.metrics.spend));
  return days.map((d) => ({
    date: d.date,
    spend: d.metrics.spend,
    conversions: d.metrics.conversions,
    height: max > 0 ? d.metrics.spend / max : 0,
  }));
}

/** Situação da conexão pra marca: sem leitor, sem conta pra marca, ou ligada. */
export function connectionState(reader: string, brandAccounts: number): 'off' | 'noAccount' | 'fake' | 'live' {
  if (reader === 'None') return 'off';
  if (brandAccounts === 0) return 'noAccount';
  return reader === 'Fake' ? 'fake' : 'live';
}
