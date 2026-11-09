import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: { environment: 'node', include: ['tests/**/*.test.ts'], exclude: ['tests/e2e/ui.test.ts'], testTimeout: 60000 },
});
