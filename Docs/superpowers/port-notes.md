# Port notes

Findings about the C# reference discovered during the Rust port. Each entry: what, where, how Rust handles it.

- Where C# throws `OverflowException` (checked `decimal` paths in `Fx.FromRatio`/`MulInt`), Rust panics; the fixtures list those inputs in `fx_skipped` and the tests assert they fail.

## Build speed (Plan 2, Task 8)

Sample world (`Assets/StreamingAssets/Data`), release build, median of 20 runs, measured by `cargo test -p fes-sim --release --test build_speed -- --ignored --nocapture` (`engine/fes-sim/benches/build.rs`):

- `SimulationBuilder::build` with default modules (shared `Arc` data, no clone): about 528 us.
- Cloning `GameDatabase` + `MapDef` (what `build_world` does on top): about 134 us.

The full-scale world and the turn budget come with Plan 4.

## Per-system timing (Plan 3, Task 8)

Sample world (`Default_Seed1_ThirtyYears`, turn 52 replay, all ported systems), release build, median of 20 replays, measured by `cargo test -p fes-parity --release --test timing -- --ignored --nocapture` (`engine/fes-parity/tests/timing.rs`). Time covers the system run only, not the byte compare.

| System | Rust, sample world | C# web, full scale |
| --- | --- | --- |
| `economy.pricing` | 141.4 us | 0.39-0.49 s |
| `agents.production` | 100.7 us | not given |
| `agents.home_surplus` | 45.4 us | not given |
| `agents.household_spending` | 5.5 us | not given |
| `agents.households` | 7.5 us | not given |
| `economy.world_events` | 4.9 us | not given |
| `economy.currency` | 2.2 us | not given |
| `economy.modifier_fold` | 1.7 us | not given |
| `agents.lod` | 0.6 us | not given |
| `core.commands` | 0.4 us | not given |
| merchants (`agents.merchant_*`) | not ported yet | 0.44-0.72 s |

C# figures: Rust port spec 7.3 (Unity web player, Chrome, single thread, full-scale world with 4,460 merchant groups, turns 0 to 53); `00_Architecture.md` gives only whole-turn figures (1.03 s average, 1.64 s worst). The sample world is much smaller than full scale, so the two columns are not comparable: they show where each side stands, not a speedup. A fair comparison needs the full-scale world (Plan 4).
