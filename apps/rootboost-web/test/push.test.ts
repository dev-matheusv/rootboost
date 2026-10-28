import { describe, expect, it } from 'vitest';
import { urlBase64ToUint8Array } from 'src/utils/push';

describe('push', () => {
  it('converte a chave VAPID (base64 url, sem preenchimento)', () => {
    expect(Array.from(urlBase64ToUint8Array('AQID-_8'))).toEqual([1, 2, 3, 251, 255]);
  });
});
