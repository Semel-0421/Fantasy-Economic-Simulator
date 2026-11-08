# Delivery: web build, GitHub Pages, itch.io

## What the workflows do

- `.github/workflows/web.yml` runs on every push to `rust-port`. Order: the engine job (`engine.yml`, reused
  through `workflow_call`; pushes to `rust-port` no longer start it twice) -> build (wasm toolchain, `npm ci`,
  lint, typecheck, tests including the Playwright browser tests, `npm run build`, Playwright smoke against
  `vite preview`) -> deploy `web/dist` to GitHub Pages.
- `.github/workflows/release.yml` runs on a tag `v*`. Order: engine job -> bench regression check ->
  build (same steps as above) -> zip attached to a GitHub release -> `butler push` to the itch.io
  `development` channel.
- Shared steps live in `.github/actions/web-build`. Pinned: Rust 1.99.1, wasm-bindgen-cli 0.2.100,
  Node 22, binaryen 123 (npm devDependency, run by `web/scripts/prepare.mjs`), Playwright 1.53.0.
- CI browsers: Playwright's bundled Chromium (`BROWSER_CHANNEL=chromium`). Locally the default is the
  installed Edge (`msedge`).

## One-time setup (the repository owner does these)

1. GitHub Pages: Settings -> Pages -> Source: **GitHub Actions**. Then Settings -> Secrets and variables ->
   Actions -> Variables: add `PAGES_ENABLED` = `true`. (The deploy job is skipped until this variable exists, so
   pushes stay green before Pages is on.)
2. itch.io push (optional, skipped without both values):
   - Create an API key at itch.io -> Settings -> API keys. Add it as the repository **secret** `BUTLER_API_KEY`.
     Never paste the key anywhere else.
   - Add the repository **variable** `ITCH_TARGET` = `your-itch-user/your-game-slug`.
3. After the first Pages deploy, open the Pages URL, play a turn, and record the bench numbers
   (`cd web/bench && npm ci && npm run wasm && npm run bench`) in the port notes.

## How to release

```
git tag v0.1.0
git push origin v0.1.0
```

The tag run must pass the engine job and the bench check before anything is built or published. The zip
(`fantasy-economic-simulator-web-<tag>.zip`) is attached to the GitHub release; butler pushes the same
`web/dist` to the `development` channel with the tag as the version.

## Bench regression check

`web/bench/ceiling.json` stores `worstMs`: the slowest of turns 0-53 may not exceed it, and the world hash after
turn 11 must still match C#. The ceiling is 3000 ms. CI runners are slower and noisier than the development
machine (worst turn well under one second there), so the ceiling catches only a real regression, not noise.
Override once with `BENCH_WORST_CEILING_MS`; change the stored value in a commit if the engine legitimately
gets slower.

## itch.io page settings

- Kind of project: **HTML**. Upload is the `web/dist` content (butler does this) or the release zip.
- Embed options: viewport 1280 x 800 (or "Click to launch in fullscreen"), enable the fullscreen button.
  Mobile friendly: on.
- **SharedArrayBuffer support: off.** The game does not use threads or `SharedArrayBuffer`.
- The build uses relative URLs, so it runs from the itch.io iframe path. The service worker caches the build
  for offline play inside the page's own scope (one cache name per build, old ones are deleted on update).
- Channels: the pipeline pushes only to `development`.

## The judged page is updated by hand only

The page that is judged (the public itch.io page and the Pages URL shown to judges) must never change by
accident. Automation touches the `development` channel and the GitHub release only. Promoting a build to the
judged page is a manual step (itch.io dashboard: set the channel live, or upload by hand) done by the owner
after checking the development build. Do not add a workflow that pushes to the default channel.

## PWA notes

- `web/public/manifest.webmanifest` and icons in `web/public/icons` (regenerate with `node web/scripts/make-icons.mjs`).
- `web/vite.config.ts` writes `dist/sw.js` after each build from `web/src/sw.template.js`: it precaches every
  built file (wasm, worker, data CSVs, assets) under the cache name `fes-<hash of contents>`.
- `web/tests/e2e/smoke.test.ts` checks the manifest and plays a turn offline after one visit.
