export type StreamSaveResult = 'saved' | 'canceled' | 'unsupported';

/**
 * Saves buffered bytes through an object URL. Used for encrypted items and as
 * the fallback when the browser has no writable file destination.
 */
export const saveBlob = (bytes: Uint8Array<ArrayBuffer>, name: string, mime: string) => {
  const url = URL.createObjectURL(new Blob([bytes], { type: mime }));
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  link.click();
  URL.revokeObjectURL(url);
};

/**
 * Streams a download to a file the user picks, without ever holding the whole
 * body in memory. Returns `unsupported` when the browser has no writable
 * destination so the caller can fall back to {@link saveBlob}.
 *
 * The picker is opened first: browsers only allow it during the click that
 * started the download, and the bytes are not requested until a destination
 * exists. A canceled picker is not an error; other picker failures fall back to
 * the buffered path.
 */
export const saveResponseStream = async (
  suggestedName: string,
  load: () => Promise<Response>
): Promise<StreamSaveResult> => {
  if (!window.showSaveFilePicker) {
    return 'unsupported';
  }

  // Suggested names are not paths; separators can make the picker throw.
  const safeName = suggestedName.replace(/[\\/]/g, '-');

  let writable: FileSystemWritableFileStream;
  try {
    const handle = await window.showSaveFilePicker({ suggestedName: safeName });
    writable = await handle.createWritable();
  } catch (error) {
    if (isAbortError(error)) {
      return 'canceled';
    }

    // SecurityError/NotAllowedError/TypeError mean this context cannot use the
    // picker. Let the caller retry with the buffered Blob path.
    return 'unsupported';
  }

  try {
    const response = await load();
    if (!response.body) {
      await discard(writable);
      return 'unsupported';
    }

    await response.body.pipeTo(writable);
    return 'saved';
  } catch (error) {
    // `pipeTo` aborts the destination on its own when the stream fails.
    await discard(writable);
    throw error;
  }
};

const discard = async (writable: FileSystemWritableFileStream) => {
  try {
    await writable.abort();
  } catch {
    // Already closed or aborted by pipeTo.
  }
};

const isAbortError = (error: unknown): boolean =>
  typeof error === 'object' && error !== null && 'name' in error && error.name === 'AbortError';
