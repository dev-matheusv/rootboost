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
export function sumByCurrency<T>(items: T[], amount: (item: T) => number, currency: (item: T) => string): Record<string, number> {
  return items.reduce<Record<string, number>>((acc, item) => {
    const code = currency(item) || 'USD';
    acc[code] = (acc[code] ?? 0) + amount(item);
    return acc;
  }, {});
}
