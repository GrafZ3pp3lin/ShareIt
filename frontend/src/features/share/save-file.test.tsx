import { afterEach, describe, expect, it, vi } from 'vitest';
import { saveResponseStream } from './save-file.ts';

const writableStream = () => {
  const chunks: Uint8Array[] = [];
  const aborted = vi.fn();
  const stream = new WritableStream<Uint8Array>({
    write: (chunk) => {
      chunks.push(chunk);
    },
    abort: aborted
  });

  return { stream, chunks, aborted };
};

const picker = (stream: WritableStream<Uint8Array>) =>
  vi.fn(async () => ({
    createWritable: async () => stream
  }));

const stubPicker = (showSaveFilePicker: unknown) => {
  vi.stubGlobal('window', { showSaveFilePicker });
};

const captureDownloads = () => {
  const click = vi.fn();
  const revoke = vi.fn();

  vi.stubGlobal('URL', {
    ...URL,
    createObjectURL: vi.fn(() => 'blob:stub'),
    revokeObjectURL: revoke
  });
  vi.stubGlobal('document', {
    createElement: vi.fn(() => ({
      href: '',
      download: '',
      click
    }))
  });

  return { click, revoke };
};

describe('saveResponseStream', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('streams the response body into the picked destination', async () => {
    const { stream, chunks } = writableStream();
    stubPicker(picker(stream));

    await expect(
      saveResponseStream('file.bin', 'application/octet-stream', async () => new Response('file-bytes'))
    ).resolves.toBe('saved');
    expect(new TextDecoder().decode(chunks[0])).toBe('file-bytes');
  });

  it('pipes the body through the frame decryptor when one is given', async () => {
    const { stream, chunks } = writableStream();
    stubPicker(picker(stream));

    const upper = new TransformStream<Uint8Array, Uint8Array>({
      transform(chunk, controller) {
        controller.enqueue(new TextEncoder().encode(new TextDecoder().decode(chunk).toUpperCase()));
      }
    });

    await expect(saveResponseStream('file.bin', 'text/plain', async () => new Response('plain'), upper)).resolves.toBe(
      'saved'
    );
    expect(new TextDecoder().decode(chunks[0])).toBe('PLAIN');
  });

  it('collects plaintext chunks into a Blob without a writable destination', async () => {
    stubPicker(undefined);
    const { click } = captureDownloads();

    await expect(
      saveResponseStream('file.bin', 'text/plain', async () => new Response('buffered-bytes'))
    ).resolves.toBe('saved');
    expect(click).toHaveBeenCalledTimes(1);
  });

  it('buffers through the decryptor too', async () => {
    stubPicker(undefined);
    const { click } = captureDownloads();

    const upper = new TransformStream<Uint8Array, Uint8Array>({
      transform(chunk, controller) {
        controller.enqueue(new TextEncoder().encode(new TextDecoder().decode(chunk).toUpperCase()));
      }
    });

    await expect(saveResponseStream('file.bin', 'text/plain', async () => new Response('plain'), upper)).resolves.toBe(
      'saved'
    );
    expect(click).toHaveBeenCalledTimes(1);
  });

  it('falls back to the buffered path when the picker is blocked by the browser context', async () => {
    const error = Object.assign(new Error('blocked'), { name: 'SecurityError' });
    stubPicker(
      vi.fn(async () => {
        throw error;
      })
    );
    const { click } = captureDownloads();

    await expect(saveResponseStream('file.bin', 'text/plain', async () => new Response('bytes'))).resolves.toBe(
      'saved'
    );
    expect(click).toHaveBeenCalledTimes(1);
  });

  it('treats a canceled picker as a no-op', async () => {
    const error = Object.assign(new Error('canceled'), { name: 'AbortError' });
    stubPicker(
      vi.fn(async () => {
        throw error;
      })
    );

    await expect(saveResponseStream('file.bin', 'text/plain', vi.fn())).resolves.toBe('canceled');
  });

  it('sanitizes path separators in the suggested name', async () => {
    const { stream } = writableStream();
    const showSaveFilePicker = picker(stream);
    stubPicker(showSaveFilePicker);

    await expect(saveResponseStream('a/b\\c.txt', 'text/plain', async () => new Response('ok'))).resolves.toBe('saved');
    expect(showSaveFilePicker).toHaveBeenCalledWith({ suggestedName: 'a-b-c.txt' });
  });

  it('falls back when the response has no body', async () => {
    const { stream, aborted } = writableStream();
    stubPicker(picker(stream));

    await expect(saveResponseStream('file.bin', 'text/plain', async () => new Response(null))).resolves.toBe(
      'unsupported'
    );
    expect(aborted).toHaveBeenCalledTimes(1);
  });

  it('surfaces transfer failures instead of treating them as canceled', async () => {
    const { stream, aborted } = writableStream();
    stubPicker(picker(stream));

    await expect(
      saveResponseStream('file.bin', 'text/plain', async () => {
        throw new Error('network failed');
      })
    ).rejects.toThrow('network failed');
    expect(aborted).toHaveBeenCalledTimes(1);
  });
});
