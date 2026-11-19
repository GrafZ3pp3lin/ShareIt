import { utf8 } from './bytes.ts';
import { isFramed, openFrames, seal } from './framed.ts';
import { frameAad, metaKey, type PackageKey } from './keys.ts';

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

  // Metadata is always a single final frame. Sealing is deterministic, so a
  // retry produces the identical blob and stays idempotent.
  return seal(await metaKey(key, itemId), plain, frameAad(packageId, itemId, 'meta', 0, true));
};

export const decodeMeta = async (
  bytes: Uint8Array,
  key: PackageKey | undefined,
  packageId: string,
  itemId: string
): Promise<ItemMeta | null> => {
  if (!key) {
    if (isFramed(bytes)) {
      throw new Error('A package key is required to open sealed metadata');
    }

    return parseMeta(bytes);
  }

  // A key is only retained for protected packages, so plaintext metadata is
  // an integrity error rather than a valid unprotected payload.
  if (!isFramed(bytes)) {
    throw new Error('Metadata is not framed');
  }

  const plain = await openFrames(await metaKey(key, itemId), bytes, (chunkIndex, isFinal) =>
    frameAad(packageId, itemId, 'meta', chunkIndex, isFinal)
  );
  return parseMeta(plain);
};
