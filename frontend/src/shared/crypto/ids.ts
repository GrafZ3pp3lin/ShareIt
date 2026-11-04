const HEX_PATTERN = /^[0-9a-f]{32}$/;

/**
 * The only place ids are formatted. Key derivation, AAD and the key schedule
 * all use this form, so differently formatted UUIDs produce identical keys.
 */
export const normalizeId = (id: string): string => {
  const hex = id.replace(/-/g, '').toLowerCase();
  if (!HEX_PATTERN.test(hex)) {
    throw new Error(`Invalid UUID: ${id}`);
  }

  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
};

/**
 * 16 raw bytes of a UUID, in the RFC 4122 big-endian order.
 */
export const idBytes = (id: string): Uint8Array<ArrayBuffer> => {
  const hex = normalizeId(id).replace(/-/g, '');
  const bytes = new Uint8Array(16);
  for (let i = 0; i < 16; i += 1) {
    bytes[i] = Number.parseInt(hex.slice(i * 2, i * 2 + 2), 16);
  }
  return bytes;
};
