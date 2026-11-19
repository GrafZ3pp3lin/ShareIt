import { describe, expect, it } from 'vitest';

import { toBase64Url } from './base64url.ts';
import { utf8 } from './bytes.ts';
import {
  FRAME_OVERHEAD_BYTES,
  FRAME_PLAINTEXT_BYTES,
  encryptFile,
  frameDecryptStream,
  isFramed,
  openFrames,
  seal,
  sealFrame,
  sealedSizeOf
} from './framed.ts';
import { deriveKeyBits, frameAad, itemKey, packageSalt } from './keys.ts';

const PACKAGE_ID = '0192f3a0-1111-7000-8000-000000000000';
const OTHER_PACKAGE_ID = '0192f3a0-2222-7000-8000-000000000000';
const ITEM_ID = '0192f3a0-3333-7000-8000-000000000000';
const OTHER_ITEM_ID = '0192f3a0-4444-7000-8000-000000000000';

const createContentKey = async (itemId = ITEM_ID, packageId = PACKAGE_ID) => {
  const packageKey = await deriveKeyBits('correct horse battery staple', await packageSalt(packageId));
  return itemKey(packageKey, itemId);
};

const frameAadFor = (packageId: string, itemId: string) => (chunkIndex: number, isFinal: boolean) =>
  frameAad(packageId, itemId, 'item', chunkIndex, isFinal);

describe('seal and openFrames', () => {
  it('round-trips arbitrary bytes with the same key and AAD', async () => {
    const key = await createContentKey();
    const plain = crypto.getRandomValues(new Uint8Array(1024));

    const sealed = await seal(key, plain, frameAadFor(PACKAGE_ID, ITEM_ID)(0, true));
    const opened = await openFrames(key, sealed, frameAadFor(PACKAGE_ID, ITEM_ID));

    expect(opened).toEqual(plain);
  });

  it('round-trips text bytes too — there is no per-type branch', async () => {
    const key = await createContentKey();
    const plain = utf8('Grüße 世界, one frame');

    const sealed = await seal(key, plain, frameAadFor(PACKAGE_ID, ITEM_ID)(0, true));

    expect(new TextDecoder().decode(await openFrames(key, sealed, frameAadFor(PACKAGE_ID, ITEM_ID)))).toBe(
      'Grüße 世界, one frame'
    );
  });

  it('writes the SIE3 magic and one fixed frame overhead', async () => {
    const key = await createContentKey();
    const plain = utf8('small');
    const sealed = await seal(key, plain, frameAadFor(PACKAGE_ID, ITEM_ID)(0, true));

    expect(isFramed(sealed)).toBe(true);
    expect(sealed.length).toBe(plain.length + FRAME_OVERHEAD_BYTES);
    expect(toBase64Url(sealed.slice(0, 4))).toBe('U0lFMw');
  });

  it('seals deterministically: identical plaintext produces identical frames', async () => {
    const key = await createContentKey();
    const plain = utf8('deterministic');
    const aad = frameAadFor(PACKAGE_ID, ITEM_ID)(0, true);

    expect(await seal(key, plain, aad)).toEqual(await seal(key, plain, aad));
  });

  it('rejects tampered ciphertext', async () => {
    const key = await createContentKey();
    const sealed = await seal(key, utf8('secret'), frameAadFor(PACKAGE_ID, ITEM_ID)(0, true));
    sealed[sealed.length - 1] ^= 0xff;

    await expect(openFrames(key, sealed, frameAadFor(PACKAGE_ID, ITEM_ID))).rejects.toThrow();
  });

  it('rejects a blob sealed for a different package', async () => {
    const key = await createContentKey();
    const sealed = await seal(key, utf8('secret'), frameAadFor(PACKAGE_ID, ITEM_ID)(0, true));

    await expect(openFrames(key, sealed, frameAadFor(OTHER_PACKAGE_ID, ITEM_ID))).rejects.toThrow();
  });

  it('rejects a blob sealed for a different item', async () => {
    const key = await createContentKey();
    const sealed = await seal(key, utf8('secret'), frameAadFor(PACKAGE_ID, ITEM_ID)(0, true));

    await expect(openFrames(key, sealed, frameAadFor(PACKAGE_ID, OTHER_ITEM_ID))).rejects.toThrow();
  });

  it('rejects a content blob presented as metadata', async () => {
    const key = await createContentKey();
    const sealed = await seal(key, utf8('secret'), frameAadFor(PACKAGE_ID, ITEM_ID)(0, true));

    const metaAad = (chunkIndex: number, isFinal: boolean) =>
      frameAad(PACKAGE_ID, ITEM_ID, 'meta', chunkIndex, isFinal);

    await expect(openFrames(key, sealed, metaAad)).rejects.toThrow();
  });

  it('treats raw bytes (and an empty array) as not framed', () => {
    expect(isFramed(new Uint8Array())).toBe(false);
    expect(isFramed(utf8('plain text'))).toBe(false);
  });

  it('refuses to open bytes without the magic', async () => {
    const key = await createContentKey();

    await expect(openFrames(key, utf8('not sealed'), frameAadFor(PACKAGE_ID, ITEM_ID))).rejects.toThrow(
      'Payload is not framed'
    );
  });

  it('rejects a truncated frame stream', async () => {
    const key = await createContentKey();
    const sealed = await seal(key, utf8('secret'), frameAadFor(PACKAGE_ID, ITEM_ID)(0, true));

    await expect(openFrames(key, sealed.slice(0, sealed.length - 3), frameAadFor(PACKAGE_ID, ITEM_ID))).rejects.toThrow(
      'Truncated frame stream'
    );
  });
});

describe('multi-frame round-trip', () => {
  it('round-trips a payload larger than one frame', async () => {
    const key = await createContentKey();
    const plain = patterned(FRAME_PLAINTEXT_BYTES + 1234);
    const aadFor = frameAadFor(PACKAGE_ID, ITEM_ID);

    const frames = await Promise.all(
      [0, 1].map(async (index) => {
        const slice = plain.slice(index * FRAME_PLAINTEXT_BYTES, (index + 1) * FRAME_PLAINTEXT_BYTES);
        return sealFrame(key, slice, aadFor(index, index === 1), index, index === 1);
      })
    );
    const blob = new Uint8Array(frames[0].length + frames[1].length);
    blob.set(frames[0], 0);
    blob.set(frames[1], frames[0].length);

    const opened = await openFrames(key, blob, aadFor);

    expect(opened).toEqual(plain);
  });

  it('rejects out-of-order or repeated chunk indexes', async () => {
    const key = await createContentKey();
    const aadFor = frameAadFor(PACKAGE_ID, ITEM_ID);
    const first = await sealFrame(key, utf8('one'), aadFor(0, false), 0, false);
    const second = await sealFrame(key, utf8('two'), aadFor(1, true), 1, true);
    const blob = new Uint8Array(first.length + second.length);
    blob.set(first, 0);
    blob.set(second, first.length);

    // Replace the second frame's chunk index with 0 (duplicate), keeping the
    // second frame's own AAD out of reach.
    const duplicated = new Uint8Array(blob);
    duplicated.set(first.slice(0, 4 + 4), first.length);

    await expect(openFrames(key, duplicated, aadFor)).rejects.toThrow('Frame sequence mismatch');
  });

  it('rejects bytes after the final frame', async () => {
    const key = await createContentKey();
    const aadFor = frameAadFor(PACKAGE_ID, ITEM_ID);
    const final = await sealFrame(key, utf8('final'), aadFor(0, true), 0, true);
    const withTrailing = new Uint8Array(final.length + 1);
    withTrailing.set(final, 0);
    withTrailing[final.length] = 0xff;

    await expect(openFrames(key, withTrailing, aadFor)).rejects.toThrow('Trailing bytes after the final frame');
  });
});

describe('sealedSizeOf', () => {
  it('adds one frame overhead for payloads up to one frame', () => {
    expect(sealedSizeOf(5)).toBe(5 + FRAME_OVERHEAD_BYTES);
    expect(sealedSizeOf(FRAME_PLAINTEXT_BYTES)).toBe(FRAME_PLAINTEXT_BYTES + FRAME_OVERHEAD_BYTES);
  });

  it('adds one overhead per full frame', () => {
    expect(sealedSizeOf(FRAME_PLAINTEXT_BYTES + 1)).toBe(FRAME_PLAINTEXT_BYTES + 1 + 2 * FRAME_OVERHEAD_BYTES);
  });

  it('sizes an empty payload as one final frame', () => {
    expect(sealedSizeOf(0)).toBe(FRAME_OVERHEAD_BYTES);
  });
});

describe('encryptFile', () => {
  it('encrypts a file slice-by-slice and round-trips it', async () => {
    const key = await createContentKey();
    const plain = patterned(FRAME_PLAINTEXT_BYTES + 77);
    const file = new Blob([plain]);

    const encrypted = await encryptFile(key, file, frameAadFor(PACKAGE_ID, ITEM_ID));

    expect(encrypted.size).toBe(sealedSizeOf(plain.length));
    const opened = await openFrames(
      key,
      new Uint8Array(await encrypted.arrayBuffer()),
      frameAadFor(PACKAGE_ID, ITEM_ID)
    );
    expect(opened).toEqual(plain);
  });

  it('produces a single final frame for a small file', async () => {
    const key = await createContentKey();
    const plain = utf8('tiny file');
    const encrypted = await encryptFile(key, new Blob([plain]), frameAadFor(PACKAGE_ID, ITEM_ID));

    expect(encrypted.size).toBe(sealedSizeOf(plain.length));
  });

  it('seals an empty file into one final frame that round-trips', async () => {
    const key = await createContentKey();
    const encrypted = await encryptFile(key, new Blob([]), frameAadFor(PACKAGE_ID, ITEM_ID));

    expect(encrypted.size).toBe(FRAME_OVERHEAD_BYTES);
    const opened = await openFrames(
      key,
      new Uint8Array(await encrypted.arrayBuffer()),
      frameAadFor(PACKAGE_ID, ITEM_ID)
    );
    expect(opened).toHaveLength(0);
  });
});

describe('frameDecryptStream', () => {
  const streamChunks = async (chunks: Uint8Array<ArrayBuffer>[], key: CryptoKey): Promise<Uint8Array<ArrayBuffer>> => {
    const parts: Uint8Array<ArrayBuffer>[] = [];
    await new ReadableStream<Uint8Array>({
      start(controller) {
        for (const chunk of chunks) {
          controller.enqueue(chunk);
        }
        controller.close();
      }
    })
      .pipeThrough(frameDecryptStream(key, frameAadFor(PACKAGE_ID, ITEM_ID)))
      .pipeTo(
        new WritableStream<Uint8Array<ArrayBuffer>>({
          write(chunk) {
            parts.push(chunk);
          }
        })
      );
    return concatTest(parts);
  };

  it('decrypts a stream delivered one byte at a time', async () => {
    const key = await createContentKey();
    const plain = utf8('streamed, byte by byte');
    const sealed = await seal(key, plain, frameAadFor(PACKAGE_ID, ITEM_ID)(0, true));
    const chunks = Array.from(sealed, (byte) => new Uint8Array([byte]));

    expect(new TextDecoder().decode(await streamChunks(chunks, key))).toBe('streamed, byte by byte');
  });

  it('decrypts a stream delivered in arbitrary large chunks', async () => {
    const key = await createContentKey();
    const plain = patterned(FRAME_PLAINTEXT_BYTES + 999);
    const aadFor = frameAadFor(PACKAGE_ID, ITEM_ID);
    const frames = [
      await sealFrame(key, plain.slice(0, FRAME_PLAINTEXT_BYTES), aadFor(0, false), 0, false),
      await sealFrame(key, plain.slice(FRAME_PLAINTEXT_BYTES), aadFor(1, true), 1, true)
    ];
    const joined = new Uint8Array(frames[0].length + frames[1].length);
    joined.set(frames[0], 0);
    joined.set(frames[1], frames[0].length);

    // Feed the whole thing in one chunk, then again split unevenly.
    expect(await streamChunks([joined], key)).toEqual(plain);
    expect(await streamChunks([joined.slice(0, 7), joined.slice(7, 9000), joined.slice(9000)], key)).toEqual(plain);
  });

  it('never emits bytes from a tampered frame', async () => {
    const key = await createContentKey();
    const plain = utf8('do not leak');
    const sealed = await seal(key, plain, frameAadFor(PACKAGE_ID, ITEM_ID)(0, true));
    const tampered = new Uint8Array(sealed);
    tampered[tampered.length - 2] ^= 0xff;

    await expect(streamChunks([tampered], key)).rejects.toThrow('Frame decryption failed');
  });

  it('rejects an empty stream with no frames', async () => {
    const key = await createContentKey();

    await expect(streamChunks([], key)).rejects.toThrow('Truncated frame stream');
  });

  it('rejects a frame header with reserved flag bits set', async () => {
    const key = await createContentKey();
    const sealed = await seal(key, utf8('secret'), frameAadFor(PACKAGE_ID, ITEM_ID)(0, true));
    const mutated = new Uint8Array(sealed);
    mutated[8] = 0x02;

    await expect(streamChunks([mutated], key)).rejects.toThrow('Invalid frame flags');
  });

  it('rejects a frame header that exceeds the maximum frame size', async () => {
    const key = await createContentKey();
    const sealed = await seal(key, utf8('secret'), frameAadFor(PACKAGE_ID, ITEM_ID)(0, true));
    const mutated = new Uint8Array(sealed);
    new DataView(mutated.buffer).setUint32(9, FRAME_PLAINTEXT_BYTES + 1);

    await expect(streamChunks([mutated], key)).rejects.toThrow('Frame exceeds maximum size');
  });
});

/**
 * Deterministic filler for payloads above the 64 KiB getRandomValues cap.
 */
const patterned = (length: number): Uint8Array<ArrayBuffer> => {
  const out = new Uint8Array(length);
  for (let i = 0; i < length; i += 1) {
    out[i] = i % 251;
  }
  return out;
};

const concatTest = (parts: Uint8Array[]): Uint8Array<ArrayBuffer> => {
  const total = parts.reduce((sum, part) => sum + part.length, 0);
  const out = new Uint8Array(total);
  let offset = 0;
  for (const part of parts) {
    out.set(part, offset);
    offset += part.length;
  }
  return out;
};
