import { downloadContent, downloadContentResponse } from '../../shared/api/content.ts';
import { openItemContent } from '../../shared/crypto/item-content.ts';
import type { PackageKey } from '../../shared/crypto/keys.ts';
import type { ShareItem } from './use-share.ts';

/**
 * Fetches an item through the package's access session, then opens the framed
 * ciphertext when the package is protected.
 */
export class AccessSessionExpiredError extends Error {
  constructor() {
    super('');
    this.name = 'AccessSessionExpiredError';
  }
}

const PACKAGE_GONE_PROBLEM_TYPE = 'urn:shareit:problem:package-gone';

/**
 * A 404 can mean the package is gone or only that one content/blob is missing.
 * Only the former may clear the session and send the visitor back to the gate.
 */
const isPackageGoneProblem = (error: unknown): boolean => {
  if (typeof error !== 'object' || error === null) {
    return false;
  }

  const candidate = error as { type?: unknown; title?: unknown };
  return candidate.type === PACKAGE_GONE_PROBLEM_TYPE || candidate.title === 'Package not found';
};

/**
 * Routes a failed download to the package-level reactions: an expired session
 * is cleared so the next request asks for an unlock again, and only a package
 * that disappeared or expired sends the visitor back to the gate.
 */
const reportDownloadFailure = (
  status: number | undefined,
  error: unknown,
  sessionToken: string | undefined,
  onSessionExpired?: (failedToken: string | undefined) => void,
  onPackageGone?: () => void
) => {
  if (status === 401) {
    onSessionExpired?.(sessionToken);
  } else if (status === 410 || (status === 404 && isPackageGoneProblem(error))) {
    onPackageGone?.();
  }
};

/**
 * Buffered read of an item: every item's bytes come through the access
 * session, then framed ciphertext is opened when the package is protected.
 */
export const readShareItem = async (
  packageId: string,
  item: ShareItem,
  key: PackageKey | undefined,
  accessSessionToken: string | undefined,
  onSessionExpired?: (failedToken: string | undefined) => void,
  onPackageGone?: () => void
): Promise<Uint8Array<ArrayBuffer>> => {
  if (!accessSessionToken) {
    onSessionExpired?.(undefined);
    throw new AccessSessionExpiredError();
  }

  const outcome = await downloadContent(packageId, item.Id, accessSessionToken);
  if (!outcome.data) {
    reportDownloadFailure(outcome.status, outcome.error, accessSessionToken, onSessionExpired, onPackageGone);
    throw outcome.error ?? new Error('Download failed');
  }

  const bytes = new Uint8Array(await outcome.data.arrayBuffer());
  return openItemContent(packageId, item.Id, key, bytes);
};

/**
 * Unsaved-body download used by the streaming save path: the caller pipes the
 * response body through a frame decryptor and straight to a writable
 * destination.
 */
export const fetchShareItem = async (
  packageId: string,
  item: ShareItem,
  accessSessionToken: string,
  onSessionExpired?: (failedToken: string | undefined) => void,
  onPackageGone?: () => void
): Promise<Response> => {
  const outcome = await downloadContentResponse(packageId, item.Id, accessSessionToken);
  if (outcome.response) {
    return outcome.response;
  }

  reportDownloadFailure(outcome.status, outcome.error, accessSessionToken, onSessionExpired, onPackageGone);

  throw outcome.error ?? new Error('Download failed');
};
