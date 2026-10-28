/** Saudação pela hora local: até 12h bom dia, até 18h boa tarde, depois boa noite. */
export function greetingKey(hour: number): 'morning' | 'afternoon' | 'evening' {
  if (hour >= 5 && hour < 12) return 'morning';
  if (hour >= 12 && hour < 18) return 'afternoon';
  return 'evening';
}

/** Primeiro nome, pra saudação não ficar formal. */
export function firstName(name: string): string {
  return name.trim().split(/\s+/)[0] ?? '';
}

/** Evento que o menu escuta pra abrir os avisos de qualquer tela. */
export const OPEN_ALERTS_EVENT = 'rb:open-alerts';
