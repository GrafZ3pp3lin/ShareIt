// Access-counter presets for the `access:` pill. `null` means unlimited.
export const DOWNLOAD_COUNT_OPTIONS = [null, 1, 3, 5, 10] as const;

export const DEFAULT_DOWNLOAD_COUNT_INDEX = 0;

// Guests get no access picker: their packages may be accessed exactly once.
export const ANONYMOUS_DOWNLOAD_COUNT = 1;

export function cycleDownloadCountIndex(index: number): number {
  return (index + 1) % DOWNLOAD_COUNT_OPTIONS.length;
}
