# Plan 7A: Non-UI Work Before Submission — Implementation Plan

> **For agents:** Roles and rules: `AGENTS.md`, `Docs/superpowers/agent-rules.md`. Claude plans and reviews, Codex builds, Copilot gates pushes. UI work is deferred by the user (2026-11-09).

**Goal:** Finish the work the judged build (due 2026-12-24) needs outside the UI: lighter engine memory and copies, tidier scenario data and text, an asset log with generation lists, and a tested release path.

**Spec:** `docs/superpowers/specs/2026-11-04-rust-web-port-design.md` (sections 3, 7.3, 8, 9). Plans 1-6.

## Global Constraints
- Shared rules hold. Free mode equals the C# reference after every engine change (parity as in `engine.yml`); bench worst < 1 s, average < 0.7 s.
- No economy rule or number changes. Scenario numbers change only in `data/scenarios/*.ron`, and each scenario's golden net profit stays at least 1.3× its target.
- No UI layout work. Data or text the UI already shows may change.
- User checklist (things only the user can do): `C:\Users\Administrator\OneDrive\바탕 화면\user_checklist\user_checklist.md`, in Korean. Deleted once every item is checked.

---

### Task 1: Engine memory and copies (Codex, gpt-6.1-sol, high)
- Measure first, then change: (a) memory and time of `preview_queued` (scratch buffers, about 45 MB after the first preview); (b) every place the build, `load` or `preview` copies large state (`GameDatabase`/`MapDef` are already `Arc`; check world sections, saves, wasm `load`); (c) peak wasm memory over turns 0-53 in the Edge bench.
- Change only what the numbers justify; results must stay equal. Target: preview scratch ≤ 20 MB or removed, no full-world copy outside `save`.
- Tests: preview equality tests still pass; `preview_fullscale` hash unchanged; a test or bench line records the memory numbers.
- Record before/after in `Docs/superpowers/port-notes.md`. **Commit.**

### Task 2: Scenario data and text (Claude writes text, Codex applies)
- The contract event shows the promised reward and, at delivery, the amount actually paid (the dialog text of the delivery event, from data).
- Scenario 2: add one or two scripted events in its quiet weeks (data only), so the player has a decision about every 3 weeks.
- English text pass over intro, dialogs and objective names in the three RON files.
- Tests: goldens still pass at ≥ 1.3× target; every event still has a deadline fallback. **Commit.**

### Task 3: Asset log and generation lists (Claude writes, Codex scaffolds)
- `Docs/assets/asset-log.md`: one row per asset (file, tool, prompt or process, date, screenshot path), and the disclosure rule from spec section 8.
- `Docs/assets/art-list.md` (SpriteCook: map tiles, towns by level, caravans, icons) and `Docs/assets/sound-list.md` (ElevenLabs: music loop, coin, button, event alert), each with sizes, formats and prompts, and Korean copies for the user.
- `web/public/assets/` folder layout and file names the UI will load later. **Commit.**

### Task 4: Release path test (Codex, Copilot gate; needs the user's accounts)
- After the user enables Pages and adds `BUTLER_API_KEY` and `ITCH_TARGET`: push, check the Pages deploy, tag `v0.1.0-rc1`, check the itch development channel upload.
- The game has no in-app bench mode yet (Plan 5 planned `?bench=fullscale`; only `web/bench` exists). Add it: `?bench=fullscale` runs turns 0-53 on the full-scale world in the Worker and shows average, worst and the hash after turn 11. No UI layout work beyond a plain result text.
- Measure the turn bench inside the itch embed (Plan 5 Task 9); record in `port-notes.md`. Gate: worst < 1 s, average < 0.7 s.
- Ask the user before tagging and before every push.

## Review Focus
- A memory change that alters results (parity) or makes a preview differ from the applied result.
- A scenario text change that breaks a golden test or hides what was actually paid.
- Asset names that do not match what the UI will load.
