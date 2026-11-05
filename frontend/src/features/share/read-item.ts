import { downloadContent } from '../../shared/api/content.ts';
import { openItemContent } from '../../shared/crypto/item-content.ts';
import type { PackageKey } from '../../shared/crypto/keys.ts';
import type { ShareItem } from './use-share.ts';

/**
 * Fetches an item through its single-use token when the server did not inline
 * it, then opens the SIE2 envelope when the package is protected.
 */
export const readShareItem = async (
  packageId: string,
  item: ShareItem,
  key: PackageKey | undefined
): Promise<Uint8Array<ArrayBuffer>> =>
  openItemContent(item, key, packageId, async (token) => {
    const outcome = await downloadContent(packageId, item.Id, token);
    if (outcome.data) {
      return outcome.data;
    }
    throw outcome.error ?? new Error('Download failed');
  });
