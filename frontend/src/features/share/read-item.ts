import { downloadContent } from '../../shared/api/content.ts';
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

    if (outcome.status === 401) {
      onSessionExpired?.(accessSessionToken);
    } else if (outcome.status === 404 || outcome.status === 410) {
      onPackageGone?.();
    }

    throw outcome.error ?? new Error('Download failed');
  });
};
