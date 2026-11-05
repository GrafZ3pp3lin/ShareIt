import { afterEach, describe, expect, it, vi } from 'vitest';
import { formatCreatedAt, packageLink, relativeSpan, shortPackageId } from './link-format.ts';

const NOW = Date.parse('2026-11-04T12:00:00Z');

const isoIn = (milliseconds: number) => new Date(NOW + milliseconds).toISOString();

describe('relativeSpan', () => {
  it('returns null for an invalid date', () => {
    expect(relativeSpan('not-a-date', NOW)).toBeNull();
  });

  it('rounds minutes away from zero', () => {
    expect(relativeSpan(isoIn(0), NOW)).toEqual({ past: false, unit: 'minute', count: 1 });
    expect(relativeSpan(isoIn(30_000), NOW)).toEqual({ past: false, unit: 'minute', count: 1 });
    expect(relativeSpan(isoIn(61_000), NOW)).toEqual({ past: false, unit: 'minute', count: 2 });
  });

  it('uses hours below one day and days from one day up', () => {
    expect(relativeSpan(isoIn(60 * 60_000), NOW)).toEqual({ past: false, unit: 'hour', count: 1 });
    expect(relativeSpan(isoIn(61 * 60_000), NOW)).toEqual({ past: false, unit: 'hour', count: 2 });
    expect(relativeSpan(isoIn(25 * 60 * 60_000), NOW)).toEqual({ past: false, unit: 'day', count: 2 });
  });

  it('marks past dates and keeps the absolute distance', () => {
    expect(relativeSpan(isoIn(-90_000), NOW)).toEqual({ past: true, unit: 'minute', count: 2 });
    expect(relativeSpan(isoIn(-2 * 60 * 60_000), NOW)).toEqual({ past: true, unit: 'hour', count: 2 });
    expect(relativeSpan(isoIn(-24 * 60 * 60_000), NOW)).toEqual({ past: true, unit: 'day', count: 1 });
  });
});

describe('formatCreatedAt', () => {
  it('returns the raw value when the date cannot be parsed', () => {
    expect(formatCreatedAt('not-a-date')).toBe('not-a-date');
  });

  it('formats a valid timestamp for display', () => {
    expect(formatCreatedAt('2026-11-04T12:00:00Z')).not.toBe('2026-11-04T12:00:00Z');
  });
});

describe('package ids and links', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shortens a UUID to its first eight hex characters', () => {
    expect(shortPackageId('0192f3ab-7cde-7def-8abc-0123456789ab')).toBe('0192f3ab');
  });

  it('builds the public link from the current origin', () => {
    vi.stubGlobal('window', { location: { origin: 'https://shareit.example' } });

    expect(packageLink('0192f3ab-7cde-7def-8abc-0123456789ab')).toBe(
      'https://shareit.example/s/0192f3ab-7cde-7def-8abc-0123456789ab'
    );
  });
});
