// Climate and biomes. Temperature falls with latitude and altitude (lapse rate). Prevailing winds
// follow the three-cell pattern (trade winds and polar easterlies blow west, westerlies blow east);
// air picks up moisture over water and drops it on land, most where it is forced uphill
// (orographic rain), leaving a rain shadow behind ranges. Biomes then follow a Whittaker-style
// temperature x moisture lookup, with elevation deciding hills and mountains.
import type { Grid } from './grid';

export type Biome = 'plains' | 'forest' | 'hills' | 'mountain' | 'water' | 'desert' | 'snow';

export interface Climate {
  /** Latitude in degrees per row (north positive). */
  latitude: Float32Array;
  temperature: Float32Array;
  rain: Float32Array;
}

export function climate(grid: Grid, h: Float32Array, water: Uint8Array, sea: number, peak: number, latTop: number, latBottom: number): Climate {
  const latitude = new Float32Array(grid.rows), temperature = new Float32Array(grid.size), rain = new Float32Array(grid.size);
  for (let row = 0; row < grid.rows; row++) latitude[row] = latTop + (latBottom - latTop) * row / Math.max(1, grid.rows - 1);
  for (let i = 0; i < grid.size; i++) {
    const lat = latitude[Math.floor(i / grid.cols)]!, height = Math.max(0, (h[i]! - sea) / Math.max(1e-6, peak - sea));
    temperature[i] = 28 - 0.52 * Math.abs(lat) - 24 * height;
  }
  for (let row = 0; row < grid.rows; row++) {
    const lat = Math.abs(latitude[row]!), westward = lat < 30 || lat > 60;
    // Zonal background: wet near the equator (ITCZ), dry subtropics, wetter mid-latitudes.
    const zonal = 0.35 * Math.exp(-((lat / 9) ** 2)) - 0.18 * Math.exp(-(((lat - 27) / 8) ** 2)) + 0.08 * Math.exp(-(((lat - 50) / 10) ** 2));
    // The world wraps east-west, so air goes round: a first lap spins up the humidity, the second
    // records rain.
    let humid = 0.5, prev = Number.NaN;
    for (let k = 0; k < 2 * grid.cols; k++) {
      const lap = k % grid.cols, col = westward ? grid.cols - 1 - lap : lap, i = row * grid.cols + col;
      const record = k >= grid.cols;
      if (water[i]) {
        humid = Math.min(1, humid + (temperature[i]! > 0 ? 0.22 : 0.1));
        if (record) rain[i] = 0.3 * humid;
        prev = Number.NaN;
        continue;
      }
      // Coming ashore is not a climb; slopes inland are (orographic lift).
      const climb = Number.isNaN(prev) ? 0 : Math.max(0, h[i]! - prev);
      const fall = Math.min(humid, humid * (0.055 + 2.6 * climb));
      humid = Math.max(0, humid - fall * 0.9);
      if (record) rain[i] = Math.max(0, fall + zonal);
      prev = h[i]!;
    }
  }
  // Air also mixes north and south a little: smooth rainfall over neighbours once.
  const smooth = rain.slice();
  for (let i = 0; i < grid.size; i++) {
    let sum = rain[i]! * 2, w = 2;
    for (let n = 0; n < 6; n++) { const j = grid.nb[i * 6 + n]!; if (j >= 0) { sum += rain[j]!; w++; } }
    smooth[i] = sum / w;
  }
  return { latitude, temperature, rain: smooth };
}

/** Rank of each land value within land (0 lowest, 1 highest). */
function landRank(values: Float32Array, land: Uint8Array): Float32Array {
  const ids = [...values.keys()].filter((i) => land[i]).sort((a, b) => values[a]! - values[b]! || a - b), rank = new Float32Array(values.length);
  ids.forEach((id, k) => { rank[id] = ids.length > 1 ? k / (ids.length - 1) : 0; });
  return rank;
}

export function biomes(grid: Grid, h: Float32Array, land: Uint8Array, c: Climate): Biome[] {
  const height = landRank(h, land), wet = landRank(c.rain, land), out: Biome[] = [];
  for (let i = 0; i < grid.size; i++) {
    if (!land[i]) { out.push('water'); continue; }
    const t = c.temperature[i]!;
    if (height[i]! > 0.93) out.push('mountain');
    else if (t < -6) out.push('snow');
    else if (height[i]! > 0.8) out.push('hills');
    else if (t > 9 && wet[i]! < 0.16) out.push('desert');
    else if (wet[i]! > 0.55 || t < 4 && wet[i]! > 0.35) out.push('forest');
    else out.push('plains');
  }
  return out;
}
