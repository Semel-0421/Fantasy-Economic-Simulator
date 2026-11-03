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
- [Confirmed] Necessities follow a constant-elasticity curve: each quintile buys in proportion to (price / anchor) to the power of minus its elasticity, nothing from five times the anchor (DEC-126). Luxuries keep the straight line. The top quintile pays for the lower quintiles' necessities up to a share of its holdings, buys more luxuries the more it holds, and luxury purchases pay a tax to the state (DEC-127).
- [Confirmed] Ordinary market goods are necessities or luxuries, with different price elasticity (DEC-045).
- [Confirmed] Market demand includes weights that follow from that class (DEC-045).
- [Confirmed] Each good is classed individually; higher grades lean toward luxury (DEC-048). A semi-necessity such as ale is closer to a luxury from mid grade up (DEC-091).
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
  supply (DEC-061). Built in P13a and P13b.
- [Draft] Default population per level, quintile shares and yearly change rates start as placeholder
  config values.
- [Confirmed] A necessity counts toward population change if its market offered it (supply or stock) in the
  last 52 weeks (`households.need_memory_weeks`, [Draft] value): a good that stops arriving is a famine,
  one never sold there is not (DEC-067).
- [Confirmed] Continents, nations and regions hold no market; their people buy at their core towns (DEC-104, which replaces DEC-068).
- [Draft] P13d: income per head starts at 0.3 / 0.3 / 0.3 / 0.5 / 2.0 coins a week per continent /
  nation / region / city / village (`households.<level>.income_permille`; was 4 / 4 / 3 / 3 / 2 coins as
  `households.<level>.income`), near what households measurably earn in the sample world, so luxury
  demand starts where measured income keeps it.
- [Draft] Elasticity values per class start as placeholder config values.
- [Draft] Claude's proposals, built in P10:
  - `goods.csv` marks each base good `necessity` (default) or `luxury`. A necessity's grades are bought
    like a luxury by a share: Low 0%, Mid 0%, High 50%, Best 100% (`demand.<grade>.luxury_share_permille`).
  - Demand curve, linear around the anchor price (the price the market is pulled toward): quantity =
    1 - elasticity x (price / anchor - 1), never below 0 and at most twice the anchor quantity when cheap
    (`demand.max_cheap_permille`). It reaches nothing at the choke price, so a good nobody supplies stops
    rising there instead of running away.
  - Elasticity: necessities 0.6 on average, from staple food studies (DEC-112), luxuries 1.5 (`demand.necessity_elasticity_permille`,
    `demand.luxury_elasticity_permille`).
  - Quintile income shares, poorest first: 2 / 3 / 5 / 10 / 80% (`households.q1..q5.income_share_permille`).
    For necessities each quintile reacts on its own, its elasticity scaled by the square root of the
    average share (20%) over its own, then against the mean of the five, so the poor give up necessities first
    (Q1 about x1.6, Q5 about x0.26) while demand as a whole moves by the stated elasticity. Demand is the average over the quintiles. Luxuries have a single curve and scale with total income.
  - A market's demand at the anchor is the old base demand times its level weight, times population over
    the level's default for the necessity part, and total income over the default for the luxury part.
  - Population starts at 20,000 / 10,000 / 4,000 / 2,000 / 500 per continent / nation / region / city /
    village node, or the map's `population`; income per head at 4 / 4 / 3 / 3 / 2 coins a week
    (`households.<level>.population`, `households.<level>.income`).
  - Yearly, after the statistics: population changes by 0.5% plus 5% of how far the year's necessity
    demand stood above or below the need at anchor prices, within +-3% (`households.base_growth_permille`,
    `growth_response_permille`, `max_change_permille`).
  - Income per head (P13c, replacing the P10 rule that followed the nation's real money growth within
    +-5%): every node's income per head moves half the way to what its households measurably received over
    the year per head and week (cash at the year's end, less cash at its start, plus what they spent),
    by at most 50% of the larger of the two (`households.income_response_permille`,
    `income_max_change_permille`, 500 and 500; the cap was 50 before). Income is never below zero.
  - In the sample world over ten years, prices now stay within about 6 to 14 times their base price
    (the price index reaches 1.8); before P10 some reached the integer limit within three years.
- [Draft] Claude's proposals, built in P13b (households pay):
  - A household budget: each week a node's households spend at most their cash over 13 weeks
    (`households.budget_weeks`). Their wished consumption is scaled by one share so that last week's
    wished purchases the markets could have delivered (supply and stock left after other demand) fit
    the budget; goods nobody sells do not count against it. The share is the same for every good and
    quintile, and scales the necessity units met that drive population change.
  - Households pay a market's price for the units it actually delivers to them, when it reprices: every
    other unit of demand is a purchase already taken from the stock, so they get what supply and stock
    leave. They never pay more than they hold. The payment goes to the traders' float of their nation.
  - Payout caps: facilities, merchants, the player and the bank sell only as many units as the float where
    they stand can pay for. NPC facilities keep no stock, so their unsold output is lost, like a player
    warehouse's unsold overflow; merchants and the player keep theirs.
  - Aggregate merchant flows between nations: the destination's traders pay the origin's for the units at
    the origin price, and move no more than their float pays for.
  - Wages go to the households of a node: facility, merchant and road upkeep, trip fees, passage and
    guards, road building and ships.

## Unity implementation notes
- `HouseholdState` (persistent, economy module): population and income per head per node, and the year's
  necessity units wanted and met. `HouseholdDemand.Weekly` turns a cell's base demand into household
  consumption at its price.
- `economy.consumption` is gone. Markets that reprice every turn (cities and villages by default, about
  three quarters of the cells) consume inside `PriceUpdateJob`; slower markets get a per-turn
  `ConsumptionJob` pass from `PricingSystem`, so a short demand event still reaches them. Pricing takes
  about 9 ms of an 18 ms full-scale turn (consumption and pricing took 5.4 ms before), mostly one more
  division per cell.
- `HouseholdCash` (persistent, economy module, P13b) holds each node's cash, what it paid this turn, last
  week's deliverable wish and each cell's household units awaiting payment. `PricingSystem` computes the
  budget shares; the jobs scale consumption and settle payments per market (each market writes only its
  own node, so the parallel job is safe); markets that reprice every turn settle the same turn without
  the pending array. `HouseholdSpendingSystem` (agents module, Markets phase, Late + 1) hands the turn's
  payments to the floats. At full scale pricing takes about 11.3 to 12.0 ms (8.7 before; about 1 ms is
  the budget and payment bookkeeping) while merchants fall from 7.4 to about 4.8 ms, since capped floats
  stop sales; a turn takes 18.4 to 19.3 ms (18.0 before).
- `HouseholdGrowthSystem` (agents module, Finance phase after the statistics) applies the yearly change
  and starts the next year's income measure (`HouseholdCash` version 2 adds the year's spending and the
  cash at its start; a P13b save starts its year at the saved cash). The view exposes population and
  income per head.
- Burst jobs must use `Fx.OneRaw`, not `Fx.One`, inside helpers they call: the static field failed with a
  null reference in Burst.

## Open questions
- Q-036 (resolved by DEC-062, built in P13d): the price index follows the published money total.
Q-030 (deferred)

## Labor market and income tax
See SYS-STATE-ECONOMY (DEC-070, DEC-071). Measured income per head is after the income tax.

## Population and necessity supply
- [Confirmed] Population growth follows the necessities actually there to buy: what households could afford, capped by
  what the market holds (DEC-072), scaled on Victorian Europe (DEC-074).
- [Draft] 1% a year at full supply (`households.base_growth_permille`), falling 2% per full shortfall (`households.growth_response_permille`) (DEC-112, DEC-118), at most 1.3% (England and
  Wales 1851-1901, checked) and at least -4% (Ireland's famine, from memory, not checked).
- [Confirmed] Continental goods are never necessities; they may be far dearer where they are scarce (DEC-103).

## Asset markets (DEC-131)
- [Confirmed] A stock exchange per nation and a property market per node: shares and land trade at their fundamental
  value times the market's premium, which households' margin credit, new idle money and the last quarter's change
  move, and which is pulled back toward one.
- [Confirmed] Each quarter a market publishes its price over the fundamental, leverage, credit growth and its bank's
  cover; it heats and bursts on published lines, never by chance.
- [Confirmed] Trading ventures at ports with a route across the sea buy a far port's good and sell it at home, and pay
  their profit by share; their shares trade on a continental exchange per continent, the third kind of asset market
  (DEC-132).
