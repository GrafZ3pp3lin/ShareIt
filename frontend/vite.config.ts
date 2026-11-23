import { defineConfig } from 'vitest/config';
import solid from 'vite-plugin-solid';
import tailwindcss from '@tailwindcss/vite';
import Icons from 'unplugin-icons/vite';

export default defineConfig({
  // Plain http is deliberate: http://localhost is a "potentially trustworthy origin" per the
  // Secure Contexts spec, so crypto.subtle (shared/crypto) and navigator.clipboard both work
  // without TLS. Serving the dev server on a non-localhost origin (e.g. a LAN IP for phone
  // testing) would need https, since crypto.subtle is undefined outside a secure context.
  server: {
    host: 'localhost',
    port: 3001
  },
  plugins: [solid(), tailwindcss(), Icons({ compiler: 'solid' })],
  oxc: {
    jsx: {
      importSource: 'solid-js'
    }
  },
  test: {
    globals: true,
    environment: 'happy-dom',
    // Keep the default per-file isolation: with `isolate: false` the module
    // registry, `vi.mock` state and globals such as `fetch` are shared between
    // test files, so one file's stubs leak into the next and the result depends
    // on how Vitest distributes files across workers.
    projects: [
      {
        test: {
          include: ['src/**/*.{test,spec}.ts'],
          name: 'unit',
          environment: 'node'
        }
      },
      {
        test: {
          include: ['src/**/*.{test,spec}.tsx'],
          name: 'components',
          environment: 'happy-dom',
          setupFiles: './src/tests/vitest-setup.ts'
        }
      }
    ]
  }
});
