// Paired full-scale survey: node scripts/map-eval.mjs output.md [before.json] [options-json]
import assert from 'node:assert/strict';
import { readFileSync, writeFileSync } from 'node:fs';
import { createServer } from 'vite';
import { morphology, minimumSeaGap, straightCoastRuns } from './map-metrics.mjs';

const mean = (xs) => xs.reduce((a, b) => a + b, 0) / xs.length;
const band = (lat) => Math.floor(lat / 10) * 10;
const sum = (xs) => xs.reduce((a, b) => a + b, 0);
const round = (x) => Number(x.toFixed(2));

function centroid(grid, latitude, cells) {
  const angles = cells.map((i) => grid.x[i] / grid.width * 2 * Math.PI);
  const longitude = Math.atan2(sum(angles.map(Math.sin)), sum(angles.map(Math.cos))) * 180 / Math.PI;
  return { latitude: mean(cells.map((i) => latitude[Math.floor(i / grid.cols)])), longitude };
}

function longitudeSpread(lons) {
  const sorted = lons.map((x) => (x + 360) % 360).sort((a, b) => a - b);
  return 360 - Math.max(...sorted.map((x, i) => (sorted[i + 1] ?? sorted[0] + 360) - x));
}

// Seam-crossing continents must average at the seam, not in the middle of the map.
assert.ok(Math.abs(centroid({ x: [1, 359], width: 360, cols: 2 }, [5], [0, 1]).longitude) < 1e-9);
assert.equal(longitudeSpread([-179, 179]), 2);
assert.equal(band(-0.1), -10);

function measure(w, ms) {
  const { grid, climate, land } = w, landCount = sum(land), bands = {}, biomes = {};
  let coastline = 0;
  for (let i = 0; i < grid.size; i++) {
    const key = band(climate.latitude[Math.floor(i / grid.cols)]);
    bands[key] ??= { cells: 0, land: 0 };
    bands[key].cells++; bands[key].land += land[i];
    if (!land[i]) continue;
    biomes[w.biome[i]] = (biomes[w.biome[i]] ?? 0) + 1;
    for (let n = 0; n < 6; n++) {
      const j = grid.nb[i * 6 + n];
      if (j >= 0 && w.height[j] < w.sea) coastline++;
    }
  }
  const centroids = w.continents.map((cells) => ({ ...centroid(grid, climate.latitude, cells), size: cells.length }));
  const counts = {};
  for (const c of centroids) counts[band(c.latitude)] = (counts[band(c.latitude)] ?? 0) + 1;
  const occupancy = Object.values(counts).sort((a, b) => b - a);
  const coastRuns = straightCoastRuns(grid, Uint8Array.from(w.height, (h) => Number(h < w.sea)), w.continents);
  return {
    seed: w.seed, layout: w.layout ?? 'scattered', attempt: w.attempt, ms, grid: `${grid.cols}x${grid.rows}`, landShare: landCount / grid.size,
    ...morphology(w, centroids),
    minimumSeaGap: minimumSeaGap(grid, w.continents),
    coastRuns, maxCoastRun: Math.max(...coastRuns),
    bands, centroids, latitudeSpread: Math.max(...centroids.map((c) => c.latitude)) - Math.min(...centroids.map((c) => c.latitude)),
    longitudeSpread: longitudeSpread(centroids.map((c) => c.longitude)),
    duplicateBands: occupancy.filter((n) => n >= 2).length, maxBand: occupancy[0],
    twoRows: (occupancy[0] + (occupancy[1] ?? 0)) >= 4,
    equatorialLand: sum(Object.entries(bands).filter(([k]) => Number(k) >= -10 && Number(k) < 10).map(([, b]) => b.land)),
    sizeRatio: Math.max(...centroids.map((c) => c.size)) / Math.min(...centroids.map((c) => c.size)),
    coastlinePerLand: coastline / landCount, riverHexes: sum(w.river), riverShare: sum(w.river) / landCount,
    lakeShare: sum(w.drainage.lake) / (landCount + sum(w.drainage.lake)),
    mountainShare: (biomes.mountain ?? 0) / landCount, desertShare: (biomes.desert ?? 0) / landCount,
    snowShare: (biomes.snow ?? 0) / landCount,
  };
}

function summary(rows) {
  const count = (fn) => `${rows.filter(fn).length}/${rows.length}`;
  return {
    'Regular rows: >=3 in 12 degrees, circular gap CV <=0.25': count((r) => r.regularRow),
    'Fuller hemisphere share >=58%': count((r) => r.hemisphereShare >= 0.58),
    'Fuller hemisphere share <=85%': count((r) => r.hemisphereShare <= 0.85),
    'Nearest-neighbour distance CV >=0.25': count((r) => r.nearestCv >= 0.25),
    'At least one aspect ratio >=2': count((r) => r.maxAspect >= 2),
    'Elongated continent at 30..150 degrees from east-west': count((r) => r.elongatedNonZonal),
    'Land share in 30..36%': count((r) => r.landShare >= 0.3 && r.landShare <= 0.36),
    'Longest aligned coast run <=6 hexes': count((r) => r.maxCoastRun <= 6),
    'At least three sea cells between mainlands': count((r) => r.minimumSeaGap >= 3),
    'Land in [-10, 10) degrees': count((r) => r.equatorialLand > 0),
    'At most two centroids per 10 degree band': count((r) => r.maxBand <= 2),
    'Two bands hold at least four centroids': count((r) => r.twoRows),
    'Any band holds two or more centroids': count((r) => r.duplicateBands > 0),
    'Largest/smallest continent >= 1.4': count((r) => r.sizeRatio >= 1.4),
    'Rivers in 40..120 hexes': count((r) => r.riverHexes >= 40 && r.riverHexes <= 120),
    'Desert share <= 10%': count((r) => r.desertShare <= 0.1),
    'Snow share <= 5%': count((r) => r.snowShare <= 0.05),
    'Generation <= 300 ms': count((r) => r.ms <= 300),
    ...Object.fromEntries(['maxCoastRun', 'minimumSeaGap', 'hemisphereShare', 'nearestCv', 'maxAspect', 'latitudeSpread', 'longitudeSpread', 'sizeRatio', 'coastlinePerLand', 'landShare', 'mountainShare', 'riverShare', 'lakeShare', 'desertShare', 'snowShare', 'riverHexes', 'ms'].map((key) => {
      const xs = rows.map((r) => r[key]);
      const scale = key.endsWith('Share') ? 100 : 1, unit = scale === 100 ? '%' : '';
      return [key, `${round(scale * mean(xs))}${unit} [${round(scale * Math.min(...xs))}, ${round(scale * Math.max(...xs))}]`];
    })),
  };
}

const check = measure({
  seed: 'check', attempt: 0, grid: { size: 4, cols: 2, rows: 2, x: [1, 359, 1, 359], y: [0, 0, 1, 1], width: 360, nb: new Int32Array(24).fill(-1) },
  climate: { latitude: [5, -5] }, land: [1, 1, 0, 0], height: [1, 1, -1, -1], sea: 0,
  biome: ['plains', 'desert', 'water', 'water'], continents: [[0, 1]], river: [1, 0, 0, 0], drainage: { lake: [0, 0, 0, 0] },
}, 10);
assert.equal(check.landShare, 0.5);
assert.equal(check.desertShare, 0.5);
assert.equal(check.riverShare, 0.5);
assert.equal(check.equatorialLand, 2);
assert.equal(check.bands[0].land, 2);
assert.equal(check.bands[-10].land, 0);
assert.equal(check.maxBand, 1);
assert.equal(check.twoRows, false);
assert.equal(summary([check])['Largest/smallest continent >= 1.4'], '0/1');

const output = process.argv[2];
if (!output?.endsWith('.md')) throw new Error('Usage: node scripts/map-eval.mjs output.md [before.json]');
const overrides = process.argv[4] ? JSON.parse(process.argv[4]) : {};
const server = await createServer({ server: { middlewareMode: true }, optimizeDeps: { noDiscovery: true, entries: [] }, logLevel: 'error' });
try {
  const { generateWorld, DEFAULT_GEN_OPTIONS } = await server.ssrLoadModule('/src/map/gen/world.ts');
  const { continentNeeds } = await server.ssrLoadModule('/src/map/hex_layout.ts');
  const { fullscaleMap } = await server.ssrLoadModule('/tests/fullscale-map.ts');
  const needs = continentNeeds(fullscaleMap);
  generateWorld('eval-warmup', needs, overrides);
  const seeds = ['scenario-1', 'scenario-2', 'scenario-3', ...Array.from({ length: 27 }, (_, i) => `sample-${i + 4}`)];
  const rows = seeds.map((seed) => {
    const start = performance.now(), w = generateWorld(seed, needs, overrides), ms = performance.now() - start;
    assert.equal(w.continents.length, 5);
    w.continents.forEach((c, k) => assert.ok(c.length >= needs[k], `${seed} continent ${k} too small`));
    assert.equal(new Set(w.continents.flat()).size, sum(w.continents.map((c) => c.length)));
    return measure(w, ms);
  });
  const stats = summary(rows), before = process.argv[3] ? summary(JSON.parse(readFileSync(process.argv[3], 'utf8')).rows) : null;
  const lines = ['# Map generator evaluation', '', `Generated ${new Date().toISOString()}; Node ${process.version}; ${process.platform}/${process.arch}.`,
    `30 paired full-scale seeds; needs ${needs.join(', ')}; grid ${rows[0].grid}. One unmeasured warmup; generation only, sequential, Vite SSR in Node (not browser layout).`,
    `Options: ${JSON.stringify({ ...DEFAULT_GEN_OPTIONS, ...overrides })}.`,
    '', 'JSON share fields are fractions; report shares use percentages. Biomes and rivers / dry land; lakes / (dry land + lakes). Coastline counts land-to-sea hex edges, excluding lakes. Longitude centroids use circular means; east-west spread is the minimum covering arc. Latitude spread is max minus min centroid latitude.',
    'Bands are half-open [lower, lower+10). Equatorial means [-10, 10). Two-row proxy: the two most occupied centroid bands hold at least four of five continents; it is a diagnostic, not a visual proof. Timing varies with machine load.', '',
    'Round 2 metrics: regular rows test every subset of >=3 centroids for latitude span <=12 degrees and CV <=0.25 of cyclic longitude gaps (including the seam gap). Hemisphere area splits equator cells equally. Nearest distances are cylindrical pixel distances between unwrapped land centroids. PCA aspect is sqrt(major/minor covariance eigenvalue); angles are degrees from east-west, modulo 180.', '',
    `| Metric (mean [min, max] where numeric) | ${before ? 'Before | After' : 'Value'} |`, `| --- | ${before ? '--- | ---' : '---'} |`,
    ...Object.entries(stats).map(([key, value]) => `| ${key} | ${before ? `${before[key]} | ` : ''}${value} |`), '',
    '## Land by latitude band (pooled over 30 maps)', '', '| Latitude | Land / cells in band | Share of all land |', '| --- | --- | --- |'];
  const pooled = {};
  for (const r of rows) for (const [k, b] of Object.entries(r.bands)) {
    pooled[k] ??= { cells: 0, land: 0 }; pooled[k].cells += b.cells; pooled[k].land += b.land;
  }
  const allLand = sum(Object.values(pooled).map((b) => b.land));
  for (const k of Object.keys(pooled).map(Number).sort((a, b) => b - a)) lines.push(`| [${k}, ${k + 10}) | ${round(100 * pooled[k].land / pooled[k].cells)}% | ${round(100 * pooled[k].land / allLand)}% |`);
  lines.push('', '## Per-seed measurements', '', '| Seed | Attempt | Centroids: latitude, longitude (degrees) / size | N-S spread | E-W arc | Max band | Duplicate bands | Size ratio | Coast / land | River hexes | Mountain / river / lake / desert / snow % | ms |', '| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |');
  for (const r of rows) lines.push(`| ${r.seed} | ${r.attempt} | ${r.centroids.map((c) => `${round(c.latitude)}, ${round(c.longitude)} / ${c.size}`).join('; ')} | ${round(r.latitudeSpread)} | ${round(r.longitudeSpread)} | ${r.maxBand} | ${r.duplicateBands} | ${round(r.sizeRatio)} | ${round(r.coastlinePerLand)} | ${r.riverHexes} | ${[r.mountainShare, r.riverShare, r.lakeShare, r.desertShare, r.snowShare].map((x) => round(100 * x)).join(' / ')} | ${round(r.ms)} |`);
  lines.push('', '## Round 2 per-seed geometry', '', '| Seed | Layout | Regular row | Fuller hemisphere | Nearest distance CV | Aspects / angles |', '| --- | --- | --- | --- | --- | --- |');
  for (const r of rows) lines.push(`| ${r.seed} | ${r.layout} | ${r.regularRow} | ${round(r.hemisphereShare)} | ${round(r.nearestCv)} | ${r.axes.map((a) => `${round(a.ratio)} / ${round(a.angle)} degrees`).join('; ')} |`);
  const types = ['scattered', 'clustered', 'rifted'];
  const perType = Object.fromEntries(types.map((type) => { const group = rows.filter((r) => r.layout === type); return [type, group.length ? summary(group) : null]; }));
  lines.push('', '## Per-type metrics', '', `| Metric | ${types.map((type) => `${type} (${rows.filter((r) => r.layout === type).length} worlds)`).join(' | ')} |`, '| --- | --- | --- | --- |');
  for (const key of Object.keys(stats)) lines.push(`| ${key} | ${types.map((type) => perType[type]?.[key] ?? 'no worlds').join(' | ')} |`);
  lines.push('', '## Straight coast runs per mainland', '', 'A run consists of consecutive mainland cells in one hex direction, all exposing the same side to the sea. Lakes are excluded; wraparound runs are counted across the seam. This detects straight-looking faceted hex coasts rather than requiring mathematically collinear polygon edges.', '', '| Seed | Mainland maximum runs (hexes) |', '| --- | --- |');
  for (const r of rows) lines.push(`| ${r.seed} | ${r.coastRuns.join(', ')} |`);
  writeFileSync(output, `${lines.join('\n')}\n`);
  writeFileSync(output.replace(/\.md$/, '.json'), JSON.stringify({ options: { ...DEFAULT_GEN_OPTIONS, ...overrides }, needs, rows, summary: stats, perType }, null, 2));
  console.log(JSON.stringify(stats, null, 2));
} finally { await server.close(); }
