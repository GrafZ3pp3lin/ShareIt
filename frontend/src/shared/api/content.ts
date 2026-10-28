import {
  createPackage,
  downloadContentWithToken,
  getPackage,
  getUploadLimits as getUploadLimitsGenerated
} from '../../client/api.ts';
import type {
  ContentPackageDetailsResponse,
  ContentPackageResponse,
  CreatePackageRequest
} from '../../client/model/index.ts';
import { getAuthHeaders, absoluteApiUrl } from './fetch-client.ts';
import { getRuntimeConfig } from '../config/runtime-config.ts';

export type ApiResult<T> = {
  data?: T;
  error?: unknown;
  response?: Response;
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

export type UploadProgressCallback = (sentBytes: number, totalBytes: number) => void;

export type UploadFileResult = ApiResult<ContentPackageResponse> & {
  canceled?: boolean;
};

/**
 * Uploads a single file with real progress reporting via XHR (the fetch API
 * cannot report upload progress). Resolves with `canceled: true` when the
 * signal was aborted before completion.
 */
export async function uploadFile(
  packageId: string,
  file: File,
  onProgress?: UploadProgressCallback,
  signal?: AbortSignal
): Promise<UploadFileResult> {
  const uploadUrl = absoluteApiUrl(`/api/v1/content/package/${packageId}/upload`);
  const headers = await getAuthHeaders(uploadUrl, 'POST');
  const baseUrl = getRuntimeConfig().apiBaseUrl;

  return new Promise((resolve) => {
    const form = new FormData();
    form.append('files', file);

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
        resolve({
          error: xhr.response ?? new Error(`Upload failed with status ${xhr.status}`)
        });
      }
    });
    xhr.addEventListener('abort', () => resolve({ canceled: true }));
    xhr.addEventListener('error', () => resolve({ error: new Error('Network error during upload') }));
    xhr.send(form);
  });
}

export async function createContentPackage(request: CreatePackageRequest): Promise<ApiResult<ContentPackageResponse>> {
  const result = await createPackage(request);
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

export async function getPackageDetails(packageId: string): Promise<ApiResult<ContentPackageDetailsResponse>> {
  const result = await getPackage(packageId);
  if (isSuccess(result.status) && isContentPackageDetailsResponse(result.data)) {
    return { data: result.data };
  }
  return { error: result.data };
}

export async function downloadContent(packageId: string, contentId: string, token: string): Promise<ApiResult<Blob>> {
  const result = await downloadContentWithToken(packageId, contentId, {
    headers: { 'X-File-Token': token }
  });
  if (result.data instanceof Blob) {
    return { data: result.data };
  }
  if (isSuccess(result.status)) {
    return { error: new Error('Download did not return a blob') };
  }
  return { error: result.data };
}
