const encoder = new TextEncoder();

export const utf8 = (value: string): Uint8Array<ArrayBuffer> => encoder.encode(value);

export const concatBytes = (...parts: Uint8Array<ArrayBuffer>[]): Uint8Array<ArrayBuffer> => {
  const total = parts.reduce((sum, part) => sum + part.length, 0);
  const out = new Uint8Array(total);
  let offset = 0;
  for (const part of parts) {
    out.set(part, offset);
    offset += part.length;
  }
  return out;
};

/**
 * Copies the view into a standalone ArrayBuffer so WebCrypto never sees a
 * pooled or offset-backed buffer.
 */
export const toArrayBuffer = (bytes: Uint8Array<ArrayBuffer>): ArrayBuffer => bytes.slice().buffer;
