// The full-scale CSVs are served as static files straight from the engine fixtures.
import { defineConfig } from 'vite';

export default defineConfig({
  publicDir: '../../engine/fixtures/fullscale',
  worker: { format: 'es' },
});
