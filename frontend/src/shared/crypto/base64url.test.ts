import { describe, expect, it } from 'vitest';
import { fromBase64Url, toBase64Url } from './base64url.ts';

describe('toBase64Url', () => {
  it('emits URL-safe base64 without padding', () => {
    expect(toBase64Url(new TextEncoder().encode('hello'))).toBe('aGVsbG8');
    expect(toBase64Url(new Uint8Array([0xfb, 0xff]))).toBe('-_8');
  });

  it('round-trips every byte value', () => {
    const bytes = Uint8Array.from({ length: 256 }, (_, index) => index);

    expect(fromBase64Url(toBase64Url(bytes))).toEqual(bytes);
  });
});

describe('fromBase64Url', () => {
  it('accepts values without padding', () => {
    expect(new TextDecoder().decode(fromBase64Url('aGVsbG8'))).toBe('hello');
  });
});
