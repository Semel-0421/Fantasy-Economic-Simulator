import { createHash } from 'node:crypto';
import { readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import { defineConfig, type Plugin } from 'vite';
import { version } from './package.json';
import { FONT_PROFILES } from './src/i18n/font-profiles';

function listFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    return statSync(path).isDirectory() ? listFiles(path) : [path];
  });
}

/** Precache the core; cache optional language chunks/fonts only for the chosen language. */
function serviceWorker(): Plugin {
  let outDir = 'dist';
  const optional = new Set<string>();
  const languageFiles: Record<string, string[]> = {};
  return {
    name: 'fes-service-worker',
    apply: 'build',
    configResolved(config) {
      outDir = config.build.outDir;
    },
    generateBundle(_, bundle) {
      const collect = (file: string, seen = new Set<string>()): Set<string> => {
        if (seen.has(file)) return seen;
        seen.add(file);
        const chunk = bundle[file];
        if (chunk?.type === 'chunk') for (const dependency of [...chunk.imports, ...chunk.referencedFiles]) collect(dependency, seen);
        return seen;
      };
      const eager = new Set(Object.values(bundle).filter(chunk => chunk.type === 'chunk' && chunk.isEntry).flatMap(chunk => [...collect(chunk.fileName)]));
      const fontFiles = Object.keys(bundle).filter(file => /(?:Galmuri11.*\.woff2|pixelify-sans-latin-400-normal.*\.woff)$/.test(file));
      for (const file of fontFiles) optional.add(file);
      for (const file of Object.keys(bundle)) if (/\/language-[^/]+\.js$/.test(file)) optional.add(file);
      for (const chunk of Object.values(bundle)) {
        if (chunk.type !== 'chunk' || !chunk.name.startsWith('language-')) continue;
        const code = chunk.name.slice('language-'.length) as keyof typeof FONT_PROFILES;
        const files = [...collect(chunk.fileName)].filter(file => !eager.has(file));
        files.forEach(file => optional.add(file));
        const fonts = fontFiles.filter(file => FONT_PROFILES[code] === 'galmuri' ? file.includes('Galmuri11') : FONT_PROFILES[code] === 'pixelify' ? file.endsWith('.woff') : false);
        languageFiles[code] = [...files, ...fonts].map(file => `./${file}`);
      }
    },
    closeBundle() {
      const files = listFiles(outDir)
        .map((f) => relative(outDir, f).replaceAll(sep, '/'))
        .filter((f) => f !== 'sw.js')
        .sort();
      const hash = createHash('sha256');
      for (const f of files) hash.update(f).update(readFileSync(join(outDir, f)));
      const template = readFileSync(join('src', 'sw.template.js'), 'utf8');
      const urls = ['./', ...files.filter(file => !optional.has(file)).map((f) => `./${f}`)];
      writeFileSync(
        join(outDir, 'sw.js'),
        template.replace('__CACHE__', `fes-${hash.digest('hex').slice(0, 12)}`).replace('__FILES__', JSON.stringify(urls)).replace('/*__OPTIONAL__*/ {}', JSON.stringify(languageFiles)),
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
