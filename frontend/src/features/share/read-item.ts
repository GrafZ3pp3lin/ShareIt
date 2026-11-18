import { downloadContent, downloadContentResponse } from '../../shared/api/content.ts';
import { openItemContent } from '../../shared/crypto/item-content.ts';
import type { PackageKey } from '../../shared/crypto/keys.ts';
import type { ShareItem } from './use-share.ts';

/**
 * Fetches an item through the package's access session when the server did not
 * inline it, then opens the SIE2 envelope when the package is protected.
 */
export class AccessSessionExpiredError extends Error {
  constructor() {
    super('');
    this.name = 'AccessSessionExpiredError';
  }
}

/**
 * Routes a failed download to the package-level reactions: an expired session
 * is cleared so the next request asks for an unlock again, and a package that
 * disappeared or expired sends the visitor back to the gate.
 */
const reportDownloadFailure = (
  status: number | undefined,
  sessionToken: string | undefined,
  onSessionExpired?: (failedToken: string | undefined) => void,
  onPackageGone?: () => void
) => {
  if (status === 401) {
    onSessionExpired?.(sessionToken);
  } else if (status === 404 || status === 410) {
    onPackageGone?.();
  }
};

export const readShareItem = async (
  packageId: string,
  item: ShareItem,
  key: PackageKey | undefined,
  accessSessionToken: string | undefined,
  onSessionExpired?: (failedToken: string | undefined) => void,
  onPackageGone?: () => void
): Promise<Uint8Array<ArrayBuffer>> => {
  return openItemContent(item, key, packageId, async () => {
    if (!accessSessionToken) {
      onSessionExpired?.(undefined);
      throw new AccessSessionExpiredError();
    }

    const outcome = await downloadContent(packageId, item.Id, accessSessionToken);
    if (outcome.data) {
      return outcome.data;
    }

    reportDownloadFailure(outcome.status, accessSessionToken, onSessionExpired, onPackageGone);

    throw outcome.error ?? new Error('Download failed');
  });
};

/**
 * Unsaved-body download used by the streaming save path: the caller writes the
 * response body straight to a writable destination.
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

  reportDownloadFailure(outcome.status, accessSessionToken, onSessionExpired, onPackageGone);

  throw outcome.error ?? new Error('Download failed');
};
