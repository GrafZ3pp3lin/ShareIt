// @vitest-environment happy-dom
import { beforeEach, describe, expect, it } from 'vitest';
import { clearShortCode, loadShortCode, saveShortCode } from './short-code-store.ts';

const PACKAGE_ID = '0192f3ab-7cde-7def-8abc-0123456789ab';
const KEY = `shareit-short-code:${PACKAGE_ID}`;
const future = () => new Date(Date.now() + 60_000).toISOString();

describe('short-code storage', () => {
  beforeEach(() => {
    window.sessionStorage.clear();
  });

  it('round-trips a normalized code', () => {
    saveShortCode(PACKAGE_ID, 'abc-234', future());

    expect(loadShortCode(PACKAGE_ID)).toBe('ABC234');
  });

  it('removes expired and malformed entries', () => {
    saveShortCode(PACKAGE_ID, 'ABC234', new Date(Date.now() - 1_000).toISOString());
    expect(loadShortCode(PACKAGE_ID)).toBeNull();
    expect(window.sessionStorage.getItem(KEY)).toBeNull();

    window.sessionStorage.setItem(KEY, 'not-json');
    expect(loadShortCode(PACKAGE_ID)).toBeNull();
    expect(window.sessionStorage.getItem(KEY)).toBeNull();

    window.sessionStorage.setItem(KEY, JSON.stringify({ Code: 'ABC', ExpiresAt: future() }));
    expect(loadShortCode(PACKAGE_ID)).toBeNull();
    expect(window.sessionStorage.getItem(KEY)).toBeNull();
  });

  it('clears only the requested package code', () => {
    saveShortCode(PACKAGE_ID, 'ABC234', future());
    saveShortCode('other-package', 'DEF567', future());

    clearShortCode(PACKAGE_ID);

    expect(loadShortCode(PACKAGE_ID)).toBeNull();
    expect(loadShortCode('other-package')).toBe('DEF567');
  });
});
