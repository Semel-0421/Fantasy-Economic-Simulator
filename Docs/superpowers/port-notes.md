# Port notes

Findings about the C# reference discovered during the Rust port. Each entry: what, where, how Rust handles it.

- Where C# throws `OverflowException` (checked `decimal` paths in `Fx.FromRatio`/`MulInt`), Rust panics; the fixtures list those inputs in `fx_skipped` and the tests assert they fail.

## Build speed (Plan 2, Task 8)

Sample world (`Assets/StreamingAssets/Data`), release build, median of 20 runs, measured by `cargo test -p fes-sim --release --test build_speed -- --ignored --nocapture` (`engine/fes-sim/benches/build.rs`):

- `SimulationBuilder::build` with default modules (shared `Arc` data, no clone): about 528 us.
- Cloning `GameDatabase` + `MapDef` (what `build_world` does on top): about 134 us.

The full-scale world and the turn budget come with Plan 4.
