export type StreamSaveResult = 'saved' | 'canceled' | 'unsupported';

/**
 * Saves buffered bytes through an object URL. Used as the fallback when the
 * browser has no writable file destination.
 */
export const saveBlob = (bytes: Blob | Uint8Array<ArrayBuffer>, name: string, mime: string) => {
  const url = URL.createObjectURL(bytes instanceof Blob ? bytes : new Blob([bytes], { type: mime }));
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  link.click();
  URL.revokeObjectURL(url);
};

/**
 * Streams a download to a file the user picks, without ever holding the whole
 * body in memory. The body is piped through the frame decryptor when one is
 * provided (protected packages), or straight through otherwise. Without a
 * writable destination, the plaintext chunks are collected into a Blob and
 * saved through an object URL.
 *
 * The picker is opened first: browsers only allow it during the click that
 * started the download, and the bytes are not requested until a destination
 * exists. A canceled picker is not an error; other picker failures fall back
 * to the buffered path.
 */
export const saveResponseStream = async (
  suggestedName: string,
  mime: string,
  load: () => Promise<Response>,
  decrypt?: TransformStream<Uint8Array, Uint8Array>
): Promise<StreamSaveResult> => {
  // Suggested names are not paths; separators can make the picker throw.
  const safeName = suggestedName.replace(/[\\/]/g, '-');

  let writable: FileSystemWritableFileStream | undefined;
  if (window.showSaveFilePicker) {
    try {
      const handle = await window.showSaveFilePicker({ suggestedName: safeName });
      writable = await handle.createWritable();
    } catch (error) {
      if (isAbortError(error)) {
        return 'canceled';
      }

      // SecurityError/NotAllowedError/TypeError mean this context cannot use the
      // picker. Fall through to the buffered Blob path.
    }
  }

  try {
    const response = await load();
    if (!response.body) {
      await discard(writable);
      return 'unsupported';
    }

    const stream = decrypt ? response.body.pipeThrough(decrypt) : response.body;

    if (writable) {
      await stream.pipeTo(writable);
      return 'saved';
    }

    const parts: BlobPart[] = [];
    await stream.pipeTo(
      new WritableStream<Uint8Array<ArrayBuffer>>({
        write(chunk) {
          parts.push(chunk);
        }
      })
    );
    saveBlob(new Blob(parts, { type: mime }), safeName, mime);
    return 'saved';
  } catch (error) {
    // `pipeTo` aborts the destination on its own when the stream fails.
    await discard(writable);
    throw error;
  }
};

const discard = async (writable?: FileSystemWritableFileStream) => {
  if (!writable) {
    return;
  }

  try {
    await writable.abort();
  } catch {
    // Already closed or aborted by pipeTo.
  }
};

const isAbortError = (error: unknown): boolean =>
  typeof error === 'object' && error !== null && 'name' in error && error.name === 'AbortError';
