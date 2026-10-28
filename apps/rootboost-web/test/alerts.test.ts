import { describe, expect, it } from 'vitest';
import type { OpsAlert } from 'src/types/api';
import { badgeText, formatDateTime } from 'src/utils/alerts';

const alert = (): OpsAlert => ({
  id: crypto.randomUUID(), kind: 'MetaConnection', key: 'meta:medlycare:Instagram', brandKey: 'medlycare',
  message: 'token vencido', count: 1, firstSeenAt: new Date().toISOString(), lastSeenAt: new Date().toISOString(),
});

describe('avisos de operação', () => {
  it('contador some sem aviso e trava em 9+', () => {
    expect(badgeText([])).toBeNull();
    expect(badgeText([alert(), alert()])).toBe('2');
    expect(badgeText(Array.from({ length: 12 }, alert))).toBe('9+');
  });

  it('data e hora curtas no fuso local', () => {
    expect(formatDateTime(new Date(2026, 9, 27, 8, 5).toISOString())).toBe('27/10 08:05');
  });
});
