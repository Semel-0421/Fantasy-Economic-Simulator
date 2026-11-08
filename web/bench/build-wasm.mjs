// Builds the release wasm, generates the JS glue, and runs wasm-opt -O3 (binaryen from npm).
import { execFileSync } from 'node:child_process';
import { statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const engine = join(here, '..', '..', 'engine');
const pkg = join(here, 'pkg');
const run = (cmd, args, cwd = here) => execFileSync(cmd, args, { cwd, stdio: 'inherit' });

// PROFILE=1 builds `WasmSim::step_profiled` (per-system times) into the wasm.
const profile = process.env.PROFILE === '1' ? ['--features', 'profile'] : [];
run('cargo', ['build', '-p', 'fes-wasm', '--release', '--target', 'wasm32-unknown-unknown', ...profile], engine);
const raw = join(engine, 'target', 'wasm32-unknown-unknown', 'release', 'fes_wasm.wasm');
run('wasm-bindgen', ['--target', 'web', '--out-dir', pkg, raw]);
const out = join(pkg, 'fes_wasm_bg.wasm');
const before = statSync(out).size;
const features = ['bulk-memory', 'nontrapping-float-to-int', 'sign-ext', 'reference-types', 'multivalue'];
run(process.execPath, [
  join(here, 'node_modules', 'binaryen', 'bin', 'wasm-opt'),
  '-O3',
  ...features.map((f) => `--enable-${f}`),
  out,
  '-o',
  out,
], here);
console.log(`fes_wasm_bg.wasm: ${before} bytes before wasm-opt, ${statSync(out).size} after`);
