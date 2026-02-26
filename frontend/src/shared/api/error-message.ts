import type { ProblemDetails } from '../../client';

const isProblemDetails = (value: unknown): value is ProblemDetails => {
  if (!value || typeof value !== 'object') {
    return false;
  }

  return 'title' in value || 'detail' in value;
};

export const getErrorMessage = (error: unknown, fallback = 'Something went wrong.') => {
  if (!error) {
    return fallback;
  }

  if (typeof error === 'string') {
    return error;
  }

  if (error instanceof Error) {
    return error.message || fallback;
  }

  if (isProblemDetails(error)) {
    return error.detail ?? error.title ?? fallback;
  }

  if (typeof error === 'object') {
    const maybeMessage = (error as { message?: string }).message;
    if (maybeMessage) {
      return maybeMessage;
    }
  }

  return fallback;
};
