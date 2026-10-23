import * as matchers from '@testing-library/jest-dom/matchers';
import h from '@solidjs/h';
import { afterEach } from 'vitest';
import { cleanup } from '@solidjs/testing-library';
import { expect } from 'vitest';

expect.extend(matchers);

// solid-testing-library's own auto-cleanup registration runs at module import
// time, before the vitest globals are populated — register it here instead.
afterEach(cleanup);

type ReactLike = {
  createElement: typeof h;
  Fragment: (props: { children?: unknown }) => unknown;
};

(globalThis as typeof globalThis & { React?: ReactLike }).React = {
  createElement: h,
  Fragment: (props) => props.children
};
