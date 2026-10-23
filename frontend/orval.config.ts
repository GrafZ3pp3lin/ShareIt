import { defineConfig } from 'orval';

export default defineConfig({
  shareit: {
    input: 'http://localhost:5155/openapi/v1.json',
    output: {
      clean: true,
      client: 'fetch',
      mode: 'split',
      target: './src/client/api.ts',
      schemas: './src/client/model',
      mock: false,
      override: {
        mutator: {
          path: './src/shared/api/fetch-client.ts',
          name: 'customFetch'
        }
      }
    }
  }
});
