import { defineConfig } from 'vite';

// Relative base: the build runs from any folder, including an itch.io HTML5 embed.
export default defineConfig({
  base: './',
  worker: { format: 'es' },
  build: { target: 'es2022', outDir: 'dist', emptyOutDir: true },
});
