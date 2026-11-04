import { utf8 } from './bytes.ts';
import { metaAad, metaKey, type PackageKey } from './keys.ts';
import { isSealed, open, seal } from './sealed.ts';

/**
 * The per-item metadata carried in `contents.meta`: the name (null for text)
 * and the real MIME type. It is sealed with the item's metadata key when the
 * package is protected, and plaintext JSON otherwise.
 */
export type ItemMeta = {
  name: string | null;
  mime: string;
};

const decoder = new TextDecoder();

export const buildMeta = (meta: ItemMeta): Uint8Array<ArrayBuffer> =>
  utf8(JSON.stringify({ name: meta.name, mime: meta.mime }));

export function parseMeta(bytes: Uint8Array): ItemMeta | null {
  let data: unknown;
  try {
    data = JSON.parse(decoder.decode(bytes));
  } catch {
    return null;
  }

  if (
    typeof data !== 'object' ||
    data === null ||
    !('name' in data) ||
    (typeof data.name !== 'string' && data.name !== null) ||
    !('mime' in data) ||
    typeof data.mime !== 'string'
  ) {
    return null;
  }

  return { name: data.name, mime: data.mime };
}

export const encodeMeta = async (
  meta: ItemMeta,
  key: PackageKey | undefined,
  packageId: string,
  itemId: string
): Promise<Uint8Array<ArrayBuffer>> => {
  const plain = buildMeta(meta);
  if (!key) {
    return plain;
  }

  return seal(await metaKey(key, itemId), plain, metaAad(packageId, itemId));
};

export const decodeMeta = async (
  bytes: Uint8Array,
  key: PackageKey | undefined,
  packageId: string,
  itemId: string
): Promise<ItemMeta | null> => {
  if (!isSealed(bytes)) {
    return parseMeta(bytes);
  }

  if (!key) {
    throw new Error('A package key is required to open sealed metadata');
  }

  const plain = await open(await metaKey(key, itemId), bytes, metaAad(packageId, itemId));
  return parseMeta(plain);
};
