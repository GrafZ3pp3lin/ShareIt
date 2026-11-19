import type { PackageSummary } from '../../shared/api/content.ts';

export type LinkStatus = 'active' | 'expiring' | 'usedUp' | 'expired';
export type LinkFilter = 'all' | 'active' | 'expiring' | 'expired';

const EXPIRING_WINDOW_MS = 24 * 60 * 60 * 1000;

/**
 * The list endpoint only knows whether a package expired; "used up" and
 * "expiring" are derived here from the remaining accesses and expiry.
 */
export const linkStatus = (item: PackageSummary, now = Date.now()): LinkStatus => {
  if (item.IsExpired) {
    return 'expired';
  }

  if (item.RemainingAccesses !== null && item.RemainingAccesses <= 0) {
    return 'usedUp';
  }

  const expiresAt = Date.parse(item.ExpiresAt);
  if (!Number.isNaN(expiresAt) && expiresAt - now <= EXPIRING_WINDOW_MS) {
    return 'expiring';
  }

  return 'active';
};

export const isDead = (status: LinkStatus) => status === 'expired' || status === 'usedUp';

/**
 * The "expired" tab is the dead bucket: it also contains used-up links so no
 * row is reachable only through "all".
 */
export const matchesFilter = (status: LinkStatus, filter: LinkFilter): boolean => {
  if (filter === 'all') {
    return true;
  }
  if (filter === 'expired') {
    return isDead(status);
  }
  return status === filter;
};
