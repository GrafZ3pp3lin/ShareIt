import type { AccessSessionResponse } from '../../client/model/index.ts';

export type StoredAccessSession = Pick<AccessSessionResponse, 'Token' | 'ExpiresAt'>;

const STORAGE_PREFIX = 'shareit-access-session:';

const storageKey = (packageId: string) => `${STORAGE_PREFIX}${packageId}`;

const isStoredAccessSession = (value: unknown): value is StoredAccessSession => {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Partial<StoredAccessSession>;
  return typeof candidate.Token === 'string' && candidate.Token.length > 0 && typeof candidate.ExpiresAt === 'string';
};

/**
 * Resolves the session for this tab. `sessionStorage` survives a reload but
 * not a tab close, which bounds the lifetime of the bearer grant.
 */
const getStorage = (): Storage | undefined => {
  try {
    return typeof sessionStorage === 'undefined' ? undefined : sessionStorage;
  } catch {
    return undefined;
  }
};

/**
 * Loads a stored session. Expiry is enforced by the server: a token that the
 * local clock considers expired is still sent so the server can make the
 * authoritative decision without consuming another access unnecessarily.
 */
export function loadAccessSession(packageId: string): StoredAccessSession | null {
  const storage = getStorage();
  if (!storage) {
    return null;
  }

  try {
    const raw = storage.getItem(storageKey(packageId));
    if (!raw) {
      return null;
    }

    const parsed: unknown = JSON.parse(raw);
    if (!isStoredAccessSession(parsed)) {
      storage.removeItem(storageKey(packageId));
      return null;
    }

    return parsed;
  } catch {
    try {
      storage.removeItem(storageKey(packageId));
    } catch {
      // Storage is unavailable; treat the session as absent.
    }
    return null;
  }
}

export function saveAccessSession(packageId: string, session: StoredAccessSession): void {
  const storage = getStorage();
  if (!storage) {
    return;
  }

  try {
    storage.setItem(
      storageKey(packageId),
      JSON.stringify({ Token: session.Token, ExpiresAt: session.ExpiresAt } satisfies StoredAccessSession)
    );
  } catch {
    // Storage can be unavailable in private modes; the in-memory signal still
    // carries the session for this page.
  }
}

export function clearAccessSession(packageId: string): void {
  const storage = getStorage();
  if (!storage) {
    return;
  }

  try {
    storage.removeItem(storageKey(packageId));
  } catch {
    // Nothing to clean up.
  }
}
