import { toBase64Url } from './base64url.ts';

const PASSPHRASE_MIN_LENGTH = 12;

/**
 * 16 random bytes as URL-safe base64 (22 chars). Shown once on the result
 * screen; a custom passphrase can be used instead.
 */
export const generatePassphrase = (): string => toBase64Url(crypto.getRandomValues(new Uint8Array(16)));

export type PassphraseStrength = 0 | 1 | 2 | 3;

export type PassphraseCheck = {
  valid: boolean;
  strength: PassphraseStrength;
};

/**
 * Minimum 12 characters plus a rough strength hint for the custom field.
 * The server never sees the value, so this is a UX guard only.
 */
export const validatePassphrase = (value: string): PassphraseCheck => {
  const trimmed = value.trim();
  if (trimmed.length < PASSPHRASE_MIN_LENGTH) {
    return { valid: false, strength: 0 };
  }

  const characterClasses = [/[a-z]/, /[A-Z]/, /\d/, /[^a-zA-Z0-9]/].filter((pattern) => pattern.test(trimmed)).length;

  const score = 1 + (trimmed.length >= 16 ? 1 : 0) + (characterClasses >= 2 ? 1 : 0);
  return { valid: true, strength: toStrength(score) };
};

const toStrength = (score: number): PassphraseStrength => {
  if (score >= 3) {
    return 3;
  }
  return score === 2 ? 2 : 1;
};
