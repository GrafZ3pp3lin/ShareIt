import { toBase64Url } from './base64url.ts';
import { concatBytes, toArrayBuffer, utf8 } from './bytes.ts';
import { idBytes, normalizeId } from './ids.ts';

/**
 * Client constant, not stored anywhere: the verifier must be derivable before
 * the client is allowed to fetch package data.
 */
const PBKDF2_ITERATIONS = 150_000;

/**
 * The 32-byte PBKDF2 output. It is used as an HMAC key (verifier) and as the
 * HKDF input for the per-item content and metadata keys.
 */
export type PackageKey = Uint8Array<ArrayBuffer>;

/**
 * `SHA-256("shareit/salt:" || packageId bytes)`. A salt is not a secret; it is
 * derived from the link so the client can derive the verifier before it has
 * fetched anything.
 */
export const packageSalt = async (packageId: string): Promise<Uint8Array<ArrayBuffer>> => {
  const material = concatBytes(utf8('shareit/salt:'), idBytes(packageId));
  return new Uint8Array(await crypto.subtle.digest('SHA-256', toArrayBuffer(material)));
};

export const deriveKeyBits = async (passphrase: string, salt: Uint8Array<ArrayBuffer>): Promise<PackageKey> => {
  const keyMaterial = await crypto.subtle.importKey('raw', utf8(passphrase), 'PBKDF2', false, ['deriveBits']);
  const bits = await crypto.subtle.deriveBits(
    {
      name: 'PBKDF2',
      salt: toArrayBuffer(salt),
      iterations: PBKDF2_ITERATIONS,
      hash: 'SHA-256'
    },
    keyMaterial,
    256
  );
  return new Uint8Array(bits);
};

const deriveSubkey = async (key: PackageKey, itemId: string, info: string): Promise<CryptoKey> => {
  const hkdf = await crypto.subtle.importKey('raw', toArrayBuffer(key), 'HKDF', false, ['deriveKey']);
  return crypto.subtle.deriveKey(
    {
      name: 'HKDF',
      hash: 'SHA-256',
      salt: toArrayBuffer(idBytes(itemId)),
      info: toArrayBuffer(utf8(info))
    },
    hkdf,
    { name: 'AES-GCM', length: 256 },
    false,
    ['encrypt', 'decrypt']
  );
};

/**
 * Per-item content key. The salt is the item id, the label keeps content and
 * metadata key domains separate.
 */
export const itemKey = (key: PackageKey, itemId: string): Promise<CryptoKey> =>
  deriveSubkey(key, itemId, 'shareit/item');

/**
 * Per-item metadata key (names and MIME types).
 */
export const metaKey = (key: PackageKey, itemId: string): Promise<CryptoKey> =>
  deriveSubkey(key, itemId, 'shareit/item-meta');

/**
 * The value stored on the package and sent back in `X-Unlock-Proof`. It is a
 * bearer proof of the passphrase — treated like a secret by the server.
 */
export const unlockProof = async (key: PackageKey, packageId: string): Promise<string> => {
  const hmac = await crypto.subtle.importKey('raw', toArrayBuffer(key), { name: 'HMAC', hash: 'SHA-256' }, false, [
    'sign'
  ]);
  const mac = await crypto.subtle.sign(
    'HMAC',
    hmac,
    toArrayBuffer(concatBytes(utf8('shareit/unlock:'), utf8(normalizeId(packageId))))
  );
  return toBase64Url(new Uint8Array(mac));
};

/**
 * Binds a frame to its package, item, role, chunk index and final flag. Ids
 * are normalized in one place, shared with key derivation.
 */
export type FrameRole = 'item' | 'meta';

export const frameAad = (
  packageId: string,
  itemId: string,
  role: FrameRole,
  chunkIndex: number,
  isFinal: boolean
): Uint8Array<ArrayBuffer> =>
  utf8(`shareit/frame:1:${normalizeId(packageId)}:${normalizeId(itemId)}:${role}:${chunkIndex}:${isFinal ? 1 : 0}`);
