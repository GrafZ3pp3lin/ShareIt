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
  define: {
    // solid-testing-library relies on "process" which is not shimmed by default
    'process.env.STL_SKIP_AUTO_CLEANUP': 'false'
  },
  test: {
    globals: true,
    projects: [
      {
        test: {
          include: ['src/**/*.unit.{test,spec}.{ts,tsx}'],
          name: 'unit',
          environment: 'node'
        }
      },
      {
        test: {
          include: ['src/**/*.browser.{test,spec}.{ts,tsx}'],
          name: 'components',
          environment: 'happy-dom',
          setupFiles: './src/tests/vitest-setup.ts'
        }
      }
    ]
  }
});
