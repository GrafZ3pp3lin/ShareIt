import { openFrames } from './framed.ts';
import { frameAad, itemKey, type PackageKey } from './keys.ts';

/**
 * One read rule for text and files: the bytes always come through the
 * package's access session — nothing is inlined. The package key is only
 * retained for protected packages, so a key means framed ciphertext is
 * required; a missing magic is an integrity error, not a reason to return raw
 * bytes. Without a key the item is unprotected and therefore raw.
 */
export const openItemContent = async (
  packageId: string,
  itemId: string,
  key: PackageKey | undefined,
  bytes: Uint8Array<ArrayBuffer>
): Promise<Uint8Array<ArrayBuffer>> => {
  if (!key) {
    return bytes;
  }

  return openFrames(await itemKey(key, itemId), bytes, (chunkIndex, isFinal) =>
    frameAad(packageId, itemId, 'item', chunkIndex, isFinal)
  );
};
