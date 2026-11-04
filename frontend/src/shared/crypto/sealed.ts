import { concatBytes, toArrayBuffer } from './bytes.ts';

/**
 * One container for text and file bytes:
 * `SIE2 | IV (12) | AES-GCM ciphertext + tag (16)`.
 */
const MAGIC = new Uint8Array([0x53, 0x49, 0x45, 0x32]);
const IV_LENGTH = 12;
const TAG_LENGTH = 16;

export const SEALED_OVERHEAD_BYTES = MAGIC.length + IV_LENGTH + TAG_LENGTH;

export const isSealed = (bytes: Uint8Array): boolean =>
  bytes.length >= SEALED_OVERHEAD_BYTES && MAGIC.every((byte, index) => bytes[index] === byte);

export const seal = async (
  key: CryptoKey,
  plain: Uint8Array<ArrayBuffer>,
  aad: Uint8Array<ArrayBuffer>
): Promise<Uint8Array<ArrayBuffer>> => {
  const iv = crypto.getRandomValues(new Uint8Array(IV_LENGTH));
  const cipher = await crypto.subtle.encrypt(
    { name: 'AES-GCM', iv: toArrayBuffer(iv), additionalData: toArrayBuffer(aad) },
    key,
    toArrayBuffer(plain)
  );
  return concatBytes(MAGIC, iv, new Uint8Array(cipher));
};

export const open = async (
  key: CryptoKey,
  sealed: Uint8Array,
  aad: Uint8Array<ArrayBuffer>
): Promise<Uint8Array<ArrayBuffer>> => {
  if (!isSealed(sealed)) {
    throw new Error('Payload is not sealed');
  }

  // Copy so the slices are backed by a plain ArrayBuffer, never a shared view.
  const bytes = new Uint8Array(sealed);
  const iv = bytes.slice(MAGIC.length, MAGIC.length + IV_LENGTH);
  const cipher = bytes.slice(MAGIC.length + IV_LENGTH);

  const plain = await crypto.subtle.decrypt(
    { name: 'AES-GCM', iv: toArrayBuffer(iv), additionalData: toArrayBuffer(aad) },
    key,
    toArrayBuffer(cipher)
  );
  return new Uint8Array(plain);
};
