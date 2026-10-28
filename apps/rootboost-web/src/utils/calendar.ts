import { addDays, localDayKey } from 'src/utils/publications';

/**
 * Feriados nacionais (AAAA-MM-DD) de 2026 e 2027. Regra do pacote do marketing: post em feriado vai pro dia
 * seguinte (ex.: Finados 02/11 vira 03/11). Lista fixa de propósito, conferida à mão; atualizar no fim de 2027.
 */
export const HOLIDAYS = new Set([
  '2026-01-01', '2026-02-16', '2026-02-17', '2026-04-03', '2026-04-21', '2026-05-01', '2026-06-04', '2026-09-07',
  '2026-10-12', '2026-11-02', '2026-11-15', '2026-11-20', '2026-12-25',
  '2027-01-01', '2027-02-08', '2027-02-09', '2027-03-26', '2027-04-21', '2027-05-01', '2027-05-27', '2027-09-07',
  '2027-10-12', '2027-11-02', '2027-11-15', '2027-11-20', '2027-12-25',
]);

/** Dias de post (segunda, quarta e sexta) e hora (12h, almoço do profissional de saúde). Padrão do MedlyCare. */
export const POST_WEEKDAYS = [1, 3, 5];
export const POST_HOUR = 12;

/**
 * Próximas datas livres pra postar, a partir de `from` (só depois dele): dia de post às 12h, feriado empurra pro
 * próximo dia útil que não é feriado, e dia que já tem post fica de fora.
 */
export function proposeSlots(from: Date, count: number, occupied: Set<string>, holidays: Set<string> = HOLIDAYS): Date[] {
  const out: Date[] = [];
  const taken = new Set(occupied);
  let day = new Date(from.getFullYear(), from.getMonth(), from.getDate());
  for (let guard = 0; out.length < count && guard < 400; guard++, day = addDays(day, 1)) {
    if (!POST_WEEKDAYS.includes(day.getDay())) continue;
    let slot = day;
    while (holidays.has(localDayKey(slot)) || slot.getDay() === 0 || slot.getDay() === 6) slot = addDays(slot, 1);
    const at = new Date(slot.getFullYear(), slot.getMonth(), slot.getDate(), POST_HOUR, 0, 0, 0);
    const key = localDayKey(at);
    if (at <= from || taken.has(key)) continue;
    taken.add(key);
    out.push(at);
  }
  return out;
}
