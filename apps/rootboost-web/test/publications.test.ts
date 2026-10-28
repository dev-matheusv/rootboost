import { describe, expect, it } from 'vitest';
import type { Publication } from 'src/types/api';
import {
  bundlePosts, canEdit, fromLocalInput, groupByDay, localDayKey, monthGrid, postTitle, queueSections, stageOf, startOfWeek, toLocalInput,
} from 'src/utils/publications';

const pub = (over: Partial<Publication>): Publication => ({
  id: crypto.randomUUID(), brandKey: 'medlycare', channel: 'Instagram', mediaUrl: 'https://x/a.mp4', caption: 'c',
  captionVersion: 1, scheduledFor: new Date(2026, 9, 21, 10, 0).toISOString(), status: 'Draft', approvedBy: null,
  approvedAt: null, approvedCaptionVersion: null, externalId: null, publishedUrl: null, error: null, attempts: 0,
  coverUrl: null, thumbOffsetMs: null, shareToFeed: true, ...over,
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

describe('fila de aprovação', () => {
  const at = (h: number, d = 30) => new Date(2026, 9, d, h, 0).toISOString();

  it('junta os canais do mesmo horário e põe Instagram primeiro', () => {
    const [b] = bundlePosts([
      pub({ channel: 'LinkedIn', scheduledFor: at(12) }),
      pub({ channel: 'Instagram', scheduledFor: at(12), caption: '\nR1 pacote completo\nresto' }),
      pub({ channel: 'Facebook', scheduledFor: at(12) }),
    ]);
    expect(b?.items.map((p) => p.channel)).toEqual(['Instagram', 'Facebook', 'LinkedIn']);
    expect(b?.title).toBe('R1 pacote completo');
  });

  it('um canal pendente põe o grupo em "precisa de você"', () => {
    expect(stageOf([pub({ status: 'Approved' }), pub({ status: 'Draft' })])).toBe('needs');
    expect(stageOf([pub({ status: 'Approved' }), pub({ status: 'Failed' })])).toBe('needs');
    expect(stageOf([pub({ status: 'Approved' }), pub({ status: 'Published' })])).toBe('scheduled');
    expect(stageOf([pub({ status: 'Published' })])).toBe('published');
  });

  it('separa as seções e mostra os publicados mais recentes primeiro', () => {
    const s = queueSections(bundlePosts([
      pub({ status: 'Published', scheduledFor: at(9, 20) }),
      pub({ status: 'Published', scheduledFor: at(9, 24) }),
      pub({ status: 'Draft', scheduledFor: at(12) }),
      pub({ status: 'Approved', scheduledFor: at(12, 28) }),
    ]));
    expect(s.needs).toHaveLength(1);
    expect(s.scheduled).toHaveLength(1);
    expect(s.published.map((b) => new Date(b.scheduledFor).getDate())).toEqual([24, 20]);
  });

  it('título corta legenda longa', () => {
    expect(postTitle('a'.repeat(60), 10)).toBe('aaaaaaaaa…');
  });

  it('grade do mês tem 42 dias e começa na segunda', () => {
    const g = monthGrid(new Date(2026, 9, 15));
    expect(g).toHaveLength(42);
    expect(localDayKey(g[0]!)).toBe('2026-09-28');
    expect(g[0]!.getDay()).toBe(1);
  });
});
