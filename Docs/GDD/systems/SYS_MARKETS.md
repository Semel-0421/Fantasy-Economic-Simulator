---
doc: SYS-MARKETS
title: Market demand and elasticity
status: draft
updated: 2026-10-27
related: [SYS-PRODUCTION, SYS-BANKING, DEC-045]
---

# Market demand and elasticity

## Purpose
Prices should react the way people buy: bread sells in a crisis, silk does not.

## Rules
- [Confirmed] Ordinary market goods are necessities or luxuries, with different price elasticity (DEC-045).
- [Confirmed] Market demand includes weights that follow from that class (DEC-045).
- [Confirmed] Each good is classed individually; higher grades lean toward luxury (DEC-048).
- [Confirmed] Demand weights follow node size, local wealth (money totals) and population (DEC-048).
- [Confirmed] A price rise lowers quantity demanded, more strongly for luxuries (DEC-048).
- [Confirmed] Household income follows a Pareto split: the top 20% move 80% of market money (DEC-049).
- [Confirmed] A basic price index (Big Mac style) will be added later and affects household purchasing
  power (DEC-049); its basket is deferred (Q-030).
- [Confirmed] Every node has five income quintiles; the Pareto split sets their shares (DEC-049, DEC-050).
- [Confirmed] Population starts from a default per node size, adjustable per node in map data (DEC-050).
- [Confirmed] Luxuries are bought in proportion to income, with no class split (DEC-050).
- [Confirmed] Population and income change yearly with economic conditions (DEC-050).
- [Draft] Default population per level, quintile shares and yearly change rates start as placeholder
  config values.
- [Draft] Elasticity values per class start as placeholder config values.

## Unity implementation notes
- Households: a `HouseholdTable` of (node, quintile) rows = 1,580 x 5 = 7,900 rows; demand per cell sums
  quintile income x elasticity inside the existing `ConsumptionJob`. `map_nodes.csv` gains an optional
  `population` column (Claude-chosen name, [Draft]).
- Built today: `ConsumptionJob` adds base demand per level weight; `PriceKernel` reprices from
  supply/demand pressure. Elasticity fits as a per-good factor on consumed quantity versus price/anchor,
  copied into a native array like `GoodBaseDemand`.

## Open questions
Q-030 (deferred)
