import { describe, expect, it } from 'vitest';
import type { PackageSummary } from '../../shared/api/content.ts';
import { linkStatus, matchesFilter } from './link-status.ts';

const NOW = Date.parse('2026-11-04T12:00:00Z');

const makePackage = (overrides: Partial<PackageSummary> = {}): PackageSummary => ({
  Id: '0192f3ab-7cde-7def-8abc-0123456789ab',
  CreatedAt: '2026-11-01T12:00:00Z',
  ExpiresAt: '2026-11-10T12:00:00Z',
  ItemCount: 3,
  TotalSize: 1024,
  MaxDownloadCount: 1,
  CurrentDownloadCount: 0,
  RemainingDownloads: 1,
  IsExpired: false,
  IsProtected: true,
  ...overrides
});

describe('linkStatus', () => {
  it('is active with a distant expiry or no expiry at all', () => {
    expect(linkStatus(makePackage(), NOW)).toBe('active');
    expect(linkStatus(makePackage({ ExpiresAt: undefined }), NOW)).toBe('active');
  });

  it('is expiring inside the 24 hour window', () => {
    expect(linkStatus(makePackage({ ExpiresAt: '2026-11-05T11:00:00Z' }), NOW)).toBe('expiring');
  });

  it('is used up when no accesses remain', () => {
    expect(linkStatus(makePackage({ RemainingDownloads: 0, CurrentDownloadCount: 1 }), NOW)).toBe('usedUp');
  });

  it('reports expired first when the package is past its expiry', () => {
    expect(
      linkStatus(makePackage({ IsExpired: true, ExpiresAt: '2026-11-01T12:00:00Z', RemainingDownloads: 0 }), NOW)
    ).toBe('expired');
  });
});

describe('matchesFilter', () => {
  it('keeps every status under all', () => {
    expect(matchesFilter('active', 'all')).toBe(true);
    expect(matchesFilter('expired', 'all')).toBe(true);
  });

  it('puts expired and used-up links into the expired bucket', () => {
    expect(matchesFilter('expired', 'expired')).toBe(true);
    expect(matchesFilter('usedUp', 'expired')).toBe(true);
    expect(matchesFilter('active', 'expired')).toBe(false);
  });

  it('matches the exact status for the other filters', () => {
    expect(matchesFilter('expiring', 'expiring')).toBe(true);
    expect(matchesFilter('active', 'expiring')).toBe(false);
  });
});
