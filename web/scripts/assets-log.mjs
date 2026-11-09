import { fileURLToPath } from 'node:url';
import { checkAssets } from './lib/asset-check.mjs';

const result = checkAssets(fileURLToPath(new URL('../public/assets/', import.meta.url)));
for (const error of result.errors) console.error(error);
console.log('| Path | Bytes | SHA-256 |\n| --- | ---: | --- |');
for (const file of result.files) console.log(`| ${file.path} | ${file.size} | ${file.sha256} |`);
process.exitCode = result.errors.length ? 1 : 0;
