import { fileURLToPath } from 'node:url';
import { checkAssets } from './lib/asset-check.mjs';

const result = checkAssets(fileURLToPath(new URL('../public/assets/', import.meta.url)));
for (const error of result.errors) console.error(error);
console.log(`Assets: ${result.files.length} valid, ${result.errors.length} errors`);
process.exitCode = result.errors.length ? 1 : 0;
