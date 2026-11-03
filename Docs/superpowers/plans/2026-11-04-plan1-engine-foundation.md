# Plan 1: Engine Foundation and Parity Harness — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Rust workspace whose primitives, save container and data loading produce exactly what the C#
reference produces, checked against fixtures exported from C#, with CI and a parity runner that later plans
extend system by system.

**Architecture:** C# stays the reference. An explicit Unity EditMode test exports fixtures (primitive
vectors, data dumps and hashes, per-turn section hashes, per-system snapshots at sampled turns) into
`engine/fixtures/`. Rust crates reproduce each piece and their tests compare against those files. The
`fes-parity` binary reports the first divergence.

**Tech Stack:** Rust stable (pinned in `rust-toolchain.toml`), cargo workspace, `serde`/`serde_json`
(tests and parity only), `flate2`, `sha2`; Unity 6000.6.6f1 EditMode tests (C#) for the exporter; GitHub
Actions.

**Spec:** `docs/superpowers/specs/2026-11-04-rust-web-port-design.md` (sections 6, 7.1, 7.4). Roadmap:
`docs/superpowers/plans/2026-11-04-rust-port-roadmap.md`.

## Global Constraints

- Simulation code uses integers and `Fx` (Q47.16 in an `i64`) only: `clippy::float_arithmetic` is denied in
  `fes-core`, `fes-data`, `fes-save`.
- Rounding is half away from zero exactly as `Fx.RoundShift` / `Fx.MulInt` (`Assets/_Game/Core/Fx.cs`).
- Strings hash by UTF-16 code unit (`DetHash.Of` iterates C# `char`s), never by UTF-8 byte.
- Binary layout is .NET `BinaryWriter`: little-endian integers, strings as 7-bit-encoded byte length then UTF-8.
- Save container: magic `0x53454646`, `FormatVersion = 1` (`Assets/_Game/Persistence/SaveSerializer.cs`).
- Persistent components are ordered by key with ordinal comparison.
- Code limits (DEC-108): cognitive complexity 15, function lines 80, parameters 7, file lines 1,000,
  nesting 3. `rustfmt` and `clippy -D warnings` must pass.
- Rules, numbers and order of operations are ported as they are. A C# bug found on the way is written
  into `docs/superpowers/port-notes.md`, not fixed.
- Code, comments, commits in English.

## Review Focus

- A string with non-ASCII characters (a CSV with an accented name, a BOM) hashes the same as C# — Task 3
  vectors include `"Ærø"`, `"\u{FEFF}key"`, and an astral character `"𝔄"` (two UTF-16 units).
- `Fx` beyond the fast path (`|value|` or `|raw|` > 2^31, numerators near `i64::MAX >> 16`) matches C#'s
  `decimal` fallback — Task 2 vectors include them.
- CSV with CRLF, a quoted field containing a comma, a newline and `""`, a `#` comment, blank lines and a
  short row reads the same — Task 5 test.
- A truncated or foreign file given to `read_save` returns an error and never panics — Task 4 test.
- A config key missing from `config.csv` takes the code default, and an unknown key is an error — Task 6 tests.

---

### Task 0: Fix the reference point

**Files:**
- Modify: `docs/superpowers/specs/2026-11-04-rust-web-port-design.md` (section 6, the reference) and its `.ko.md`
- Modify: `Docs/GDD/03_Decisions.md` (new DEC entry), `Docs/GDD/00_Architecture.md` (baseline line)

- [ ] **Step 1: Confirm the other session's work is committed.** Run `git status --short Assets/_Game` on
  `meta-analysis-fixes`. Expected: no modified simulation files. If any remain, stop and ask the user.
- [ ] **Step 2: Run all EditMode tests at that commit** (`unity command test run --mode editor --timeout 1800 --no-banner --format json`). Expected: all pass, including `GoldenRunTests`.
- [ ] **Step 3: Record the reference.** Write the commit hash into spec section 6 (both languages) and a
  DEC entry: "C# reference for the Rust port is `<hash>`; C# simulation code is frozen from here." Record the
  web turn baseline from spec 7.3 in `00_Architecture.md` in place of the outdated figure.
- [ ] **Step 4: Branch.** `git switch -c rust-port <hash>` in the main checkout (the Unity Editor must
  open this project for Task 1's export; no worktree).
- [ ] **Step 5: Commit** the docs, plus `docs/superpowers/specs/*`, `docs/superpowers/plans/*`,
  `Docs/GDD/references/CHROMA_AWARDS_S2.md`: `git commit -m "Fix the C# reference for the Rust port"`.

---

### Task 1: C# fixture exporter

**Files:**
- Create: `Assets/_Game/Tests/EditMode/ParityFixtureExporter.cs`
- Create: `Assets/_Game/Tests/EditMode/ParityJson.cs` (minimal reflection JSON writer)
- Create: `engine/fixtures/README.md` (what each file is, how to regenerate)
- Output: `engine/fixtures/**` (committed)

**Interfaces:**
- Produces (files later tasks read):
  - `primitives.json`: `{ "fx": [{"op","a","b","result"}], "det_hash": {"mix":[[x,r]], "combine":[[a,b,r]], "hash":[[seed,turn,stream,entity,salt,r]], "of":[[text,r]]}, "det_random": [{"seed", "next_ulong":[8], "next_int_1000":[8], "next_fx01_raw":[8], "chance_300":[8], "spread_250_raw":[8]}] }`. 64-bit values as decimal strings.
  - `data/config_effective.json`: `{ key: value }` of `SimulationConfig.EffectiveValues`.
  - `data/config_fields.json`: every public field and get-only property of `SimulationConfig`.
  - `data/definitions.json`: `GameDatabase` lists (`Goods`, `Recipes`, `Facilities`, `Events`, `MarketProfiles`, specialties) and `MapDef` (`Nodes`, `Routes`).
  - `data/hashes.json`: `{ "content_hash", "map_hash", "data_hash", "config_hash" }` (decimal strings).
  - `data/config_defaults_effective.json`: `EffectiveValues` of `SimulationConfig.Load(null)` (every code default).
  - `runs/<name>/start.save`: `sim.Save` right after `Build()`.
  - `runs/<name>/turns.json`: per turn after `AdvanceTurn`: `{ "turn", "world_hash", "sections": { key: fnv } }`; `"final_sha256_32"` equal to the `GoldenRunTests` hash.
  - `runs/Default_Seed1_ThirtyYears/steps/turn_<t>.bin.gz` for t in {0, 1, 3, 51, 52, 53, 103, 104}: the full save before the turn, then after each system and after each phase's event dispatch, every section whose bytes changed (format in README).
- JSON rules (`ParityJson`): public instance fields and get-only properties in declaration order; enums
  as names; `Fx` as its raw integer; id structs (`NodeId`, `GoodId`, ...) as their `Value`; `long`/`ulong`
  as decimal strings; lists and arrays as arrays.

- [ ] **Step 1: Write `ParityFixtureExporter.WritesParityFixtures`** as `[Test, Explicit]`. It builds each
  kept golden run (`Default_Seed1_ThirtyYears`, `Default_Seed2`, `Default_Seed3`, `NoBills`; spec 6 drops
  the others) from `TestWorlds.Sample()` / `SampleConfig`, writes the files above to
  `Path.Combine(Application.dataPath, "..", "engine", "fixtures")`, and asserts each run's
  `final_sha256_32` equals the hash in `GoldenRunTests`.
- [ ] **Step 2: Step snapshots without touching simulation code.** Reproduce `TurnPipeline.Advance` in the
  exporter from public members (`sim.Systems`, `sim.Context.Events.BeginTurn/Dispatch`,
  `sim.Context.Clock.Advance`), snapshotting after each `Execute` and each `Dispatch`. Assert that the turn's
  `world_hash` equals what `AdvanceTurn` gives on a twin simulation; if a member is not public, stop and
  report rather than changing C# simulation code.
- [ ] **Step 3: Primitive vectors.** Deterministic inputs from `DetRandom(0xF1E5)`, plus fixed edge cases:
  `0`, `±1`, `±2^31`, `±(2^31+1)`, `i64::MAX >> 16`, `-(i64::MAX >> 16)`, odd halves (`±0x8000` raw) for
  every `Fx` op (`FromInt, FromPermille, FromRatio, MulInt, MulIntRaw, RoundShift, +, -, *, /`), 300 rows
  each. Skip inputs where C# throws (`OverflowException`) and list them in README.
- [ ] **Step 4: Run it.** `unity recompile --timeout 200 --no-banner --format json`, then
  `unity command test run --mode editor --filter WritesParityFixtures --include_explicit true --timeout 1800 --no-banner --format json`.
  Expected: pass; `engine/fixtures/` filled. If the total exceeds 50 MB, track `*.gz` and `*.save` with Git LFS.
- [ ] **Step 5: Run `GoldenRunTests`.** Expected: all pass (the exporter changed nothing).
- [ ] **Step 6: Commit** `git commit -m "Export parity fixtures from the C# reference"`.

---

### Task 2: Workspace, CI and `Fx`

**Files:**
- Create: `engine/Cargo.toml` (workspace: `fes-core`, `fes-save`, `fes-data`, `fes-parity`), `engine/rust-toolchain.toml` (current stable, recorded), `engine/clippy.toml`, `engine/rustfmt.toml`
- Create: `engine/fes-core/src/lib.rs`, `engine/fes-core/src/fx.rs`, `engine/fes-core/tests/fx_vectors.rs`
- Create: `engine/scripts/check_file_length.sh`, `.github/workflows/engine.yml`
- Create: `docs/superpowers/port-notes.md` (empty list of C# findings)

**Interfaces:**
- Produces: `pub struct Fx(i64)` (`Copy, Eq, Ord, Hash, Debug, Default`) with `FRACTION_BITS = 16`,
  `ONE_RAW`, `ZERO`, `ONE`; `from_raw(i64)`, `raw(self) -> i64`, `from_int(i64)`, `from_permille(i64)`,
  `from_ratio(i64, i64)`, `mul_int(self, i64) -> i64`, `mul_int_raw(i64, i64) -> i64`,
  `round_shift(i64) -> i64`, `min`, `max`, `clamp`; `Add, Sub, Neg, Mul, Div` with C# semantics (division
  by zero gives `ZERO`, `*` uses an arithmetic shift).
- Produces: `fes_core::testing::fixtures_dir() -> PathBuf` (behind the `testing` feature; `FES_FIXTURES`
  if set, else `engine/fixtures`) and `fixture_json(rel: &str) -> serde_json::Value`, used by every crate's tests.

- [ ] **Step 1: Write `fx_vectors::all_ops_match_csharp`**: reads `primitives.json` `fx` rows and asserts
  each op's result equals `result`, reporting the first failing row's op and inputs.
- [ ] **Step 2: Run** `cargo test -p fes-core --test fx_vectors` in `engine/`. Expected: FAIL (no `Fx`).
- [ ] **Step 3: Implement `Fx` in `fes-core/src/fx.rs`.** The C# `decimal` fallbacks become `i128`
  arithmetic: `mul_int` rounds `value * raw / 65536` half away from zero; `from_ratio` truncates
  `numerator * 65536 / denominator` toward zero. If a vector disagrees (C# `decimal` rounds the quotient
  to 28-29 significant digits first), emulate that rounding for the fallback branch only and note it in
  `port-notes.md`.
- [ ] **Step 4: Run** the test. Expected: PASS.
- [ ] **Step 5: CI and limits.** `clippy.toml`: `cognitive-complexity-threshold = 15`,
  `too-many-lines-threshold = 80`, `too-many-arguments-threshold = 7`, `excessive-nesting-threshold = 4`
  (calibrate once so a C#-depth-3 shape passes and depth 4 fails; record the value used).
  `[workspace.lints.clippy]`: `cognitive_complexity`, `too_many_lines`, `excessive_nesting` = deny;
  `float_arithmetic` = deny in the three sim crates. `check_file_length.sh` fails on any `.rs` over 1,000
  lines. `engine.yml` on push and PR: `cargo fmt --check`, `cargo clippy --all-targets -- -D warnings`,
  `cargo test`, the script, then `cargo run -p fes-parity -- --fixtures fixtures`.
- [ ] **Step 6: Run** `cargo fmt --check && cargo clippy --all-targets -- -D warnings && cargo test` in `engine/`. Expected: clean.
- [ ] **Step 7: Commit** `git commit -m "Rust workspace, CI and fixed-point Fx matching C#"`.

---

### Task 3: `DetHash` and `DetRandom`

**Files:**
- Create: `engine/fes-core/src/det.rs`, `engine/fes-core/tests/det_vectors.rs`

**Interfaces:**
- Produces: `mod det_hash { FNV_OFFSET, FNV_PRIME, GAMMA, fn mix(u64)->u64, fn combine(u64,u64)->u64, fn hash(seed: u64, turn: i32, stream: u32, entity: i32, salt: u32)->u64, fn fnv_step(u64,u8)->u64, fn of(&str)->u32 }`.
- Produces: `pub struct DetRandom { state: u64 }` with `new(u64)`, `for_key(world_seed: u64, turn: i32, stream: u32, entity: i32, salt: u32)`, `next_u64`, `next_u32`, `next_int(i32)->i32`, `next_int_range(i32,i32)->i32`, `next_fx01()->Fx`, `chance_permille(i32)->bool`, `next_spread(i32)->Fx`.

- [ ] **Step 1: Write `det_vectors::hash_functions_match_csharp`** (every `det_hash` row) and
  `det_vectors::random_draws_match_csharp` (each `det_random` sequence, drawn in the exporter's order),
  plus `det_vectors::of_hashes_utf16_units` asserting `of("𝔄")` equals the fixture row (the exporter writes it).
- [ ] **Step 2: Run** `cargo test -p fes-core --test det_vectors`. Expected: FAIL.
- [ ] **Step 3: Implement** in `det.rs`; `of` iterates `text.encode_utf16()`; casts follow C#
  (`(ulong)(uint)turn`, wrapping multiplication).
- [ ] **Step 4: Run** the test. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Deterministic hashing and random draws matching C#"`.

---

### Task 4: .NET binary IO and the save container

**Files:**
- Create: `engine/fes-core/src/io.rs` (writer, reader, FNV sink)
- Create: `engine/fes-save/src/lib.rs`, `engine/fes-save/tests/container.rs`

**Interfaces:**
- Produces (`fes_core::io`): `DotNetWriter<W: std::io::Write>` with `write_u8, write_bool, write_i32, write_u32, write_i64, write_u64, write_string(&str), write_bytes(&[u8])`; `DotNetReader<'a>` over `&'a [u8]` with matching `read_*` returning `Result<_, IoError>` and `position() -> usize`; `FnvSink` implementing `std::io::Write` with `hash() -> u64`.
- Produces (`fes_save`): `SaveHeader { format_version: i32, data_hash: u64, seed: u64, turn: i32 }`, `RawSection { key: String, version: i32, bytes: Vec<u8> }`, `fn write_save(&SaveHeader, &[RawSection]) -> Vec<u8>`, `fn read_save(&[u8]) -> Result<(SaveHeader, Vec<RawSection>), SaveError>`, `fn world_hash(&[RawSection]) -> u64` (sorted by key, ordinal; writes key, version, bytes into `FnvSink`), `fn section_hash(&RawSection) -> u64` (FNV of `bytes` only, the `turns.json` convention).

- [ ] **Step 1: Write tests in `container.rs`:**
  - `start_save_roundtrips_byte_for_byte`: for each run, `write_save(read_save(start.save))` equals the file.
  - `world_hash_matches_turn_zero`: `world_hash` of `start.save` sections equals C#'s hash before turn 1 (the exporter writes `world_hash` for turn 0 in `turns.json`).
  - `header_matches_data_hash`: `start.save` header `data_hash` equals `hashes.json` `data_hash`, `seed` the run's seed, `turn` 0.
  - `strings_use_seven_bit_lengths`: a 200-byte string is prefixed `[0xC8, 0x01]`.
  - `rejects_damaged_input`: wrong magic, `FormatVersion` 2, a section length past the end, and every prefix of `start.save` return `Err`, never panic.
- [ ] **Step 2: Run** `cargo test -p fes-save`. Expected: FAIL.
- [ ] **Step 3: Implement** `io.rs` and `fes-save` (read the header, then count, then key/version/length/bytes per section).
- [ ] **Step 4: Run** the tests. Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Save container and .NET binary IO matching SaveSerializer"`.

---

### Task 5: CSV reader

**Files:**
- Create: `engine/fes-data/src/lib.rs`, `engine/fes-data/src/csv.rs`, `engine/fes-data/src/source.rs`, `engine/fes-data/tests/csv.rs`

**Interfaces:**
- Produces: `trait DataSource { fn exists(&self, name: &str) -> bool; fn read_text(&self, name: &str) -> Result<String, DataError>; }`, `FileDataSource::new(PathBuf)`, `InMemoryDataSource` (`add(name, text)`).
- Produces: `CsvTable::parse(name: &str, text: &str) -> Result<CsvTable, DataError>`; `name()`, `row_count()`, `has_column`, `get_string(row, col) -> Result<String>` (trimmed, missing trailing field = `""`), `get_string_or`, `get_int`, `get_int_or`, `get_long`, `get_enum<T: CsvEnum>`.
- Produces: `DataError` with file name and 1-based line/row in its message, as C#'s `FormatException` texts.

- [ ] **Step 1: Port the CsvTable tests** from `Assets/_Game/Tests/EditMode/DataAndWorldTests.cs` (every
  test touching `CsvTable`, same names in snake_case), and add `crlf_quotes_comments_blank_lines_short_rows`
  with this input and assertions:
  `"#c\r\nkey,value\r\n\r\na,\"x, \"\"y\"\"\r\nz\"\r\nb\r\n"` → 2 rows; row 0 value `x, "y"\r\nz`
  (the newline kept as written); row 1 value `""`. Also `unterminated_quote_is_an_error` and `numeric_enum_name_is_rejected` (`"9"`).
- [ ] **Step 2: Run** `cargo test -p fes-data --test csv`. Expected: FAIL.
- [ ] **Step 3: Implement** the row reader as the C# state machine (`_inQuotes`, `_fieldStarted`, `_rowStarted`; whitespace per `char.IsWhiteSpace`).
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "CSV reader matching CsvTable"`.

---

### Task 6: `SimulationConfig`

**Files:**
- Create: `engine/fes-data/src/config/mod.rs` and one file per C# `Apply*` topic (e.g. `config/levels.rs`, `config/merchant_judgement.rs`), each under 1,000 lines
- Create: `engine/fes-data/tests/config.rs`

**Interfaces:**
- Consumes: `CsvTable` (Task 5), `Fx`, `det_hash` (Tasks 2-3).
- Produces: `SimulationConfig::load(Option<&CsvTable>) -> Result<SimulationConfig, DataError>`; fields with the C# names in snake_case and C# types (`Fx`, `i32`, `i64`, arrays); `effective_values() -> &BTreeMap<String, String>`; `compute_hash() -> u64`; `to_parity_json() -> serde_json::Value` (C# field names, `ParityJson` rules) behind the `parity` feature.

- [ ] **Step 1: Write tests:** `effective_values_match_csharp` (equal maps to `config_effective.json`);
  `fields_match_csharp` (`to_parity_json()` equals `config_fields.json`, first differing field named);
  `hash_matches_csharp` (`compute_hash()` equals `hashes.json` `config_hash`);
  `missing_key_takes_default` (`load(None)` gives `config_defaults_effective.json`);
  `unknown_key_is_an_error` (a table with `no.such.key,1` returns `Err` naming the key).
- [ ] **Step 2: Run** `cargo test -p fes-data --test config --features parity`. Expected: FAIL.
- [ ] **Step 3: Implement** topic by topic in `SimulationConfigReader.cs` order; values are normalized as C# normalizes them before entering `_effective`.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "SimulationConfig matching C# values and hash"`.

---

### Task 7: Definitions, map and the data hash

**Files:**
- Create: `engine/fes-data/src/defs.rs` (goods, recipes, facilities, events, market profiles, specialties), `engine/fes-data/src/database.rs`, `engine/fes-data/src/map.rs`
- Create: `engine/fes-data/tests/database.rs`

**Interfaces:**
- Produces: `GameDatabase::load(&dyn DataSource) -> Result<GameDatabase, DataError>` with `config`, `goods`, `recipes`, `facilities`, `events`, `market_profiles`, `specialty(tag)`, `try_get_good(base_key, grade)`, `content_hash() -> u64`; `fn hash_text(file: &str, text: &str) -> u64` (CRLF and CR to LF, leading U+FEFF trimmed); `MapDef::load(&dyn DataSource)`, `compute_hash()`; `fn data_hash(&GameDatabase, &MapDef) -> u64` (= `combine(content_hash, map_hash)`).
- Produces: id newtypes `NodeId(i32)`, `GoodId(i32)` (`NONE = -1`, `GoodId::from(base, grade)`, `base_index`, `grade`) in `fes-core/src/ids.rs`; enums from `Assets/_Game/Core/Enums.cs` with the same discriminants.

- [ ] **Step 1: Write tests:** `definitions_match_csharp` (parity JSON vs `definitions.json`);
  `hashes_match_csharp` (`content_hash`, `map_hash`, `data_hash` vs `hashes.json`);
  `crlf_and_bom_do_not_change_hash` (`hash_text("goods.csv", "\u{FEFF}a\r\nb\r")` equals `hash_text("goods.csv", "a\nb\n")`);
  `file_order_is_csharp_order` (config first and unhashed as text, then goods, recipes, facilities, events, profiles, specialties).
- [ ] **Step 2: Run** `cargo test -p fes-data --test database --features parity`. Expected: FAIL.
- [ ] **Step 3: Implement** following `GameDatabase.Load`, `Definitions.cs` and `CsvMapSource.cs`.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `git commit -m "Definitions, map and data hash matching C#"`.

---

### Task 8: Parity runner

**Files:**
- Create: `engine/fes-parity/src/main.rs`, `engine/fes-parity/src/report.rs`, `engine/fes-parity/tests/runner.rs`

**Interfaces:**
- Consumes: Tasks 4-7.
- Produces: `fes-parity --fixtures <dir> [--run <name>]`. Per run it checks the data hash and the
  `start.save` round trip, then lists every section key with status `ported-equal`, `ported-different`
  (first differing byte offset) or `not-ported`. Exit code 0 when nothing is `ported-different`.
- Produces: `type BuildSection = fn(&GameDatabase, &MapDef, u64) -> Vec<u8>;` and
  `fn ported_sections() -> Vec<(&'static str, BuildSection)>` (section key, turn-0 bytes), empty in this
  plan; Plan 2 adds to it.

- [ ] **Step 1: Write `runner::reports_all_sections_not_ported_and_succeeds`** (run on the fixtures: exit 0, every key of `start.save` listed `not-ported`) and `runner::flags_a_wrong_section` (register a fake section for the first key that returns one changed byte: exit 1, offset reported).
- [ ] **Step 2: Run** `cargo test -p fes-parity`. Expected: FAIL.
- [ ] **Step 3: Implement** the runner and report (plain text, one line per section, summary count).
- [ ] **Step 4: Run** the test, then `cargo run -p fes-parity -- --fixtures fixtures`. Expected: PASS; summary `0 ported, N not ported, 0 different`.
- [ ] **Step 5: Ask the user before pushing.** On a yes, push `rust-port` and check CI passes
  (`gh run list --branch rust-port --limit 1`). Without a push, run the workflow's commands locally instead.
- [ ] **Step 6: Commit** `git commit -m "Parity runner for ported sections"`.
