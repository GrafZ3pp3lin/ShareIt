/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import solid from "vite-plugin-solid";
import tailwindcss from "@tailwindcss/vite";
import Icons from "unplugin-icons/vite";
import { playwright } from "@vitest/browser-playwright";

export default defineConfig({
  plugins: [solid(), tailwindcss(), Icons({ compiler: "solid" })],
  define: {
    // solid-testing-library relies on "process" which is not shimmed by default
    "process.env.STL_SKIP_AUTO_CLEANUP": "false",
  },
  test: {
    globals: true,
    setupFiles: "./src/tests/vitest-setup.ts",
    projects: [
      {
        test: {
          include: [
            "tests/unit/**/*.{test,spec}.{ts,tsx}",
            "tests/**/*.unit.{test,spec}.{ts,tsx}",
          ],
          name: "unit",
          environment: "node",
        },
      },
      {
        test: {
          // an example of file based convention,
          // you don't have to follow it
          include: [
            "tests/browser/**/*.{test,spec}.{ts,tsx}",
            "tests/**/*.browser.{test,spec}.{ts,tsx}",
          ],
          name: "browser",
          browser: {
            enabled: true,
            headless: true,
            provider: playwright(),
            instances: [{ browser: "chromium" }],
          },
        },
      },
    ],
  },
});
