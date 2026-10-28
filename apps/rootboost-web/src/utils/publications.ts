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

export type AgendaView = 'queue' | 'week' | 'month';
export type BundleStage = 'needs' | 'scheduled' | 'published';

/** Posts do mesmo reel em canais diferentes (mesmo horário) andam juntos na fila e na prévia. */
export interface PostBundle {
  key: string;
  scheduledFor: string;
  title: string;
  stage: BundleStage;
  items: Publication[];
}

const channelOrder: Record<string, number> = { Instagram: 0, Facebook: 1, LinkedIn: 2 };

/** Primeira linha da legenda, curta: serve de nome do post na fila e no calendário. */
export function postTitle(caption: string, max = 48): string {
  const line = caption.split('\n').map((l) => l.trim()).find((l) => l.length > 0) ?? '';
  return line.length > max ? `${line.slice(0, max - 1).trimEnd()}…` : line;
}

/** Precisa de você vence agendado, que vence publicado: o grupo mostra o que falta fazer. */
export function stageOf(items: Publication[]): BundleStage {
  if (items.some((p) => p.status === 'Draft' || p.status === 'Failed')) return 'needs';
  if (items.some((p) => p.status === 'Approved' || p.status === 'Publishing')) return 'scheduled';
  return 'published';
}

/** Junta por minuto agendado; dentro do grupo, Instagram, Facebook e LinkedIn nessa ordem. */
export function bundlePosts(items: Publication[]): PostBundle[] {
  const byKey = new Map<string, Publication[]>();
  for (const p of items) {
    const key = new Date(p.scheduledFor).toISOString().slice(0, 16);
    byKey.set(key, [...(byKey.get(key) ?? []), p]);
  }
  return [...byKey.entries()]
    .map(([key, list]) => {
      const sorted = [...list].sort((a, b) => (channelOrder[a.channel] ?? 9) - (channelOrder[b.channel] ?? 9));
      return { key, scheduledFor: sorted[0]!.scheduledFor, title: postTitle(sorted[0]!.caption), stage: stageOf(sorted), items: sorted };
    })
    .sort((a, b) => a.scheduledFor.localeCompare(b.scheduledFor));
}

/** As três seções da fila. Publicados vêm do mais recente pro mais antigo e só os últimos `keepPublished`. */
export function queueSections(bundles: PostBundle[], keepPublished = 6): Record<BundleStage, PostBundle[]> {
  return {
    needs: bundles.filter((b) => b.stage === 'needs'),
    scheduled: bundles.filter((b) => b.stage === 'scheduled'),
    published: bundles.filter((b) => b.stage === 'published').reverse().slice(0, keepPublished),
  };
}

/** 6 semanas (42 dias) a partir da segunda da semana do dia 1: grade fixa, não pula de altura entre meses. */
export function monthGrid(anchor: Date): Date[] {
  const first = startOfWeek(new Date(anchor.getFullYear(), anchor.getMonth(), 1));
  return Array.from({ length: 42 }, (_, i) => addDays(first, i));
}

export function stageColor(s: BundleStage): string {
  return { needs: 'warning', scheduled: 'primary', published: 'positive' }[s];
}

export function isVideo(url: string): boolean {
  try {
    return /\.(mp4|mov)$/i.test(new URL(url).pathname);
  } catch {
    return false;
  }
}
