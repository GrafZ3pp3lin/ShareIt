import { describe, expect, it } from 'vitest';
import {
  SHORT_CODE_ALPHABET,
  SHORT_CODE_LENGTH,
  SHORT_CODE_LOCATOR_LENGTH,
  SHORT_CODE_SECRET_LENGTH,
  formatShortCode,
  generateCodeSecret,
  normalizeShortCode,
  splitShortCode
} from './short-code.ts';

const PATTERN = /^[2-9A-HJ-KM-NP-Z]{3}$/;

describe('short codes', () => {
  it('uses a 31-character unambiguous alphabet', () => {
    expect(SHORT_CODE_ALPHABET).toHaveLength(31);
    expect(new Set(SHORT_CODE_ALPHABET).size).toBe(31);
    expect(SHORT_CODE_ALPHABET).not.toMatch(/[01IOL]/);
  });

  it('splits the user-facing code into a 3+3 locator and secret', () => {
    expect(SHORT_CODE_LENGTH).toBe(6);
    expect(SHORT_CODE_LOCATOR_LENGTH).toBe(3);
    expect(SHORT_CODE_SECRET_LENGTH).toBe(3);
    expect(splitShortCode('ABC234')).toEqual({ locator: 'ABC', secret: '234' });
  });

  it('normalizes case, hyphens and whitespace', () => {
    expect(normalizeShortCode(' abc-234 ')).toBe('ABC234');
    expect(normalizeShortCode('a b c 2 3 4')).toBe('ABC234');
  });

  it('rejects ambiguous or wrong-length values', () => {
    expect(normalizeShortCode('')).toBeNull();
    expect(normalizeShortCode('ABC23')).toBeNull();
    expect(normalizeShortCode('ABC2345')).toBeNull();
    expect(normalizeShortCode('ABC23O')).toBeNull();
    expect(normalizeShortCode('ABC23I')).toBeNull();
    expect(normalizeShortCode('ABC23L')).toBeNull();
    expect(normalizeShortCode('ABC23ſ')).toBeNull();
    expect(normalizeShortCode('ABC2ß')).toBeNull();
    expect(normalizeShortCode('A'.repeat(65))).toBeNull();
  });

  it('generates three secret characters from the alphabet', () => {
    for (let index = 0; index < 100; index += 1) {
      expect(generateCodeSecret()).toMatch(PATTERN);
    }
  });

  it('formats a six-character code for display', () => {
    expect(formatShortCode('ABC234')).toBe('ABC-234');
    expect(formatShortCode('ABC23')).toBe('ABC23');
  });
});
