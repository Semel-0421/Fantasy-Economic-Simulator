# Parity fixtures

Exported from the C# reference simulation (frozen at `4bf3804`) for the Rust port to compare against.
Do not edit by hand.

## Regenerate

In the Unity Editor (project open), run the explicit test
`FES.Tests.ParityFixtureExporter.WritesParityFixtures`:

```
unity recompile --timeout 200 --no-banner --format json
unity command test run --mode editor --filter WritesParityFixtures --include_explicit true --timeout 3000 --no-banner --format json
```

It overwrites everything here except this README, and fails if any run's final save hash differs from
`GoldenRunTests`, or if a stepped turn's world hash differs from `AdvanceTurn` on a twin simulation.

## JSON conventions (`ParityJson`)

- Objects: public instance fields, then public readable (non-indexer) properties, each in declaration
  (metadata token) order. Field and property names are the C# names.
- Enums as their names. Id structs (`NodeId`, `GoodId`, ...) as their `int Value`.
- `long`, `ulong` and `Fx` (its `Raw`) as **decimal strings**; 32-bit and smaller integers as numbers.
- Lists and arrays as arrays; dictionaries as objects.

## Files

### `primitives.json`

- `fx`: rows `{op, a, b, result}` (all decimal strings), 300 per op. Ops: `FromInt(a)`, `FromPermille(a)`,
  `RoundShift(a)` (unary, `b` is `"0"`), `FromRatio(a, b)`, `MulInt` (`Fx.FromRaw(a).MulInt(b)`: `a` is
  the Fx raw, `b` the integer), `MulIntRaw(a, b)` (`a` integer, `b` raw), and `+ - * /` on raws. `result`
  is a raw for ops returning `Fx`, the integer otherwise. Each op first takes every edge input (pairs for
  binary ops): `0, ±1, ±2^31, ±(2^31+1), ±(i64::MAX >> 16), ±0x8000`; then random inputs from
  `DetRandom(0xF1E5)` (a random 64-bit pattern shifted right 0..63 bits, random sign). C# arithmetic is
  unchecked, so shifts and products wrap.
- `fx_skipped`: inputs where C# threw `OverflowException` (not counted in the 300). See below.
- `det_hash`: `mix [[x, r]]`, `combine [[a, b, r]]`, `hash [[seed, turn, stream, entity, salt, r]]`
  (seed and r strings, the 32-bit arguments numbers), `of [[text, r]]` (r a number). 100 rows each.
- `det_random`: per seed `{seed, next_ulong, next_int_1000, next_fx01_raw, chance_300, spread_250_raw}`,
  each list eight draws from a **fresh** `DetRandom(seed)`.

### `data/`

- `config_effective.json`: `SimulationConfig.EffectiveValues` for the shipped data.
- `config_defaults_effective.json`: `SimulationConfig.Load(null).EffectiveValues` (every code default).
- `config_fields.json`: the shipped `SimulationConfig` object.
- `definitions.json`: `GameDatabase` `Categories, Goods, Recipes, Facilities, Events, MarketProfiles`,
  `Specialties` (rules for the tags listed in `specialties.csv`, in file order) and `Map {Nodes, Routes}`.
  `Facilities[].Recipe` is the full recipe object.
- `hashes.json`: `content_hash` (`GameDatabase.ContentHash`), `map_hash` (`MapDef.ComputeHash`),
  `data_hash` (`DetHash.Combine(content, map)`, the save header's), `config_hash` (`SimulationConfig.ComputeHash`).

### `runs/<name>/`

Kept golden runs (spec section 6): `Default_Seed1_ThirtyYears`, `Default_Seed2`, `Default_Seed3`, `NoBills`.

- `start.save`: `GameSimulation.Save` right after `Build()`.
- `turns.json`: `{name, seed, config, turns: [...], final_sha256_32}`. One entry per `AdvanceTurn`:
  `turn` is the clock turn after it (1-based count of turns run), `world_hash` is `WorldState.ComputeHash`,
  `sections` maps each persistent component key to the FNV-1a 64 hash of the bytes its `Write` produces
  (section bytes only, no key or version). `final_sha256_32` is the first 32 hex digits of SHA-256 of the
  final save, equal to the `GoldenRunTests` hash.
- `turn0.json`: `{world_hash, sections}` in the `turns.json` entry format, for the built world before any turn
  (`start.save` content). Hashes are decimal u64 strings.
- `steps/turn_<t>.bin.gz` (`Default_Seed1_ThirtyYears` only, t in 0, 1, 3, 51, 52, 53, 103, 104; `t` is
  the clock turn before the turn runs). Gzip of, little-endian, strings as .NET `BinaryWriter` strings
  (7-bit-encoded length prefix, UTF-8 bytes):

  ```
  bytes[8]  "FESSTEP1"
  i32       t
  i32 n, bytes[n]   the full save before the turn (same format as start.save)
  i32       record count
  record*:
    string  label          "system:<id>" after each system's Execute,
                           "dispatch:<SimPhase>" after each phase's event dispatch,
                           "clock" after the clock advance
    u64     world hash after this step
    i32     changed section count
    (string key, i32 version, i32 n, bytes[n] section bytes)*   sections whose bytes changed
  ```

  The exporter replays `TurnPipeline.Advance` from public members. `EventBus.BeginTurn` is internal and
  skipped (it only clears the presentation turn log); the clock is advanced through `SimClock`'s public
  `Write`/`Read`. Each stepped turn's world hash is checked against `AdvanceTurn` on a twin.

## Skipped Fx inputs

81 inputs threw `OverflowException` and are listed in `primitives.json` `fx_skipped` (op, a, b):
8 for `FromRatio` (the exact `decimal` path, used when |a| > i64::MAX >> 16, whose quotient does not fit
a `long`) and 73 for `MulInt` (the `decimal` path, used when |a| or |b| > 2^31, whose rounded product does
not fit a `long`; including the four `±(i64::MAX >> 16)` edge pairs). No other op threw: `/` by a zero raw
returns zero, and the other ops wrap silently. The Rust port must not be expected to match these inputs.

## Game data

`fes-parity` reads the game data (CSV files) from `<repo>/Assets/StreamingAssets/Data` by default;
`--data <dir>` overrides it. The engine shares this data with the Unity tree rather than keeping a copy.

### `build/` (Default_Seed1 built world, Plan 2)

- `nodes.json`: `{nodes, routes, outgoing}`. `nodes` is the `NodeTable` in built order: `key, level, parent,
  continent, core, child_start, child_count, children` (the CSR slice). `routes` is the `RouteTable`:
  `from, to, travel_turns, cost_per_unit, base_fee, terrain`. `outgoing` has one row per node:
  `out_start, out_count, routes` (route indices from `RouteIndex`). Not exported: `CoreStrength`, which is
  zero until classes are absorbed.
- `systems.json`: `{systems, modules}`. `systems` is every system in pipeline order (`id, phase, order`);
  `modules` is `ModuleOrder.Sort` of the default modules (`id, requires`).
