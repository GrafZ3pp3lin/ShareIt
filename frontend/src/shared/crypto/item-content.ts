import { fromBase64Url } from './base64url.ts';
import { itemAad, itemKey, type PackageKey } from './keys.ts';
import { isSealed, open } from './sealed.ts';

export type ReadableItem = {
  Id: string;
  Payload?: string | null;
};

export type ItemDownloader = () => Promise<Blob>;

/**
 * One read rule for text and files: an inline payload if the server sent one,
 * otherwise a download authorized by the package's access session.
 */
export const readItemBytes = async (item: ReadableItem, download: ItemDownloader): Promise<Uint8Array<ArrayBuffer>> => {
  if (item.Payload != null) {
    return fromBase64Url(item.Payload);
  }

  const blob = await download();
  return new Uint8Array(await blob.arrayBuffer());
};

/**
 * Reads the item bytes and opens the `SIE2` envelope when the package is
 * protected. Unprotected items are raw bytes.
 */
export const openItemContent = async (
  item: ReadableItem,
  key: PackageKey | undefined,
  packageId: string,
  download: ItemDownloader
): Promise<Uint8Array<ArrayBuffer>> => {
  const bytes = await readItemBytes(item, download);

  if (!key || !isSealed(bytes)) {
    return bytes;
  }

  return open(await itemKey(key, item.Id), bytes, itemAad(packageId, item.Id));
};
