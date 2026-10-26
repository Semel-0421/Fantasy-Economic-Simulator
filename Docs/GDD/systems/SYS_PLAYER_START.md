---
doc: SYS-PLAYER-START
title: Player start
status: draft
updated: 2026-10-27
related: [SYS-BANKING, SYS-INVENTORY, DEC-040]
---

# Player start

## Purpose
The opening shapes the first goal: a peddler with no bank access builds a base before credit is possible.

## Rules
- [Confirmed] The player always starts in a randomly chosen village (DEC-040).
- [Confirmed] Villages have no bank, so the player cannot borrow until reaching a city with assets that can
  serve as collateral (DEC-040, DEC-032, DEC-038).
- [Draft] Starting cash stays the config value `player.start_cash`; the starting cart is empty.

## Unity implementation notes
- The village is picked with `DetRandom` from the world seed, so the same seed always starts in the same
  village and saves stay deterministic.
- The existing `player.start_node` config key (used by the sample data and tests) becomes a test/debug
  override; shipped data leaves it empty ([Draft], Claude's proposal).
