# Plan 7C: Names, Assets Pass and Judged Release — Implementation Plan

> **For agents:** Roles and rules: `AGENTS.md`, `Docs/superpowers/agent-rules.md`. Claude plans, writes the player-facing names and text, and reviews; Codex builds (one commit per task, `git commit -- <paths>`); Copilot gates before every push; the user approves every push and does all account work. Korean copy: `2026-11-09-plan7c-release.ko.md`.

**Goal:** Close the roadmap's Plan 7 (assets, polish, submission): make the world read like a fantasy world instead of keys, integrate the SpriteCook/ElevenLabs files when the user delivers them, produce the gameplay video and thumbnail from the real build, and ship a frozen judged release before 2026-12-24.

**Spec:** sections 3 (contest constraints), 5, 8, 9.1. Roadmap row 7. Plans 7A, 7B.

## Why these tasks (Claude, 2026-11-09)
- The full-scale world ships with empty names (`web/public/data/fullscale/map_nodes.csv` name column is blank; goods are `g0_0`…`g19_49`), so judges read "Buy g19_0 here and sell it in k0n1". This hurts Theme, Craft and Creativity scores more than any remaining visual issue.
- At the fitted map view the vector town symbols are dense and compete with labels (Plan 7B Task 12 concern). The user chose a hex tile map (2026-11-09), which replaces the box layout.
- Assets are still absent (`present.json` is empty); the loader and fallbacks exist, but nothing checks delivered files against the contract.
- The contest needs a downloadable gameplay video and a thumbnail; the submission freezes once submitted.

## Global Constraints
- **Engine untouched; data hashes untouched.** Names and the hex layout are UI layers keyed by node/good key. No change to `engine/`, to engine fixtures, to `map_nodes.csv`/`goods.csv` or to save bytes. Scenario RON text may change (Plan 7A rule): each scenario's golden net profit stays ≥ 1.3x its target and every scenario golden test passes.
- Display names are deterministic (a pure function of the key; no runtime randomness, no network).
- No runtime network calls, no AI at runtime. Gates per task in `web/`: lint, typecheck, test, build; tasks touching screens also smoke and test:ui. Bench only if something unexpectedly touches the engine (it must not).
- User-only steps (accounts, generating assets, itch.io page, final submission) go to the user checklist in Korean, never done by an agent.

---

### Task 1: Display names (Claude writes names; Codex, gpt-6.1-sol, medium)
- `web/src/data/names.ts`: `placeName(key)` and `goodName(key)`. A curated table (Claude supplies it in the task brief) covers every node and good the three scenarios mention plus the 5 continents and their nations; every other key gets a generated name from fixed English-pronounceable syllable tables, chosen by a hash of the key (FNV-1a), unique within its level (a test checks no duplicates among the 1,580 nodes and 1,000 goods), never offensive (Claude reviews a full dump before commit).
- Grades keep their text: `goodLabel` shows `Name (high)`.
- Everywhere the UI shows a node or good (map labels, travel, market, cargo, finance, scenario panel, dialogs, results, log, menu pitch) uses the display name. Scenario and event text that contains a key literal (`k0n0r0c1`, `g19_0`) is rewritten at render time by a whole-word key → name substitution; RON files may also be edited by Claude to read naturally.
- Tests: every scenario-mentioned key has a curated name; no key pattern (`/\bk\d+(n\d+)?/`, `/\bg\d+_\d+\b/`) remains in any visible text on the three scenario start screens and the results fixture (Playwright text scan); names stable across runs (snapshot of 20 samples).
- **Commit.**

### Task 2: Hex tile map (Codex, gpt-6.1-sol, high) — user decision 2026-11-09
The user wants the map drawn as hex tiles, with the strategy-game hex map as the reference. We use the genre convention (a hex grid, territory borders, terrain tiles, units on tiles) in our own palette and pixel style; no copying of any game's art, icons or layout.
- **Grid:** pointy-top axial hex grid (`web/src/map/hex.ts`: axial/pixel conversion, neighbours, hex line, hex under a point). Logical tile 28x32 px at zoom 1.
- **Layout (`web/src/map/hex_layout.ts`, deterministic, UI only):** each continent is an island of land hexes separated by sea; inside it each nation is one contiguous territory, inside that each region a contiguous area; every city and village sits on its own hex inside its region. Placement from the node hierarchy and route graph only (no randomness; ties broken by node id), so the same map data always gives the same picture. Replaces the box layout of `layout.ts` for drawing; `layout.ts` keys and `screenOf`/picking contracts stay.
- **Terrain per hex:** hexes on a route's hex line take that route's terrain (road over plains, river, mountain pass, sea lane); other land hexes are plains/forest/hills by a hash of the hex coordinate weighted by distance to mountain routes; sea elsewhere. Terrain is decoration only; travel time and rules still come from the engine routes.
- **Drawing:** terrain layer rendered once into a cached texture (redrawn only on resize), nation borders as thick coloured edges between hexes of different nations, region borders thin and dotted, roads/sea lanes as lines through hex centres, towns as markers on their hex (dot at low zoom, emblem/sprite when town labels show), player/selected/target markers always visible. Labels stay in the screen-space layer of Plan 7B.
- **Input:** tap/click picks the town on the hex under the pointer, else the nearest town within one hex; pan/zoom/keyboard as now.
- **Performance:** layout of the full-scale world ≤ 100 ms once at game start (measured, recorded); pan/zoom at 60 fps on the dev machine (frame time recorded in the report); no work inside a turn.
- **Assets:** terrain tiles become hex tiles. Update `Docs/assets/art-list(.ko).md`, `web/public/assets/README.md`, the manifest and the asset log: `map/terrain/{plains,forest,hills,mountain,water}.png` hex tiles 28x32 RGBA (transparent outside the hex), plus `map/terrain/road_overlay.png` optional. Tell the user (and the pixel-assets session) before they generate terrain art.
- **Tests:** no two towns share a hex; each town inside its region area; each nation contiguous; layout identical across two runs; layout time budget; picking returns the town on a hex; label overlap tests green; screenshots at 1280, 900 and 360 for review.
- **Commit.**

### Task 3: Asset check and integration path (Codex, gpt-6.1-sol, medium)
- `npm run check:assets` (`web/scripts/check-assets.mjs`): for every file present under `web/public/assets/`, check name and folder against the manifest, PNG size (exact), colour type RGBA, no semi-transparent edge halo beyond 1 px, file ≤ 200 KB; OGG container, sample rate 48 kHz and duration within ±10 % of the README length (parse the Ogg/Vorbis headers in Node; no ffmpeg). Unknown files fail. Exit 1 with a list on any failure.
- `npm run assets:log` prints the asset-log rows for present files (path, size, sha256) so Claude can update `Docs/assets/asset-log.md` with real dates and evidence paths.
- Credits switch to the real tool lines automatically when files exist (already in Plan 7B Task 6; add a test with a fake present list).
- Tests: fixtures of one good and one bad PNG/OGG (tiny, generated in the test) pass/fail as expected.
- **Commit.** When the user delivers files: run the check, fix names/sizes with the user, update the asset log, screenshot each screen, commit assets by path (each ≤ 5 MB; total budget 8 MB).

### Task 4: English text pass (Claude writes; Codex applies, gpt-6.1-sol, low)
- Codex extracts every player-visible string (UI modules, scenario RON intro/dialogs/objectives/events, guide, credits) into `.superpowers/sdd/<plan>/strings.md`.
- Claude edits for clarity, tone (merchant guild, plain English, sentence case), consistency of terms (coins, week, licence, cart), and length on 360 px. Codex applies the edits exactly.
- Tests: scenario goldens pass; UI tests updated only where a string changed.
- **Commit.**

### Task 5: Capture tools for video and thumbnail (Codex, gpt-6.1-sol, medium)
- `npm run capture` (`web/scripts/capture.mjs`, Playwright on installed Edge, production build): plays a scripted 60-90 s highlight of scenario 1 (intro, buy grain, travel, sale with growth presentation, contract delivery, results) at 1920x1080 with Playwright `recordVideo` (WebM), and saves stills at 1920x1080 and 1280x720 of the menu, map, growth moment and results to `web/capture/` (git-ignored).
- The script uses the existing golden path helpers, waits for real turns (no faked numbers), and turns the guide off for the recording.
- The user edits and exports the final MP4 and thumbnail (their tool of choice); Claude writes the shot list and a 1-line caption per shot.
- **Commit** (script only; no media in git).

### Task 6: Release 1.0 and submission checklist (Claude writes; Codex, gpt-6.1-sol, low; user for accounts)
- `Docs/release/judged-release.md` (+ `.ko.md`): pre-flight (all gates, parity, bench in the itch embed, test:ui, asset check, English text, credits/disclosure, contest rules re-read on the official pages, no console errors, offline play), the tag `v1.0.0`, release workflow to the itch `development` channel, the user's manual check, then the user promotes the same build to the judged page and submits; after submission the judged page is never updated (spec 9.1).
- Version bump in `web/package.json` and the in-game version line in Credits.
- Ask the user before tagging and before every push. Record the final bench numbers from the itch embed in `port-notes.md`.
- **Commit.**

## Order and dependencies
Tasks 1 → 4 (text pass after names), 2 and 3 independent (Task 2 first updates the terrain asset contract, before the user generates terrain art), 5 after 1-2 (video shows names and clean map), 6 last. Asset delivery from the user can land any time after Task 3.

## Review Focus
- Any change to engine, fixtures, data CSVs or save bytes.
- A key literal still visible to the player; duplicate or inappropriate generated names.
- A hex layout that is not deterministic, puts two towns on one hex, or copies another game's art.
- Asset check too lenient (wrong size or alpha passes) or too strict for valid files.
- Capture script that fakes results instead of playing real turns.
- A release step that updates the judged page after submission or is done by an agent instead of the user.
