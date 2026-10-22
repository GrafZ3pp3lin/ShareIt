import { parseEncryptedTextEnvelope, type EncryptedTextEnvelope } from './envelope.ts';

const PBKDF2_ITERATIONS = 150000;
const textEncoder = new TextEncoder();
const textDecoder = new TextDecoder();

const toBase64Url = (bytes: Uint8Array) => {
  let binary = '';
  bytes.forEach((byte) => {
    binary += String.fromCharCode(byte);
  });
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
};

const fromBase64Url = (value: string) => {
  const padded = value.padEnd(Math.ceil(value.length / 4) * 4, '=');
  const base64 = padded.replace(/-/g, '+').replace(/_/g, '/');
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i += 1) {
    bytes[i] = binary.charCodeAt(i);
  }
  return bytes;
};

const toArrayBuffer = (bytes: Uint8Array<ArrayBuffer>): ArrayBuffer => bytes.slice().buffer;

const deriveKey = async (passphrase: string, salt: Uint8Array<ArrayBuffer>, iterations: number) => {
  const keyMaterial = await crypto.subtle.importKey('raw', textEncoder.encode(passphrase), 'PBKDF2', false, [
    'deriveKey'
  ]);
  return crypto.subtle.deriveKey(
    {
      name: 'PBKDF2',
      salt: toArrayBuffer(salt),
      iterations,
      hash: 'SHA-256'
    },
    keyMaterial,
    { name: 'AES-GCM', length: 256 },
    false,
    ['encrypt', 'decrypt']
  );
};

export const generatePassphrase = () => {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  return toBase64Url(bytes);
};

export const encryptText = async (plainText: string, passphrase: string) => {
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const key = await deriveKey(passphrase, salt, PBKDF2_ITERATIONS);
  const cipherBuffer = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, textEncoder.encode(plainText));

  const envelope: EncryptedTextEnvelope = {
    v: 1,
    alg: 'AES-GCM',
    iter: PBKDF2_ITERATIONS,
    salt: toBase64Url(salt),
    iv: toBase64Url(iv),
    ct: toBase64Url(new Uint8Array(cipherBuffer))
  };

  return JSON.stringify(envelope);
};

export const decryptText = async (payload: string, passphrase: string) => {
  const envelope = parseEncryptedTextEnvelope(payload);
  if (!envelope) {
    throw new Error('Payload is not encrypted');
  }

  const salt = fromBase64Url(envelope.salt);
  const iv = fromBase64Url(envelope.iv);
  const cipherBytes = fromBase64Url(envelope.ct);
  const key = await deriveKey(passphrase, salt, envelope.iter);
  const plainBuffer = await crypto.subtle.decrypt(
    { name: 'AES-GCM', iv: toArrayBuffer(iv) },
    key,
    toArrayBuffer(cipherBytes)
  );
  return textDecoder.decode(plainBuffer);
};

// Binary envelope for files: magic "SIE1" (4 bytes) + salt (16) + IV (12) +
// PBKDF2 iterations (4, big-endian) + AES-GCM ciphertext with auth tag (16).
const FILE_ENVELOPE_MAGIC = new Uint8Array([0x53, 0x49, 0x45, 0x31]);
const FILE_ENVELOPE_HEADER_LENGTH = 4 + 16 + 12 + 4;
const FILE_ENVELOPE_TAG_LENGTH = 16;

export const ENCRYPTED_FILE_MIME_TYPE = 'application/x-shareit-encrypted';
export const ENCRYPTED_FILE_OVERHEAD_BYTES = FILE_ENVELOPE_HEADER_LENGTH + FILE_ENVELOPE_TAG_LENGTH;

export const isEncryptedFileEnvelope = (bytes: Uint8Array) =>
  bytes.length >= FILE_ENVELOPE_HEADER_LENGTH && FILE_ENVELOPE_MAGIC.every((byte, index) => bytes[index] === byte);

export const isEncryptedBlob = async (blob: Blob) =>
  isEncryptedFileEnvelope(new Uint8Array(await blob.slice(0, FILE_ENVELOPE_HEADER_LENGTH + 1).arrayBuffer()));

export const encryptFile = async (file: File, passphrase: string) => {
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const key = await deriveKey(passphrase, salt, PBKDF2_ITERATIONS);
  const plain = new Uint8Array(await file.arrayBuffer());
  const cipherBuffer = await crypto.subtle.encrypt(
    { name: 'AES-GCM', iv: toArrayBuffer(iv) },
    key,
    toArrayBuffer(plain)
  );

  const cipher = new Uint8Array(cipherBuffer);
  const header = new Uint8Array(FILE_ENVELOPE_HEADER_LENGTH);
  header.set(FILE_ENVELOPE_MAGIC, 0);
  header.set(salt, 4);
  header.set(iv, 20);
  new DataView(header.buffer).setUint32(32, PBKDF2_ITERATIONS, false);

  const envelope = new Uint8Array(header.length + cipher.length);
  envelope.set(header, 0);
  envelope.set(cipher, header.length);

  return new File([envelope], file.name, { type: ENCRYPTED_FILE_MIME_TYPE });
};

export const decryptFile = async (encryptedBlob: Blob, passphrase: string) => {
  const bytes = new Uint8Array(await encryptedBlob.arrayBuffer());
  if (!isEncryptedFileEnvelope(bytes)) {
    throw new Error('Payload is not encrypted');
  }

  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const iterations = view.getUint32(32, false);
  const salt = bytes.slice(4, 20);
  const iv = bytes.slice(20, 32);
  const cipher = bytes.slice(FILE_ENVELOPE_HEADER_LENGTH);

  const key = await deriveKey(passphrase, salt, iterations);
  const plainBuffer = await crypto.subtle.decrypt(
    { name: 'AES-GCM', iv: toArrayBuffer(iv) },
    key,
    toArrayBuffer(cipher)
  );
  return new Blob([plainBuffer]);
};
