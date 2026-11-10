# Plan 7D: Language options (Korean and the 20 most spoken languages) — Implementation Plan

> **For agents:** Roles and rules: `AGENTS.md`, `Docs/superpowers/agent-rules.md`. Claude plans, writes the translations and names, and reviews; Codex builds (one commit per task, `git commit -- <paths>`); Copilot gates before every push; the user approves every push. Korean copy: `2026-11-10-plan7d-korean.ko.md`.

**Goal:** A language choice in Settings so the developer, and later players, can play the whole game in their language: menus, HUD, guide, dialogs, scenario text and objectives, place and goods names. English stays the default and the judged language.

**User decisions (2026-11-10):** a language selector in the Settings menu of every build; everything is translated, including names; languages = Korean plus the world's 20 most spoken languages.

## Languages (21 including English)
Source: Ethnologue 2025, total speakers (first + second language). Egyptian Arabic (rank 15) is written in Modern Standard Arabic, so Standard Arabic covers it and the next language, Vietnamese, takes its place. Korean is added at the user's request.

| Code | Language | Script | Direction |
| --- | --- | --- | --- |
| en | English (default) | Latin | LTR |
| zh-Hans | Mandarin Chinese, simplified | Han | LTR |
| hi | Hindi | Devanagari | LTR |
| es | Spanish | Latin | LTR |
| ar | Standard Arabic | Arabic | **RTL** |
| fr | French | Latin | LTR |
| bn | Bengali | Bengali | LTR |
| pt | Portuguese | Latin | LTR |
| ru | Russian | Cyrillic | LTR |
| id | Indonesian | Latin | LTR |
| ur | Urdu | Arabic (Nastaliq) | **RTL** |
| de | German | Latin | LTR |
| ja | Japanese | Kana + Kanji | LTR |
| pcm | Nigerian Pidgin | Latin | LTR |
| mr | Marathi | Devanagari | LTR |
| te | Telugu | Telugu | LTR |
| tr | Turkish | Latin | LTR |
| ha | Hausa | Latin | LTR |
| ta | Tamil | Tamil | LTR |
| vi | Vietnamese | Latin | LTR |
| ko | Korean | Hangul | LTR |

**Quality note.** Translations are written by Claude (AI) and are not reviewed by native speakers, except Korean (the developer). Nigerian Pidgin, Hausa and Urdu have the least written reference material; expect rougher text there. Credits says so in English: "Translations other than English and Korean are machine-made and unreviewed."

## Global Constraints
- **Default English, never auto-detected.** A first visit, a cleared browser and every test that does not opt in see English, so the judged build plays in English (spec 3). The choice is stored in `localStorage` (`fes-lang`).
- **Engine, data and saves untouched.** No change to `engine/`, fixtures, `data/scenarios/*.ron`, `map_nodes.csv`/`goods.csv`, save bytes or golden hashes. Languages are a UI layer: the engine keeps sending its English text and keys; the UI looks up the translation.
- Deterministic names in every language (a pure function of key and language).
- No runtime network or AI. Fonts are bundled and loaded only for the language that needs them.
- Gates per task in `web/`: lint, typecheck, test (`--no-file-parallelism`), build; tasks touching screens also smoke and test:ui.

## Design (Claude)
- **Catalogues.** `web/src/i18n/`: `en.ts` is the source (`id -> string | (params) => string`); one file per language with the same shape, type-checked to cover every id (`satisfies Catalogue`). Functions allow plurals and word order per language (use `Intl.PluralRules`). `t(id, params?)` reads the language set once at startup. Changing the language stores it and reloads the page (the selector is only in the main menu, so no game is lost).
- **Registry.** `languages.ts`: code, native name (shown in the selector: "English", "中文（简体）", "हिन्दी", "Español", "العربية", …, "한국어"), direction, font stack, `Intl` locale.
- **Scenario text.** The engine sends RON strings in English. `scenario/<code>.ts` maps each **exact English source string** of the three RON files to the translation, keeping `{qty}`, `{good}`, `{dest}`, `{deadline}`, `{reward}` and node/good keys (`k0n0r0c0`, `g6_0`) intact so `displayText` still substitutes names. A test parses the RON files and fails on any missing string or any placeholder/key mismatch, for every language.
- **Names.** Latin-script languages keep the English fantasy place names (proper nouns) and translate goods and the generic words (landforms, qualifiers). Non-Latin scripts transliterate: curated place names by hand, generated names through a per-script table for each English syllable (`al`, `bel`, … → `알`, `벨`, …), so a name stays the same name in every script. Goods use per-language noun tables in the same category order. Uniqueness and banned-word checks run per language.
- **Numbers and dates.** `Intl.NumberFormat(locale, { numberingSystem: 'latn' })` for grouping with Latin digits in every language (keeps tests and screenshots comparable); week/year phrases come from the catalogue.
- **Right-to-left.** `<html dir="rtl">` for ar and ur. CSS moves to logical properties (`margin-inline-start`, `inset-inline-end`, `text-align: start`) where layout is directional. The map canvas is not mirrored; only its labels' text runs RTL.
- **Fonts.** English keeps Pixelify Sans (headings) and Atkinson Hyperlegible (body). For every other language, a pixel heading font is used only if it is OFL-licensed and covers every character the catalogue uses for that language (checked by a test reading the font's character map); otherwise headings and body use the system stack for the script (e.g. `"Noto Sans Devanagari", "Nirmala UI", "Mangal", sans-serif`). Expected: Pixelify Sans for Latin/Cyrillic/Vietnamese if coverage passes, Galmuri11 for Korean and Japanese if coverage passes, system fonts for Han, Arabic, Indic scripts. Credits lists every bundled font and licence. `<html lang>` follows the choice.

---

### Task 1: i18n layer, English catalogue, registry, RTL groundwork (Codex, gpt-6.1-sol, high)
- Add `web/src/i18n/` (`t`, language state, `en.ts`, `languages.ts`); move every player-visible literal of `web/src/main.ts` and `web/src/ui/*.ts` into `en.ts` (inventory: `.superpowers/sdd/2026-11-09-plan7c-release/strings.md`; structural ids, CSS selectors, storage keys and bench output stay literal). Publish the id list to `.superpowers/sdd/2026-11-10-plan7d-korean/catalogue-ids.md` as soon as it is stable.
- Settings: a labelled `Language` select listing all 21 native names, stored in `fes-lang`, reload on change. Unfinished languages fall back to English per id until their task lands.
- `dir`/`lang` on `<html>`; CSS converted to logical properties where needed; `Intl` number formatting.
- Tests: English screens unchanged (existing UI tests pass as they are); catalogue coverage test (every language file covers every `en` id with the same parameter names) that reports, not fails, while files are still English fallbacks; fails once a language is marked complete.
- **Commit.**

### Task 2: Korean pilot (Claude writes; Codex applies, gpt-6.1-sol, low)
- Claude writes `ko.ts` and `scenario/ko.ts` and the Korean names tables. Tone: short, plain 해요체 for guide and dialogs, nouns for buttons and labels. The pilot fixes the file format and the review checklist for the other languages.
- Codex applies, marks Korean complete, adds the RON coverage test, runs gates. **Commit.**

### Task 3: The other 19 languages (Claude writes, Codex applies, gpt-6.1-sol, low)
- Claude translates from `en.ts` and the RON strings (Korean as a second reference for meaning), one language at a time or in parallel Claude subagents, then spot-checks placeholders, keys, length and tone. Same files per language: catalogue, scenario map, names tables.
- Codex applies in batches (e.g. Latin-script languages, then Cyrillic/CJK, then Indic, then RTL), marks each complete, runs gates. **One commit per batch.**

### Task 4: Names and fonts in every language (Codex, gpt-6.1-sol, medium)
- Language-aware `names.ts` (deterministic, unique, banned-word check per language); per-script syllable tables; goods nouns.
- Fonts: bundle the pixel fonts that pass coverage, lazy-load per language, system stacks otherwise; coverage test; Credits licence lines and the machine-translation note.
- Tests: every key gets a unique non-empty name in every language; English names unchanged (existing name tests and goldens pass).
- **Commit.**

### Task 5: Review pass (Codex, gpt-6.1-sol, medium; Claude reviews)
- `test:ui` runs for ko, ar (RTL), ur (RTL, Nastaliq height), ja, zh-Hans, hi (Indic shaping), de and ru (long words): no overflow or clipped text at desktop and phone widths, no English left except approved proper nouns, accessibility checks pass, screenshots to `web/test-results/ui-<code>/`.
- A fast check for all 21: each screen renders with that language, no missing id, no console error.
- Claude reviews the screenshots and the text in place; fixes go back into the language files.
- User checklist: re-read whether optional extra languages are allowed in the contest (default stays English).
- **Commit.**

## Order and dependencies
1 → 2 → 3 → 4 → 5. Task 2 and 3 text can be written while Task 1 is built once the id list is published. Task 4's names tables come from Tasks 2 and 3.

## Review Focus
- English output changed in any way, or another language chosen without the user selecting it.
- A RON string, objective or dialog left in English in a complete language.
- Placeholders or node/good keys lost in a translation.
- Broken RTL layout (mirrored map, misplaced numbers, clipped Nastaliq).
- Non-deterministic or duplicate names; tofu (missing glyph boxes) in a bundled font.
- A font loaded for a language that does not use it; any network request.
