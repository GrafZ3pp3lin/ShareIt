// @vitest-environment happy-dom
import { beforeEach, describe, expect, it } from 'vitest';
import { clearAccessSession, loadAccessSession, saveAccessSession } from './access-session.ts';

const PACKAGE_ID = '0192f3ab-7cde-7def-8abc-0123456789ab';
const KEY = `shareit-access-session:${PACKAGE_ID}`;

describe('access-session storage', () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  it('round-trips a session and ignores the client clock for expiry', () => {
    saveAccessSession(PACKAGE_ID, {
      Token: 'session-1',
      ExpiresAt: new Date(Date.now() - 60_000).toISOString()
    });

    expect(loadAccessSession(PACKAGE_ID)).toEqual({
      Token: 'session-1',
      ExpiresAt: expect.any(String)
    });
  });

  it('removes corrupt JSON and malformed sessions', () => {
    window.sessionStorage.setItem(KEY, 'not-json');
    expect(loadAccessSession(PACKAGE_ID)).toBeNull();
    expect(window.sessionStorage.getItem(KEY)).toBeNull();

    window.sessionStorage.setItem(KEY, JSON.stringify({ Token: '', ExpiresAt: '2026-11-04T12:15:00Z' }));
    expect(loadAccessSession(PACKAGE_ID)).toBeNull();
    expect(window.sessionStorage.getItem(KEY)).toBeNull();
  });

  it('clears only the requested package session', () => {
    saveAccessSession(PACKAGE_ID, { Token: 'session-1', ExpiresAt: '2026-11-04T12:15:00Z' });
    saveAccessSession('other-package', { Token: 'session-2', ExpiresAt: '2026-11-04T12:15:00Z' });

    clearAccessSession(PACKAGE_ID);

    expect(loadAccessSession(PACKAGE_ID)).toBeNull();
    expect(loadAccessSession('other-package')?.Token).toBe('session-2');
  });
});
