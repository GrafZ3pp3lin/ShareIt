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

describe('saveResponseStream', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('falls back to the buffered path when the picker is unavailable', async () => {
    stubPicker(undefined);
    const load = vi.fn();

    await expect(saveResponseStream('file.bin', load)).resolves.toBe('unsupported');
    expect(load).not.toHaveBeenCalled();
  });

  it('falls back when the picker is blocked by the browser context', async () => {
    const error = Object.assign(new Error('blocked'), { name: 'SecurityError' });
    stubPicker(
      vi.fn(async () => {
        throw error;
      })
    );
    const load = vi.fn();

    await expect(saveResponseStream('file.bin', load)).resolves.toBe('unsupported');
    expect(load).not.toHaveBeenCalled();
  });

  it('treats a canceled picker as a no-op', async () => {
    const error = Object.assign(new Error('canceled'), { name: 'AbortError' });
    stubPicker(
      vi.fn(async () => {
        throw error;
      })
    );

    await expect(saveResponseStream('file.bin', vi.fn())).resolves.toBe('canceled');
  });

  it('sanitizes path separators in the suggested name', async () => {
    const { stream } = writableStream();
    const showSaveFilePicker = picker(stream);
    stubPicker(showSaveFilePicker);

    await expect(saveResponseStream('a/b\\c.txt', async () => new Response('ok'))).resolves.toBe('saved');
    expect(showSaveFilePicker).toHaveBeenCalledWith({ suggestedName: 'a-b-c.txt' });
  });

  it('streams the response body into the picked destination', async () => {
    const { stream, chunks } = writableStream();
    stubPicker(picker(stream));

    await expect(saveResponseStream('file.bin', async () => new Response('file-bytes'))).resolves.toBe('saved');
    expect(new TextDecoder().decode(chunks[0])).toBe('file-bytes');
  });

  it('falls back when the response has no body', async () => {
    const { stream, aborted } = writableStream();
    stubPicker(picker(stream));

    await expect(saveResponseStream('file.bin', async () => new Response(null))).resolves.toBe('unsupported');
    expect(aborted).toHaveBeenCalledTimes(1);
  });

  it('surfaces transfer failures instead of treating them as canceled', async () => {
    const { stream, aborted } = writableStream();
    stubPicker(picker(stream));

    await expect(
      saveResponseStream('file.bin', async () => {
        throw new Error('network failed');
      })
    ).rejects.toThrow('network failed');
    expect(aborted).toHaveBeenCalledTimes(1);
  });
});
