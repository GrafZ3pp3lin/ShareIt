import { concatBytes, toArrayBuffer, utf8 } from './bytes.ts';

/**
 * One framed container for text, meta and file bytes.
 *
 * A sealed blob is the magic followed by a sequence of frames. Each frame is:
 *
 * ```
 * "SIE3" (4) | chunkIndex u32 BE (4) | flags u8 (1: bit 0 = isFinal)
 *    | ciphertextLength u32 BE (4) | AES-GCM ciphertext + tag (ciphertextLength + 16)
 * ```
 *
 * The 12-byte AES-GCM IV is derived deterministically from the chunk index, so
 * sealing the same bytes twice produces identical frames — retries of one item
 * are byte-for-byte idempotent. Keys are already unique per
 * (packageId, itemId, role), so IV reuse across items is safe. The AAD binds
 * each frame to (packageId, itemId, role, chunkIndex, isFinal); nothing is
 * emitted before its GCM tag verifies.
 */
const MAGIC = new Uint8Array([0x53, 0x49, 0x45, 0x33]); // "SIE3"
const CHUNK_INDEX_OFFSET = MAGIC.length;
const FLAGS_OFFSET = CHUNK_INDEX_OFFSET + 4;
const LENGTH_OFFSET = FLAGS_OFFSET + 1;
const HEADER_BYTES = LENGTH_OFFSET + 4;
const TAG_BYTES = 16;

export const FRAME_PLAINTEXT_BYTES = 4 * 1024 * 1024;
export const FRAME_OVERHEAD_BYTES = HEADER_BYTES + TAG_BYTES;

/**
 * Total sealed size of a plaintext payload: the plaintext plus one fixed
 * header and GCM tag per 4 MiB frame. An empty payload still seals into one
 * final frame, so the sealed size is never 0.
 */
export const sealedSizeOf = (plainSize: number): number =>
  plainSize + Math.max(1, Math.ceil(plainSize / FRAME_PLAINTEXT_BYTES)) * FRAME_OVERHEAD_BYTES;

export const isFramed = (bytes: Uint8Array): boolean =>
  bytes.length >= HEADER_BYTES && MAGIC.every((byte, index) => bytes[index] === byte);

const readUint32 = (bytes: Uint8Array, offset: number): number =>
  new DataView(bytes.buffer, bytes.byteOffset + offset, 4).getUint32(0);

const frameIv = async (chunkIndex: number): Promise<Uint8Array<ArrayBuffer>> => {
  const indexBytes = new Uint8Array(4);
  new DataView(indexBytes.buffer).setUint32(0, chunkIndex);
  const digest = await crypto.subtle.digest(
    'SHA-256',
    toArrayBuffer(concatBytes(utf8('shareit/frame-iv:'), indexBytes))
  );
  return new Uint8Array(digest).slice(0, 12);
};

export type FrameAad = (chunkIndex: number, isFinal: boolean) => Uint8Array<ArrayBuffer>;

/**
 * Encrypts one plaintext frame. `aad` must already include the chunk index and
 * final flag for this frame.
 */
export const sealFrame = async (
  key: CryptoKey,
  plain: Uint8Array<ArrayBuffer>,
  aad: Uint8Array<ArrayBuffer>,
  chunkIndex: number,
  isFinal: boolean
): Promise<Uint8Array<ArrayBuffer>> => {
  if (plain.length > FRAME_PLAINTEXT_BYTES) {
    throw new Error('Frame exceeds maximum size');
  }

  const iv = await frameIv(chunkIndex);
  const cipher = await crypto.subtle.encrypt(
    { name: 'AES-GCM', iv: toArrayBuffer(iv), additionalData: toArrayBuffer(aad) },
    key,
    toArrayBuffer(plain)
  );

  const header = new Uint8Array(HEADER_BYTES);
  header.set(MAGIC, 0);
  new DataView(header.buffer).setUint32(CHUNK_INDEX_OFFSET, chunkIndex);
  header[FLAGS_OFFSET] = isFinal ? 1 : 0;
  new DataView(header.buffer).setUint32(LENGTH_OFFSET, plain.length);

  return concatBytes(header, new Uint8Array(cipher));
};

/**
 * Seals per-item metadata as a single final frame.
 */
export const seal = async (
  key: CryptoKey,
  plain: Uint8Array<ArrayBuffer>,
  aad: Uint8Array<ArrayBuffer>
): Promise<Uint8Array<ArrayBuffer>> => sealFrame(key, plain, aad, 0, true);

/**
 * Encrypts a file slice-by-slice into 4 MiB frames without ever allocating a
 * whole-file plaintext buffer. The result is a Blob of encrypted frames.
 */
export const encryptFile = async (key: CryptoKey, file: File | Blob, aadFor: FrameAad): Promise<Blob> => {
  const parts: BlobPart[] = [];
  let offset = 0;
  let chunkIndex = 0;

  // Seals at least one frame even for an empty file, so a sealed blob is
  // never 0 bytes and always opens back into the original (empty) plaintext.
  do {
    const slice = file.slice(offset, offset + FRAME_PLAINTEXT_BYTES);
    // oxlint-disable-next-line no-await-in-loop -- slices are encrypted one at a time to keep memory bounded
    const plain = new Uint8Array(await slice.arrayBuffer());
    offset += plain.length;
    const isFinal = offset >= file.size;

    // oxlint-disable-next-line no-await-in-loop -- frames stay in order, one per slice
    parts.push(await sealFrame(key, plain, aadFor(chunkIndex, isFinal), chunkIndex, isFinal));
    chunkIndex += 1;
  } while (offset < file.size);

  return new Blob(parts, { type: 'application/octet-stream' });
};

/**
 * Decrypts all frames of a buffered sealed blob. Only fully verified frames
 * are appended to the result.
 */
export const openFrames = async (
  key: CryptoKey,
  sealed: Uint8Array,
  aadFor: FrameAad
): Promise<Uint8Array<ArrayBuffer>> => {
  if (!isFramed(sealed)) {
    throw new Error('Payload is not framed');
  }

  const parts: Uint8Array<ArrayBuffer>[] = [];

  await new ReadableStream<Uint8Array>({
    start(controller) {
      controller.enqueue(sealed);
      controller.close();
    }
  })
    .pipeThrough(frameDecryptStream(key, aadFor))
    .pipeTo(
      new WritableStream<Uint8Array<ArrayBuffer>>({
        write(chunk) {
          parts.push(chunk);
        }
      })
    );

  return concatBytes(...parts);
};

/**
 * Streams encrypted frames through to decrypted plaintext. Each frame is
 * decrypted and its GCM tag verified before its plaintext is emitted, so a
 * consumer never sees bytes from a tampered or truncated frame.
 */
export const frameDecryptStream = (
  key: CryptoKey | (() => Promise<CryptoKey>),
  aadFor: FrameAad
): TransformStream<Uint8Array, Uint8Array> => {
  const queue = new ByteQueue();
  let expectedChunkIndex = 0;
  let finished = false;
  let keyPromise: Promise<CryptoKey> | undefined;

  const resolveKey = (): Promise<CryptoKey> => {
    keyPromise ??= typeof key === 'function' ? key() : Promise.resolve(key);
    return keyPromise;
  };

  const decryptFrame = async (
    header: FrameHeader,
    cipher: Uint8Array<ArrayBuffer>
  ): Promise<Uint8Array<ArrayBuffer>> => {
    const [resolvedKey, iv] = await Promise.all([resolveKey(), frameIv(header.chunkIndex)]);
    const plain = await crypto.subtle.decrypt(
      {
        name: 'AES-GCM',
        iv: toArrayBuffer(iv),
        additionalData: toArrayBuffer(aadFor(header.chunkIndex, header.isFinal))
      },
      resolvedKey,
      toArrayBuffer(cipher)
    );
    return new Uint8Array(plain);
  };

  return new TransformStream<Uint8Array, Uint8Array>({
    async transform(chunk, controller) {
      if (chunk.length === 0) {
        return;
      }

      if (finished) {
        controller.error(new Error('Trailing bytes after the final frame'));
        return;
      }

      queue.push(chunk);

      while (true) {
        const header = queue.peekHeader();
        if (!header || queue.byteLength < HEADER_BYTES + header.ciphertextLength + TAG_BYTES) {
          return;
        }

        queue.consume(HEADER_BYTES);
        const cipher = queue.consume(header.ciphertextLength + TAG_BYTES);

        if (header.chunkIndex !== expectedChunkIndex) {
          controller.error(new Error('Frame sequence mismatch'));
          return;
        }

        let plain: Uint8Array<ArrayBuffer>;
        try {
          // oxlint-disable-next-line no-await-in-loop -- each frame is verified and emitted before the next is read
          plain = await decryptFrame(header, cipher);
        } catch {
          controller.error(new Error('Frame decryption failed'));
          return;
        }

        controller.enqueue(plain);
        expectedChunkIndex += 1;
        if (header.isFinal) {
          finished = true;
          if (queue.byteLength > 0) {
            controller.error(new Error('Trailing bytes after the final frame'));
            return;
          }
        }
      }
    },
    flush(controller) {
      if (!finished) {
        controller.error(new Error('Truncated frame stream'));
      }
    }
  });
};

type FrameHeader = {
  chunkIndex: number;
  isFinal: boolean;
  ciphertextLength: number;
};

/**
 * Byte accumulator for the streaming frame reader. Chunks arrive in arbitrary
 * sizes, so headers and frames are assembled across parts without copying the
 * whole stream at once.
 */
class ByteQueue {
  private parts: Uint8Array[] = [];
  private length = 0;

  get byteLength(): number {
    return this.length;
  }

  push(chunk: Uint8Array): void {
    if (chunk.length > 0) {
      this.parts.push(chunk);
      this.length += chunk.length;
    }
  }

  peekHeader(): FrameHeader | null {
    if (this.length < HEADER_BYTES) {
      return null;
    }

    const header = this.peek(HEADER_BYTES);
    if (!MAGIC.every((byte, index) => header[index] === byte)) {
      throw new Error('Payload is not framed');
    }

    const flags = header[FLAGS_OFFSET];
    if (flags !== 0 && flags !== 1) {
      throw new Error('Invalid frame flags');
    }

    const ciphertextLength = readUint32(header, LENGTH_OFFSET);
    if (ciphertextLength > FRAME_PLAINTEXT_BYTES) {
      throw new Error('Frame exceeds maximum size');
    }

    return {
      chunkIndex: readUint32(header, CHUNK_INDEX_OFFSET),
      isFinal: flags === 1,
      ciphertextLength
    };
  }

  /**
   * Returns the first <paramref name="count"/> bytes without consuming them.
   */
  peek(count: number): Uint8Array {
    const out = new Uint8Array(count);
    let copied = 0;

    for (const part of this.parts) {
      if (copied >= count) {
        break;
      }

      const take = Math.min(part.length, count - copied);
      out.set(part.subarray(0, take), copied);
      copied += take;
    }

    return out;
  }

  consume(count: number): Uint8Array<ArrayBuffer> {
    const out = new Uint8Array(count);
    let copied = 0;

    while (copied < count) {
      const part = this.parts.shift();
      if (!part) {
        break;
      }

      const take = Math.min(part.length, count - copied);
      out.set(part.subarray(0, take), copied);
      copied += take;
      this.length -= take;

      if (take < part.length) {
        this.parts.unshift(part.subarray(take));
      }
    }

    return out;
  }
}
