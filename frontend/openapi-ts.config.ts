import { defineConfig } from "@hey-api/openapi-ts";

export default defineConfig({
  input: {
    path: "http://localhost:5155/openapi/v1.json",
  },
  output: {
    postProcess: ["oxfmt"],
    path: "./src/client",
  },
  plugins: [
    "@hey-api/client-fetch",
    "@hey-api/sdk",
    {
      enums: "javascript",
      name: "@hey-api/typescript",
    },
  ],
});