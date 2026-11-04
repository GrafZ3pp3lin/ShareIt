// Temporary bridge for the pre-redesign share page while the composer is
// migrated (M3). The share page is replaced in M4; this file goes with it.

import type { ContentItemInPackage } from '../../client/model/index.ts';
import { fromBase64Url } from '../../shared/crypto/base64url.ts';
import { parseMeta } from '../../shared/crypto/meta.ts';
import { isSealed } from '../../shared/crypto/sealed.ts';

export type LegacyItem = {
  Id: string;
  Type: 'text' | 'file';
  FileName?: string;
  MimeType: string;
  Size: number;
  Text?: string;
  DownloadToken?: string;
  DownloadUrl?: string;
  TokenExpiresAt?: string;
};

const ENCRYPTED_FILE_MIME_TYPE = 'application/x-shareit-encrypted';

export const toLegacyItem = (item: ContentItemInPackage): LegacyItem => {
  const payload = item.Payload ? fromBase64Url(item.Payload) : undefined;
  const meta = item.Meta ? parseMeta(fromBase64Url(item.Meta)) : null;
  const sealed = payload ? isSealed(payload) : false;

  return {
    Id: item.Id,
    Type: item.Kind === 'text' ? 'text' : 'file',
    FileName: meta?.name ?? undefined,
    MimeType: meta?.mime ?? (sealed ? ENCRYPTED_FILE_MIME_TYPE : 'application/octet-stream'),
    Size: Number(item.Size),
    Text: item.Kind === 'text' && payload && !sealed ? new TextDecoder().decode(payload) : undefined,
    DownloadToken: item.DownloadToken ?? undefined,
    DownloadUrl: item.DownloadUrl ?? undefined,
    TokenExpiresAt: item.TokenExpiresAt ?? undefined
  };
};
