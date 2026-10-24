import type { Publication, PublicationStatus } from 'src/types/api';

/** Segunda-feira 00:00 (hora local) da semana que contém a data. */
export function startOfWeek(d: Date): Date {
  const x = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const offset = (x.getDay() + 6) % 7; // domingo vira 6
  x.setDate(x.getDate() - offset);
  return x;
}

export function addDays(d: Date, n: number): Date {
  const x = new Date(d);
  x.setDate(x.getDate() + n);
  return x;
}

/** Chave AAAA-MM-DD no fuso local (o post das 23h de domingo é de domingo, não de segunda). */
export function localDayKey(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

/** Os 7 dias da semana com os posts de cada dia, em ordem de horário. */
export function groupByDay(weekStart: Date, items: Publication[]): { key: string; date: Date; items: Publication[] }[] {
  const days = Array.from({ length: 7 }, (_, i) => {
    const date = addDays(weekStart, i);
    return { key: localDayKey(date), date, items: [] as Publication[] };
  });
  const byKey = new Map(days.map((d) => [d.key, d]));
  for (const p of [...items].sort((a, b) => a.scheduledFor.localeCompare(b.scheduledFor))) {
    byKey.get(localDayKey(new Date(p.scheduledFor)))?.items.push(p);
  }
  return days;
}

export function statusColor(s: PublicationStatus): string {
  return { Draft: 'grey-6', Approved: 'primary', Publishing: 'warning', Published: 'positive', Failed: 'negative' }[s];
}

/** Só rascunho e aprovado podem mudar (o domínio recusa o resto). */
export function canEdit(p: Publication): boolean {
  return p.status === 'Draft' || p.status === 'Approved' || p.status === 'Failed';
}

/** Valor pro input datetime-local a partir de ISO, no fuso local. */
export function toLocalInput(iso: string): string {
  const d = new Date(iso);
  return `${localDayKey(d)}T${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
}

/** datetime-local (sem fuso) vira ISO com o fuso do navegador, que é o de quem agenda. */
export function fromLocalInput(value: string): string {
  return new Date(value).toISOString();
}
