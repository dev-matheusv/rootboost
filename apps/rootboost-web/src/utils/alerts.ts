import type { OpsAlert } from 'src/types/api';

/** Data e hora local curtas, ex.: "27/10 20:49". */
export function formatDateTime(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(d.getDate())}/${pad(d.getMonth() + 1)} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** Texto do contador do menu: some sem aviso, trava em "9+" pra não estourar o espaço. */
export function badgeText(alerts: OpsAlert[]): string | null {
  if (alerts.length === 0) return null;
  return alerts.length > 9 ? '9+' : String(alerts.length);
}
