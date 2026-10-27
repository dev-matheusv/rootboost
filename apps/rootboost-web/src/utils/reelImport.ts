/**
 * Lê o .txt de um reel pronto e separa a legenda de cada canal. Formato dos .txt do MedlyCare:
 *
 *   INSTAGRAM (Reels)
 *   ------------------
 *   legenda...
 *   ------------------
 *
 * Estrito de propósito: o 1º post saiu com o texto alternativo no lugar da legenda (24/10/2026).
 * Bloco com "Texto alternativo" ou "enquete" dentro é recusado em vez de adivinhado.
 */
export type ReelChannel = 'instagram' | 'facebook' | 'linkedin';

export interface ReelBlocks {
  instagram?: string;
  facebook?: string;
  linkedin?: string;
  errors: string[];
}

const HEADERS: Record<ReelChannel, RegExp> = {
  instagram: /^INSTAGRAM\b/,
  facebook: /^FACEBOOK\b/,
  linkedin: /^LINKEDIN\b/,
};

const isRule = (line: string) => /^-{10,}$/.test(line.trim());

export function parseReelText(text: string): ReelBlocks {
  const lines = text.replace(/\r/g, '').split('\n');
  const out: ReelBlocks = { errors: [] };

  for (const channel of Object.keys(HEADERS) as ReelChannel[]) {
    const head = lines.findIndex((l) => HEADERS[channel].test(l.trim()));
    if (head < 0) continue;
    const start = lines.findIndex((l, i) => i > head && isRule(l));
    const end = start < 0 ? -1 : lines.findIndex((l, i) => i > start && isRule(l));
    if (start < 0 || end < 0) {
      out.errors.push(`${channel}: bloco sem as duas linhas de traços`);
      continue;
    }
    const caption = lines.slice(start + 1, end).join('\n').trim();
    if (!caption) {
      out.errors.push(`${channel}: legenda vazia`);
    } else if (/texto alternativo|enquete|^(INSTAGRAM|FACEBOOK|LINKEDIN)\b/im.test(caption)) {
      out.errors.push(`${channel}: o bloco tem outro conteúdo misturado (texto alternativo, enquete ou outro canal)`);
    } else {
      out[channel] = caption;
    }
  }

  if (!out.instagram && !out.facebook && !out.linkedin && out.errors.length === 0)
    out.errors.push('nenhum bloco INSTAGRAM, FACEBOOK ou LINKEDIN encontrado');
  return out;
}

/** Primeira e última linha, pra conferência rápida antes de criar. */
export function edges(caption: string): { first: string; last: string } {
  const ls = caption.split('\n').filter((l) => l.trim());
  return { first: ls[0] ?? '', last: ls[ls.length - 1] ?? '' };
}
