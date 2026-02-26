/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import solid from 'vite-plugin-solid';
import tailwindcss from '@tailwindcss/vite';
import Icons from 'unplugin-icons/vite';

export default defineConfig({
  plugins: [solid(), tailwindcss(), Icons({ compiler: 'solid' })],
  esbuild: {
    jsx: 'preserve',
    jsxImportSource: 'solid-js',
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
          include: ['tests/unit/**/*.{test,spec}.{ts,tsx}', 'tests/**/*.unit.{test,spec}.{ts,tsx}', 'src/**/*.unit.{test,spec}.{ts,tsx}'],
          name: 'unit',
          environment: 'node',
        },
      },
      {
        test: {
          include: [
            'tests/**/*.browser.{test,spec}.{ts,tsx}',
            'src/**/*.browser.{test,spec}.{ts,tsx}',
          ],
          name: 'components',
          environment: 'jsdom',
          setupFiles: './src/tests/vitest-setup.ts',
        },
      },
    ],
  },
});
