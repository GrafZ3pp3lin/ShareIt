import { describe, expect, it } from 'vitest';
import { concatBytes, toArrayBuffer, utf8 } from './bytes.ts';

describe('utf8', () => {
  it('encodes and decodes unicode text', () => {
    const text = 'Grüße 世界';
    const bytes = utf8(text);

    expect(new TextDecoder().decode(bytes)).toBe(text);
  });
});

describe('concatBytes', () => {
  it('concatenates parts in order', () => {
    const result = concatBytes(utf8('one'), new Uint8Array(), utf8('two'));

    expect(new TextDecoder().decode(result)).toBe('onetwo');
    expect(result).toHaveLength(6);
  });
});

describe('toArrayBuffer', () => {
  it('copies an offset view into a standalone buffer', () => {
    const backing = new Uint8Array([1, 2, 3, 4]);
    const view = backing.subarray(1, 3);

    const buffer = toArrayBuffer(view);

    expect(buffer.byteLength).toBe(2);
    expect(new Uint8Array(buffer)).toEqual(new Uint8Array([2, 3]));
    backing[1] = 99;
    expect(new Uint8Array(buffer)).toEqual(new Uint8Array([2, 3]));
  });
});
