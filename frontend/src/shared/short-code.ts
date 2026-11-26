/**
 * User-facing access codes. A code has six characters (`ABC-234`): the server
 * generates and stores the first three (the locator) and resolves it to the
 * package id; the sender's browser generates the last three (the secret) and
 * never sends it to the server. The secret is the passphrase of the package
 * encryption schedule.
 */
export const SHORT_CODE_ALPHABET = '23456789ABCDEFGHJKMNPQRSTUVWXYZ';
export const SHORT_CODE_LENGTH = 6;
export const SHORT_CODE_LOCATOR_LENGTH = 3;
export const SHORT_CODE_SECRET_LENGTH = SHORT_CODE_LENGTH - SHORT_CODE_LOCATOR_LENGTH;

const PATTERN = /^[2-9A-HJ-KM-NP-Z]{6}$/;

const stripSeparators = (value: string): string | null => {
  if (value.length > 64) {
    return null;
  }

  const stripped = value.replace(/[-\s]/g, '');
  // Unicode folding can turn a homoglyph such as U+017F into a valid ASCII
  // letter; reject non-ASCII before uppercasing.
  if (stripped.split('').some((character) => character.charCodeAt(0) > 0x7f)) {
    return null;
  }

  return stripped.toUpperCase();
};

/**
 * Normalizes a full six-character code from typed input. Hyphens and
 * whitespace are cosmetic; the canonical form is uppercase without separators.
 */
export const normalizeShortCode = (value: string): string | null => {
  const normalized = stripSeparators(value);
  return normalized !== null && PATTERN.test(normalized) ? normalized : null;
};

export type SplitShortCode = {
  locator: string;
  secret: string;
};

/**
 * Splits a normalized six-character code into the server locator and the
 * client secret. Callers must normalize/validate first.
 */
export const splitShortCode = (code: string): SplitShortCode => ({
  locator: code.slice(0, SHORT_CODE_LOCATOR_LENGTH),
  secret: code.slice(SHORT_CODE_LOCATOR_LENGTH, SHORT_CODE_LENGTH)
});

/**
 * Generates the three-character client secret in the browser. Rejection
 * sampling keeps the modulo uniform (248 is the largest multiple of 31 that
 * fits in a byte).
 */
export const generateCodeSecret = (): string => {
  const limit = Math.floor(256 / SHORT_CODE_ALPHABET.length) * SHORT_CODE_ALPHABET.length;
  const buffer = new Uint8Array(1);
  let secret = '';

  while (secret.length < SHORT_CODE_SECRET_LENGTH) {
    crypto.getRandomValues(buffer);
    if (buffer[0] < limit) {
      secret += SHORT_CODE_ALPHABET[buffer[0] % SHORT_CODE_ALPHABET.length];
    }
  }

  return secret;
};

/**
 * Formats a code for reading aloud: `ABC-234`.
 */
export const formatShortCode = (code: string): string =>
  code && code.length === SHORT_CODE_LENGTH
    ? `${code.slice(0, SHORT_CODE_LOCATOR_LENGTH)}-${code.slice(SHORT_CODE_LOCATOR_LENGTH)}`
    : code;
