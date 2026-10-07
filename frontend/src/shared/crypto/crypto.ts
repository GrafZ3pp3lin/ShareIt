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
