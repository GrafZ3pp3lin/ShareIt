import * as matchers from '@testing-library/jest-dom/matchers';
import { afterEach } from 'vitest';
import { cleanup } from '@solidjs/testing-library';
import { expect } from 'vitest';

expect.extend(matchers);

// solid-testing-library's own auto-cleanup registration runs at module import
// time, before the vitest globals are populated — register it here instead.
afterEach(cleanup);
