export type EncryptedTextEnvelope = {
  v: 1;
  alg: 'AES-GCM';
  iter: number;
  salt: string;
  iv: string;
  ct: string;
};

export function parseEncryptedTextEnvelope(value: string): EncryptedTextEnvelope | null {
  try {
    const parsed = JSON.parse(value) as Partial<EncryptedTextEnvelope>;
    if (
      parsed &&
      parsed.v === 1 &&
      parsed.alg === 'AES-GCM' &&
      typeof parsed.iter === 'number' &&
      typeof parsed.salt === 'string' &&
      typeof parsed.iv === 'string' &&
      typeof parsed.ct === 'string'
    ) {
      return parsed as EncryptedTextEnvelope;
    }
    return null;
  } catch {
    return null;
  }
}
