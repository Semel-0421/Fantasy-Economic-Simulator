# Scenario playtest notes (Plan 6 Task 9)

Played on the full-scale world (engine/fixtures/fullscale) in the browser: the Playwright golden paths
(web/tests/ui/golden.test.ts) click the real panels in installed Microsoft Edge 157.0.4341.42
(channel `msedge`); the engine golden tests (engine/fes-sim/tests/scenario_s*.rs) give the same numbers.

## Results after tuning

| Scenario | Weeks | Active weeks | Human-pace estimate | Net profit (target) | Before tuning |
|---|---|---|---|---|---|
| S1 First Venture | 20 | ~8 (goal reached week 10) | ~30-35 min | 1,359 (1,000) | 1,059 |
| S2 Shares and Bonds | 22 | ~4 | ~25-30 min | 3,657 (objectives) | 3,657 |
| S3 Licences and Trade Abroad | 24 | ~13 (home week 21) | ~30-35 min | 2,653 (1,000) | 1,235 |

Human-pace estimate: intro and news ~2 min; a week with orders (read market, preview, confirm, travel)
~2.5 min; a quiet week ~0.5-1 min (looking at prices, the turn itself < 1 s). Not timed with real players.

## Tuning (scenario numbers only, no economy rules changed)

- S1: tool contract reward 1,200 -> 1,500 (dialog text updated). The golden path clears the 1,000 target
  by 359 instead of 59; guards and insurance stay affordable.
- S3: the k0n1 shortage price shock 800% -> 1,000%. Revenue 9,780 -> 11,280; net 2,653 after 2,400 of
  licences and 1,203 of guards/insurance, so a player who buys fewer goods or loses a sale week still wins.
- S2 unchanged (wide margin already).

## Budget

- Edge bench (web/bench, full scale, 52 turns): avg 390 ms, worst 793 ms (turn 52), hash after 11 matches.
  Gate (< 1 s) met.
- `preview_queued` on the full-scale world, Edge, through the worker, 4 orders queued: median 130 ms
  before -> 10 ms after (first call 65 ms while the buffers are allocated). Native release: 108 -> 7.7 ms.
  The preview no longer serializes the world's three largest sections (83 MB in all): demand forecasts
  (never written by a command) are left alone, markets and household cash are copied into reused buffers
  and swapped back. Results unchanged (preview tests, parity, hashes).

## Calendar

In a scenario the header, turn log and travel arrival show the scenario week ("Week 1" at start, same
as the panel's "Week 1 of 20"); free mode keeps "Year y, Week w".
