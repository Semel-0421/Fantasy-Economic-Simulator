// Geometry-only diagnostics shared by the survey and hand-checked fixtures.
const mean = (xs) => xs.reduce((a, b) => a + b, 0) / xs.length;
const wrap = (dx, width) => dx - width * Math.round(dx / width);

export function coefficientVariation(xs) {
  const m = mean(xs);
  return m > 0 ? Math.sqrt(mean(xs.map((x) => (x - m) ** 2))) / m : 0;
}

export function principalAxes(points, width) {
  const phase = Math.atan2(mean(points.map((p) => Math.sin(p.x / width * 2 * Math.PI))), mean(points.map((p) => Math.cos(p.x / width * 2 * Math.PI))));
  const anchor = phase * width / (2 * Math.PI);
  const xs = points.map((p) => anchor + wrap(p.x - anchor, width)), ys = points.map((p) => p.y);
  const x = mean(xs), y = mean(ys);
  const xx = mean(xs.map((v) => (v - x) ** 2)), yy = mean(ys.map((v) => (v - y) ** 2));
  const xy = mean(xs.map((v, i) => (v - x) * (ys[i] - y)));
  const d = Math.hypot(xx - yy, 2 * xy);
  return { x, y, ratio: Math.sqrt(Math.max(1e-9, xx + yy + d) / Math.max(1e-9, xx + yy - d)), angle: (Math.atan2(2 * xy, xx - yy) * 90 / Math.PI + 180) % 180 };
}

export function hasRegularRow(centroids) {
  for (let mask = 0; mask < 1 << centroids.length; mask++) {
    const group = centroids.filter((_, k) => mask & 1 << k);
    if (group.length < 3 || Math.max(...group.map((c) => c.latitude)) - Math.min(...group.map((c) => c.latitude)) > 12) continue;
    const lons = group.map((c) => (c.longitude + 360) % 360).sort((a, b) => a - b);
    const gaps = lons.map((x, i) => (lons[i + 1] ?? lons[0] + 360) - x);
    if (coefficientVariation(gaps) <= 0.25) return true;
  }
  return false;
}

export function morphology(w, centroids) {
  const axes = w.continents.map((cells) => principalAxes(cells.map((i) => ({ x: w.grid.x[i], y: w.grid.y[i] })), w.grid.width));
  const nearestDistances = axes.length < 2 ? [0] : axes.map((a, k) => Math.min(...axes.filter((_, j) => j !== k).map((b) => Math.hypot(wrap(a.x - b.x, w.grid.width), a.y - b.y))));
  let north = 0, south = 0;
  w.land.forEach((v, i) => {
    const lat = w.climate.latitude[Math.floor(i / w.grid.cols)];
    north += v * (lat > 0 ? 1 : lat === 0 ? 0.5 : 0);
    south += v * (lat < 0 ? 1 : lat === 0 ? 0.5 : 0);
  });
  return { axes, nearestDistances, nearestCv: coefficientVariation(nearestDistances),
    hemisphereShare: north + south ? Math.max(north, south) / (north + south) : 0.5,
    regularRow: hasRegularRow(centroids), maxAspect: Math.max(...axes.map((a) => a.ratio)),
    elongatedNonZonal: axes.some((a) => a.ratio >= 2 && a.angle >= 30 && a.angle <= 150) };
}

export function minimumSeaGap(grid, continents) {
  const owner = new Int32Array(grid.size).fill(-1), dist = new Int32Array(grid.size), queue = new Int32Array(grid.size);
  let head = 0, tail = 0, gap = grid.size;
  continents.forEach((cells, b) => { for (const i of cells) { owner[i] = b; queue[tail++] = i; } });
  while (head < tail) {
    const i = queue[head++];
    for (let k = 0; k < 6; k++) {
      const j = grid.nb[i * 6 + k];
      if (j < 0) continue;
      if (owner[j] === -1) { owner[j] = owner[i]; dist[j] = dist[i] + 1; queue[tail++] = j; }
      else if (owner[j] !== owner[i]) gap = Math.min(gap, dist[i] + dist[j]);
    }
  }
  return gap;
}

/** Aligned coastal cells keep the same sea-facing side while advancing in one hex direction. */
export function straightCoastRuns(grid, water, continents) {
  const owner = new Int32Array(grid.size).fill(-1);
  continents.forEach((cells, b) => { for (const i of cells) owner[i] = b; });
  return continents.map((cells, b) => {
    const facesSea = (i, face) => i >= 0 && owner[i] === b && grid.nb[i * 6 + face] >= 0 && water[grid.nb[i * 6 + face]];
    const runFrom = (i, face, step) => {
      let run = 1, j = grid.nb[i * 6 + step];
      while (run < cells.length && facesSea(j, face)) { run++; j = grid.nb[j * 6 + step]; }
      return run;
    };
    const longestAt = (i, face) => {
      let best = 0;
      for (let step = 0; step < 6; step++) best = Math.max(best, runFrom(i, face, step));
      return best;
    };
    let longest = 0;
    for (const i of cells) for (let face = 0; face < 6; face++) if (facesSea(i, face)) longest = Math.max(longest, longestAt(i, face));
    return longest;
  });
}
