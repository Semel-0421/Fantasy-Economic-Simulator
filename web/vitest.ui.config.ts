import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: { environment: 'node', include: ['tests/e2e/ui.test.ts'], testTimeout: 240000, fileParallelism: false },
});
