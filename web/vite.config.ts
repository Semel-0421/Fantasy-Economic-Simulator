import { createHash } from 'node:crypto';
import { readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { defineConfig, type Plugin } from 'vite';
import { version } from './package.json';

function listFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    return statSync(path).isDirectory() ? listFiles(path) : [path];
  });
}

/** Writes dist/sw.js: a precache of every built file under a cache name derived from their contents. */
function serviceWorker(): Plugin {
  let outDir = 'dist';
  return {
    name: 'fes-service-worker',
    apply: 'build',
    configResolved(config) {
      outDir = config.build.outDir;
    },
    closeBundle() {
      const files = listFiles(outDir)
        .map((f) => relative(outDir, f).replaceAll(sep, '/'))
        .filter((f) => f !== 'sw.js')
        .sort();
      const hash = createHash('sha256');
      for (const f of files) hash.update(f).update(readFileSync(join(outDir, f)));
      const template = readFileSync(join('src', 'sw.template.js'), 'utf8');
      const urls = ['./', ...files.map((f) => `./${f}`)];
      writeFileSync(
        join(outDir, 'sw.js'),
        template.replace('__CACHE__', `fes-${hash.digest('hex').slice(0, 12)}`).replace('__FILES__', JSON.stringify(urls)),
      );
    },
  };
}

// Relative base: the build runs from any folder, including an itch.io HTML5 embed.
export default defineConfig({
  base: './',
  define: { __APP_VERSION__: JSON.stringify(version) },
  worker: { format: 'es' },
  build: { target: 'es2022', outDir: 'dist', emptyOutDir: true },
  plugins: [serviceWorker()],
});
