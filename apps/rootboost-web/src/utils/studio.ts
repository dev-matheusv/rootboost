import type { CreativeJobStatus } from 'src/types/api';

/** Um trecho do vídeo como a pessoa vai ver e ouvir: o que aparece na tela e a frase falada naquele momento. */
export interface ScriptSegment {
  label: string;
  title: string;
  sub: string;
  fala: string | null;
}

type Pair = [string, string];
interface Roteiro {
  abertura?: Pair | null;
  fecho?: Pair | null;
  blocos?: { titulo?: string; sub?: string }[];
}

/**
 * Junta roteiro e falas na ordem do vídeo (abertura?, blocos, final), do mesmo jeito que o worker monta.
 * Serve pra ler o roteiro sem abrir JSON. Falta de fala aparece como null (a API recusa, mas a prévia não quebra).
 */
export function scriptSegments(roteiro: unknown, falas: unknown): ScriptSegment[] {
  if (!roteiro || typeof roteiro !== 'object') return [];
  const r = roteiro as Roteiro;
  const lines = Array.isArray(falas) ? falas.map((f) => (typeof f === 'string' ? f : '')) : [];
  const out: ScriptSegment[] = [];
  let i = 0;
  const next = () => lines[i++] ?? null;
  if (Array.isArray(r.abertura)) out.push({ label: 'opening', title: r.abertura[0] ?? '', sub: r.abertura[1] ?? '', fala: next() });
  (r.blocos ?? []).forEach((b, n) => out.push({ label: `block:${n + 1}`, title: b.titulo ?? '', sub: b.sub ?? '', fala: next() }));
  out.push({ label: 'closing', title: Array.isArray(r.fecho) ? r.fecho[0] : '', sub: Array.isArray(r.fecho) ? r.fecho[1] : '', fala: next() });
  return out;
}

/** Fala por minuto calma (~2,4 palavras por segundo, regra do pacote): estimativa da duração da voz. */
export function estimateSeconds(falas: unknown): number {
  if (!Array.isArray(falas)) return 0;
  const words = falas.reduce<number>((n, f) => n + (typeof f === 'string' ? f.trim().split(/\s+/).filter(Boolean).length : 0), 0);
  return Math.round(words / 2.4);
}

/** Lê JSON colado ou de arquivo; erro de sintaxe vira mensagem em vez de exceção. */
export function parseJson(text: string): { value: unknown; error: string | null } {
  try {
    return { value: JSON.parse(text), error: null };
  } catch (e) {
    return { value: null, error: e instanceof Error ? e.message : String(e) };
  }
}

/** Etapa na barra do topo: roteiro, fila, render, revisão, posts. */
export function jobStep(s: CreativeJobStatus): number {
  return { Draft: 0, ScriptReady: 0, Failed: 1, Queued: 1, Rendering: 2, Review: 3, Done: 4 }[s];
}

export function jobColor(s: CreativeJobStatus): string {
  return { Draft: 'grey-6', ScriptReady: 'secondary', Queued: 'info', Rendering: 'primary', Review: 'warning', Done: 'positive', Failed: 'negative' }[s];
}

/** Job em andamento no worker: a tela atualiza sozinha enquanto houver algum. */
export function isActive(s: CreativeJobStatus): boolean {
  return s === 'Queued' || s === 'Rendering';
}

/** Edição em texto de um trecho: o que aparece na tela (título e subtítulo) e o que a voz fala. */
export interface SegmentEdit {
  label: string;
  title: string;
  sub: string;
  fala: string;
}

/**
 * Aplica a edição feita em texto de volta nos contratos (roteiro.json e falas.json), sem a pessoa ver JSON.
 * Mantém tudo o que não é texto (trechos de imagem, cena, nome) do jeito que estava.
 */
export function applySegmentEdits(roteiro: unknown, edits: SegmentEdit[]): { roteiro: Record<string, unknown>; falas: string[] } {
  const r = JSON.parse(JSON.stringify(roteiro ?? {})) as Record<string, unknown> & {
    abertura?: [string, string] | null;
    fecho?: [string, string] | null;
    blocos?: Record<string, unknown>[];
  };
  const falas: string[] = [];
  for (const e of edits) {
    falas.push(e.fala.trim());
    if (e.label === 'opening') r.abertura = [e.title.trim(), e.sub.trim()];
    else if (e.label === 'closing') {
      if (e.title.trim() || e.sub.trim() || r.fecho) r.fecho = [e.title.trim(), e.sub.trim()];
    } else {
      const i = Number(e.label.split(':')[1]) - 1;
      const b = r.blocos?.[i];
      if (b) {
        b.titulo = e.title.trim();
        b.sub = e.sub.trim();
      }
    }
  }
  return { roteiro: r, falas };
}

/** Cena que a IA descreveu pro bloco (o que gravar), quando houver. */
export function sceneOf(roteiro: unknown, label: string): string {
  if (!label.startsWith('block:') || !roteiro || typeof roteiro !== 'object') return '';
  const blocos = (roteiro as { blocos?: { cena?: unknown }[] }).blocos ?? [];
  const cena = blocos[Number(label.split(':')[1]) - 1]?.cena;
  return typeof cena === 'string' ? cena : '';
}
