import { describe, expect, it } from 'vitest';
import { firstName, greetingKey } from 'src/utils/overview';

describe('visão geral', () => {
  it('saudação pela hora', () => {
    expect(greetingKey(8)).toBe('morning');
    expect(greetingKey(12)).toBe('afternoon');
    expect(greetingKey(19)).toBe('evening');
    expect(greetingKey(2)).toBe('evening');
  });

  it('primeiro nome', () => {
    expect(firstName('  Matheus Vinicius ')).toBe('Matheus');
    expect(firstName('')).toBe('');
  });
});
