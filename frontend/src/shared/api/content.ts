import {
  createPackage as createPackageGenerated,
  deletePackage as deletePackageGenerated,
  downloadContent as downloadContentGenerated,
  getDownloadContentUrl,
  getPackage as getPackageGenerated,
  getUploadLimits as getUploadLimitsGenerated,
  listPackages as listPackagesGenerated,
  resolveShortCode as resolveShortCodeGenerated
} from '../../client/api.ts';
import type {
  ContentPackageDetailsResponse,
  ContentPackageListItem,
  ContentPackageOwnerResponse,
  CreatePackageRequest,
  ResolveShortCodeResponse,
  UploadLimitsResponse
} from '../../client/model/index.ts';
import { getAuthHeaders, absoluteApiUrl, parseProblem } from './fetch-client.ts';
import { isValidId } from '../crypto/ids.ts';
import { SHORT_CODE_LOCATOR_LENGTH } from '../short-code.ts';
import { getRuntimeConfig } from '../config/runtime-config.ts';

export type ApiResult<T> = {
  data?: T;
  error?: unknown;
  /** HTTP status of the response, when the wrapper has one. */
  status?: number;
  /** Seconds until a 429 response can be retried, when the server sent one. */
  retryAfterSeconds?: number;
};

const retryAfterSeconds = (headers?: Headers): number | undefined => {
  const value = headers?.get('Retry-After');
  if (!value) {
    return undefined;
  }

  const seconds = Number(value);
  return Number.isFinite(seconds) && seconds >= 0 ? Math.ceil(seconds) : undefined;
};

export type UploadLimits = {
  AnonymousFileLimitBytes: number;
  AuthenticatedFileLimitBytes: number;
  AnonymousMaxLifetimeHours: number;
  AuthenticatedMaxLifetimeHours: number;
  AnonymousMaxAccessCount: number;
  AuthenticatedMaxAccessCount: number;
  MaxItemsPerPackage: number;
};

const toUploadLimits = (value: UploadLimitsResponse): UploadLimits => ({
  AnonymousFileLimitBytes: Number(value.AnonymousFileLimitBytes),
  AuthenticatedFileLimitBytes: Number(value.AuthenticatedFileLimitBytes),
  AnonymousMaxLifetimeHours: Number(value.AnonymousMaxLifetimeHours),
  AuthenticatedMaxLifetimeHours: Number(value.AuthenticatedMaxLifetimeHours),
  AnonymousMaxAccessCount: Number(value.AnonymousMaxAccessCount),
  AuthenticatedMaxAccessCount: Number(value.AuthenticatedMaxAccessCount),
  MaxItemsPerPackage: Number(value.MaxItemsPerPackage)
});

const isOptionalShortCode = (value: unknown): value is string | null | undefined =>
  value == null || (typeof value === 'string' && value.length === SHORT_CODE_LOCATOR_LENGTH);

// XHR returns `unknown`; the generated client path does not need a guard.
const isContentPackageOwnerResponse = (value: unknown): value is ContentPackageOwnerResponse =>
  typeof value === 'object' &&
  value !== null &&
  'Id' in value &&
  typeof value.Id === 'string' &&
  (!('ShortCode' in value) || isOptionalShortCode(value.ShortCode));

/** A list row with its generated int64 fields narrowed to numbers. */
export type PackageSummary = {
  Id: string;
  HasShortCode: boolean;
  CreatedAt: string;
  ExpiresAt: string;
  ItemCount: number;
  TotalSize: number;
  MaxAccessCount: number | null;
  CurrentAccessCount: number;
  RemainingAccesses: number | null;
  IsExpired: boolean;
  IsProtected: boolean;
};

export type PackageListPage = {
  items: PackageSummary[];
  nextCursor: string | null;
};

const toPackageSummary = (item: ContentPackageListItem): PackageSummary => ({
  Id: item.Id,
  HasShortCode: item.HasShortCode,
  CreatedAt: item.CreatedAt,
  ExpiresAt: item.ExpiresAt,
  ItemCount: Number(item.ItemCount),
  TotalSize: Number(item.TotalSize),
  MaxAccessCount: item.MaxAccessCount == null ? null : Number(item.MaxAccessCount),
  CurrentAccessCount: Number(item.CurrentAccessCount),
  RemainingAccesses: item.RemainingAccesses == null ? null : Number(item.RemainingAccesses),
  IsExpired: item.IsExpired,
  IsProtected: item.IsProtected
});

export type UploadProgressCallback = (sentBytes: number, totalBytes: number) => void;

export type UploadItem = {
  Id: string;
  Kind: 'text' | 'file';
  /** base64url of the framed-or-plaintext `{ name, mime }` blob. */
  Meta: string;
  File: Blob;
};

export type UploadItemResult = ApiResult<ContentPackageOwnerResponse> & {
  canceled?: boolean;
};

/**
 * Uploads a single item — text bytes or file bytes — with real progress
 * reporting via XHR. Resolves with `canceled: true` when the signal was
 * aborted before completion.
 *
 * The body is a raw binary PUT: the payload is streamed from the Blob by the
 * browser (the client never reads the bytes into memory), the metadata rides
 * in the X-Content-Kind/X-Content-Meta headers and Content-Length is the
 * declared size.
 */
export async function uploadItem(
  packageId: string,
  item: UploadItem,
  uploadToken: string,
  onProgress?: UploadProgressCallback,
  signal?: AbortSignal
): Promise<UploadItemResult> {
  if (signal?.aborted) {
    return { canceled: true };
  }

  const uploadUrl = absoluteApiUrl(`/api/v1/content/package/${packageId}/content/${item.Id}`);
  const headers = await getAuthHeaders(uploadUrl, 'PUT');

  // Token/DPoP generation is asynchronous; the signal may have been aborted
  // while we were waiting. An already-aborted signal will not fire again.
  if (signal?.aborted) {
    return { canceled: true };
  }

  const baseUrl = getRuntimeConfig().apiBaseUrl;

  return new Promise((resolve) => {
    const xhr = new XMLHttpRequest();
    xhr.open('PUT', `${baseUrl}/api/v1/content/package/${packageId}/content/${item.Id}`);
    xhr.responseType = 'json';
    for (const [name, value] of Object.entries(headers)) {
      xhr.setRequestHeader(name, value);
    }
    xhr.setRequestHeader('Content-Type', 'application/octet-stream');
    xhr.setRequestHeader('X-Upload-Token', uploadToken);
    xhr.setRequestHeader('X-Content-Kind', item.Kind);
    xhr.setRequestHeader('X-Content-Meta', item.Meta);

    if (onProgress) {
      xhr.upload.addEventListener('progress', (event) => {
        if (event.lengthComputable) {
          onProgress(event.loaded, event.total);
        }
      });
    }

    const abort = () => xhr.abort();
    const cleanup = () => {
      signal?.removeEventListener('abort', abort);
    };

    if (signal) {
      signal.addEventListener('abort', abort, { once: true });
    }

    xhr.addEventListener('load', () => {
      cleanup();
      if (xhr.status >= 200 && xhr.status < 300) {
        if (isContentPackageOwnerResponse(xhr.response)) {
          resolve({ data: xhr.response });
        } else {
          resolve({ error: new Error(`Unexpected upload response (status ${xhr.status})`) });
        }
      } else {
        resolve({ error: xhr.response ?? new Error(`Upload failed with status ${xhr.status}`) });
      }
    });
    xhr.addEventListener('abort', () => {
      cleanup();
      resolve({ canceled: true });
    });
    xhr.addEventListener('error', () => {
      cleanup();
      resolve({ error: new Error('Network error during upload') });
    });
    xhr.send(item.File);
  });
}

export async function createPackage(
  request: CreatePackageRequest,
  unlockProof?: string
): Promise<ApiResult<ContentPackageOwnerResponse>> {
  const result = await createPackageGenerated(request, unlockProof ? { 'X-Unlock-Proof': unlockProof } : undefined);
  if (result.status === 200 || result.status === 201) {
    return { data: result.data };
  }
  return { error: result.data };
}

export async function getUploadLimits(): Promise<ApiResult<UploadLimits>> {
  const result = await getUploadLimitsGenerated();
  if (result.status === 200) {
    return { data: toUploadLimits(result.data) };
  }
  return { error: result.data };
}

/**
 * Resolves a three-character short-code locator to the canonical package
 * UUID. The response contains no package metadata and the request does not
 * claim an access.
 */
export async function resolveShortCode(code: string): Promise<ApiResult<ResolveShortCodeResponse>> {
  const result = await resolveShortCodeGenerated({ Code: code });
  const retryAfter = retryAfterSeconds(result.headers);
  if (result.status === 200 && isValidId(result.data.PackageId)) {
    return { data: result.data, status: result.status, retryAfterSeconds: retryAfter };
  }
  return { error: result.data, status: result.status, retryAfterSeconds: retryAfter };
}

/**
 * Fetches a package with the unlock proof and/or the existing access session.
 * Both headers are only ever attached to this call.
 */
export async function getPackageDetails(
  packageId: string,
  unlockProof?: string,
  accessSession?: string
): Promise<ApiResult<ContentPackageDetailsResponse>> {
  const headers: { 'X-Unlock-Proof'?: string; 'X-Access-Session'?: string } = {};
  if (unlockProof) {
    headers['X-Unlock-Proof'] = unlockProof;
  }
  if (accessSession) {
    headers['X-Access-Session'] = accessSession;
  }

  const result = await getPackageGenerated(packageId, Object.keys(headers).length > 0 ? headers : undefined);
  if (result.status === 200) {
    return { data: result.data, status: result.status };
  }
  return { error: result.data, status: result.status };
}

export async function downloadContent(
  packageId: string,
  contentId: string,
  accessSession: string
): Promise<ApiResult<Blob>> {
  const result = await downloadContentGenerated(packageId, contentId, {
    'X-Access-Session': accessSession
  });
  if (result.status !== 200) {
    return { error: result.data, status: result.status };
  }
  if (result.data instanceof Blob) {
    return { data: result.data, status: result.status };
  }
  return { error: new Error('Download did not return a blob'), status: result.status };
}

export type DownloadResponseResult = {
  response?: Response;
  error?: unknown;
  status?: number;
};

/**
 * Fetches an item body as a raw `Response` so a caller can pipe it straight to
 * a writable destination. The generated client parses the body into a `Blob`,
 * which copies the whole file into memory first.
 */
export async function downloadContentResponse(
  packageId: string,
  contentId: string,
  accessSession: string
): Promise<DownloadResponseResult> {
  const url = absoluteApiUrl(getDownloadContentUrl(packageId, contentId));
  const headers = {
    ...(await getAuthHeaders(url, 'GET')),
    'X-Access-Session': accessSession
  };

  const response = await fetch(url, { headers });
  if (response.status === 200) {
    return { response, status: response.status };
  }

  return { error: await parseProblem(response), status: response.status };
}

export async function listPackages(cursor?: string): Promise<ApiResult<PackageListPage>> {
  const result = await listPackagesGenerated(cursor ? { cursor } : undefined);
  if (result.status === 200) {
    return {
      data: {
        items: result.data.Items.map(toPackageSummary),
        nextCursor: result.data.NextCursor ?? null
      },
      status: result.status
    };
  }
  return { error: result.data, status: result.status };
}

export async function deletePackage(packageId: string): Promise<ApiResult<true>> {
  const result = await deletePackageGenerated(packageId);
  if (result.status === 204) {
    return { data: true, status: result.status };
  }
  return { error: result.data, status: result.status };
}
