import {
  createPackage,
  downloadContentWithToken,
  getPackage,
  type ContentPackageDetailsResponse,
  type ContentPackageResponse,
  type CreatePackageRequest
} from '../../client/index.ts';
import { client } from '../../client/client.gen.ts';
import { getRuntimeConfig } from '../config/runtime-config.ts';
import { getAuthHeaders } from './client.ts';
type ApiResult<T> = {
  data?: T;
  error?: unknown;
  request?: Request;
  response?: Response;
};

export type UploadLimits = {
  AnonymousFileLimitBytes: number;
  AuthenticatedFileLimitBytes: number;
};

const isUploadLimits = (value: unknown): value is UploadLimits =>
  typeof value === 'object' &&
  value !== null &&
  'AnonymousFileLimitBytes' in value &&
  'AuthenticatedFileLimitBytes' in value;

export async function createContentPackage(request: CreatePackageRequest): Promise<ApiResult<ContentPackageResponse>> {
  return createPackage({
    body: request
  });
}

export async function getUploadLimits(): Promise<ApiResult<UploadLimits>> {
  const result = await client.get({ url: '/api/v1/content/package/limits' });
  if (isUploadLimits(result.data)) {
    return { data: result.data, error: undefined, response: result.response };
  }
  return {
    error: result.error ?? new Error('Unexpected upload limits response')
  };
}

export type UploadProgressCallback = (sentBytes: number, totalBytes: number) => void;

export type UploadFileResult = ApiResult<ContentPackageResponse> & {
  canceled?: boolean;
};

const isContentPackageResponse = (value: unknown): value is ContentPackageResponse =>
  typeof value === 'object' && value !== null && 'Id' in value;

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
  const headers = await getAuthHeaders();
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

export async function getPackageDetails(packageId: string): Promise<ApiResult<ContentPackageDetailsResponse>> {
  return getPackage({
    path: { packageId }
  });
}

export async function downloadContent(packageId: string, contentId: string, token: string): Promise<ApiResult<Blob>> {
  const result = await downloadContentWithToken({
    path: { packageId, contentId },
    headers: { 'X-File-Token': token },
    parseAs: 'blob'
  });
  if (result.data instanceof Blob) {
    return { data: result.data, error: undefined, request: result.request, response: result.response };
  }
  return {
    error: result.error ?? new Error('Download did not return a blob')
  };
}
