import { afterEach, describe, expect, it } from 'vitest';
import { AxiosError, type AxiosResponse } from 'axios';
import { clearSession, readSession, writeSession } from 'src/services/session';
import { toLoginFailure } from 'src/stores/authStore';

const base = { token: 'tok', name: 'Dono', email: 'dono@rootboost.test' };

describe('session', () => {
  afterEach(() => clearSession());

  it('devolve a sessão enquanto o token vale', () => {
    writeSession({ ...base, expiresAtUtc: '2030-01-01T00:00:00Z' });
    expect(readSession(new Date('2029-12-31T00:00:00Z'))?.token).toBe('tok');
  });

  it('descarta a sessão vencida', () => {
    writeSession({ ...base, expiresAtUtc: '2020-01-01T00:00:00Z' });
    expect(readSession(new Date('2026-10-22T00:00:00Z'))).toBeNull();
    expect(sessionStorage.length).toBe(0);
  });

  it('ignora lixo no storage em vez de quebrar a tela', () => {
    sessionStorage.setItem('rootboost.session', '{nao-e-json');
    expect(readSession()).toBeNull();
  });
});

describe('toLoginFailure', () => {
  const withStatus = (status: number) =>
    new AxiosError('x', 'ERR', undefined, undefined, { status } as AxiosResponse);

  it.each([
    [401, 'invalid'],
    [503, 'notConfigured'],
    [429, 'tooMany'],
  ])('status %i vira %s', (status, expected) => {
    expect(toLoginFailure(withStatus(status))).toBe(expected);
  });

  it('sem resposta da API é falta de conexão', () => {
    expect(toLoginFailure(new AxiosError('Network Error'))).toBe('offline');
  });
});
