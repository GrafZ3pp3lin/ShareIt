import { describe, expect, it } from 'vitest';
import { formatBytes } from './format-bytes.ts';

describe('formatBytes', () => {
  it('keeps small values in bytes', () => {
    expect(formatBytes(0)).toBe('0 B');
    expect(formatBytes(1023)).toBe('1023 B');
  });

  it('shows one decimal below 10 KB and rounds from 10 KB up', () => {
    expect(formatBytes(1024)).toBe('1.0 KB');
    expect(formatBytes(9.5 * 1024)).toBe('9.5 KB');
    expect(formatBytes(10 * 1024)).toBe('10 KB');
    expect(formatBytes(10.4 * 1024)).toBe('10 KB');
  });

  it('uses megabytes from 1 MB up', () => {
    expect(formatBytes(1024 * 1024)).toBe('1.0 MB');
    expect(formatBytes(9.5 * 1024 * 1024)).toBe('9.5 MB');
    expect(formatBytes(10.6 * 1024 * 1024)).toBe('11 MB');
  });
});
