# Port notes

Findings about the C# reference discovered during the Rust port. Each entry: what, where, how Rust handles it.

- Where C# throws `OverflowException` (checked `decimal` paths in `Fx.FromRatio`/`MulInt`), Rust panics; the fixtures list those inputs in `fx_skipped` and the tests assert they fail.

## Build speed (Plan 2, Task 8)

Sample world (`Assets/StreamingAssets/Data`), release build, median of 20 runs, measured by `cargo test -p fes-sim --release --test build_speed -- --ignored --nocapture` (`engine/fes-sim/benches/build.rs`):

- `SimulationBuilder::build` with default modules (shared `Arc` data, no clone): about 528 us.
- Cloning `GameDatabase` + `MapDef` (what `build_world` does on top): about 134 us.

The full-scale world and the turn budget come with Plan 4.

## Per-system timing (Plan 3, Task 8)

Sample world (`Default_Seed1_ThirtyYears`, turn 52 replay, all ported systems), release build, median of 20 replays, measured by `cargo test -p fes-parity --release --test timing -- --ignored --nocapture` (`engine/fes-parity/tests/timing.rs`). Time covers the system run only, not the byte compare or the service checks. Re-measured after the Plan 3 final review fixes (split borrows instead of per-turn column clones).

| System | Rust, sample world | C# web, full scale |
| --- | --- | --- |
| `economy.pricing` | 141.6 us | 0.39-0.49 s |
| `agents.production` | 96.7 us | not given |
| `agents.home_surplus` | 45.7 us | not given |
| `agents.household_spending` | 5.6 us | not given |
| `agents.households` | 7.9 us | not given |
| `economy.world_events` | 4.9 us | not given |
| `economy.currency` | 1.9 us | not given |
| `economy.modifier_fold` | 1.4 us | not given |
| `agents.lod` | 0.5 us | not given |
| `core.commands` | 0.4 us | not given |
| merchants (`agents.merchant_*`) | not ported yet | 0.44-0.72 s |

C# figures: Rust port spec 7.3 (Unity web player, Chrome, single thread, full-scale world with 4,460 merchant groups, turns 0 to 53); `00_Architecture.md` gives only whole-turn figures (1.03 s average, 1.64 s worst). The sample world is much smaller than full scale, so the two columns are not comparable: they show where each side stands, not a speedup. A fair comparison needs the full-scale world (Plan 4).

## Full-scale timing (Plan 4, Task 10)

World: `TestWorlds.FullScaleShipped()` exported by `ParityFixtureExporter.WritesFullScaleData` to `engine/fixtures/fullscale/` (CSVs, about 150 KB, no LFS; `turns.json` holds the C# world hash for turns 0 to 11). Rust builds the world from the CSVs with seed 1, matches the C# world hash at turn 0 and after turns 1 to 11, then plays turns 0 to 53 in release, single thread (`cargo test -p fes-parity --release --test timing full_scale -- --ignored --nocapture`).

| | Rust native, release | C# web (Chrome, single thread) |
| --- | --- | --- |
| Average turn | 0.473 s | 1.03 s |
| Worst turn | 0.778 s (turn 52, the new-year turn) | 1.64 s |

Per-system medians over the 54 turns (max in brackets): `agents.merchants` 221 ms (346), `economy.pricing` 150 ms (179), `agents.home_surplus` 24 ms (32), `agents.credit` 23 ms (35), `agents.banks` 7.6 ms (21), `agents.production` 6.7 ms (11), everything else under 1 ms median. `agents.demand_forecast` runs every 13th turn only (median 0, max 256 ms); turn 52 is such a turn, which is why it is the worst: merchants and pricing plus the forecast.

The native worst turn is over 0.5 s (0.778 s), so the top three systems on that turn and a speed-up for each (none changes a rule or a result):

1. `agents.merchants` (about 0.35 s on turn 52). The detailed job weighs every stocked cell against every outgoing route for each group (`job_search.rs` `weigh_cell` / `weigh_route`). Cache per node, once per turn, the route list with cost, danger and travel time already folded (the `Caches` arrays are rebuilt per turn but the per-(cell, route) score inputs are recomputed per group), and skip a route when an upper bound of its score (full capacity at the best price spread) cannot beat the best trip found so far. The groups of one node are independent until they settle, so the search phase can also run per node on a thread pool and settle in group order.
2. `economy.pricing` (about 0.18 s). `reprice.rs` / `demand.rs` call `weekly` for every cell of every due market, and `weekly` runs a bit-by-bit `log2` and `exp2` per quintile. Memoize `log_of_ratio` and the five `constant_elasticity` terms by `(price, anchor)` per cell: a price that did not move since last turn (most cells) gives the same pair, so store the last result in a column and reuse it. Markets only share the money ledger at the end of `execute`, so the market loop can be split into chunks over a thread pool with per-chunk tallies merged in market order.
3. `agents.demand_forecast` (0.26 s on quarter turns). `plan_makers` runs `CHAIN_PASSES` full passes over all makers and `measure_needs` walks every cell. Run the passes only over makers whose inputs changed (a maker's plan depends on its fed cells), and reuse the `rows` and `lines` buffers between quarters rather than rebuilding them with `size`. Spreading this one system over several turns is not an option (it would change results), but its cost can be hidden by computing it on a worker thread while the Markets phase runs, as it only writes `DemandForecast`.

Not measured here: native multi-thread, and the C# per-system split at full scale (the web baseline has only the whole-turn figures and the pricing and merchants ranges above).

## Browser timing (Plan 5, Task 1)

The same full-scale world (seed 1, turns 0 to 53) as `fes-wasm` in a dedicated Web Worker, single thread, on the development desktop. Build: `opt-level=3`, LTO, `codegen-units=1`, no debug assertions, `wasm-bindgen` 0.2.100 (`--target web`), `wasm-opt -O3` (binaryen 123). Browser: the installed Microsoft Edge 157 (Chromium 157.0.4341.42), headed, driven by Playwright 1.53 (`web/bench`: `npm install`, `npm run wasm`, `npm run bench`). Playwright's bundled Chromium 138 does not start on this machine (Windows side-by-side configuration error), so Edge stands in for Chrome; both are the same Chromium/V8 engine. The world hash after turn 11 equals the C# `turns.json` (348782575289596057) in every run.

| | Rust native, release | Rust wasm (Edge 157, Worker) | C# web (Chrome, single thread) |
| --- | --- | --- | --- |
| Average turn | 0.473 s | 0.55 s (0.548 / 0.560 / 0.549 over three runs) | 1.03 s |
| Worst turn | 0.778 s (turn 52) | 1.11-1.16 s (turn 52, the new-year turn, every run) | 1.64 s |
| Build the turn-0 world | | 0.16-0.19 s | |

Wasm file (`fes_wasm_bg.wasm`): 1,541,568 bytes after `wasm-bindgen`, 1,247,819 bytes (1.19 MiB) after `wasm-opt -O3`. Other turns over 0.7 s: 13 (0.83 s), 26 (0.79 s), 39 (0.85 s), the quarter turns where `agents.demand_forecast` runs.

Against the budget (spec 7.3: average under 0.7 s, worst under 1 s): the average passes, the worst turn (52) misses by about 0.15 s. Wasm runs at about 1.16x native on average and 1.45x on turn 52. Task 2's speed-ups (merchants, pricing, demand forecast above) target exactly that turn. Not yet measured inside an itch.io embed.
