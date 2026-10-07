export type EncryptedTextEnvelope = {
  v: 1;
  alg: 'AES-GCM';
  iter: number;
  salt: string;
  iv: string;
  ct: string;
};

export function parseEncryptedTextEnvelope(value: string): EncryptedTextEnvelope | null {
  let data: unknown;
  try {
    data = JSON.parse(value);
  } catch {
    return null;
  }
  if (
    typeof data !== 'object' ||
    data === null ||
    !('v' in data) ||
    typeof data.v !== 'number' ||
    data.v !== 1 ||
    !('alg' in data) ||
    data.alg !== 'AES-GCM' ||
    !('iter' in data) ||
    typeof data.iter !== 'number' ||
    !('salt' in data) ||
    typeof data.salt !== 'string' ||
    !('iv' in data) ||
    typeof data.iv !== 'string' ||
    !('ct' in data) ||
    typeof data.ct !== 'string'
  ) {
    return null;
  }
  return { v: data.v, alg: data.alg, iter: data.iter, salt: data.salt, iv: data.iv, ct: data.ct };
}
