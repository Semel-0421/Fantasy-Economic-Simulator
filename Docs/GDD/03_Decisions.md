---
doc: DECISIONS
title: Decision log and open questions
status: draft
updated: 2026-10-27
related: [SYS-MARKETS, SYS-MERCHANTS, SYS-PRODUCTION, SYS-INVENTORY, SYS-TRAVEL-COSTS, SYS-LICENSES, SYS-ROUTE-HAZARDS, SYS-BANKING, SYS-PLAYER-START, SYS-INFRASTRUCTURE, SYS-INTEREST-GROUPS, ROADMAP]
---

# Decision log and open questions

## Open questions

### Q-042 [resolved] Cross-continent trade by NPC merchants
- Registered: 2026-10-27 (Claude added, from the design review)
- Context: merchant groups hold one continent's coin and never cross continents; only the player trades
  between them.
- Options: A) large groups trade directly, exchanging coins  B) aggregate flows only  C) the player only
- Claude's recommendation: B at 25% of the normal flow share
- Impact: SYS-MERCHANTS, SYS-BANKING
- Resolved: DEC-066

### Q-041 [resolved] Growth of the NPC economy
- Registered: 2026-10-27 (Claude added, from the design review)
- Context: the 8,500 starting facilities are never built, upgraded or closed; group cash only lobbies.
- Options: A) groups invest  B) upgrades only  C) demand-driven spawning · who invests in what · closing
  unprofitable facilities
- Claude's recommendation: A; companies anything, state enterprises roads and strategic goods, private
  enterprises local facilities, guilds none; close after two losing years
- Impact: SYS-INTEREST-GROUPS, SYS-PRODUCTION, SYS-INFRASTRUCTURE
- Resolved: DEC-065

### Q-040 [resolved] Automation as the business grows
- Registered: 2026-10-27 (Claude added, from the design review)
- Context: player facility output always goes to the warehouse and must be hauled by the player; there is
  no way to hire merchant groups.
- Options: managers, hired merchant groups, regional agencies, contracts · a company treasury or cash
  per asset · arbitrage or standing orders · wages
- Claude's recommendation: managers and hired groups, agencies later; a company treasury; both modes,
  arbitrage first; wages a share of upkeep
- Impact: SYS-AUTOMATION, SYS-PRODUCTION, SYS-MERCHANTS
- Resolved: DEC-064

### Q-039 [resolved] The game's goal, progression and failure
- Registered: 2026-10-27 (Claude added, from the design review)
- Context: nothing ends the game, measures progress or defines losing.
- Options: A) sandbox with ranks  B) scenarios  C) dynasty · F1) ruin and restart  F2) game over  F3) no
  failure · rank measures
- Claude's recommendation: A, F1, reach and influence first
- Impact: OVERVIEW, SYS-PROGRESSION
- Resolved: DEC-063

### Q-038 [resolved] How the money loop closes
- Registered: 2026-10-27 (Claude added, from the design review)
- Context: money enters when markets pay any seller and through auctions nobody pays for; fees, tolls,
  upkeep, licenses and purchases vanish. Measured in the sample world: money totals x6.3 in six years
  against x1.46 coin supply (reviews/PROPOSALS_2026-10-27.md, item 1).
- Options: A) full circular flow with household cash and trader floats  B) route sinks and cap payouts
  C) keep the loop open, index from totals  D) A's ledger with one purse per nation
- Claude's recommendation: A, built through D first
- Impact: SYS-MARKETS, SYS-BANKING, SYS-PRODUCTION, SYS-TRAVEL-COSTS, SYS-MERCHANTS, SYS-INTEREST-GROUPS
- Resolved: DEC-061

### Q-037 [resolved] Status-quo bias in the policy review, and who opposes minting
- Registered: 2026-10-27 (Claude added)
- Context: with a fixed move threshold, P12b's sample world pushed every policy one way each year to its
  limit within five years; minting could only rise, since only state enterprises take a side on it
  (DEC-059).
- Options: A) the threshold rises with every step a policy stands from its start  B) A, plus a step back
  toward the start when pressure falls short  C) no bias · strength per step +50% / +100% / +200% ·
  minting: the same bias only, or other groups opposing minting once prices rise
- Claude's recommendation: A at +100%, minting under the same bias only
- Impact: SYS-INTEREST-GROUPS
- Resolved: DEC-060

### Q-036 [resolved] When measured money totals drive the price index
- Registered: 2026-10-27 (Claude added)
- Context: DEC-042 makes the continent total drive inflation. P9b measured the totals growing about
  twentyfold every half year because unmet demand drove prices without bound. P10's demand curve stopped
  that: prices now stay bounded. But markets still pay every seller at the current price, whatever
  households can afford, so facility cash and the totals still grow by roughly the starting total each
  year in the sample world (the coin supply grows under 10% a year). Fed into the price index, that growth would
  read as heavy inflation. The index stays on the minted coin supply.
- Options: A) keep the coin supply as the index's money measure; totals are published only  B) switch the
  index to the published continent total once household spending is limited by income (wages from
  facility upkeep paid to households, purchases capped by their budget)  C) a mix of both
- Claude's recommendation: B, after a phase that closes the money loop between households and markets
- Impact: SYS-BANKING, SYS-MARKETS
- Resolved: DEC-062

### Q-035 [resolved] What each kind of interest group wants
- Registered: 2026-10-27 (Claude added)
- Context: groups push policy each year (DEC-058), but nothing says which way each kind pushes.
- Open: per kind (company, guild, state enterprise, private enterprise) and per policy (tolls and
  surcharges, licensed goods and license prices, minting rate), whether it pushes up or down, or decides
  case by case from its own trade
- Claude's recommendation: guilds push for protection (higher tolls and surcharges, more licensed goods,
  dearer licenses); companies for open trade (the opposite); state enterprises for more minting; private
  enterprises follow their own trade (protection where they produce, open trade where they buy)
- Impact: SYS-INTEREST-GROUPS
- Resolved: DEC-059

### Q-034 [resolved] How interest groups influence policy
- Registered: 2026-10-27 (Claude added)
- Context: groups are political forces too (DEC-055), but nothing says what they can change or how.
- Open: which policies move (tariff and surcharge rates, which goods need licenses, license prices, road
  building, minting) · where influence comes from (assets, cash spent on lobbying, guild membership) · how
  a nation decides (strongest group wins, weighted vote, yearly review) · whether the player can lobby
- Impact: SYS-INTEREST-GROUPS, SYS-TRAVEL-COSTS, SYS-LICENSES
- Resolved: DEC-058

### Q-033 [resolved] How the player relates to interest groups
- Registered: 2026-10-27
- Context: asked in the interest group form without a recommendation; the user accepted only the
  recommended answers, so this stays open.
- Options (several can apply): A) found a company (the existing command becomes the player's company)
  B) join guilds for a fee, for their benefits  C) buy and sell company shares  D) bid when state
  enterprises are privatized
- Claude's recommendation: A and B first; C and D later
- Impact: SYS-INTEREST-GROUPS
- Resolved: DEC-056, DEC-057

### Q-032 [resolved] When a facility counts as out of cash, and what happens to its warehouse
- Registered: 2026-10-27 (Claude added)
- Context: P4 needs a trigger for bankruptcy (DEC-028, DEC-035). Facilities have no running costs yet, so
  cash never goes below zero; it only falls short of the inputs' price.
- Open: bankrupt after N consecutive weeks unable to pay for inputs that are in stock (built as a [Draft]
  with N = 4, `facility.bankrupt_weeks`), at once, or only with upkeep costs (wages) that can push cash
  below zero · whether goods in the warehouse of a lost player facility are lost with it (built as a
  [Draft]), moved to the player, or sold
- Impact: SYS-PRODUCTION, SYS-INVENTORY
- Resolved: DEC-051, DEC-052

### Q-031 [resolved] How households are modelled
- Registered: 2026-10-27 (Claude added)
- Context: household income follows a Pareto split, the top 20% moving 80% of money (DEC-049), and demand
  weights use population and local wealth (DEC-048). Nodes have no population or households yet.
- Open: households as two income classes per node (top 20% / bottom 80%) or finer brackets · where
  population comes from (map data, derived from node level) · whether luxuries are bought mostly by the
  top class
- Impact: SYS-MARKETS, map_nodes.csv
- Resolved: DEC-050

### Q-030 [open] Contents of the basic price index basket (deferred)
- Registered: 2026-10-27
- Context: a Big Mac-style basic price index fitting the setting is to be added later (DEC-049).
- Open: which goods and quantities form the basket · whether it is per nation or per continent
- Impact: SYS-MARKETS, SYS-BANKING

### Q-029 [resolved] Necessity and luxury elasticity, and demand weights
- Registered: 2026-10-27 (Claude added)
- Context: goods are classed as necessities or luxuries with different price elasticity, and market demand
  carries matching weights (DEC-045).
- Open: how goods are classed (per category or per good; are higher grades luxuries) · what the demand
  weight depends on (node level, population, local wealth or income) · whether elasticity changes demand
  quantity, price response, or both
- Impact: SYS-MARKETS, goods.csv
- Resolved: DEC-048

### Q-028 [resolved] When a randomly triggered financial event ends
- Registered: 2026-10-27 (Claude added)
- Context: events trigger at random but last "until conditions recover, at least one quarter" (DEC-044);
  with a random trigger there is no triggering condition to recover from.
- Options: A) until the bank's reserve ratio returns above a config threshold  B) a random duration with
  a one-quarter minimum  C) until quarterly defaults fall below a threshold
- Impact: SYS-BANKING
- Resolved: DEC-047

### Q-027 [resolved] Financial event triggers and effects
- Registered: 2026-10-27 (Claude added)
- Context: money totals can trigger financial events such as bank runs and credit crunches (DEC-042).
- Open: what triggers each event (reserve ratio, defaults, price index jumps) · its effects (withdrawal
  limits, frozen lending, rate spikes) · duration
- Impact: SYS-BANKING
- Resolved: DEC-044

### Q-026 [resolved] Do banks lend only from a finite reserve
- Registered: 2026-10-27
- Options: A) only from the bank's reserve, so a nation can run out of credit  B) unlimited
- Note: left unselected in the money totals form and again in the follow-up form.
- Impact: SYS-BANKING
- Resolved: DEC-046

### Q-025 [resolved] What the national and continental money totals count, and what they drive
- Registered: 2026-10-27 (Claude added)
- Context: banks belong to a nation or a continent, and money is aggregated per nation and per continent
  (DEC-041).
- Open: what a total includes (cash held by everyone, bank deposits, bank reserves, money in transit) ·
  whether banks lend from a finite reserve, so a nation can run short of credit · whether the totals feed
  anything (interest rates, the price index, events) or are statistics only
- Impact: SYS-BANKING
- Resolved: DEC-042

### Q-024 [resolved] What happens to a seized facility
- Registered: 2026-10-27 (Claude added)
- Context: after the grace period the bank seizes collateral facilities (DEC-037).
- Options: A) the bank sells it to a new NPC operator at once  B) it is auctioned, and the player may
  bid  C) the bank holds it and the former owner can buy it back
- Also open: whether the sale proceeds beyond the debt go back to the borrower.
- Impact: SYS-BANKING, SYS-PRODUCTION
- Resolved: DEC-039

### Q-023 [resolved] Collateral for borrowers without facilities
- Registered: 2026-10-27 (Claude added)
- Context: NPC merchant groups may borrow (DEC-037), but the loan limit is based on collateral facilities,
  which merchant groups and a starting peddler do not own.
- Options: A) no facility, no loan  B) cargo, ships and deposits also count as collateral
  C) a small unsecured limit from config
- Impact: SYS-BANKING, SYS-MERCHANTS
- Resolved: DEC-038

### Q-022 [resolved] Loan terms and default
- Registered: 2026-10-27 (Claude added)
- Context: banks lend, and unpaid debt brings penalties such as facility seizure (DEC-033).
- Open: who can borrow (player only, or NPC merchants and facilities too) · loan limit and collateral ·
  interest rate and term · default process (grace period, what is seized first, other penalties)
- Impact: SYS-BANKING, SYS-PRODUCTION
- Resolved: DEC-037

### Q-021 [resolved] Deposits, international bank location and exchange fee
- Registered: 2026-10-27 (Claude added)
- Context: national bank branches are linked within a nation; each continent has one international bank
  where coins are exchanged (DEC-032).
- Open: whether deposits earn interest · whether purchases can be paid from deposits or only from cash ·
  which node hosts each international bank (the continent node or a designated city) · whether exchange
  charges a fee (DEC-019 had one in config before it was superseded)
- Impact: SYS-BANKING, SYS-TRAVEL-COSTS
- Resolved: DEC-036

### Q-020 [resolved] A player-owned facility running out of cash
- Registered: 2026-10-27 (Claude added)
- Context: bankrupt facilities get a new operator (DEC-028); for a player-owned facility that means losing it.
- Options: A) same rule, the player loses the facility  B) it draws from the player's cash or bank first
  C) it idles until the player funds it
- Impact: SYS-PRODUCTION, SYS-BANKING
- Resolved: DEC-035

### Q-019 [resolved] Ships for sea and river routes
- Registered: 2026-10-27 (Claude added)
- Context: sea and river routes need a ship (DEC-025).
- Open: how the player gets one (buy, rent, pay for passage) · its capacity versus the cart · whether NPC
  merchants need one
- Impact: SYS-ROUTE-HAZARDS, SYS-INVENTORY, SYS-MERCHANTS
- Resolved: DEC-034

### Q-018 [resolved] Player facility output when its warehouse is full
- Registered: 2026-10-27 (Claude added)
- Context: player-owned output always goes to the site's warehouse (DEC-005), and capacity is limited
  (DEC-009).
- Options: A) the facility stops producing  B) the overflow is sold at the market  C) the overflow is lost
- Impact: SYS-PRODUCTION, SYS-INVENTORY
- Resolved: DEC-029

### Q-017 [resolved] Rate between the gold-weight unit and each continent's coin
- Registered: 2026-10-27 (Claude added)
- Context: the player's purse is held in a gold-weight unit (DEC-019); every trade converts it to and
  from the local coin.
- Options: A) fixed mint ratio (`currency.coins_per_gold`)  B) market rate that follows the continent's
  price index, so inflation makes a continent's coin worth less gold
- Impact: SYS-TRAVEL-COSTS, SYS-INVENTORY
- Resolved: DEC-030

### Q-016 [resolved] Facility cash flow and running out of cash
- Registered: 2026-10-27 (Claude added)
- Context: every facility holds cash (DEC-018) and must buy inputs (DEC-004).
- Open: starting cash · how NPC facilities earn (output sold at the market's current price?) · at zero
  cash: A) idle until it can buy again  B) bankrupt and replaced, like merchants (DEC-011)
- Impact: SYS-PRODUCTION
- Resolved: DEC-028

### Q-015 [resolved] Price used to value cargo for tariffs
- Registered: 2026-10-27 (Claude added)
- Context: tariffs are a share of goods value, charged on entering every node (DEC-014); the node may not
  trade the good.
- Options: A) the entered node's market price, falling back to the nearest ancestor market that trades it
  B) the good's base price (stable, ignores local scarcity)
- Impact: SYS-TRAVEL-COSTS
- Resolved: DEC-027

### Q-014 [resolved] Random encounter rules
- Registered: 2026-10-27 (Claude added)
- Open: when it is rolled (per trip or per travel turn) · what sets the danger (route terrain, region,
  lawlessness) · loss size (share of cash, share of cargo) · countermeasures (guards, escorts, safer
  routes) · whether NPC merchants face it too
- Impact: SYS-ROUTE-HAZARDS, SYS-MERCHANTS
- Resolved: DEC-026

### Q-013 [resolved] What hard terrain does to a route
- Registered: 2026-10-27 (Claude added)
- Open: how a route's terrain is recorded (sea, river, mountain...) · its effect: longer travel, higher
  cost, lower carrying capacity, a required vehicle (ship, pack animals), or impassable without one
- Impact: SYS-ROUTE-HAZARDS, SYS-TRAVEL-COSTS, map_routes.csv
- Resolved: DEC-025

### Q-012 [resolved] License rules
- Registered: 2026-10-27 (Claude added)
- Open: which goods need a license (by category, good or grade) · who issues it and where it is valid
  (node, nation, continent) · how it is obtained (price, duration) · whether it gates buying, selling or
  both · whether NPC merchants need one
- Impact: SYS-LICENSES, SYS-MERCHANTS
- Resolved: DEC-024

### Q-011 [resolved] Buying from an empty or short market stock
- Registered: 2026-10-27 (Claude added)
- Options: A) fill partially up to stock  B) reject the whole order
- Impact: SYS-INVENTORY, SYS-PRODUCTION, SYS-MERCHANTS
- Resolved: DEC-020

### Q-010 [resolved] Currency the player's cash is held in
- Registered: 2026-10-27 (Claude added)
- Open: which coin the single purse is held in and whether conversion charges a money-changer fee.
- Impact: SYS-TRAVEL-COSTS, SYS-INVENTORY
- Resolved: DEC-019

### Q-009 [resolved] Who pays for facility inputs
- Registered: 2026-10-27 (Claude added)
- Options: A) NPC facilities hold no cash  B) every facility keeps cash and earns from its output sales
- Impact: SYS-PRODUCTION
- Resolved: DEC-018

### Q-008 [resolved] Unit of the route base fee
- Registered: 2026-10-27 (Claude added)
- Options: A) once per trip  B) per travel turn
- Impact: SYS-TRAVEL-COSTS
- Resolved: DEC-017

### Q-007 [resolved] How node location is described, and what a specialty gives
- Registered: 2026-10-27 (Claude added)
- Options: A) terrain/feature tags per node  B) explicit specialty goods per node; effects: production
  bonus · always carried · higher grade · lower local price
- Impact: SYS-PRODUCTION, map data
- Resolved: DEC-016

### Q-006 [resolved] Do NPC merchants pay tariffs and travel costs
- Registered: 2026-10-27 (Claude added)
- Options: A) yes, same rules as the player  B) no
- Impact: SYS-MERCHANTS, SYS-TRAVEL-COSTS
- Resolved: DEC-015

### Q-005 [resolved] Tariff basis and shape
- Registered: 2026-10-27 (Claude added)
- Options: basis A) share of value B) coins per unit; shape A) one rate per node B) per node and category
- Impact: SYS-TRAVEL-COSTS
- Resolved: DEC-014

### Q-004 [resolved] Where tariffs are charged
- Registered: 2026-10-27 (Claude added)
- Options: A) on entering any node along the route  B) when selling into a node's market  C) only at
  jurisdiction borders
- Impact: SYS-TRAVEL-COSTS, SYS-MERCHANTS
- Resolved: DEC-014

### Q-003 [resolved] Capacity values
- Registered: 2026-10-27 (Claude added)
- Needed: cart max size and weight · merchant group capacity per level · warehouse capacity
- Impact: SYS-INVENTORY, SYS-MERCHANTS
- Resolved: DEC-013

### Q-002 [resolved] Granularity of weight and size
- Registered: 2026-10-27 (Claude added)
- Options: A) per base good  B) per grade item
- Impact: SYS-INVENTORY, goods.csv
- Resolved: DEC-012

### Q-001 [resolved] What happens when a merchant's cash runs out
- Registered: 2026-10-27
- Options: A) bankrupt, then replaced by a new merchant group  B) unlimited cash
- Impact: SYS-MERCHANTS
- Resolved: DEC-011

## Decision log

### DEC-069 Facility slots without local inputs; four smithies per city
- Date: 2026-10-31
- Decision: a facility type with inputs is drawn only where some facility makes every input in the node's
  subtree; a slot with no such type goes to a primary producer. Cities keep four smithies, and iron tools'
  base demand rises from 4 to 12 to absorb them ([Draft] value).
- Reason: smithies glutted tools and bakeries stood in villages without wheat. Measured: tripling the
  demand only cut smithy defaults from 377 to 330 in ten years (one smithy per city gave 74), since city
  households' budgets bind.
- Alternatives: leave the slot empty (recommended, rejected) · random placement (rejected) · one or two
  smithies per city (one recommended, rejected)
- Impact: [SYS-PRODUCTION](systems/SYS_PRODUCTION.md)

### DEC-068 Continent and nation markets are supplied
- Date: 2026-10-31
- Decision: households at continent and nation nodes buy like any others, so goods must reach them. Maps
  connect a region to its nation and a nation to its continent by routes.
- Reason: those nodes had no routes, so about 2M coins sat with households who could buy nothing.
- Alternatives: send wages and starting cash only to nodes that can buy (recommended, rejected) · fewer
  people at those nodes (rejected) · leave them (rejected)
- Impact: [SYS-MARKETS](systems/SYS_MARKETS.md), [SYS-MERCHANTS](systems/SYS_MERCHANTS.md)

### DEC-067 Population needs count usually traded goods
- Date: 2026-10-31
- Decision: a necessity counts toward population change if its market offered it within the last 52 weeks.
  A good that stops arriving is a famine; a good never sold there is not.
- Alternatives: only goods offered that week (rejected: a total famine would not count) · every good the
  market carries (rejected: population fell 2.7% a year)
- Impact: [SYS-MARKETS](systems/SYS_MARKETS.md)

### DEC-066 Large merchant groups trade across continents
- Date: 2026-10-27
- Decision: large NPC merchant groups also trade between continents, changing coins at international
  banks. Flows on routes between continents run at 65% of the normal flow share.
- Alternatives: aggregate flows only (recommended, rejected) · the player only (rejected) · 25% share
  (recommended, rejected)
- Impact: [SYS-MERCHANTS](systems/SYS_MERCHANTS.md), [SYS-BANKING](systems/SYS_BANKING.md)
- Resolves: Q-042

### DEC-065 Interest groups invest; losing facilities close
- Date: 2026-10-27
- Decision: interest groups invest the cash they receive (DEC-061): new facilities, level-ups and road
  paving, at most one project per group a year. Every kind of group may invest in anything. A facility
  that loses money two years in a row closes.
- Alternatives: upgrades only (rejected) · demand-driven spawning (rejected) · no growth (rejected) ·
  investment rights per kind (recommended, rejected) · no closures (rejected)
- Impact: [SYS-INTEREST-GROUPS](systems/SYS_INTEREST_GROUPS.md), [SYS-PRODUCTION](systems/SYS_PRODUCTION.md),
  [SYS-INFRASTRUCTURE](systems/SYS_INFRASTRUCTURE.md)
- Resolves: Q-041

### DEC-064 Delegation: managers and hired merchant groups, each with its own cash
- Date: 2026-10-27
- Decision: the player can put managers on facilities and hire merchant groups; regional agencies for
  distant continents come later. Each delegated asset keeps its own cash (no company treasury). Hired
  groups trade by free arbitrage and by standing orders, arbitrage first. Wages are a share of the
  asset's upkeep ([Draft] +20%).
- Alternatives: managers only (rejected) · agencies only (rejected) · contracts with NPC groups
  (rejected) · a company treasury (recommended, rejected) · one trading mode only (rejected) · fixed wages
  (rejected)
- Impact: [SYS-AUTOMATION](systems/SYS_AUTOMATION.md), [SYS-PRODUCTION](systems/SYS_PRODUCTION.md),
  [SYS-MERCHANTS](systems/SYS_MERCHANTS.md)
- Resolves: Q-040

### DEC-063 An endless sandbox with seven ranks; ruin ends the game
- Date: 2026-10-27
- Decision: the game has no victory or fixed end. Seven ranks mark the player's standing and unlock
  nothing; they are measured by reach and influence first, net worth once the economy is balanced. Ruin
  ends the game.
- Alternatives: scenarios with win conditions (rejected) · a dynasty (rejected) · ruin and restart as a
  peddler (recommended, rejected) · no failure (rejected) · net worth, reach or influence alone (rejected)
- Impact: [OVERVIEW](01_Overview.md), [SYS-PROGRESSION](systems/SYS_PROGRESSION.md)
- Resolves: Q-039

### DEC-062 The price index follows published money totals once households spend
- Date: 2026-10-27
- Decision: once households pay for their consumption (P13b), each continent's price index follows its
  published money total instead of the minted coin supply.
- Alternatives: keep the coin supply (rejected) · a mix of both (rejected)
- Impact: [SYS-BANKING](systems/SYS_BANKING.md), [SYS-MARKETS](systems/SYS_MARKETS.md)
- Resolves: Q-036

### DEC-061 A closed money loop
- Date: 2026-10-27
- Decision: money is created only by minting and moves between holders otherwise. Built in two steps: a
  purse per nation first, then cash per node.
  - Households hold cash per node and spend it on consumption split by the quintile shares; demand
    follows their budget.
  - Facility upkeep is paid as wages to the households of the facility's node.
  - Tolls and license prices go to the nation's bank reserve; trip fees, passage and guards are wages
    at the trip's origin; insurance premiums go to the guild; a ship's price goes to the households of
    the port.
  - Markets pay sellers only from traders' floats; when a float runs short, payouts are capped.
  - Facility profit above 26 weeks of costs is paid out at 10% a week to the owning group, which passes
    about 80% of it to the top quintile of its home node.
  - The state spends reserves above a target as public wages.
  - Auction buyers and new operators pay from their group's cash; withdrawals in a bank run go to the
    nation's households.
  - At the start, household cash is set so that each continent's money total equals its starting coin
    supply.
- Reason: measured in the sample world, money totals grew x6.3 in six years against x1.46 coin supply;
  an emulation of this loop kept the total equal to the coin supply with prices nearly unchanged
  (reviews/PROPOSALS_2026-10-27.md, item 1)
- Alternatives: route sinks and cap payouts only (rejected) · open loop with the index from totals
  (rejected) · nation purses only (rejected as the end state; kept as the first step) · dividends to
  groups only, or none (rejected) · market payouts from nothing (rejected) · no state spending (rejected)
  · household cash per nation (rejected) · small starting household cash (rejected)
- Impact: [SYS-MARKETS](systems/SYS_MARKETS.md), [SYS-BANKING](systems/SYS_BANKING.md),
  [SYS-PRODUCTION](systems/SYS_PRODUCTION.md), [SYS-TRAVEL-COSTS](systems/SYS_TRAVEL_COSTS.md),
  [SYS-MERCHANTS](systems/SYS_MERCHANTS.md), [SYS-INTEREST-GROUPS](systems/SYS_INTEREST_GROUPS.md)
- Resolves: Q-038

### DEC-060 Status-quo bias in the policy review; inflation turns groups against minting
- Date: 2026-10-27
- Decision: a policy needs more weight to move the further it already stands from its start: each step
  away raises the move threshold by 100% of itself. Groups other than state enterprises oppose minting
  once prices have risen over the published year (changes DEC-059, where none of them took a side).
- Alternatives: also stepping back toward the start when pressure falls short (rejected) · no bias
  (rejected) · +50% or +200% per step (rejected) · minting under the bias only (recommended, rejected)
- Impact: [SYS-INTEREST-GROUPS](systems/SYS_INTEREST_GROUPS.md)
- Resolves: Q-037

### DEC-059 Interest groups' stances
- Date: 2026-10-27
- Decision: guilds, companies and private enterprises decide tolls and licenses from their own trade: they
  push for protection where they produce and for open trade where they buy, and for licenses they hold to
  be protected and licenses they lack to be relaxed. None of them takes a side on minting. State
  enterprises push tolls up, licenses wider and dearer to protect their monopolies, and minting up.
  Stances change with each group's business: a group losing money leans toward protection.
- Alternatives: guilds always protectionist and companies always for open trade (recommended, rejected) ·
  state enterprises staying out of tolls (recommended, rejected) · stances fixed per kind (recommended,
  rejected)
- Impact: [SYS-INTEREST-GROUPS](systems/SYS_INTEREST_GROUPS.md)
- Resolves: Q-035
- Changed by: DEC-060 (groups other than state enterprises now oppose minting when prices rise)

### DEC-058 Policy influence: yearly weighted review
- Date: 2026-10-27
- Decision: interest groups can move toll and specialty surcharge rates, which goods need licenses and
  license prices, and the minting rate. Influence comes from the value of a group's assets, its lobbying
  spending, and its membership and employment. Each nation reviews policy once a year and moves each
  policy one step, weighted by influence. The player can lobby in their company's name.
- Alternatives: nation road building as a policy (rejected) · the strongest group's demand wins (rejected)
  · quarterly reviews (rejected) · lobbying only through guilds, or not at all (rejected)
- Impact: [SYS-INTEREST-GROUPS](systems/SYS_INTEREST_GROUPS.md), SYS-TRAVEL-COSTS, SYS-LICENSES,
  SYS-BANKING (minting); open: Q-035
- Resolves: Q-034

### DEC-057 Guild membership benefits
- Date: 2026-10-27
- Decision: guild members get cheaper insurance, use of the guild hall warehouse in that city, and the
  guild's recommendation for licenses in its trade, which makes them cheaper.
- Alternatives: price information from other cities (rejected)
- Impact: [SYS-INTEREST-GROUPS](systems/SYS_INTEREST_GROUPS.md), SYS-ROUTE-HAZARDS (insurance),
  SYS-INVENTORY (warehouses), SYS-LICENSES
- Resolves: Q-033

### DEC-056 How the player relates to interest groups
- Date: 2026-10-27
- Decision: the player founds a company (the existing command becomes the player's company) and can join
  guilds for a fee. Trading company shares and bidding when state enterprises are privatized come in a
  later phase.
- Impact: [SYS-INTEREST-GROUPS](systems/SYS_INTEREST_GROUPS.md)
- Resolves: Q-033

### DEC-055 Economic interest groups
- Date: 2026-10-27
- Decision: interest groups are both asset owners and political forces. Companies run facilities and
  merchant groups across regions (the player's company is one); guilds are city and trade associations
  with membership, running insurance, guild halls and license recommendations; state enterprises belong
  to nations (the state banks, monopolies on strategic goods); private enterprises are small personal or
  family businesses. At the start every NPC facility and merchant group belongs to a group.
- Reason: user requirement (memo: companies, guilds, state and private enterprises); details from the
  decision form's recommendations
- Alternatives: asset owners only (rejected) · political forces only (rejected) · some NPC businesses left
  independent (rejected)
- Impact: [SYS-INTEREST-GROUPS](systems/SYS_INTEREST_GROUPS.md); open: Q-033, Q-034

### DEC-054 Paved roads
- Date: 2026-10-27
- Decision: paving lowers a route's per-unit charge and its encounter chance. Some roads are paved in the
  map data from the start; the player and interest groups can pay to build more. A road whose upkeep is
  not paid slowly decays.
- Reason: user requirement (memo: paved roads); details from the decision form's recommendations
- Alternatives: fewer travel turns (rejected) · easing mountain penalties (rejected) · nations building
  over time (rejected) · permanent roads (rejected) · builders charging tolls (rejected)
- Impact: [SYS-INFRASTRUCTURE](systems/SYS_INFRASTRUCTURE.md), SYS-TRAVEL-COSTS, SYS-ROUTE-HAZARDS

### DEC-053 Ports
- Date: 2026-10-27
- Decision: the map data marks ports. Ships are bought and sold only at ports, water routes connect ports
  only, and a ship stays docked at the port where the player lands.
- Reason: user requirement (memo: ports); details from the decision form's recommendations
- Alternatives: ports from a coast tag (rejected) · every end of a water route (rejected) · harbour dues
  (rejected) · port market bonus (rejected)
- Previous: ships bought in any city and travelling with the player (P8 draft, replaced)
- Impact: [SYS-INFRASTRUCTURE](systems/SYS_INFRASTRUCTURE.md), [SYS-ROUTE-HAZARDS](systems/SYS_ROUTE_HAZARDS.md)

### DEC-052 A lost facility's warehouse can be emptied for a while
- Date: 2026-10-27
- Decision: when the player loses a facility to bankruptcy, they can still take the goods out of its
  warehouse for a period; whatever is left after that is gone.
- Alternatives: lost with the facility (Claude's earlier draft, rejected) · sold, proceeds to the player
  (rejected) · sold, proceeds to the new operator (rejected)
- Impact: [SYS-PRODUCTION](systems/SYS_PRODUCTION.md), [SYS-INVENTORY](systems/SYS_INVENTORY.md)
- Resolves: Q-032

### DEC-051 Upkeep, and bankruptcy after five weeks below zero
- Date: 2026-10-27
- Decision: facilities pay a weekly upkeep (wages), which can push their cash below zero. A facility whose
  cash stays below zero for 5 consecutive weeks goes bankrupt.
- Alternatives: bankrupt after N weeks unable to pay for inputs in stock (Claude's earlier draft, rejected)
  · bankrupt the first week it cannot pay (rejected) · bankrupt as soon as cash is below zero (rejected)
- Impact: [SYS-PRODUCTION](systems/SYS_PRODUCTION.md), config `facility.bankrupt_weeks`
- Resolves: Q-032

### DEC-050 Households and population
- Date: 2026-10-27
- Decision: every node has five income quintiles. Population starts from a default per node size and can be
  adjusted per node in the map data. Luxuries are bought in proportion to income, with no class split.
  Population and income change yearly with economic conditions.
- Alternatives: two classes per node (recommended, rejected) · population entered per node or from size only
  (rejected) · luxuries mostly by the top class (recommended, rejected) · fixed population (rejected)
- Impact: [SYS-MARKETS](systems/SYS_MARKETS.md), map_nodes.csv
- Resolves: Q-031

### DEC-049 Basic price index and Pareto household income
- Date: 2026-10-27
- Decision: a Big Mac-style basic price index fitting the setting will be added later; it affects
  household purchasing power. Household income follows the Pareto principle: the top 20% account for 80%
  of all money moving through the market.
- Reason: user requirement (lending and elasticity form, memo)
- Impact: [SYS-MARKETS](systems/SYS_MARKETS.md); open: Q-030 (deferred), Q-031

### DEC-048 Elasticity classes and demand weights
- Date: 2026-10-27
- Decision: each good is classed individually, and higher grades lean toward luxury. Demand weights follow
  node size (village to continent), local wealth (from money totals) and population. A price rise lowers
  the quantity demanded, more strongly for luxuries.
- Alternatives: per category (rejected) · per good ignoring grade (rejected) · only price response speed
  differs (rejected)
- Impact: [SYS-MARKETS](systems/SYS_MARKETS.md); open: Q-031
- Resolves: Q-029

### DEC-047 Random financial events end when the reserve ratio recovers
- Date: 2026-10-27
- Decision: a financial event ends once the bank's reserve ratio returns above a config threshold, after at
  least one quarter.
- Alternatives: random duration (rejected) · default rate falling (rejected)
- Impact: [SYS-BANKING](systems/SYS_BANKING.md)
- Resolves: Q-028

### DEC-046 Banks lend only from a finite reserve
- Date: 2026-10-27
- Decision: banks lend only from their reserves, so a nation can run out of credit.
- Alternatives: unlimited (rejected) · rates rise beyond the reserve but lending continues (rejected)
- Impact: [SYS-BANKING](systems/SYS_BANKING.md)
- Resolves: Q-026

### DEC-045 Price elasticity for necessities and luxuries
- Date: 2026-10-27
- Decision: ordinary market goods are necessities or luxuries with different price elasticity, and market
  demand includes weights that follow from it.
- Reason: user requirement (financial events form, memo 2)
- Impact: [SYS-MARKETS](systems/SYS_MARKETS.md); open: Q-029

### DEC-044 Financial events
- Date: 2026-10-27
- Decision: financial events trigger with a low random probability. Effects: a bank run limits deposit
  withdrawals, a credit crunch stops new loans, and loan interest rates spike. An event lasts until
  conditions recover, at least one quarter. Statistics also measure the price index and inflation.
- Alternatives: reserve ratio or default-rate triggers (recommended, rejected) · price index spike trigger
  (rejected) · early repayment demands (rejected) · fixed duration (rejected)
- Impact: [SYS-BANKING](systems/SYS_BANKING.md); open: Q-028
- Resolves: Q-027

### DEC-043 Quarterly financial records, yearly public statistics
- Date: 2026-10-27
- Decision: financial state is recorded every quarter. Statistics are published every year for the player
  to consult, and NPC agents use the same published statistics at the same time.
- Reason: user requirement (money totals form, memo)
- Impact: [SYS-BANKING](systems/SYS_BANKING.md), [SYS-MERCHANTS](systems/SYS_MERCHANTS.md)

### DEC-042 Money totals: contents, attribution, minting and effects
- Date: 2026-10-27
- Decision: a nation's money total counts cash held by the player, merchant groups and facilities (in
  transit included), bank deposits and the banks' own reserves. Cash counts toward the nation of the node
  where it is now. Yearly minted coins go to the international bank, which distributes them to national
  banks. Totals drive loan interest (low reserves raise rates), the price index (continent total drives
  inflation) and financial events such as bank runs and credit crunches.
- Alternatives: counting loan balances (rejected) · home nation attribution (rejected) · minted coins kept
  by the international bank or released to markets (rejected) · statistics only (rejected)
- Impact: [SYS-BANKING](systems/SYS_BANKING.md); open: Q-026, Q-027
- Resolves: Q-025
- Changed by: DEC-061 (holders include household cash and traders' floats; money moves instead of
  vanishing), DEC-062 (the price index follows the total once households spend)

### DEC-041 Banks are state-owned; money is aggregated per nation and per continent
- Date: 2026-10-27
- Decision: national banks belong to their nation and international banks to their continent. Money is
  aggregated as each nation's total and each continent's total.
- Reason: user requirement (loan collateral form, memo 2)
- Impact: [SYS-BANKING](systems/SYS_BANKING.md); open: Q-025

### DEC-040 The player always starts in a random village
- Date: 2026-10-27
- Decision: the player always starts in a randomly chosen village. Banks, and therefore loans, exist only
  from city level up, so the player first builds assets that can serve as collateral.
- Reason: user requirement (loan collateral form, memo 1)
- Impact: [SYS-PLAYER-START](systems/SYS_PLAYER_START.md), [SYS-BANKING](systems/SYS_BANKING.md)

### DEC-039 Seized facilities are auctioned; leftover proceeds go to the bank
- Date: 2026-10-27
- Decision: a seized facility is auctioned and the player may bid; with no winning bid it is sold to a new
  NPC operator. Proceeds left after repaying the debt go to the bank, not the former owner.
- Alternatives: immediate sale to an NPC (rejected) · bank holds it for buy-back (rejected) · leftover to the
  former owner (recommended, rejected)
- Impact: [SYS-BANKING](systems/SYS_BANKING.md), [SYS-PRODUCTION](systems/SYS_PRODUCTION.md)
- Resolves: Q-024

### DEC-038 Assets besides facilities count as collateral
- Date: 2026-10-27
- Decision: borrowers without facilities can still borrow: bank deposits, owned ships, cargo stored in
  warehouses and cargo on the cart also count as collateral.
- Alternatives: small unsecured limit only (rejected) · no facility, no loan (rejected) · licenses as
  collateral (rejected)
- Impact: [SYS-BANKING](systems/SYS_BANKING.md), [SYS-MERCHANTS](systems/SYS_MERCHANTS.md), [SYS-INVENTORY](systems/SYS_INVENTORY.md)
- Resolves: Q-023

### DEC-037 Loan rules
- Date: 2026-10-27
- Decision: the player, NPC facilities and NPC merchant groups can all borrow. The limit is based on the
  value of collateral facilities. Loans are repaid in weekly instalments; interest rate and term are config
  values. After a grace period, an unpaid loan leads to seizure of the collateral facilities.
- Alternatives: player only (recommended, rejected) · credit rating or fixed limit (rejected) · lump sum at
  maturity (rejected) · immediate seizure or penalty interest first (rejected)
- Impact: [SYS-BANKING](systems/SYS_BANKING.md), [SYS-MERCHANTS](systems/SYS_MERCHANTS.md), [SYS-PRODUCTION](systems/SYS_PRODUCTION.md); open: Q-023, Q-024
- Resolves: Q-022

### DEC-036 Deposits, international bank location and exchange fee
- Date: 2026-10-27
- Decision: deposits earn interest at a config rate. At nodes with a branch, purchases can be paid from
  deposits. Each continent's international bank sits in a city designated in the map data. Exchange charges
  a fee whose rate is a config value.
- Alternatives: no interest (rejected) · cash only (rejected) · the continent node itself (rejected) · no fee
  (rejected)
- Impact: [SYS-BANKING](systems/SYS_BANKING.md), map_nodes.csv
- Resolves: Q-021

### DEC-035 Player facilities go bankrupt like NPC facilities
- Date: 2026-10-27
- Decision: a player-owned facility whose cash runs out goes bankrupt like an NPC facility; the player loses
  it and it gets a new operator.
- Alternatives: covered from the player's cash and deposits (recommended, rejected) · idle until funded
  (rejected)
- Impact: [SYS-PRODUCTION](systems/SYS_PRODUCTION.md)
- Resolves: Q-020

### DEC-034 Ships: passage first, owned ships later
- Date: 2026-10-27
- Decision: the player crosses sea and river routes by paying for passage, carrying only the cart's load;
  later the player can buy a ship with a larger capacity. NPC merchants need no ship.
- Alternatives: purchase only or rental only (rejected) · always the cart's capacity (rejected) · NPC under
  the same rule (recommended, rejected)
- Impact: [SYS-ROUTE-HAZARDS](systems/SYS_ROUTE_HAZARDS.md), [SYS-INVENTORY](systems/SYS_INVENTORY.md), [SYS-MERCHANTS](systems/SYS_MERCHANTS.md)
- Resolves: Q-019

### DEC-033 Loans and debt, with seizure on default
- Date: 2026-10-27
- Decision: banks offer loans. Debt that is not repaid brings penalties, such as seizure of facilities.
- Reason: user requirement (second open questions form, memo)
- Impact: [SYS-BANKING](systems/SYS_BANKING.md); open: Q-022

### DEC-032 National banks and one international bank per continent
- Date: 2026-10-27
- Decision: each nation runs a bank with one branch in every node of city level and above; branches within
  a nation are linked. Each continent has one international bank; at its node, local coins are exchanged at
  the exchange rate.
- Reason: user requirement (second open questions form, memo)
- Impact: [SYS-BANKING](systems/SYS_BANKING.md); open: Q-021

### DEC-031 Cash in local coins; exchange only at international banks
- Date: 2026-10-27
- Decision: the player carries cash in continents' coins. Another continent's coins are obtained only by
  exchanging at an international bank node; trades are paid in the market's local coin, with no
  automatic conversion.
- Reason: aligns currency with the banking memo (DEC-032); chosen by the user over keeping the gold-unit
  purse or using gold units for bank accounts only.
- Previous: DEC-019 gold-weight purse with automatic conversion (superseded); DEC-008 automatic conversion
  at the trade (changed)
- Impact: [SYS-TRAVEL-COSTS](systems/SYS_TRAVEL_COSTS.md), [SYS-BANKING](systems/SYS_BANKING.md), [SYS-INVENTORY](systems/SYS_INVENTORY.md)

### DEC-030 Exchange rates follow price indices
- Date: 2026-10-27
- Decision: the rate between currencies follows each continent's price index, so an inflating continent's
  coin weakens.
- Alternatives: fixed mint ratio (rejected)
- Note: asked for the gold-weight purse; that purse was superseded (DEC-031), so the rule now applies to
  exchange between continents' coins.
- Impact: [SYS-BANKING](systems/SYS_BANKING.md)
- Resolves: Q-017

### DEC-029 Warehouse overflow is sold at the market
- Date: 2026-10-27
- Decision: when a player facility's warehouse is full, output that does not fit is sold at the market.
- Alternatives: stop production (recommended, rejected) · lose the overflow (rejected)
- Impact: [SYS-PRODUCTION](systems/SYS_PRODUCTION.md), [SYS-INVENTORY](systems/SYS_INVENTORY.md)
- Resolves: Q-018

### DEC-028 Facility cash flow and bankruptcy
- Date: 2026-10-27
- Decision: starting cash per industry tier comes from config; NPC output is sold at the market's current
  price; a facility whose cash runs out goes bankrupt and gets a new operator.
- Alternatives: idle until funded (rejected) · start with a few weeks of input value (rejected)
- Impact: [SYS-PRODUCTION](systems/SYS_PRODUCTION.md); open: Q-020
- Resolves: Q-016

### DEC-027 Tolls valued at the entered node's market price
- Date: 2026-10-27
- Decision: cargo is valued at the entered node's market price, or the nearest ancestor market that trades
  the good when the node does not.
- Alternatives: base price (rejected)
- Impact: [SYS-TRAVEL-COSTS](systems/SYS_TRAVEL_COSTS.md)
- Resolves: Q-015

### DEC-026 Encounter rules
- Date: 2026-10-27
- Decision: one encounter roll per trip. Danger depends on route terrain, regional safety and cargo
  value. A loss hits cash or cargo at random, with ratios in config. Countermeasures: hiring guards and
  merchant-guild insurance. Detailed regions roll for NPC merchants; far regions apply the expected loss.
- Alternatives: roll per travel turn (recommended, rejected) · cargo only or cash only (rejected) · no
  countermeasures (rejected) · NPC exempt (rejected)
- Impact: [SYS-ROUTE-HAZARDS](systems/SYS_ROUTE_HAZARDS.md)
- Resolves: Q-014

### DEC-025 Hard terrain: longer, costlier, ships on water
- Date: 2026-10-27
- Decision: hard terrain adds travel turns and cost; sea and river routes need a ship.
- Alternatives: terrain changing encounter risk directly (recommended, rejected; terrain still feeds danger
  through DEC-026) · reduced capacity on mountain paths (rejected)
- Impact: [SYS-ROUTE-HAZARDS](systems/SYS_ROUTE_HAZARDS.md); open: Q-019
- Resolves: Q-013

### DEC-024 License rules
- Date: 2026-10-27
- Decision: licenses are per individual good, issued by a nation and valid inside it, bought and renewed
  every year, and required for selling only. Only some NPC merchant groups hold licenses.
- Alternatives: per category (recommended, rejected) · higher grades only (rejected) · city or continent
  scope (rejected) · permanent or earned (rejected) · gating buying too (recommended, rejected) · NPC buys
  like the player or exempt (rejected)
- Impact: [SYS-LICENSES](systems/SYS_LICENSES.md)
- Resolves: Q-012

### DEC-023 Random encounters on the road
- Date: 2026-10-27
- Decision: bandits, monsters and similar dangers occur as random encounters while travelling and reduce
  the traveller's cash or cargo.
- Reason: user requirement (open questions form, memo 3)
- Impact: [SYS-ROUTE-HAZARDS](systems/SYS_ROUTE_HAZARDS.md); open: Q-014

### DEC-022 Hard terrain shapes travel
- Date: 2026-10-27
- Decision: crossing hard terrain such as sea, rivers and mountains is treated differently from ordinary
  roads.
- Reason: user requirement (open questions form, memo 2)
- Impact: [SYS-ROUTE-HAZARDS](systems/SYS_ROUTE_HAZARDS.md); open: Q-013

### DEC-021 Special goods need a license
- Date: 2026-10-27
- Decision: trading special goods requires a license.
- Reason: user requirement (open questions form, memo 1)
- Impact: [SYS-LICENSES](systems/SYS_LICENSES.md); open: Q-012

### DEC-020 Orders beyond market stock fill partially
- Date: 2026-10-27
- Decision: an order larger than the market's stock fills up to the stock; the rest is not bought.
- Alternatives: reject the whole order (rejected)
- Impact: SYS-INVENTORY, SYS-PRODUCTION, SYS-MERCHANTS
- Resolves: Q-011

### DEC-019 [superseded] Player purse in a gold-weight unit, exchange fee in config
- Date: 2026-10-27
- Decision: the player's single purse is held in a gold-weight reference unit and converted to the local
  coin for every trade. Conversion charges a money-changer fee whose rate is a config value.
- Alternatives: the starting continent's coin (rejected) · no fee (rejected)
- Impact: [SYS-TRAVEL-COSTS](systems/SYS_TRAVEL_COSTS.md)
- Resolves: Q-010
- Superseded by: DEC-031 (cash in local coins, exchange only at international banks); the exchange fee carries over to Q-021

### DEC-018 Every facility holds cash
- Date: 2026-10-27
- Decision: all facilities, NPC and player-owned, hold cash and pay for their inputs.
- Alternatives: only player facilities pay; NPC inputs leave stock for free (recommended, rejected)
- Impact: [SYS-PRODUCTION](systems/SYS_PRODUCTION.md); open: Q-016
- Resolves: Q-009

### DEC-017 Route base fee once per trip
- Date: 2026-10-27
- Decision: the base fee is paid once per trip, not per travel turn.
- Alternatives: per travel turn (rejected)
- Impact: [SYS-TRAVEL-COSTS](systems/SYS_TRAVEL_COSTS.md)
- Resolves: Q-008

### DEC-016 Specialties from terrain tags, with four effects
- Date: 2026-10-27
- Decision: nodes carry terrain tags (coast, mountain, forest...) and specialties are assigned from them
  automatically. A specialty gives: a production bonus, guaranteed presence in the node's market, a chance
  of higher-grade output, and a lower local price.
- Alternatives: specialties set per node by hand (rejected)
- Impact: [SYS-PRODUCTION](systems/SYS_PRODUCTION.md), map_nodes.csv
- Resolves: Q-007

### DEC-015 NPC merchants pay the same costs as the player
- Date: 2026-10-27
- Decision: NPC merchants pay travel fees and tariffs under the player's rules.
- Alternatives: exempt (rejected)
- Impact: [SYS-MERCHANTS](systems/SYS_MERCHANTS.md)
- Resolves: Q-006

### DEC-014 Tolls on entering every node, as a share of value with specialty surcharges
- Date: 2026-10-27
- Decision: a tariff is charged each time cargo enters a node along the route. It is a share of the
  cargo's value: the node's base rate, plus a surcharge on goods in the node's own specialty categories
  (protection of local producers).
- Alternatives: charged only when selling (recommended, rejected) · only at jurisdiction borders
  (rejected) · coins per unit (rejected) · single rate per node (rejected) · hand-set rate per node and
  category (rejected)
- Impact: [SYS-TRAVEL-COSTS](systems/SYS_TRAVEL_COSTS.md); open: Q-015
- Resolves: Q-004, Q-005

### DEC-013 Capacities start as placeholder values
- Date: 2026-10-27
- Decision: cart, merchant and warehouse capacities start as placeholder config values and are tuned in
  balancing.
- Impact: [SYS-INVENTORY](systems/SYS_INVENTORY.md)
- Resolves: Q-003

### DEC-012 Weight and size per base good
- Date: 2026-10-27
- Decision: weight and size are set per base good; all four grades share them.
- Alternatives: per grade item (rejected)
- Impact: [SYS-INVENTORY](systems/SYS_INVENTORY.md), goods.csv
- Resolves: Q-002

### DEC-011 Bankrupt merchants are replaced
- Date: 2026-10-27
- Decision: a merchant group whose cash runs out goes bankrupt and is replaced by a new group.
- Alternatives: unlimited cash (rejected)
- Impact: [SYS-MERCHANTS](systems/SYS_MERCHANTS.md)
- Resolves: Q-001

### DEC-010 Node specialty goods
- Date: 2026-10-27
- Decision: every node has specialty goods chosen from its location, so each market stocks goods that fit
  where it is.
- Reason: user requirement (economy rules draft, memo 3)
- Impact: [SYS-PRODUCTION](systems/SYS_PRODUCTION.md); detailed by DEC-016

### DEC-009 Cargo limited by item weight and size
- Date: 2026-10-27
- Decision: every item has its own weight and size. A carrier (e.g. the cart) has a max total size and a
  max total weight, and cargo must fit both.
- Reason: user requirement (economy rules draft, memo 1)
- Impact: [SYS-INVENTORY](systems/SYS_INVENTORY.md), [SYS-MERCHANTS](systems/SYS_MERCHANTS.md), goods.csv; detailed by DEC-012, DEC-013

### DEC-008 Cross-continent trades in local coin, converted at the exchange rate
- Date: 2026-10-27
- Decision: a trade in another continent's market is priced in that continent's coin and converted with
  the current exchange rate.
- Alternatives: separate purse per continent (rejected) · single currency (rejected)
- Impact: [SYS-TRAVEL-COSTS](systems/SYS_TRAVEL_COSTS.md)
- Changed by: DEC-031 (no automatic conversion at the trade; the player pays with that continent's coins)

### DEC-007 Travel costs a base fee plus a per-unit cargo charge, and nodes levy protective tariffs
- Date: 2026-10-27
- Decision: travelling a route costs a base fee plus a charge per cargo unit. Moving goods between nodes
  is taxed by medieval protective tariffs whose rates differ per node, so tariffs are part of every
  trade decision.
- Alternatives: per-unit charge only (recommended, rejected) · free travel (rejected)
- Impact: [SYS-TRAVEL-COSTS](systems/SYS_TRAVEL_COSTS.md); detailed by DEC-014, DEC-015, DEC-017

### DEC-006 Player trades at the current price; volume feeds supply and demand
- Date: 2026-10-27
- Decision: the player buys and sells at the market's current price. The traded quantity is added to the
  market's demand (buying) or supply (selling), so it moves the price at the next repricing.
- Alternatives: instant price impact by volume (rejected) · fixed price (rejected)
- Impact: [SYS-INVENTORY](systems/SYS_INVENTORY.md)

### DEC-005 Player-owned output always goes to storage; storage is the cart plus owned sites
- Date: 2026-10-27
- Decision: output of player-owned facilities always goes to the player's storage, never straight to the
  market. Player goods live in the cart (travels with the player) and in warehouses at owned sites
  (facilities, guild halls).
- Alternatives: by control mode (recommended, rejected) · always sold at market (rejected) · cart only
  (rejected) · warehouses only (rejected)
- Impact: [SYS-INVENTORY](systems/SYS_INVENTORY.md), [SYS-PRODUCTION](systems/SYS_PRODUCTION.md)

### DEC-004 Facilities buy inputs from market stock; missing any input stops production
- Date: 2026-10-27
- Decision: a facility buys its recipe inputs from the stock of the nearest market that trades them. If
  any input is short, the facility produces nothing that week (all or nothing).
- Alternatives: scale output by the secured share (recommended, rejected) · no input limit (rejected) ·
  record inputs as demand only (rejected)
- Impact: [SYS-PRODUCTION](systems/SYS_PRODUCTION.md)

### DEC-003 NPC merchants trade by arbitrage with real cargo and travel time
- Date: 2026-10-27
- Decision: merchant groups pick trades by price gap minus transport cost; they carry cargo with a
  capacity, and travel takes the route's turns. Merchants of aggregate (far) nodes are approximated by
  node-to-node trade flows proportional to price gaps.
- Alternatives: fixed trade routes (rejected) · abstract supply-demand levelling (rejected) · trade only in
  detailed regions (rejected)
- Impact: [SYS-MERCHANTS](systems/SYS_MERCHANTS.md)

### DEC-002 Quality grades are independent items
- Date: 2026-10-27 (recorded from the architecture phase)
- Decision: 20 categories x 50 goods x 4 grades = 4,000 independent items, each with its own market cell.
- Alternatives: one item per good with a quality multiplier (rejected)
- Impact: [00_Architecture.md](00_Architecture.md), goods.csv

### DEC-001 Hybrid simulation architecture
- Date: 2026-10-27 (recorded from the architecture phase)
- Decision: plug-in modules + sparse SoA markets + Q47.16 fixed-point math, turn = 1 week, LOD between
  detailed and aggregate nodes.
- Reason: modular growth plus determinism and Android-scale cost
- Impact: [00_Architecture.md](00_Architecture.md)
