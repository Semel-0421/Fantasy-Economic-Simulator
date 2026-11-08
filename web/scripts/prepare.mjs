// Build-time assets: the wasm package (src/sim/pkg) and the game data (public/data).
// The wasm is rebuilt only when missing or with --force (`npm run wasm`).
import { execFileSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const web = join(here, '..');
const repo = join(web, '..');
const engine = join(repo, 'engine');
const pkg = join(web, 'src', 'sim', 'pkg');
const run = (cmd, args, cwd) => execFileSync(cmd, args, { cwd, stdio: 'inherit' });

/** wasm-bindgen-cli must be the version the engine's wasm-bindgen crate is locked to (engine/Cargo.lock). */
function checkBindgen() {
  const lock = readFileSync(join(engine, 'Cargo.lock'), 'utf8');
  const wanted = /name = "wasm-bindgen"\r?\nversion = "([^"]+)"/.exec(lock)?.[1];
  const have = /wasm-bindgen (\S+)/.exec(execFileSync('wasm-bindgen', ['--version'], { encoding: 'utf8' }))?.[1];
  if (wanted !== have) throw new Error(`wasm-bindgen-cli ${have} does not match Cargo.lock ${wanted}: cargo install wasm-bindgen-cli --version ${wanted} --locked`);
}

function buildWasm() {
  checkBindgen();
  run('cargo', ['build', '-p', 'fes-wasm', '--release', '--target', 'wasm32-unknown-unknown'], engine);
  const raw = join(engine, 'target', 'wasm32-unknown-unknown', 'release', 'fes_wasm.wasm');
  run('wasm-bindgen', ['--target', 'web', '--out-dir', pkg, raw], web);
  const out = join(pkg, 'fes_wasm_bg.wasm');
  const before = statSync(out).size;
  const features = ['bulk-memory', 'nontrapping-float-to-int', 'sign-ext', 'reference-types', 'multivalue'];
  run(
    process.execPath,
    [join(web, 'node_modules', 'binaryen', 'bin', 'wasm-opt'), '-O3', ...features.map((f) => `--enable-${f}`), out, '-o', out],
    web,
  );
  console.log(`fes_wasm_bg.wasm: ${before} bytes before wasm-opt, ${statSync(out).size} after`);
}

function copyData() {
  const from = join(repo, 'Assets', 'StreamingAssets', 'Data');
  const to = join(web, 'public', 'data');
  mkdirSync(to, { recursive: true });
  for (const name of readdirSync(from)) {
    if (name.endsWith('.csv')) copyFileSync(join(from, name), join(to, name));
  }
  // The scenarios run on the full-scale world (engine/fixtures/fullscale) as public/data/fullscale.
  const fullscale = join(engine, 'fixtures', 'fullscale');
  const fullscaleTo = join(to, 'fullscale');
  mkdirSync(fullscaleTo, { recursive: true });
  for (const name of readdirSync(fullscale)) {
    if (name.endsWith('.csv')) copyFileSync(join(fullscale, name), join(fullscaleTo, name));
  }
  // Scenario files (data/scenarios/*.ron, format in its README) ship as public/data/scenarios.
  const scenarios = join(repo, 'data', 'scenarios');
  const scenariosTo = join(to, 'scenarios');
  mkdirSync(scenariosTo, { recursive: true });
  for (const name of readdirSync(scenarios)) {
    if (name.endsWith('.ron')) copyFileSync(join(scenarios, name), join(scenariosTo, name));
  }
}

if (process.argv.includes('--force') || !existsSync(join(pkg, 'fes_wasm_bg.wasm'))) buildWasm();
copyData();
