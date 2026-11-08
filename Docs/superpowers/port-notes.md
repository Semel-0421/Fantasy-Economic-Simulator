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

## Speed-ups to the gate (Plan 5, Task 2)

Profiled per system in the browser: `PROFILE=1 npm run wasm` builds `WasmSim::step_profiled` (feature `profile` of `fes-wasm`, `Simulation::step_clocked` with `performance.now`), and the bench then lists the worst turn's systems. Same world and browser as above (Edge 157, Worker, single thread, turns 0 to 53). Native: `cargo test -p fes-parity --release --test timing full_scale -- --ignored --nocapture`. After every step `fes-parity --runs` (all four runs, every comparison and the final hash) and `--steps` were equal, and the full-scale hashes after turns 1 to 11 matched C#.

Turn 52 in wasm before any change: `agents.demand_forecast` 348 ms, `agents.merchants` 254 ms, `economy.pricing` 170 ms, `agents.contracts` 99 ms, `agents.policy` 90 ms, `agents.credit` 41 ms. The forecast walks 1,195,000 market cells; its cost was mostly the component lookups (`WorldState::get`, a hash map by type) made three times per cell, and a 1.2-million-row buffer allocated each quarter.

| Step | Native avg / worst (turn 52) | Native forecast / merchants / pricing max | Wasm avg / worst (turn 52) | Wasm turn 52: forecast / merchants / pricing |
| --- | --- | --- | --- | --- |
| Before (Task 1) | 0.440-0.473 s / 0.732-0.778 s | 256 / 346 / 179 ms | 0.55-0.56 s / 1.11-1.16 s | 348 / 254 / 170 ms |
| 1. `demand_forecast`: components looked up once per run (slices in `Run`), no per-quarter row buffer (the `need` column holds the stock meanwhile) | 0.433 s / 0.622 s | 134 / 333 / 171 ms | 0.542 s / 0.935 s | 162 / 256 / 170 ms |
| 2. `log2`: top bit from `leading_zeros`, stop squaring once the value is exactly 1.0 (it squares to itself; unit test against the first port over a sweep of inputs) | 0.420 s / 0.589 s | 123 / 340 / 168 ms | 0.490-0.494 s / 0.871-0.879 s (four runs) | 137 / 229 / 148 ms |

Result: in the browser bench the average is 0.49 s (gate 0.6 s) and the worst turn 0.87-0.88 s (gate 0.9 s), still turn 52. The margin on the worst turn is small (about 25 ms). Not applied, next candidates in order of expected gain on turn 52: the per-node merchant route cache with upper-bound pruning (merchants 229 ms), a per-cell `(price, anchor)` memo of the pricing curve (148 ms), the forecast's `measure_needs` over every cell (about 100 ms native), and the new-year systems `agents.contracts` (99 ms) and `agents.policy` (95 ms), which were not profiled further.

### Second pass (target: worst under 0.75 s)

A native split of `agents.merchants` on turn 52 showed the route search (`DetailedJob::execute`) at about 95 ms and `refresh_needs` at about 102 ms: the household demand curve (`weekly`: one `log2`, five `exp2`) on all 1,195,000 cells. Pricing's consumption runs the same curve on the same cells. A per-cell memo of the curve hit only about 30% (most prices move every turn), so the memo is keyed by the price ratio instead: `CurveMemo`, a direct-mapped table of 65,536 slots, each holding the last ratio that fell in it and its necessity factor. The factor depends only on the ratio and the demand parameters (the memo resets if they change), so a hit returns exactly what the curve gives; about 95% of lookups hit. Also in the merchant commit: a group's licence draws and its "may cross continents" test are worked out once per search instead of on every route (no measurable gain alone). The route cache with upper-bound pruning was not done: the search is under half of the merchant time, and a sound bound needs a bound on `beyond` (three hops of prices, tolls and coin rates).

| Step | Native avg / worst (turn 52) | Native merchants median / pricing median | Wasm avg / worst (turn 52) | Wasm turn 52: merchants / pricing / forecast |
| --- | --- | --- | --- | --- |
| 3. Curve memo in merchants' `refresh_needs` (+ licence checks per search) | 0.346 s / 0.523 s | 143 / 139 ms | 0.429 s / 0.791 s | 159 / 152 / 129 ms |
| 4. Same memo in pricing's consumption | 0.274 s / 0.468 s | 140 / 69 ms | 0.36-0.39 s / 0.82-0.88 s (three runs) | 165-178 / 88-132 / 141-155 ms |

The browser runs after step 4 were taken while another agent was building the web app on the same machine. Every system on turn 52 read slower than in the step 3 run, including ones step 4 does not touch (`agents.contracts` 99 to 129 ms, `agents.policy` 92 to 134 ms, `agents.credit` 38 to 67 ms), so the step 4 wasm worst turn is load noise, not a regression. Natively, step 4 cut turn 52 by 55 ms. The 0.75 s goal is not shown in the browser. Remaining on turn 52 (wasm, quiet run): merchants about 160 ms (search plus the rest of the job), the forecast about 130 ms (`measure_needs` over every cell), `agents.contracts` about 100 ms and `agents.policy` about 90 ms (new-year work, not yet profiled), pricing about 90 ms.

### JavaScript number safety (Plan 5 final review)

The web app reads every number from the engine as a JavaScript `number`, exact only up to 2^53. `engine/fes-sim/tests/js_safe_integers.rs` plays `Default_Seed1_ThirtyYears` (1,560 turns) and, after every turn, serializes the events, the player's coin balances and every view (purse, cargo, contracts, shares, licences, statement, and the quotes of the player's market) and asserts the largest absolute integer stays below 2^53. Largest value seen over the whole run: 14,592,496 (far below 2^53 = 9,007,199,254,740,992). The full-scale fixtures were not played for this; the sample data is what the web app ships.


## Plan 7A Task 1: engine memory and copies (2026-11-09)

Measured before changing the engine, on `rust-port` at `1e03f2e`. The full-scale
`preview_fullscale` test now prints requested live/peak heap bytes and elapsed time
for scenario build, first and queued previews, hashing, save and load, and asserts
that first-preview retained bytes are at most 20,000,000. Its pre-change failure
was 49,285,064 bytes. The fixed pre-change world hash is
`1121562210749400463`; repeated previews and a save/load round trip must retain it.

### Measurements

Native command: `cargo test -p fes-sim --release --all-features --test preview_fullscale -- --nocapture --test-threads=1`.
These are requested allocation bytes in the test binary, not RSS. The counting
allocator delegates allocation/free to `System` but uses allocate-copy-free
reallocation, so transient peaks and timings are instrumentation measurements;
use the Edge result for production wasm memory. Timings vary with machine load.

| Operation | Before bytes | After bytes | Meaning |
| --- | ---: | ---: | --- |
| Scenario build | 107,672,646 | 107,672,646 | Retained heap; unchanged construction |
| First preview | 49,285,064 | 14,398,384 | Retained heap, including scratch; 70.8% reduction |
| First preview | 51,562,653 | 16,675,973 | Peak extra heap |
| World hash | 151,622,866 | 100,663,640 | Peak extra heap; 33.6% reduction |
| Load | 189,273,761 | 106,275,166 | Peak extra heap; 43.8% reduction |
| Save | 350,213,056 | 350,213,056 | Peak extra heap; intentionally unchanged |

Final instrumented native timings (before from `red-memory.log`, after from
`native-final.log`, one run each): first preview 13.2504 -> 4.812 ms; queued preview
8.7773 -> 3.3986 ms; hash 190.0371 -> 140.102 ms; load 164.6588 -> 64.5706 ms.
The earlier baseline was slower (29.5758/19.0725 ms for previews); these timings
show machine-load variation and are not a statistical performance claim.

Installed Edge 157.0.4341.42, `npm run wasm` then `node run-bench.mjs`, no other task
build during the final bench: peak wasm linear memory **294,846,464 -> 243,269,632
bytes** (17.5% reduction). Quiet before/after average **367.783 -> 359.817 ms**,
worst **755.9 -> 779.6 ms**, both worst turns 52. The small worst-time increase
is within the observed run variation; no turn-system work was changed. Final
average <700 ms, worst <1000 ms and stored ceiling 3000 ms all pass. Both hashes
after turn 11 equal `348782575289596057`. Initial before run overlapped native
compilation (average415.967 ms, worst1022.7 ms); that run is retained in the local
report and is not claimed as a performance-gate pass.


### Copy audit and changes

- `SimulationBuilder::build`, world/economy/agent registration: definitions are
  already shared through `Arc<GameDatabase>` and `Arc<MapDef>`. World vectors are
  built and moved into components. Static route/layout columns and small command
  result copies are necessary separate data, not cloned whole worlds. No change.
- `preview_queued`: copy only `MarketStore.stock/supply/demand` and
  `HouseholdCash.cash/saved_year` for recognized player commands. The audit includes
  indirect money-ledger and bond-desk calls: securities purchases also write
  `saved_year`. Other small sections and touched services are still restored.
  Existing `PlayerCommand::from_command` recognition covers both the probe and
  every queued command. Unknown custom commands retain the prior market/household
  restoration coverage through temporary serialized sections; their extra data
  is not retained in scratch. The forecast is excluded as before. New command
  write paths must update the copied columns and restoration tests together.
- `WorldState::hash`: sort components by UTF-16 key and hash the same key, version
  and bytes, serializing one section into a reused buffer. This removes the
  simultaneous full-world section copy without changing every component writer.
  The largest section still needs a buffer; this is not a zero-allocation hash.
- `Simulation::load/load_with`: `read_save_borrowed` validates the same container
  and borrows section payloads directly from the input. Component readers still
  populate their owned vectors. The owned `read_save` API remains compatible for
  tools/tests. Borrowed/owned parsing errors and input aliasing are tested.
- Wasm `load`: calls the same improved `Simulation::load`; the wasm-bindgen
  JS-to-wasm input copy remains necessary at the boundary. The worker receives
  the input as a transferred buffer. `host.replace` keeps the previous simulation
  until the replacement succeeds, preserving failure safety; it does not clone
  that simulation. No wrapper or host changes were justified.
- `save` still collects sections then writes the output buffer, as permitted by
  the task. `sections()` remains available to parity/replay diagnostics and tests;
  production build/load/preview/hash paths no longer make a full-world snapshot.

The Edge bench now records `peakWasmBytes` from exported wasm linear memory.
Linear memory cannot shrink, so its size after turns 0-53 is the high-water mark,
including allocator slack and the hash after turn 11. The turn workload and
stored `ceiling.json` are unchanged. This bench does not issue previews or loads;
those paths are covered by the native full-scale measurement above.
