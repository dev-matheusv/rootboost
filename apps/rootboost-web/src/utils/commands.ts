/** Item da busca rápida (Ctrl+K): página, ação ou post. */
export interface Command {
  id: string;
  label: string;
  group: string;
  hint?: string;
  keywords?: string;
  run: () => void;
}

/** Sem acento e minúsculo: "publicacoes" acha "Publicações". */
export function normalize(s: string): string {
  return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
}

/** Todas as palavras digitadas precisam aparecer (em qualquer ordem) no nome, no grupo ou nas palavras-chave. */
export function filterCommands(commands: Command[], query: string, limit = 12): Command[] {
  const words = normalize(query).split(/\s+/).filter(Boolean);
  if (!words.length) return commands.slice(0, limit);
  return commands
    .filter((c) => {
      const text = normalize(`${c.label} ${c.group} ${c.keywords ?? ''}`);
      return words.every((w) => text.includes(w));
    })
    .slice(0, limit);
}
