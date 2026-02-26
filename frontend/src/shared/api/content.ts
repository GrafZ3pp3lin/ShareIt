import {
  addTextToPackage,
  createPackage,
  downloadContentWithToken,
  getPackage,
  uploadFileToPackage,
  type ContentPackageDetailsResponse,
  type ContentPackageResponse,
  type CreatePackageRequest,
} from '../../client';
type ApiResult<T> = {
  data?: T;
  error?: unknown;
  request?: Request;
  response?: Response;
};

export async function createContentPackage(request: CreatePackageRequest): Promise<ApiResult<ContentPackageResponse>> {
  return createPackage({
    body: request,
  });
}

export async function addTextContent(packageId: string, text: string): Promise<ApiResult<ContentPackageResponse>> {
  return addTextToPackage({
    path: { packageId },
    body: { Text: text },
  });
}

export async function uploadFiles(packageId: string, files: File[]): Promise<ApiResult<ContentPackageResponse>> {
  return uploadFileToPackage({
    path: { packageId },
    body: { files },
  });
}

export async function getPackageDetails(packageId: string): Promise<ApiResult<ContentPackageDetailsResponse>> {
  return getPackage({
    path: { packageId },
  });
}

export async function downloadContent(packageId: string, contentId: string, token: string): Promise<ApiResult<Blob>> {
  const result = await downloadContentWithToken({
    path: { packageId, contentId },
    headers: { 'X-File-Token': token },
    parseAs: 'blob',
  });
  if (result.data instanceof Blob) {
    return result as ApiResult<Blob>;
  }
  return {
    error: result.error ?? new Error('Download did not return a blob'),
  };
}
