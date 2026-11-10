# Judged release 1.0: procedure and submission checklist

Plan 7C Task 6. Spec sections 3 (contest constraints) and 9.1 (pipeline). Korean copy: `judged-release.ko.md`.
Pipeline details: `Docs/superpowers/delivery.md`.

**Who does what.** Agents prepare the build and run the checks. The **user** approves every tag and push, does all
account work on itch.io and the contest site, plays the development build, promotes it, and submits. No agent
touches the judged page.

## 1. Before the tag (agents, on the release commit)

All on one commit of `rust-port`, with a clean tree except the user's local Unity files (`Packages/`,
`ProjectSettings/`), which are never committed.

| # | Check | How | Pass |
| --- | --- | --- | --- |
| 1 | Web gates | in `web/`: `npm run lint`, `typecheck`, `test -- --no-file-parallelism`, `build`, `smoke`, `test:ui` | all exit 0 |
| 2 | Engine and parity | the `engine` CI job on the release commit (fmt, clippy, tests, parity runs) | green |
| 3 | Turn budget | `web/bench`: `npm run wasm`, `node run-bench.mjs` (stored ceiling); the tag run repeats it | pass |
| 4 | Assets | `npm run check:assets`; `npm run assets:log` matches `Docs/assets/asset-log.md` | exit 0; every shipped file logged |
| 5 | English text | no key literal (`g12_3`, `k0n1`) or Korean visible on any screen; `test:ui` screenshots reviewed | none found |
| 6 | Credits and disclosure | Credits screen lists fonts and libraries, the AI tools actually used (SpriteCook, ElevenLabs, Claude), the version line `v1.0.0` | matches the shipped build |
| 7 | Console | production build, all three scenarios and free mode started, one year played in scenario 1 | no console errors or warnings from game code |
| 8 | Offline | smoke's offline step passes (service worker serves a turn offline after one visit) | pass |
| 9 | Versions | `web/package.json` `version` is `1.0.0`; Credits shows the same | equal |
| 10 | Copilot gate | pre-push check of the release commit | PASS |

## 2. Before the tag (user)

- Re-read the contest rules on the **official pages** (not a copy): deadline, required fields, video length and
  format, thumbnail size, AI-tool tags, and anything changed since the spec was written. Report changes to Claude.
- Confirm the gameplay video and thumbnail come from this build (`npm run capture`, Plan 7C Task 5). Re-capture if
  text or assets changed after the last capture.
- Confirm the itch.io secrets are still set: `BUTLER_API_KEY` (secret) and `ITCH_TARGET` (variable).

## 3. Tag and release (agent, after the user says yes)

```
git tag -a v1.0.0 -m "Judged release 1.0.0"
git push origin v1.0.0
```

The `release` workflow runs the engine job, the bench check, then builds, attaches
`fantasy-economic-simulator-web-v1.0.0.zip` to the GitHub release, and pushes the same `web/dist` to the itch.io
**`development`** channel with `--userversion v1.0.0`. Nothing else is published. If any job fails, fix on a new
commit and tag `v1.0.1`; a pushed tag is never moved or deleted.

## 4. Manual check on itch.io (user)

On the development page, in a normal browser window (not a developer build):
- The page shows version `v1.0.0` (itch.io build list and the Credits screen).
- Embed loads in the iframe; fullscreen button works; no download is needed.
- Play scenario 1 to the results screen; start scenarios 2 and 3; start free mode, save, reload, continue.
- Bench in the embed: open the page with `?bench=fullscale` and record average and worst turn. Gate: worst < 1 s,
  average < 0.7 s. Claude records the numbers in `Docs/superpowers/port-notes.md`.
- Sound plays after the first click; the mute setting persists.

If anything fails, stop here: report it, and the fix goes out as `v1.0.1` through steps 1-4 again.

## 5. Promote and submit (user)

1. In the itch.io dashboard, put the **same** `v1.0.0` build live on the judged page (set the checked channel live
   or upload the release zip by hand). No new build is made for this step.
2. Fill the judged page: description, screenshots, the gameplay video, the thumbnail, the AI-tool disclosure and
   tool tags, the credits.
3. Submit on the contest site with the judged page link.

## 6. After submission

- **The judged page is frozen.** It is never updated, re-uploaded, or edited in a way that changes the game, until
  judging ends (spec 9.1).
- Development continues on the `development` channel and GitHub Pages. No workflow pushes to the judged page's
  channel (`Docs/superpowers/delivery.md`).
- Record the submitted version, date and links in `Docs/superpowers/port-notes.md`.
