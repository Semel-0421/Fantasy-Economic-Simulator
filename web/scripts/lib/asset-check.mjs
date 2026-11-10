import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { checkPng } from './png.mjs';
import { checkOgg } from './ogg.mjs';

const manifest = JSON.parse(readFileSync(new URL('../../src/assets/manifest.json', import.meta.url), 'utf8'));
const contract = new Map(Object.values(manifest).map((asset) => [asset.path, asset]));

function checkFile(path, absolute) {
  const asset = contract.get(path);
  if (!asset) throw new Error('Unknown asset name/folder (not in manifest)');
  const size = statSync(absolute).size;
  if (size > 200000) throw new Error(`File is ${size} bytes; maximum 200 KB (200000 bytes)`);
  const data = readFileSync(absolute);
  if (path.endsWith('.png')) checkPng(data, asset.size, path.startsWith('map/terrain/'));
  else checkOgg(data, Number.parseFloat(asset.size));
  return { path, size, sha256: createHash('sha256').update(data).digest('hex') };
}

export function checkAssets(root) {
  const files = [], errors = [];
  const visit = (folder, prefix = '') => {
    for (const entry of readdirSync(folder, { withFileTypes: true }).sort((a,b) => a.name.localeCompare(b.name, 'en'))) {
      const path = prefix + entry.name, absolute = join(folder, entry.name);
      if (entry.isSymbolicLink()) { errors.push(`${path}: symbolic links are not assets`); continue; }
      if (entry.isDirectory()) { visit(absolute, `${path}/`); continue; }
      if (entry.name === '.gitkeep' || path === 'README.md' || path === 'present.json') continue;
      try { files.push(checkFile(path, absolute)); } catch (error) { errors.push(`${path}: ${error.message}`); }
    }
  };
  visit(root);
  return { files, errors };
}
