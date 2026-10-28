import { describe, expect, it } from 'vitest';
import { proposeSlots } from 'src/utils/calendar';
import { localDayKey } from 'src/utils/publications';

const keys = (d: Date[]) => d.map((x) => `${localDayKey(x)} ${x.getHours()}h`);

describe('próxima data de post', () => {
  it('segunda, quarta e sexta às 12h, só depois de agora', () => {
    const tuesday = new Date(2026, 9, 27, 9, 0); // ter 27/10
    expect(keys(proposeSlots(tuesday, 3, new Set()))).toEqual(['2026-10-28 12h', '2026-10-30 12h', '2026-11-03 12h']);
  });

  it('Finados (seg 02/11) vai pra terça 03/11', () => {
    const friday = new Date(2026, 9, 30, 13, 0); // sex 30/10 depois do meio-dia
    expect(keys(proposeSlots(friday, 2, new Set()))).toEqual(['2026-11-03 12h', '2026-11-04 12h']);
  });

  it('dia que já tem post fica de fora', () => {
    const monday = new Date(2026, 9, 26, 8, 0);
    expect(keys(proposeSlots(monday, 2, new Set(['2026-10-26', '2026-10-28'])))).toEqual(['2026-10-30 12h', '2026-11-03 12h']);
  });

  it('no próprio dia de post antes do meio-dia vale o mesmo dia', () => {
    expect(keys(proposeSlots(new Date(2026, 9, 28, 10, 0), 1, new Set()))).toEqual(['2026-10-28 12h']);
  });
});
