import { describe, expect, it } from 'vitest';
import type { Publication } from 'src/types/api';
import { canEdit, fromLocalInput, groupByDay, localDayKey, startOfWeek, toLocalInput } from 'src/utils/publications';

const pub = (over: Partial<Publication>): Publication => ({
  id: crypto.randomUUID(), brandKey: 'medlycare', channel: 'Instagram', mediaUrl: 'https://x/a.mp4', caption: 'c',
  captionVersion: 1, scheduledFor: new Date(2026, 9, 21, 10, 0).toISOString(), status: 'Draft', approvedBy: null,
  approvedAt: null, approvedCaptionVersion: null, externalId: null, publishedUrl: null, error: null, attempts: 0, ...over,
});

describe('agenda de publicações', () => {
  it('semana começa na segunda, inclusive vindo de um domingo', () => {
    expect(localDayKey(startOfWeek(new Date(2026, 9, 25)))).toBe('2026-10-19'); // domingo 25/10
    expect(localDayKey(startOfWeek(new Date(2026, 9, 19)))).toBe('2026-10-19'); // a própria segunda
  });

  it('agrupa por dia local e ordena por horário', () => {
    const week = startOfWeek(new Date(2026, 9, 21));
    const days = groupByDay(week, [
      pub({ id: 'tarde', scheduledFor: new Date(2026, 9, 21, 18, 0).toISOString() }),
      pub({ id: 'manha', scheduledFor: new Date(2026, 9, 21, 9, 0).toISOString() }),
      pub({ id: 'domingo-noite', scheduledFor: new Date(2026, 9, 25, 23, 30).toISOString() }),
    ]);
    expect(days).toHaveLength(7);
    expect(days[2]?.items.map((p) => p.id)).toEqual(['manha', 'tarde']);
    expect(days[6]?.items.map((p) => p.id)).toEqual(['domingo-noite']);
  });

  it('publicado ou publicando não pode ser editado', () => {
    expect(canEdit(pub({ status: 'Published' }))).toBe(false);
    expect(canEdit(pub({ status: 'Publishing' }))).toBe(false);
    expect(canEdit(pub({ status: 'Approved' }))).toBe(true);
  });

  it('ida e volta do campo de data e hora preserva o horário local', () => {
    expect(toLocalInput(fromLocalInput('2026-10-21T09:30'))).toBe('2026-10-21T09:30');
  });
});
