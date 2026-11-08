# Plan 5: Web Shell and Delivery — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Rust simulation plays in a browser — a wasm Web Worker behind a TypeScript UI with a main
menu, a map, the Next-week turn flow and trading screens — meets the turn budget in Chrome, works offline
as a PWA, and deploys automatically to a development page on every push, with a release path to itch.io.

**Architecture:** `engine/fes-wasm` wraps `fes_sim::Simulation` with `wasm-bindgen`; it runs in a dedicated
Web Worker and talks to the page only through serialisable messages (`PlayerCommand`, `SimEventRecord`,
views — the serde types from Plan 4). `web/` is a Vite + TypeScript app: DOM for panels and menus, PixiJS
for the map. Game data (the CSVs) ships as static files. Saves are the byte-compatible save in IndexedDB.
CI builds wasm and web on every push and publishes to GitHub Pages (development page); a tag builds a
release artefact for itch.io via `butler`.

**Tech Stack:** Rust 1.99.1, `wasm-bindgen`, `wasm-pack` (or `wasm-bindgen-cli`), `wasm-opt`; Node LTS,
Vite, TypeScript (strict), PixiJS 8, `idb-keyval` (IndexedDB), Vitest, Playwright (Chromium) for smoke and
timing; GitHub Actions + GitHub Pages; `butler`.

**Spec:** `docs/superpowers/specs/2026-11-04-rust-web-port-design.md` (sections 5, 7.1-7.3, 9). Plans 1-4.

## Global Constraints

- The simulation is unchanged by this plan except Task 2's speed-ups, which must keep every golden run and
  step fixture equal (`fes-parity --runs` and `--steps`). No rule or number changes.
- The UI never touches simulation state directly: commands in, events and views out (spec 7.2). One turn
  runs only when the player presses **Next week**; the UI stays responsive while it computes (Worker).
- Budget gate (spec 7.3): full-scale world, turns 0-53, Chrome on the development desktop, single thread,
  inside an itch.io embed: worst turn under 1 s and average under 0.7 s. Threads are not used.
- Release build: `opt-level=3`, LTO, `codegen-units=1`, `wasm-opt -O3`; no debug assertions in the shipped wasm.
- Player-facing text in English (contest rule). No network calls from the game at runtime (no in-game AI or
  APIs — user decision); everything is static files.
- TypeScript strict mode; ESLint + Prettier; no `any` in message types. Message types are generated from
  or checked against the Rust serde types (a round-trip test in CI).
- Layout works from 360 px wide (Android later) to desktop; touch and mouse.
- Deploy: development page = GitHub Pages on every push to `rust-port`; release = tag `v*` → artefact zip →
  `butler push` to the itch.io development channel. The judged page is never updated by automation.
- Never stage `Packages/` or `ProjectSettings/`; stage by path. Ask the user before every push and before
  anything that needs their accounts (itch.io API key, GitHub Pages setting).

## Review Focus

- A turn that takes longer than a frame must not freeze the page — the Worker test drives Next week while
  measuring main-thread long tasks.
- A save written by one build loads in the next (same `FormatVersion`) and a corrupt or foreign IndexedDB
  entry shows an error instead of crashing — Task 4 tests.
- The page served from a sub-path (`/fes/` on Pages, itch's iframe path) loads wasm, worker and data with
  relative URLs — Task 8 smoke test on the deployed URL.
- A command rejected by the simulation shows its C# message to the player and changes nothing.
- The first visit offline after one online visit still plays (service worker caches wasm, data, assets).

---

### Task 1: `fes-wasm` and the browser timing page

**Files:** `engine/fes-wasm/{Cargo.toml, src/lib.rs}`, `engine/Cargo.toml` (member, release profile),
`web/bench/{index.html, bench.ts}`, `Docs/superpowers/port-notes.md`.

**Interfaces:**
- Produces (`wasm-bindgen`): `WasmSim::new(files: JsValue /* {name: text} */, seed: u64) -> Result<WasmSim, JsError>`;
  `step() -> JsValue` (TurnReport as serde JSON-compatible value); `queue(cmd: JsValue) -> Result<(), JsError>`;
  `preview(cmd: JsValue) -> JsValue`; `view(name: &str, arg: JsValue) -> JsValue`; `save() -> Vec<u8>`;
  `WasmSim::load(files, bytes: &[u8]) -> Result<WasmSim, JsError>`; `turn() -> i32`; `world_hash() -> String`.
- Produces: a bench page that loads the full-scale CSVs (`engine/fixtures/fullscale/`), plays turns 0-53 in
  a Worker, and prints average, worst, the new-year turn and the world hash after turn 11 (must equal the
  C# full-scale `turns.json`).

- [ ] **Step 1:** Test (Rust, wasm-bindgen-test in Node) `wasm_sim_matches_native_hash_after_11_turns`.
- [ ] **Step 2:** Build release wasm; run the bench in Chromium (Playwright, headed) on the development
  desktop; record the numbers in `port-notes.md` beside native (avg 0.47 s, worst 0.78 s) and C# web
  (1.03 s, 1.64 s). **Commit.**

### Task 2: Speed-ups to the gate

**Files:** as needed under `engine/fes-sim/src/` (merchants, pricing, demand forecast, the integer
log2/exp2), `Docs/superpowers/port-notes.md`.

- [ ] **Step 1:** Profile the wasm build (Chrome performance panel or a per-system timer in the bench).
- [ ] **Step 2:** Apply, one commit each, re-measuring after each, the speed-ups from `port-notes.md` that do
  not use threads: exact lookup tables for log2/exp2 (identical results), per-node merchant route cache with
  upper-bound pruning, per-cell pricing memo and one log per cell in the quintile loop, demand-forecast
  dirty-maker pass and buffer reuse, and moving forecast work off the new-year turn only if C# results are
  unchanged (a schedule change that alters results is a rule change — not allowed).
- [ ] **Step 3:** After each: `fes-parity --runs` (release) and `--steps` equal. Stop when the bench shows
  worst < 0.9 s and average < 0.6 s in Chromium on the development desktop (margin for the itch embed);
  if not reachable, report the remaining gap and the next candidates. **Commit.**

### Task 3: `web/` scaffold and the worker bridge

**Files:** `web/{package.json, vite.config.ts, tsconfig.json, index.html, src/main.ts, src/sim/{worker.ts,
bridge.ts, types.ts}, src/data/files.ts}`, `web/tests/bridge.test.ts`.

**Interfaces:**
- Produces: `class SimBridge { static start(seed): Promise<SimBridge>; next(): Promise<TurnReport>;
  queue(cmd: PlayerCommand): Promise<void>; preview(cmd): Promise<CommandResult>; view<K>(name: K, arg?):
  Promise<Views[K]>; save(): Promise<Uint8Array>; static load(bytes): Promise<SimBridge> }` — every call a
  typed message to the Worker.
- `types.ts`: TypeScript types for `PlayerCommand`, `SimEventRecord`, views, checked by a Rust test that
  serialises one of each variant to `web/tests/fixtures/*.json`, and a Vitest test that parses them.

- [ ] Tests first (bridge round trip in a Worker under Vitest/happy-dom or Playwright; type fixtures);
  implement; `npm run build` makes `web/dist` with relative URLs. **Commit.**

### Task 4: Saves in IndexedDB

**Files:** `web/src/save/store.ts`, `web/tests/save.test.ts`.

- [ ] Free-mode saves (slots: autosave each Next week + 3 manual), scenarios never save (spec 9.2). A
  corrupt or foreign entry shows "This save can't be loaded." and keeps the others. Tests first. **Commit.**

### Task 5: Main menu and turn flow

**Files:** `web/src/ui/{menu.ts, hud.ts, turn.ts, styles.css}`, `web/tests/ui/*.test.ts`.

- [ ] Main menu: Scenario 1-3 (shown as "Coming soon" placeholders until Plan 6), **Free mode** last,
  Continue (latest save), Settings (sound volume placeholder). HUD: coins panel, date (year/week), Next week
  button. Turn flow (spec 5.2a): the player acts, presses Next week, the button shows progress, input to the
  map stays live, the report's events come back and are listed in a turn log. Tests with Playwright for the
  flow. **Commit.**

### Task 6: Map

**Files:** `web/src/map/{map.ts, layout.ts}`, `engine/fes-sim/src/views.rs` (map view if missing),
`web/tests/map.test.ts`.

- [ ] Map view from Rust (`MapView { nodes: [{id, key, level, x, y, continent}], routes: [{from, to, turns,
  terrain}] }` — use `MapDef` coordinates; if none exist, a deterministic layout computed in TS and
  documented). PixiJS renders nodes by level, routes, the player's position and destination; pan and zoom by
  drag/wheel/pinch; tap a town to select it; Travel issues the travel command. **Commit.**

### Task 7: Trading screens

**Files:** `web/src/ui/{market.ts, cargo.ts, purse.ts, errors.ts}`, tests.

- [ ] Market screen for the current town (`market_view`): goods, price, stock; buy/sell quantity with
  `preview` showing cost and the rejection message before confirming; cargo and purse panels refresh from
  views after each command and each turn. Rejections show the simulation's message. **Commit.**

### Task 8: PWA and delivery

**Files:** `web/public/manifest.webmanifest`, `web/src/sw.ts` (or `vite-plugin-pwa`), icons,
`.github/workflows/{web.yml, release.yml}`, `web/tests/e2e/smoke.spec.ts`, `Docs/superpowers/delivery.md`.

- [ ] PWA: installable, offline after one visit (cache wasm, worker, data, assets; versioned cache name).
- [ ] `web.yml` on every push: Rust gates (reuse engine job), wasm release build, `npm ci && npm run build`,
  Playwright smoke (menu → Free mode → buy → Next week → save → reload → continue) against `vite preview`,
  then deploy `web/dist` to GitHub Pages. The user enables Pages (Settings → Pages → GitHub Actions) — ask.
- [ ] `release.yml` on tag `v*`: same build, zip artefact attached to a GitHub release, `butler push` to
  the itch.io **development** channel using secret `BUTLER_API_KEY` (the user creates the key and the secret
  — ask; never handle the key). Blocked unless parity and the bench gate (Task 1 page in headless Chromium,
  as a regression check against a stored ceiling) pass.
- [ ] `delivery.md`: how to release, how to set up the itch page (HTML, embed size, SharedArrayBuffer off),
  and the rule that the judged page is updated by hand only.
- [ ] **Commit.** Ask the user before pushing; after the first deploy, open the Pages URL in the browser
  pane, play one turn, and record the bench numbers there.

### Task 9: Turn budget in the itch.io embed

- [ ] With the user (their itch account): upload the release artefact to a private itch.io draft page,
  open it in Chrome on the development desktop, run the bench mode (`?bench=fullscale`), record average and
  worst in `port-notes.md` and `00_Architecture.md`. Gate: worst < 1 s, average < 0.7 s. If missed, return
  to Task 2 with the numbers. **Commit.**
