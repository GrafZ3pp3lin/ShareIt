import * as matchers from '@testing-library/jest-dom/matchers';
import h from 'solid-js/h';
import { expect } from 'vitest';

expect.extend(matchers);

type ReactLike = {
  createElement: typeof h;
  Fragment: (props: { children?: unknown }) => unknown;
};

(globalThis as typeof globalThis & { React?: ReactLike }).React = {
  createElement: h,
  Fragment: (props) => props.children,
};
