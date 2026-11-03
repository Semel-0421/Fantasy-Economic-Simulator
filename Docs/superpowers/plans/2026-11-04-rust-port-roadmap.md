# Rust port roadmap

**Spec:** `docs/superpowers/specs/2026-11-04-rust-web-port-design.md`

The spec covers several subsystems that each deserve their own plan. Each plan below ends in working,
tested software; each is written when the one before it is done, from what that one taught.

| # | Plan | Ends with | Spec | Target week |
|---|---|---|---|---|
| 1 | Engine foundation and parity harness | Rust primitives, save container, data loading equal to C#; fixtures exported from C#; parity runner and CI | 6, 7.1, 7.4 | 1 |
| 2 | World build and turn pipeline | World, routes, markets and every component built at turn 0 byte-equal to C#; phase/order pipeline, events, commands | 6, 7.2 | 1-2 |
| 3 | Economy systems | Environment, production, pricing, households, currency systems equal to C# per system on the step fixtures | 6 | 2-3 |
| 4 | Agents and finance systems | Merchants, finance, groups, policy and the rest; every kept golden run equal for its full length | 6, 7.3 | 3-4 |
| 5 | Web shell and delivery | wasm worker, interface (7.2), UI shell, map, trading screens, PWA, CI deploy to a dev page and `butler` release; turn budget measured in the itch.io embed | 7.2, 7.3, 9 | 2-4 (in parallel with 3-4, against the interface) |
| 6 | Scenarios and presentation | Scenario mode and its rules, scenarios 1-3, preview equality, growth presentation, results statement, free mode | 5 | 4-6 |
| 7 | Assets, polish, submission | Asset log, art and audio pass, English text, gameplay video and thumbnail, judged release on 2026-12-24 | 3, 8, 9.1 | 6-7 |

Plan 5 starts once Plan 2 has fixed the interface types it needs; it uses a stub that returns recorded
turns until the real systems land.

Each plan gets a Korean `.ko.md` copy for the user's review.
