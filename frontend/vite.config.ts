/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import solid from 'vite-plugin-solid';
import tailwindcss from '@tailwindcss/vite';
import Icons from 'unplugin-icons/vite';
import mkcert from 'vite-plugin-mkcert';

export default defineConfig({
  server: {
    host: 'localhost',
    port: 3001,
  },
  plugins: [solid(), tailwindcss(), Icons({ compiler: 'solid' }), mkcert()],
  oxc: {
    jsx: {
      importSource: 'solid-js',
    },
  },
  define: {
    // solid-testing-library relies on "process" which is not shimmed by default
    'process.env.STL_SKIP_AUTO_CLEANUP': 'false',
  },
  test: {
    globals: true,
    projects: [
      {
        test: {
          include: ['src/**/*.unit.{test,spec}.{ts,tsx}'],
          name: 'unit',
          environment: 'node',
        },
      },
      {
        test: {
          include: ['src/**/*.browser.{test,spec}.{ts,tsx}'],
          name: 'components',
          environment: 'jsdom',
          setupFiles: './src/tests/vitest-setup.ts',
        },
      },
    ],
  },
});
