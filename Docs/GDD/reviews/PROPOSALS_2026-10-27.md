---
doc: PROPOSALS-2026-10-27
title: Decisions needed after the design review
date: 2026-10-27
status: draft
related: [REVIEW-2026-10-27, Q-036, SYS-MARKETS, SYS-BANKING, SYS-MERCHANTS, SYS-PRODUCTION, SYS-INTEREST-GROUPS]
---

# Decisions needed after the design review

Five open decisions from [REVIEW_2026-10-27](REVIEW_2026-10-27.md), each with the facts in the code
today, the options, Claude's recommendation and its expected effects. Nothing here is decided; the
user's answers become DEC entries. Item 1 (the money loop) was analysed separately by an agent, with
measurements of the sample world.

## 1. The money loop (extends Q-036)

Analysed by an agent with a temporary probe that diffed every cash holder each turn over six years of
the sample world (2 continents; per continent 76 facilities, 230 merchant groups, 364 market cells; the
player idle). Figures are per continent per year; "experiment" marks emulations run inside the probe,
which was deleted afterwards.

### Facts (measured)
- Money from nothing: market payouts to any seller (facilities, warehouse overflow, merchants, the
  player), auctions with no bid (the bank receives the reserve price, nobody pays) and cash resets after
  bankruptcy or seizure, deposit interest (the reserve is not debited), insurance refunds and ship
  resale. Minting is the only intended source.
- Money into nothing: facility input purchases and upkeep, merchant purchases, fees, tolls and upkeep,
  the player's purchases, fees, tolls, licenses, ship, road building and upkeep, robbery, bank-run drains.
- Already conserved: guild fees, lobbying, loans, deposits, exchange fees, facility cash moves, bids.
- Aggregate merchant flows move goods worth 0.39 to 0.48M a year with no money at all.

| Flow (average of years 2 to 6) | Coins a year |
|---|---|
| Facility output sales | +1,026k |
| Facility input purchases | -99k |
| Facility upkeep | -239k |
| Production net | +688k |
| Minting into reserves | +460k (falling) |
| Auctions with no bid, cash resets | +315k |
| Merchants, net | +21k |
| Household consumption at market prices (estimate) | 0.98 to 1.21M |
| Household income parameter (population x income per head) | 9.25M |

- Money total: 1.46M at the start, 9.24M after six years (x6.3); coin supply 5.0M to 7.3M.
- Growth comes from production profit (about 50%), minting (33%) and auction windfalls (23%).
- Smithies lose 4.6k a year and default 1.45 times a year each; they cause most auctions (27 to 52 NPC
  loan defaults a year). Wheat farms gain 37.9k a year each.
- Households spend about 11% of their parameter income, so a budget cap on today's numbers never binds.

### Options
| Option | Rules | Decisions | Cost (full scale, estimate) | Risks |
|---|---|---|---|---|
| A. Full circular flow (recommended, via D) | Each node's households hold cash; upkeep is paid to them as wages; profits above a buffer go to the owning group as dividends; a trader float per nation pays sellers and receives household spending and purchases, and caps payouts when short; tolls and licenses go to the nation's reserve, trip fees, passage and guards are wages at the origin; auction buyers and new operators pay; bank-run withdrawals go to households; the state spends reserves above a target; demand is scaled by the household budget | Satisfies DEC-042, DEC-043, DEC-049, DEC-050, DEC-051 | +1 to 1.5 ms a turn; new household cash and float sections with migrations | Deflation if household cash starts small; tuning |
| B. Route sinks, cap payouts | Sinks go to reserves; weekly payouts per nation capped at population x income, no household cash | Strains DEC-042 | +0.3 ms | Never binds at today's income |
| C. Open loop, index from totals | Price index = published total over the start, damped | DEC-042 in letter only | ~0 | Runaway inflation (experiment) |
| D. Nation purse | A's ledger with one purse per nation instead of per-node households | DEC-042 yes; DEC-050 income stays a parameter | +0.5 ms | Weaker local signals |

Experiments:
- C: index from totals reached 5.9 by year 6 with prices 19x base (the index raises anchors, anchors raise
  revenue, revenue raises totals); a square-root version still reached 2.26 and kept rising.
- A4 (purse seeded at coin supply minus money total, dividends of 10% a week above 26 weeks of costs,
  payout cap, the state spending 2% a week of reserves above twice the start): the money total equals
  the coin supply (5.0M to 7.26M, +5 to 10% a year); an index from totals runs 1.00 to 1.33 in year 6
  (against 1.46 on coin supply); prices barely change (4.3x against 4.6x base in year 6); facility cash
  stays flat at 0.22 to 0.25M; dividends 0.5 to 0.8M a year; reserves 1.3 to 1.4M instead of 3.6M; the
  budget never binds.
- A1 (no state spending): reserves hoard 3.55M and the purse drains 150 to 280k a year.
- A5 (purse seeded at 250k): consumption -65 to -82% and prices -34% in year 2, recovering by years 5
  and 6.
- A3 (no dividends): the purse goes negative in year 2 and demand collapses.

### Recommendation and expected effects
A, built through D first (nation purses, then per-node households).
- Money totals grow only by minting (+5 to 10% a year instead of about +1.5M); seeded so that money
  total equals coin supply, the two coincide, and Q-036 can resolve to its option B (the index from
  published totals), running about 1.33 by year 6.
- Prices stay set by scarcity as long as household cash covers 26 to 52 weeks of consumption (0.5 to
  1M per sample continent); with less, consumption and prices fall for two to three years.
- Facility cash stops piling up; defaults should fall once auction buyers must pay, though smithies
  still fail until their recipe is retuned. NPC loan counts stay the same; reserves grow about 60% more
  slowly. The player's income is unchanged while floats are healthy; the player's fees recirculate.
- To retune: income per head (derived income is about 1.4M a year against the 9.25M parameter; derive it
  or rescale to about 0.5 coin a week), facility and merchant upkeep and starting cash, smithy and bakery
  recipes, auction reserve share and facility values, the initial reserve share against the coin supply,
  the minting rate, the bank-run drain; new keys for budget weeks, the dividend buffer and rate, and the
  state spending rule.

### Plan
1. P13a conservation: an invariant test (change in a continent's money = minted, every turn); fix every
   source from nothing; route every sink to a receiver; nation purses; seed total = coin supply.
2. P13b spending: households pay for consumption, demand scaled by budget; floats cap payouts;
   aggregate flows transfer value between floats.
3. P13c: dividends and group payouts, the state spending rule, income per head derived yearly from
   measured income (DEC-050).
4. P13d: the price index from published totals; retune with a ten-year run; keep the extra turn cost
   within 1.5 ms.

### Questions (recommended answer)
1. Upkeep is paid as wages to households of the facility's node. (yes)
2. Tolls and licenses go to the nation's reserve; trip fees, passage and guards are wages at the origin;
   insurance goes to the guild; ship purchases go to households at the port. (yes)
3. Profits above 26 weeks of costs are paid out at 10% a week to the owning group, which passes about 80%
   to its home node's top quintile. (yes)
4. Markets pay sellers only from trader floats, never coins from nothing. (yes)
5. The state spends reserves above a target as public wages. (yes; otherwise reserves hoard the minted
   coins)
6. Household cash per node, spending split by quintile shares. (per node)
7. Household cash seeded so that each continent's total equals its starting coin supply. (yes)
8. Q-036: the price index from published totals once P13b is built. (yes, Q-036 option B)
9. NPC auction buyers and new operators pay from their group's cash. (yes)
10. Bank-run withdrawals go to the nation's households. (yes)

Item 4 (NPC growth) depends on question 3: dividends give groups the cash they invest.

## 2. The game's goal, core loop, progression and failure

### Facts
- The pitch: a peddler in one kingdom grows into a five-continent trading power while lower tiers
  automate. The player starts in a random village with starting cash and no bank (DEC-040).
- Nothing ends the game, measures progress, or defines losing. Bankruptcy exists only for facilities
  (DEC-035) and seizure stops at "debt left after all assets stays owed" (SYS-BANKING).
- The architecture forbids hard gates; `GuidanceState` collects direction signals that nothing reads.
- Turn cost (about 18 ms at full scale) allows long games: 40 years are 2,080 turns, about 40 s of
  simulation in total.

### Options
| Option | Rules | Fits | Costs and risks |
|---|---|---|---|
| A. Open sandbox with ranks (recommended) | No end. Seven ranks by measured standing: Peddler, Trader, Merchant house (first facility or warehouse), Company (founded, several sites), National power (a share of a nation's policy influence), Continental house (ranks in most nations of a continent), World house (all five continents). Ranks unlock nothing; they set goals, news and guidance. | The pitch; "no hard gates" | One yearly rank system and a net-worth figure (the balance sheet exists). Risk: aimless play; ranks and guidance answer it |
| B. Scenarios with win conditions | Fixed goals and a deadline (e.g. net worth X in 30 years, or influence in all continents), a final score | Replayable with seeds | Thresholds depend on balance, which the open money loop still moves; conflicts with an endless sandbox |
| C. Dynasty | The trader ages; the game ends at death (about 40 years) with a legacy score, or an heir continues with part of the estate | Strong arc | A character system, inheritance rules |

Failure, for any option:
| Failure option | Rules |
|---|---|
| F1. Ruin and restart (recommended) | When debt exceeds everything the player owns and the player has no coins for a set time, the player is ruined: assets go to the creditors, the player starts again as a peddler where they are, with a small purse; ranks reset. No game over. |
| F2. Game over | Ruin ends the game. |
| F3. No failure | Debt just stays owed (today). |

### Recommendation and expected effects
A with F1, and B later as an optional challenge mode once balance is stable.
- Gives every system a purpose on screen: banks and licenses serve the Trader rank, facilities the
  Merchant house, interest groups and lobbying the National power, exchange and ships the Continental
  and World ranks.
- Code: a yearly `RankSystem` reading the balance sheet, influence (already computed by the policy
  review) and presence per continent; one event per rank change; a ruin check next to loan seizure.
  Cost negligible (yearly, the policy review already gathers holdings).
- Balance dependency: rank thresholds in net worth should wait for the money loop; thresholds in reach
  (sites, nations, continents) do not.

### Questions
- Goal style: A, B or C? (recommended A)
- Failure: F1, F2 or F3? (recommended F1)
- Rank measure: net worth, reach (sites and continents), influence, or a mix? (recommended a mix: reach
  and influence first, net worth later)

## 3. Automation as the business grows

### Facts
- Facilities have a Manual/Automated mode (`SetFacilityControlCommand`), but production ignores it:
  player-owned output always goes to the site's warehouse (DEC-005), overflow is sold.
- So every unit the player's facilities make must be carried and sold by the player's own cart. With
  several facilities the player spends the game hauling; this is where the pitch's automation is
  missing.
- Player-owned merchant groups are allowed by `AgentTable` (owner Player), but there is no command to
  hire one, and the merchant behavior skips groups that are not Automated.
- Facility levels never change (all level 1) and the player cannot build facilities, only buy them.

### Options
| Option | Rules | Costs and risks |
|---|---|---|
| A. Delegate per asset (recommended) | (1) A facility set to Automated gets a manager: output is sold at the market like an NPC's, inputs bought from its cash, for a manager's wage (e.g. +20% upkeep). Manual keeps today's warehouse rule. (2) `HireMerchantCommand` at a city or above: the player pays a group's starting stake and a weekly wage; the group runs the existing arbitrage behavior with the company's cash, or a standing order ("haul facility X's warehouse to the best market", "buy G at A, sell at B"). (3) The company treasury (group cash, which exists) pays wages and receives profits; the player moves money in and out. | Small: the mode flag exists, the behavior exists. Hired groups make their nodes detailed (LOD), about 17 µs per detailed group. Risk: automation must earn less per unit than hands-on play early, so manual trading still matters; the wage carries that. |
| B. Regional agencies | Once the company holds sites in a region, an agency there trades for it as an aggregate flow: income from capital and price gaps, no single groups | Cheap at any scale; abstract, less to watch. Fits continents far from the player |
| C. Contracts with NPC groups | Supply or purchase contracts with guilds and companies (quantity per week at a price); NPCs do the logistics | Rich diplomacy; contract enforcement and pricing rules are a large design |

### Recommendation and expected effects
A now, B later for distant continents.
- The player's attention moves from hauling to choosing what to delegate, which is the pitch's
  "automate lower tiers".
- Company cash becomes the hub of the business: needed anyway for ranks, lobbying and investment.
- Money: wages are a new flow; under the money loop recommendation they would go to households.
- Cost: dozens of hired groups add well under 1 ms a turn.

### Questions
- Delegation scope: managers for facilities, hired merchant groups, or both? (recommended both)
- Treasury: one company treasury for delegated assets, or each asset keeps its own cash as today?
  (recommended a company treasury)
- Hired merchants: free arbitrage, standing orders, or both? (recommended both, arbitrage first)
- Manager and merchant wages: a share of upkeep ([Draft] +20%) or a fixed amount?

## 4. Growth of the NPC economy

### Facts
- 8,500 facilities are spawned at the start; none are ever built, upgraded (all level 1) or closed;
  bankruptcy only replaces the operator or sends the facility to auction.
- Group cash only pays lobbying (5% a year); facility profits stay in facility cash, which grows without
  limit in the sample world.
- Demand moves: population and income change yearly (P10). Supply cannot follow, so shortages last
  forever and merchants are the only response.
- DEC-054 says interest groups can build roads; DEC-055 says companies run businesses across regions.

### Options
| Option | Rules | Costs and risks |
|---|---|---|
| A. Groups invest (recommended) | (1) Dividends: facility cash above 26 weeks of costs goes to the owning group (item 1, question 3: 10% a week). (2) Investment: a group with cash above a threshold starts at most one project a year: a new facility of the type with the best margin at anchor prices in its nation, or a level-up of its most profitable facility; state enterprises (nations) pave the busiest unpaved routes. (3) Exit: a facility unprofitable for two years in a row closes (row kept, marked closed). | Ties groups, roads and the money loop together; dividends stop facility cash piling up. Facility count grows: production cost grows about linearly (1.3 ms per 8,500 facilities), so cap the world total. Risk: investment booms; one project per group per year and a payback threshold limit it. |
| B. Upgrades only | No new facilities; profitable ones gain levels, unprofitable ones lose them | Cheaper; geography stays static |
| C. Demand-driven spawning | Where demand exceeds local supply for a year, a local private enterprise opens a facility, free of charge | Simple; creates capital from nothing, which the money loop work is meant to stop |

### Recommendation and expected effects
A, after the money loop decision (the loop decides how much cash groups have to invest).
- Shortages close within a few years instead of never; prices drift back toward anchors.
- Group cash grows from dividends, so lobbying (5% of cash) gains weight; policy reviews become more
  active. Retune `policy.npc_lobby_share_permille` then.
- Roads appear where trade is busy, which matters to the player's routes.
- Cost: yearly, plus the larger facility table each week.

### Questions
- Do groups invest? (recommended yes, option A)
- Dividends: decided with item 1, question 3 (recommended: above 26 weeks of costs, 10% a week)
- Who may invest in what: companies anything, guilds none, state enterprises roads and strategic goods,
  private enterprises local facilities? (recommended as written)
- Closure of unprofitable facilities? (recommended yes, after two losing years)

## 5. Cross-continent trade by NPC merchants

### Facts
- Merchant groups hold one continent's coin; both the detailed pass and the aggregate flows skip routes
  to another continent (Claude's [Draft] in P5).
- The aggregate flows move goods only, not money: they need no coins at all.
- Exchange rates follow price indices (DEC-030), and DEC-031 makes foreign coins come only from
  international banks, with a 3% fee.
- The sample world has one sea route pair between its two continents (8 turns, base fee 20).
- So today only the player trades between continents: a monopoly on the longest, most valuable routes,
  and continents' price levels never meet.

### Options
| Option | Rules | Costs and risks |
|---|---|---|
| A. Detailed groups trade across continents | Large groups (nation and continent level) may load at home, sail, sell for foreign coins and change them back at the destination's international bank, paying the exchange fee | Full fidelity; needs per-group foreign coin balances or immediate exchange; more state |
| B. Aggregate flows only (recommended) | The aggregate pass also moves goods along routes between continents, the margin computed after converting at the exchange rate and the exchange fee, at a reduced share (e.g. a quarter of the normal flow share) | Small: the flows carry no money; a few more routes in the job. Exchange rates start to matter to prices |
| C. Keep the player's monopoly | Confirm today's [Draft]: continents trade only through the player | No work; continents' prices drift apart without limit |

### Recommendation and expected effects
B, with a reduced share so the player keeps most of the margin on long routes.
- Price gaps between continents shrink toward the transport and exchange costs; the player's long
  routes stay profitable but less extreme. Exchange rates (DEC-030) start to affect what moves.
- Minting differences between continents become visible in trade, which gives the minting policy
  (DEC-058, DEC-060) consequences beyond inflation.
- Cost: negligible (a handful of extra routes in the aggregate job).

### Questions
- Who trades across continents: nobody (C), aggregate flows (B) or detailed groups too (A)?
  (recommended B)
- Share of the normal flow on those routes? ([Draft] 25%)

## Order of work if the recommendations are accepted
1. Money loop (item 1): every other balance number depends on it.
2. Automation A (item 3) and ranks with ruin (item 2): they make the player's game.
3. NPC growth A (item 4): needs item 1's cash flows.
4. Cross-continent flows B (item 5): independent, small; can go any time.
5. Write 01_Overview from the answers to item 2.
