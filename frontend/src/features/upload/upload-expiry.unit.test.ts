import { describe, expect, it } from 'vitest';
import { cycleExpiryIndex, EXPIRY_OPTIONS } from './upload-expiry.ts';

describe('cycleExpiryIndex', () => {
  it('advances to the next index', () => {
    expect(cycleExpiryIndex(0)).toBe(1);
    expect(cycleExpiryIndex(1)).toBe(2);
  });

  it('wraps around to the first index', () => {
    expect(cycleExpiryIndex(EXPIRY_OPTIONS.length - 1)).toBe(0);
  });
});
