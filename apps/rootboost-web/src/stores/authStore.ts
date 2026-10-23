import { defineStore } from 'pinia';
import { isAxiosError } from 'axios';
import { api } from 'src/services/api';
import { clearSession, readSession, writeSession, type Session } from 'src/services/session';
import type { LoginResponse } from 'src/types/api';

/** Motivo de falha do login, já pronto pra virar mensagem traduzida na tela. */
export type LoginFailure = 'invalid' | 'notConfigured' | 'tooMany' | 'offline';

export const useAuthStore = defineStore('auth', {
  state: () => ({
    session: readSession(),
    isLoading: false,
  }),

  getters: {
    isAuthenticated: (state) => state.session !== null,
    userName: (state) => state.session?.name ?? '',
    userEmail: (state) => state.session?.email ?? '',
  },

  actions: {
    async login(email: string, password: string): Promise<LoginFailure | null> {
      this.isLoading = true;
      try {
        const { data } = await api.post<LoginResponse>('/auth/login', { email, password });
        const session: Session = { token: data.token, expiresAtUtc: data.expiresAtUtc, name: data.name, email: data.email };
        writeSession(session);
        this.session = session;
        return null;
      } catch (error) {
        return toLoginFailure(error);
      } finally {
        this.isLoading = false;
      }
    },

    logout() {
      clearSession();
      this.session = null;
    },
  },
});

export function toLoginFailure(error: unknown): LoginFailure {
  if (!isAxiosError(error) || !error.response) return 'offline';
  switch (error.response.status) {
    case 401:
      return 'invalid';
    case 503:
      return 'notConfigured';
    case 429:
      return 'tooMany';
    default:
      return 'offline';
  }
}
