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
    isolate: false,
    projects: [
      {
        test: {
          include: ['src/**/*.{test,spec}.ts'],
          name: 'unit',
          environment: 'node',
          isolate: false
        }
      },
      {
        test: {
          include: ['src/**/*.{test,spec}.tsx'],
          name: 'components',
          environment: 'happy-dom',
          isolate: false,
          setupFiles: './src/tests/vitest-setup.ts'
        }
      }
    ]
  }
});
