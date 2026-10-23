export type UploadResult = {
  link: string;
  passphrase?: string;
  expiryLabel: string;
  encrypted: boolean;
  /** True when the result panel shows a link for a package whose file uploads did not all finish. */
  incomplete?: boolean;
};
