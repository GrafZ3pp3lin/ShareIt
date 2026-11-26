import { normalizeShortCode } from './short-code.ts';

type StoredShortCode = {
  Code: string;
  ExpiresAt: string;
};

const STORAGE_PREFIX = 'shareit-short-code:';

const storageKey = (packageId: string) => `${STORAGE_PREFIX}${packageId}`;

/**
 * Keeps the full code in the creating tab only. The server never receives the
 * client secret, so this is the only place the owner links list can find a
 * code to copy after the result panel is gone.
 */
const getStorage = (): Storage | undefined => {
  try {
    return typeof sessionStorage === 'undefined' ? undefined : sessionStorage;
  } catch {
    return undefined;
  }
};

const isStoredShortCode = (value: unknown): value is StoredShortCode => {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Partial<StoredShortCode>;
  return (
    typeof candidate.Code === 'string' &&
    normalizeShortCode(candidate.Code) !== null &&
    typeof candidate.ExpiresAt === 'string' &&
    Number.isFinite(Date.parse(candidate.ExpiresAt))
  );
};

export function loadShortCode(packageId: string): string | null {
  const storage = getStorage();
  if (!storage) {
    return null;
  }

  const key = storageKey(packageId);
  try {
    const raw = storage.getItem(key);
    if (!raw) {
      return null;
    }

    const parsed: unknown = JSON.parse(raw);
    if (!isStoredShortCode(parsed)) {
      storage.removeItem(key);
      return null;
    }

    if (Date.parse(parsed.ExpiresAt) <= Date.now()) {
      storage.removeItem(key);
      return null;
    }

    return normalizeShortCode(parsed.Code);
  } catch {
    try {
      storage.removeItem(key);
    } catch {
      // Storage is unavailable; treat the code as absent.
    }
    return null;
  }
}

export function saveShortCode(packageId: string, code: string, expiresAt: string): void {
  const normalized = normalizeShortCode(code);
  if (normalized === null || !Number.isFinite(Date.parse(expiresAt))) {
    return;
  }

  const storage = getStorage();
  if (!storage) {
    return;
  }

  try {
    storage.setItem(
      storageKey(packageId),
      JSON.stringify({ Code: normalized, ExpiresAt: expiresAt } satisfies StoredShortCode)
    );
  } catch {
    // Storage can be unavailable in private modes; the result panel still
    // carries the code for this page.
  }
}

export function clearShortCode(packageId: string): void {
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
