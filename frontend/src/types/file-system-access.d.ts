/**
 * File System Access API entry point used to stream downloads straight to
 * disk. `createWritable()` and `FileSystemWritableFileStream` are already in
 * lib.dom, but the window-level picker is not, so it is declared here. Feature
 * detection in `save-file.ts` guards every use.
 */
interface SaveFilePickerOptions {
  suggestedName?: string;
  types?: Array<{ description?: string; accept: Record<string, string[]> }>;
}

interface Window {
  showSaveFilePicker?(options?: SaveFilePickerOptions): Promise<FileSystemFileHandle>;
}
