// Sessão do Console. O token fica no sessionStorage (some ao fechar a aba) por enquanto.
// Próximo passo, já previsto: token só em memória + renovação por cookie httpOnly, como o
// MC-322 do MedlyCare. Isso pede o endpoint de renovação no backend.

const KEY = 'rootboost.session';

export interface Session {
  token: string;
  expiresAtUtc: string;
  name: string;
  email: string;
}

export function readSession(now: Date = new Date()): Session | null {
  try {
    const raw = sessionStorage.getItem(KEY);
    if (!raw) return null;
    const session = JSON.parse(raw) as Session;
    if (!session.token || new Date(session.expiresAtUtc) <= now) {
      sessionStorage.removeItem(KEY);
      return null;
    }
    return session;
  } catch {
    return null;
  }
}

export function writeSession(session: Session): void {
  sessionStorage.setItem(KEY, JSON.stringify(session));
}

export function clearSession(): void {
  sessionStorage.removeItem(KEY);
}
