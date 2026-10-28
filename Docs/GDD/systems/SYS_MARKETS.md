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
- [Confirmed] Households hold cash per node and pay for their consumption, split by the quintile shares;
  demand follows their budget (DEC-061).
- [Confirmed] Markets pay sellers only from traders' floats; when a float runs short, payouts are capped
  (DEC-061).
- [Confirmed] At the start, household cash makes each continent's money total equal its starting coin
  supply (DEC-061). Built in P13a with one purse per nation; household cash per node comes with P13b.
- [Draft] Default population per level, quintile shares and yearly change rates start as placeholder
  config values.
- [Draft] Elasticity values per class start as placeholder config values.
- [Draft] Claude's proposals, built in P10:
  - `goods.csv` marks each base good `necessity` (default) or `luxury`. A necessity's grades are bought
    like a luxury by a share: Low 0%, Mid 0%, High 50%, Best 100% (`demand.<grade>.luxury_share_permille`).
  - Demand curve, linear around the anchor price (the price the market is pulled toward): quantity =
    1 - elasticity x (price / anchor - 1), never below 0 and at most twice the anchor quantity when cheap
    (`demand.max_cheap_permille`). It reaches nothing at the choke price, so a good nobody supplies stops
    rising there instead of running away.
  - Elasticity: necessities 0.5, luxuries 1.5 (`demand.necessity_elasticity_permille`,
    `demand.luxury_elasticity_permille`).
  - Quintile income shares, poorest first: 2 / 3 / 5 / 10 / 80% (`households.q1..q5.income_share_permille`).
    For necessities each quintile reacts on its own, its elasticity scaled by the square root of the
    average share (20%) over its own, so the poor give up necessities first (Q1 x3.2, Q5 x0.5); demand is
    the average over the quintiles. Luxuries have a single curve and scale with total income.
  - A market's demand at the anchor is the old base demand times its level weight, times population over
    the level's default for the necessity part, and total income over the default for the luxury part.
  - Population starts at 20,000 / 10,000 / 4,000 / 2,000 / 500 per continent / nation / region / city /
    village node, or the map's `population`; income per head at 4 / 4 / 3 / 3 / 2 coins a week
    (`households.<level>.population`, `households.<level>.income`).
  - Yearly, after the statistics: population changes by 0.5% plus 5% of how far the year's necessity
    demand stood above or below the need at anchor prices, within +-3%; income per head in a nation
    changes by half the nation's real money growth (money total over a year before, divided by the price
    index's change), within +-5% (`households.base_growth_permille`, `growth_response_permille`,
    `max_change_permille`, `income_response_permille`, `income_max_change_permille`).
  - In the sample world over ten years, prices now stay within about 6 to 14 times their base price
    (the price index reaches 1.8); before P10 some reached the integer limit within three years.

## Unity implementation notes
- `HouseholdState` (persistent, economy module): population and income per head per node, and the year's
  necessity units wanted and met. `HouseholdDemand.Weekly` turns a cell's base demand into household
  consumption at its price.
- `economy.consumption` is gone. Markets that reprice every turn (cities and villages by default, about
  three quarters of the cells) consume inside `PriceUpdateJob`; slower markets get a per-turn
  `ConsumptionJob` pass from `PricingSystem`, so a short demand event still reaches them. Pricing takes
  about 9 ms of an 18 ms full-scale turn (consumption and pricing took 5.4 ms before), mostly one more
  division per cell.
- `HouseholdGrowthSystem` (agents module, Finance phase after the statistics) applies the yearly change,
  since it reads the nation records. The view exposes population and income per head.
- Burst jobs must use `Fx.OneRaw`, not `Fx.One`, inside helpers they call: the static field failed with a
  null reference in Burst.

## Open questions
- Q-036: P10 stopped the price runaway, but markets still pay sellers whatever households can afford, so
  money totals keep growing; the price index stays on the minted coin supply for now.
Q-030 (deferred)
