import {
  createPackage as createPackageGenerated,
  deletePackage as deletePackageGenerated,
  downloadContentWithToken,
  getPackage as getPackageGenerated,
  getUploadLimits as getUploadLimitsGenerated,
  listPackages as listPackagesGenerated
} from '../../client/api.ts';
import type {
  ContentPackageDetailsResponse,
  ContentPackageListItem,
  ContentPackageListResponse,
  ContentPackageResponse,
  CreatePackageRequest
} from '../../client/model/index.ts';
import { getAuthHeaders, absoluteApiUrl } from './fetch-client.ts';
import { getRuntimeConfig } from '../config/runtime-config.ts';

export type ApiResult<T> = {
  data?: T;
  error?: unknown;
  response?: Response;
  /** HTTP status of the response, when the wrapper has one. */
  status?: number;
};

const isSuccess = (status: number): boolean => status >= 200 && status < 300;

export type UploadLimits = {
  AnonymousFileLimitBytes: number;
  AuthenticatedFileLimitBytes: number;
  AnonymousMaxLifetimeHours: number;
  AuthenticatedMaxLifetimeHours: number;
  AnonymousMaxDownloadCount: number;
  AuthenticatedMaxDownloadCount: number;
};

const isUploadLimits = (value: unknown): value is UploadLimits =>
  typeof value === 'object' &&
  value !== null &&
  'AnonymousFileLimitBytes' in value &&
  'AuthenticatedFileLimitBytes' in value &&
  'AnonymousMaxLifetimeHours' in value &&
  'AuthenticatedMaxLifetimeHours' in value &&
  'AnonymousMaxDownloadCount' in value &&
  'AuthenticatedMaxDownloadCount' in value;

const isContentPackageResponse = (value: unknown): value is ContentPackageResponse =>
  typeof value === 'object' && value !== null && 'Id' in value;

const isContentPackageDetailsResponse = (value: unknown): value is ContentPackageDetailsResponse =>
  typeof value === 'object' && value !== null && 'Package' in value && 'Items' in value;

const isContentPackageListItem = (value: unknown): value is ContentPackageListItem =>
  typeof value === 'object' &&
  value !== null &&
  'Id' in value &&
  'CreatedAt' in value &&
  'ItemCount' in value &&
  'TotalSize' in value &&
  'CurrentDownloadCount' in value &&
  'IsExpired' in value &&
  'IsProtected' in value;

const isContentPackageListResponse = (value: unknown): value is ContentPackageListResponse =>
  typeof value === 'object' &&
  value !== null &&
  'Items' in value &&
  Array.isArray(value.Items) &&
  value.Items.every(isContentPackageListItem);

/** A list row with its generated int64 fields narrowed to numbers. */
export type PackageSummary = {
  Id: string;
  CreatedAt: string;
  ExpiresAt?: string;
  ItemCount: number;
  TotalSize: number;
  MaxDownloadCount: number | null;
  CurrentDownloadCount: number;
  RemainingDownloads: number | null;
  IsExpired: boolean;
  IsProtected: boolean;
};

export type PackageListPage = {
  items: PackageSummary[];
  nextCursor: string | null;
};

const toPackageSummary = (item: ContentPackageListItem): PackageSummary => ({
  Id: item.Id,
  CreatedAt: item.CreatedAt,
  ExpiresAt: item.ExpiresAt ?? undefined,
  ItemCount: Number(item.ItemCount),
  TotalSize: Number(item.TotalSize),
  MaxDownloadCount: item.MaxDownloadCount == null ? null : Number(item.MaxDownloadCount),
  CurrentDownloadCount: Number(item.CurrentDownloadCount),
  RemainingDownloads: item.RemainingDownloads == null ? null : Number(item.RemainingDownloads),
  IsExpired: item.IsExpired,
  IsProtected: item.IsProtected
});

export type UploadProgressCallback = (sentBytes: number, totalBytes: number) => void;

export type UploadItem = {
  Id: string;
  Kind: 'text' | 'file';
  /** base64url of the sealed-or-plaintext `{ name, mime }` blob. */
  Meta: string;
  File: Blob;
  FileName: string;
};

export type UploadItemResult = ApiResult<ContentPackageResponse> & {
  canceled?: boolean;
};

/**
 * Uploads a single item — text bytes or file bytes — with real progress
 * reporting via XHR. Resolves with `canceled: true` when the signal was
 * aborted before completion.
 */
export async function uploadItem(
  packageId: string,
  item: UploadItem,
  onProgress?: UploadProgressCallback,
  signal?: AbortSignal
): Promise<UploadItemResult> {
  const uploadUrl = absoluteApiUrl(`/api/v1/content/package/${packageId}/upload`);
  const headers = await getAuthHeaders(uploadUrl, 'POST');
  const baseUrl = getRuntimeConfig().apiBaseUrl;

  return new Promise((resolve) => {
    const form = new FormData();
    form.append('Id', item.Id);
    form.append('Kind', item.Kind);
    form.append('Meta', item.Meta);
    form.append('File', item.File, item.FileName);

    const xhr = new XMLHttpRequest();
    xhr.open('POST', `${baseUrl}/api/v1/content/package/${packageId}/upload`);
    xhr.responseType = 'json';
    for (const [name, value] of Object.entries(headers)) {
      xhr.setRequestHeader(name, value);
    }

    if (onProgress) {
      xhr.upload.addEventListener('progress', (event) => {
        if (event.lengthComputable) {
          onProgress(event.loaded, event.total);
        }
      });
    }

    if (signal) {
      signal.addEventListener('abort', () => xhr.abort(), { once: true });
    }

    xhr.addEventListener('load', () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        if (isContentPackageResponse(xhr.response)) {
          resolve({
            data: xhr.response,
            response: new Response(JSON.stringify(xhr.response), { status: xhr.status })
          });
        } else {
          resolve({ error: new Error(`Unexpected upload response (status ${xhr.status})`) });
        }
      } else {
        resolve({ error: xhr.response ?? new Error(`Upload failed with status ${xhr.status}`) });
      }
    });
    xhr.addEventListener('abort', () => resolve({ canceled: true }));
    xhr.addEventListener('error', () => resolve({ error: new Error('Network error during upload') }));
    xhr.send(form);
  });
}

export async function createPackage(request: CreatePackageRequest): Promise<ApiResult<ContentPackageResponse>> {
  const result = await createPackageGenerated(request);
  if (isSuccess(result.status) && isContentPackageResponse(result.data)) {
    return { data: result.data };
  }
  return { error: result.data };
}

export async function getUploadLimits(): Promise<ApiResult<UploadLimits>> {
  const result = await getUploadLimitsGenerated();
  if (isUploadLimits(result.data)) {
    return { data: result.data };
  }
  return {
    error: !isSuccess(result.status) ? result.data : new Error('Unexpected upload limits response')
  };
}

/**
 * Fetches a package, optionally with the `X-Unlock-Proof` header. The header is
 * only ever attached to this call.
 */
export async function getPackageDetails(
  packageId: string,
  unlockProof?: string
): Promise<ApiResult<ContentPackageDetailsResponse>> {
  const result = await getPackageGenerated(
    packageId,
    unlockProof ? { headers: { 'X-Unlock-Proof': unlockProof } } : undefined
  );
  if (isSuccess(result.status) && isContentPackageDetailsResponse(result.data)) {
    return { data: result.data, status: result.status };
  }
  return { error: result.data, status: result.status };
}

export async function downloadContent(packageId: string, contentId: string, token: string): Promise<ApiResult<Blob>> {
  const result = await downloadContentWithToken(packageId, contentId, {
    headers: { 'X-File-Token': token }
  });
  if (result.data instanceof Blob) {
    return { data: result.data, status: result.status };
  }
  if (isSuccess(result.status)) {
    return { error: new Error('Download did not return a blob'), status: result.status };
  }
  return { error: result.data, status: result.status };
}

export async function listPackages(cursor?: string): Promise<ApiResult<PackageListPage>> {
  const result = await listPackagesGenerated(cursor ? { cursor } : undefined);
  if (isSuccess(result.status) && isContentPackageListResponse(result.data)) {
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
  if (isSuccess(result.status)) {
    return { data: true, status: result.status };
  }
  return { error: result.data, status: result.status };
}
