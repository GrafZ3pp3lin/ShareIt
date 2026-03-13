import { describe, expect, it } from 'vitest';
import { getErrorMessage } from './error-message.ts';

describe('getErrorMessage', () => {
  it('returns the fallback for nullish values', () => {
    expect(getErrorMessage(null)).toBe('Something went wrong.');
    expect(getErrorMessage(undefined, 'Custom fallback')).toBe('Custom fallback');
  });

  it('returns string errors directly', () => {
    expect(getErrorMessage('Request failed')).toBe('Request failed');
  });

  it('prefers Error.message for Error instances', () => {
    expect(getErrorMessage(new Error('Boom'))).toBe('Boom');
    expect(getErrorMessage(new Error(''), 'Fallback')).toBe('Fallback');
  });

  it('extracts ProblemDetails detail before title', () => {
    expect(getErrorMessage({ detail: 'Detailed problem', title: 'Problem title' })).toBe('Detailed problem');
    expect(getErrorMessage({ title: 'Problem title' })).toBe('Problem title');
  });

  it('falls back to object.message for non-Error objects', () => {
    expect(getErrorMessage({ message: 'Plain object message' })).toBe('Plain object message');
  });

  it('returns the fallback for unsupported shapes', () => {
    expect(getErrorMessage({ code: 500 }, 'Fallback')).toBe('Fallback');
    expect(getErrorMessage(12345)).toBe('Something went wrong.');
  });
});
