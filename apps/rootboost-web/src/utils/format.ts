// Formatação de número e data pra tela. Funções puras, testadas em test/format.test.ts.

export function formatMoney(value: number, currency: string, locale = 'pt-BR'): string {
  return new Intl.NumberFormat(locale, { style: 'currency', currency: currency || 'USD' }).format(value);
}

export function formatPercent(fraction: number, locale = 'pt-BR'): string {
  return new Intl.NumberFormat(locale, { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(fraction);
}

export function formatInt(value: number, locale = 'pt-BR'): string {
  return new Intl.NumberFormat(locale).format(value);
}

export function formatDateTime(iso: string, locale = 'pt-BR'): string {
  return new Intl.DateTimeFormat(locale, { dateStyle: 'short', timeStyle: 'short' }).format(new Date(iso));
}

/** Soma valores por moeda: pedidos podem vir em USD e EUR, e somar moedas diferentes mente. */
/** Data pura (AAAA-MM-DD) sem fuso: não pode virar o dia anterior no Brasil. */
export function formatDate(isoDate: string, locale = 'pt-BR'): string {
  const [y = 1970, m = 1, d = 1] = isoDate.slice(0, 10).split('-').map(Number);
  return new Intl.DateTimeFormat(locale, { dateStyle: 'short', timeZone: 'UTC' }).format(new Date(Date.UTC(y, m - 1, d)));
}

/** Soma valores por moeda: pedidos podem vir em USD e EUR, e somar moedas diferentes mente. */
export function sumByCurrency<T>(items: T[], amount: (item: T) => number, currency: (item: T) => string): Record<string, number> {
  return items.reduce<Record<string, number>>((acc, item) => {
    const code = currency(item) || 'USD';
    acc[code] = (acc[code] ?? 0) + amount(item);
    return acc;
  }, {});
}
