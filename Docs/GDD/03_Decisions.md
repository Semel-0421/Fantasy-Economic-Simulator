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

### Q-043 What a core node does
- Open, from DEC-101: its weight in play, and whether nation and region nodes keep their own markets and people.

## Decision log

### DEC-103 Special goods are no necessity
- Date: 2026-11-02
- Decision: [Confirmed] the continents' special goods sit between semi-necessities and luxuries and are never necessities, and a wide price gap while they are scarce abroad is expected. [Draft] All four stay in the luxury class at every grade, because the semi class counts part of a good as a necessity.
- Reason: the user's instruction.
- Impact: SYS-MARKETS; goods.csv unchanged

### DEC-102 The player trades bonds only
- Date: 2026-11-02
- Decision: [Confirmed] bonds are the only credit open to the player; bills are closed to them, against abuse and error. [Draft] The player buys new bonds of the state or of a group based in the nation at that nation's bank, within the issuer's debt limit and not in a credit crunch; the coins go where a sale's would. They sell at face to those with idle money that week, as far as those offer. What bonds pay goes to the player's account with that bank. Traders never pay the player's merchant groups or facilities in bills. The player's own group cannot be dealt with.
- Reason: the user's instruction.
- Impact: SYS-BANKING

### DEC-101 Nodes are classes with one core node
- Date: 2026-11-02
- Decision: [Confirmed] nodes are classified large to small: continent, nation, region, city, village. Each class has one core node, where its seat stands, such as a capital or city hall. [Draft] A core is a town or village inside the class, named in the `core` column of map_nodes.csv, or else its first town. A town or village is its own core. What a core does is not decided (Q-043): for now it is only recorded, and nation and region nodes keep their own markets and people.
- Reason: the user's instruction; the weight of a core is to be discussed separately.
- Impact: 00_Architecture, 02_Data_Formats, sample map_nodes.csv

### DEC-100 Every node is detailed in play
- Date: 2026-11-02
- Decision: [Confirmed] the game calculates every node in detail; the mode around the player is no longer used. `lod.mode` defaults to 1. [Draft] The aggregate code stays for now, and some tests use it.
- Reason: the user's instruction.
- Impact: 00_Architecture, 02_Data_Formats, sample config.csv

### DEC-099 Merchant spending, loss aversion and price cover
- Date: 2026-11-02
- Decision: [Draft] merchants' spending is counted by item. Loss aversion is by personality: cautious 3, normal 2, bold 1. A group counts a trip's upkeep and expected losses when it chooses a load. It buys the policy first and hires guards for what it still fears, coins at risk included. As a hedge it may buy price cover from the bank at its destination: for 0.8% of a load's offer the bank makes up a sale below 90% of that offer, as far as 10% of its spendable reserve goes in a week. It buys when the 6% chance it expects, weighted by its aversion if a 15% fall would mean a loss, beats the fee. Not abroad.
- Reason: the user asked what merchants spend, for an algorithm that spends less, and for hedges following loss aversion. The details are Claude's proposal.
- Limits: this does not make merchants' spending clearly smaller. Protection and theft come to 819k a year, 6.9% of sales, against 792k, 6.3%; the mix moved to policies. The largest costs are goods at 41% and wages at 28%, and wages are paid out of cash above a buffer, as households' income. Price cover is little used, at 6k of fees and 5k of claims a year.
- Impact: SYS-MERCHANTS, SYS-BANKING, 02_Data_Formats, sample config.csv

### DEC-098 Traders keep a margin and spend what they do not need; far flows pay their carriers
- Date: 2026-11-01
- Decision: [Confirmed] traders pay any seller the price less 25% (first 30%; the user asked to adjust it, and at 30% bills nearly stopped): merchants, producers and the player. Buyers still pay the price. A float above eight weeks of households' wishes pays 2% a week of the excess to the nation's households. Aggregate flows pay their whole margin as wages at the origin, out of the float. This changes the selling side of DEC-006 and DEC-028, which paid sellers the current price.
- Reason: Claude's proposal, under the user's instruction to analyze the remaining problems and act on them; the user confirmed the margin of 25%.
- Causes found. With every node detailed, producers and merchants were paid about 4M a year more than households paid for the same goods, and banks covered the gap through protested bills. Stock is not the cause, growing only 0.2M a year. A seller was paid the price standing before its load arrived, and households paid the lower price after, and traders had no margin to cover that. Around the player, aggregate flows took no margin and paid no wages, so traders kept the whole gap between village and city prices and held 6-7M idle while households held 1.3-1.6M.
- Limits: the margin has a narrow range. At 25% 10% of bills are lost, at 30% 2-15%, and at 35% bills stop and floats swell to 7.8M while groups fall to 3.6M. Around the player cities remain short of money, with a week's budget of 86-94k against 111-124k of necessities, and floats still hold about 6M; a lower keep or faster payout sends the coins to facilities and groups, not to cities.
- Impact: SYS-MARKETS, SYS-MERCHANTS, SYS-PRODUCTION, SYS-INVENTORY, SYS-BANKING, 02_Data_Formats, sample config.csv

### DEC-097 Bills of exchange, government and corporate bonds
- Date: 2026-11-01
- Decision: credit widens to bills, government bonds and corporate bonds, among NPCs. The player has no credit commands yet.
- Reason: the user's decision. Traders' money was the largest limit found, and idle money sat with nation and village households.
- [Draft] Bills. A nation's traders who cannot pay give a bill for the rest of a merchant's sale, due in 8 weeks, up to 8 weeks of what households wish to buy, less what they already owe. The bank pays the seller the face less 2% as far as it has coins above half its reserve target; otherwise the seller holds the bill. Due bills are paid from at most 25% of the float a week. A bill 26 weeks overdue is protested and the holder takes the loss. Bills apply at home only, and producers do not take them (`credit.bill_producers`, off).
- [Draft] Government bonds. A nation whose towns fall short of necessities sells bonds up to that shortfall, up to 30% of its share of the coin supply, and pays the towns with the coins. 4% a year, paid off over 20 years from the bank's spendable reserve. It sells none while it is behind on its bonds or in a credit crunch.
- [Draft] Corporate bonds. A group feeds a facility short of 4 weeks of working cash, if its output is worth more than its inputs: from its own money above its starting cash, then from bonds, up to 50% of the value of its facilities. New facilities may be part paid the same way. 7% a year, paid off over 10 years. A group 26 weeks behind defaults and its holders lose what is left.
- [Draft] Holders. Households offer 0.5% a week of their cash beyond 26 weeks of spending, and groups 0.5% a week of their cash beyond twice their starting cash.
- Limits, before DEC-098 cut protests to 8-15%: at year 30 about half of the bills' face is protested, and at year 60 about 60%. The traders' float does not cover what they owe, so banks in effect pay merchants out of reserves, which taxes and minting refill: reserves were 3.2-3.5M at year 30, against 7.0M without credit, and 4.1M at year 60. A higher repayment share drains the floats and does worse: 50% 13.31M, 100% 12.56M, against 14.49M at 25% (three seeds). Bonds change supply little on their own; bills and working cash do most of the work (04_Roadmap).
- Impact: SYS-BANKING, 02_Data_Formats, sample config.csv

### DEC-096 Each continent makes its own goods, and groups carry them across the sea
- Date: 2026-11-01
- Decision: each continent has special goods made only there, so that trade between continents forms: spice and silk on Aurelia, fur and amber on Borealis. Builds P17 (DEC-066).
- Reason: the user's decision
- [Draft] Goods. `exotic` category, luxury class, traded at every level (Low and Mid at villages). `facilities.csv` gains a `continent` column; such facilities stand only there, 3 of each type at the start, in villages, and are never built elsewhere by groups, states or subsidies.
- [Draft] Who sails. Groups based at region level or above (`merchants.cross_level`), and not of the cautious personality unless based at a port (`merchants.port_crossers`). Abroad a group goes anywhere; it buys no guards or insurance and sells for coins only. It values a sale after the rate and the 3% fee, and coins pass through both international banks. Aggregate flows cross at 65% (`merchants.cross_flow_permille`).
- [Draft] What it took for trade to form. Built with only the rules above, 6 ships sailed out and 8 back in ten years, for 130 units a year, in one seed: 9 of the 10 groups allowed to sail were stranded for good on the foreign continent node. The following came from tracing that:
  - A group abroad with nothing worth doing sails home with whatever it holds.
  - A market whose traders cannot pay for 20 units is no market to a group judging a load (`merchants.judge_coin_units`): high prices at the top nodes come from nobody supplying them, not from anybody being able to pay.
  - A group leaving with one load still bids for a better one it could not find (`merchants.bids` 2), which draws goods to the port. Without that the port rarely held them.
  - Nations keep their international bank between 2% and 4% of the coin supply, out of and into their reserves (`bank.international_balance`). A one-sided surplus otherwise emptied one bank and stopped its exporters.
  - The sea route takes 4 weeks at 6 a unit (previous: 8 weeks at 12).
- Limits: with every node detailed, about 34 ships sail out a year and 2,300-3,000 units of special goods cross, plus 6,700-7,700 units of other goods. The prices stay far apart, with special goods at 66-90% of anchor at home and 154-168% abroad, and only 65-230 units in foreign stock at a time. Around the player, aggregate flows carry much more: prices fall to 12-15% of anchor at home and 26-73% abroad.
- Alternatives: city-level groups sailing as well (`merchants.cross_level` 3): more ships, no more special goods, people no better. Not kept.
- Impact: SYS-MERCHANTS, SYS-BANKING, 02_Data_Formats, goods.csv, recipes.csv, facilities.csv, market_profiles.csv, map_routes.csv, sample config.csv

### DEC-095 The market package: judging three markets ahead, bids, sister-city roads, merchants in towns, a 13-week budget
- Date: 2026-11-01
- Decision: apply the best mix from the supply trials, with merchants judging three markets ahead instead of two.
- Reason: the user's decision, after the review of real supply rigidities (04_Roadmap)
- [Draft] Details by Claude.
  - Households spend at most their cash over 13 weeks a week (previous: 26).
  - A group values a load at the best price within three markets, after each hop's charge and toll, even through a market that does not trade the good (`merchants.judge_hops`).
  - A group with nothing to buy bids for what it would carry if the market had it (`merchants.bids`). The bid raises the price and stops when the margin closes.
  - Direct roads join sister cities: 2 weeks, 3 a unit, fee 5, paved.
  - Merchants: 2 per continent, 2 per nation, 8 per region, 6 per city, 2 per village (`merchants.<level>`).
  - A group stranded above its home with nothing to carry comes back down empty (`merchants.return_home`). As first built it could go up rather than down, and looked unhelpful; once fixed, groups idle at nation nodes fell from 14 to 2.
- Impact: SYS-MERCHANTS, SYS-MARKETS, 02_Data_Formats, map_routes.csv, sample config.csv

### DEC-094 Traders start with most of the free money, and households lend to them
- Date: 2026-11-01
- Decision: switch on the loans of DEC-092, households lending 1% a week of what they can spare at 5% a year, and give the traders' floats 90% of the starting money nobody holds (previous: 10%). The bank does not lend to them.
- Reason: the user's decision, on Claude's recommendation: it kept the most people at years 30 and 60, and traders' money was the largest limit found
- Limits: every node detailed it works; around the player, where far merchants do not draw on the float, the gain is small and coins sit idle there (04_Roadmap). Supply does not reach 100% in cities.
- Impact: SYS-BANKING, 02_Data_Formats, sample config.csv

### DEC-093 Merchants weigh a sale against carrying the load on
- Date: 2026-11-01
- Decision: a group with a load works out what it brings here at the current price and what it would bring elsewhere after the trip's costs, and does the better of the two, selling below cost when that is still better. A group that would not last the trip sells here, whatever it loses.
- Reason: the user's decision. Until now a group sold everything on arrival without comparing anything, and a group with no cash paid for trips to markets whose traders could not pay either.
- [Draft] Details by Claude. Worth elsewhere: the load's value as far as the traders there can pay, less toll, base fee, per-unit charge, upkeep for the trip's weeks and the expected cargo loss. Worth here: the load's value as far as the traders here can pay. A group would not last the trip when its cash does not cover those costs and its weeks below zero plus the route's turns reach merchants.bankrupt_weeks. With no market worth the trip it waits and tries again each week. What was paid for the load is kept for its profit and loss and for counting losses; it does not change the choice, since that money is gone either way.
- Limits: only groups stepped one by one. Aggregate flows carry no load to weigh.
- Alternatives: a group below zero selling to its node's households at a discount when traders cannot pay, tested and not kept: failures fall further, people and supply do not rise (04_Roadmap).
- Impact: SYS-MERCHANTS, 02_Data_Formats

### DEC-092 Loans to the traders' float, on trial
- Date: 2026-11-01
- Decision: test moving households' idle money to traders as loans rather than a grant. Built and switched off at first; switched on by DEC-094.
- Reason: the user's decision to test; traders' money was the largest limit found
- [Draft] Details by Claude, numbers placeholders. Each week the float pays simple interest and a 52nd of each loan, interest first, within 25% of the float. While it pays all that fell due, each node's households lend 1% of their cash beyond 26 weeks of spending. Unpaid interest is not income, and lending is neither income nor spending. The bank can lend a share of its reserve above target the same way.
- Limits: a loan does not help a merchant directly, since the float is what pays sellers. No defaults or write-offs, because lending stops while traders are behind.
- Impact: SYS-STATE-ECONOMY, 02_Data_Formats

### DEC-091 Plans led by households, raw goods included; ale a semi-necessity; population holds at full supply
- Date: 2026-11-01
- Decision: the demand forecast gives household demand the highest weight. Raw goods follow the same forecast. Ale becomes a semi-necessity, closer to a luxury from mid grade up. Population holds at 100% supply.
- Reason: the user's decision; the quarterly plan read only what sold, so a shortage looked like low demand and throttled makers up the chain
- [Draft] Details by Claude, numbers placeholders. Forecast of a market's good: 70% what consumers would take at settled prices, 30% smoothed off-take. Consumers are households and, up the chain, the makers they set running, through six passes. Makers serve their own market first, with two quarters' cover (open orders added, none for goods made to order), less that market's stock. What they cannot serve, and demand at markets without makers, goes to the nation's makers with spare capacity, less what its markets hold beyond their own needs. Plans stay at 10-100% of capacity; a recipe's plan is the sum over its output grades. Semi-necessity luxury shares 25/65/90/100% by grade; ale's demand and prices split by grade like foods; a brewery makes 18, 24, 2, 1 of 45 by grade. Population change is 4% of the supply gap, with no base growth or urban penalty. Investors do not build for a good whose makers run below 80%. The price step no longer applies its one-coin pull to the anchor against the market's pressure: on cheap goods that coin cancelled the rise, so a market short by more than half still showed a price near the anchor.
- Limits: nation-wide stock counts against makers with spare capacity even where merchants do not carry it; supply is well under 100% until traders have money (see the roadmap).
- Impact: SYS-PRODUCTION, SYS-MARKETS, 02_Data_Formats

### DEC-090 Food grades
- Date: 2026-11-01
- Decision: best and high grade foods are priced well apart from mid and low, and made in smaller amounts, so scarcity forms by itself.
- Reason: the user's decision
- [Draft] Details by Claude, numbers placeholders. Provisions only (`food.*` keys). Price low 0.7, mid 1, high 2.5, best 6 times base. Demand split 36/50/10/4%. Output about 40/53/4-5% and low, mid, high; only bread makes best (18, 24, 2, 1 of 45). Cheese and salt fish demand rose from 3 to 5, so the food total is 54.
- Impact: SYS-MARKETS, SYS-PRODUCTION, 02_Data_Formats

### DEC-089 More foods on the market
- Date: 2026-11-01
- Decision: a larger share of food comes from alternatives. Wheat must still be milled, bread stays preferred over other foods, and markets sell more kinds of goods.
- Reason: the user's decision; bread was half of the need and short while wheat sat idle
- [Draft] Details by Claude, numbers placeholders. Household demand: bread 22 (was 40), porridge 8, beans 8, vegetables 6, cheese 3, salt fish 3, wheat 0 (was 10), the same 50 in all. New producers: bean farm, vegetable farm, dairy, fishery (primary), porridge kitchen (tertiary, from barley). Fish need no coast yet.
- Impact: SYS-MARKETS, SYS-PRODUCTION

### DEC-088 Contract terms that fit, and a deposit
- Date: 2026-11-01
- Decision: a contract's term follows what each party needs. A deposit of 20% of the whole contract's value is paid in advance to sign.
- Reason: the user's decision
- [Draft] Details by Claude, numbers placeholders. Term is the shorter of the weeks the seller's stock lasts at the volume and the weeks the buyer's money covers the deposit on, from 8 to 104 weeks. Each delivery draws 20% of its bill from the deposit. At the end the undelivered part returns from the seller, as far as it holds. A buyer who cannot pay breaks the contract and forfeits what is left, the first penalty for breaking.
- Impact: SYS-MERCHANTS, SYS-INTEREST-GROUPS, 02_Data_Formats

### DEC-087 Strategic grants and supply contracts
- Date: 2026-11-01
- Decision: the state grants part of its money to groups producing important national resources, to grow those industries. Supply contracts run person to person, group to person and group to group: the unit price scales with volume, below market, and the buyer must buy for the whole term.
- Reason: the user's decision; part of the safety-net proposal was accepted, so merchant bodies get no state money
- [Draft] Details by Claude, numbers placeholders. Strategic: mines, smelters, smithy, cartwright, wagonworks (facilities.csv `strategic`); 20% a year of the reserve above target, by facility levels. Contracts: up to 8 a year per nation on the widest gaps between a market with 200 or more in stock and the settled price (the anchor, or less) of one with less; weekly volume is the lesser of stock over 26 weeks and half what the buyer's market takes; 52 weeks; discount off the settled price 5%, plus 0.1% per unit, to 25%, never below the seller's price; the buyer must hold 8 weeks of the bill to sign. Each week the seller buys from its market, the buyer pays the contract price and sells into its own. A buyer unable to pay breaks the contract, with no further penalty. Delivery has no transit time, toll or risk.
- Impact: SYS-STATE-ECONOMY, SYS-MERCHANTS, SYS-INTEREST-GROUPS, 02_Data_Formats

### DEC-086 Fewer merchants in associations
- Date: 2026-11-01
- Decision: a third as many merchants. As a supply safety net, merchants may belong to guilds and other bodies; members can draw on the body's budget and pay half the insurance premium when using carts and wagons.
- Reason: the user's decision; with every node detailed, trade earned about 29% of merchants' running costs
- [Draft] Details by Claude, numbers placeholders. Groups per node 17, 17, 10, 3, 1. Each year a 30% chance to join the richest guild of the home's city, else the richest company of the nation. Fee 5% a year of cash above starting cash; below 20% of starting cash a member draws up to 10% of the body's cash a week. Running costs are unchanged.
- Impact: SYS-MERCHANTS, SYS-INTEREST-GROUPS, 02_Data_Formats

### DEC-085 Orders, factory plans, vehicle costs and risks
- Date: 2026-11-01
- Decision: carts, wagons and some luxuries are ordered at a node with stock and collected after a period. Factories change output to demand, checked and forecast each quarter. Pack animals cost least to run and wagons most, to lengthen upgrades. Higher grades draw more raids, cost more to insure, and lead merchants to hire guards.
- Reason: the user's decision; vehicle factories had overproduced
- [Draft] Details by Claude, numbers placeholders. Made to order: cart, wagon, cookware. An order takes the nearest node's stock, else waits at the nearest market; it reserves when stock is there, is collected and paid after 8 weeks, and lapses unfilled after 52. Households order cookware quarterly with what is left after necessities. Secondary and tertiary factories run at 10-100% of capacity: forecast is the mean of last forecast and last quarter's net off-take; target adds a quarter's cover (not for ordered goods) and open orders, less stock. Pack, cart, wagon: running cost 0.6, 1, 1.8; raid chance 0.7, 1, 1.6; premium 0.8, 1, 2; next vehicle at cash of 6 and 20 times its price. Merchants hire guards when four times the expected saving beats their cost, and insure when the refund beats the premium.
- Limits: orders are seen only by factories selling to that same market, so factories cannot stop completely (stopping them removed carts from the world in a test); away from the player, raids apply as expected loss, with no guards or insurance; the player's own peddler is unchanged.
- Impact: SYS-MERCHANTS, SYS-PRODUCTION, SYS-MARKETS, 02_Data_Formats

### DEC-084 Merchant vehicles
- Date: 2026-11-01
- Decision: each merchant group has its own carrying capacity. Merchants invest money to change vehicle, such as carts and wagons, and factories make and sell the vehicles.
- Reason: the user's decision; moving more goods was the best tested lever for the nation node's supply
- [Draft] Details by Claude, numbers placeholders. Pack 0.5x, cart 1x, wagon 3x; start 50/35/15%. They multiply cargo limits, and in aggregate also the share of stock a node's merchants move. Yearly: 10% chance to wear down a step, then buy the next vehicle at cash of 3 times its price, or bid if none is in stock. A replacement after bankruptcy starts on pack animals. New goods cart (150) and wagon (500), factories cartwright and wagonworks. Player-hired automated groups buy too; there is no player command yet, and the player's own peddler is unchanged.
- Impact: SYS-MERCHANTS, SYS-PRODUCTION, 02_Data_Formats

### DEC-083 Necessities first, then luxuries
- Date: 2026-11-01
- Decision: households buy necessities first and extend to luxuries gradually as money allows.
- Reason: the user's decision; a short budget used to cut every good alike, so luxuries took money from food
- [Draft] Details by Claude. The week's budget pays wished necessities first; what is left pays luxuries in proportion. A good's necessity and luxury parts follow its existing luxury share. Public wages (DEC-082) now answer the shortfall on necessities only.
- Impact: SYS-MARKETS, SYS-STATE-ECONOMY

### DEC-082 Wages that follow output and public wages in towns
- Date: 2026-11-01
- Decision: wages rise with what a facility produces (not its sales), and reserve above target is paid as public wages in towns. Weights are set by test.
- Reason: the user's decision; a city's supply rose from 29% to 129% with the household budget limit removed, so city households are short of money.
- [Draft] Output wage is 2.5% of output at base prices, public wages 3% a week of the excess, split by how far each town's cash falls short of what it would buy. Numbers are the best of a thirty-year grid, within noise of its neighbors; output wages of 10% and above shrank facilities and worsened population.
- Impact: SYS-STATE-ECONOMY, 02_Data_Formats

### DEC-081 Primary producers buy tools and seed
- Date: 2026-11-01
- Decision: farms, mines and pastures buy tools, and farms seed. Labor stays the weekly upkeep, which is paid as wages.
- Reason: the user's decision
- [Draft] Details by Claude, numbers placeholders. Recipe role `aid`: all bought or none, and without them output is 70%, so the tools-bars-ore-mines cycle cannot deadlock. Wheat farm 15 wheat and 1 tool, barley farm 12 barley and 1 tool, mine and pasture 1 tool. A missing aid bids its price up, within the output's worth and the cash.
- Impact: SYS-PRODUCTION, 02_Data_Formats

### DEC-080 Paid expansion and NPC investment on every node
- Date: 2026-11-01
- Decision: factories can be expanded with money, upkeep rises and output rises much more. NPCs may found and manage facilities on every node, and the founder takes the income.
- Reason: the user's decision; measured finished goods capacity was about 20-30% of demand, close to the supply rate
- [Draft] Details by Claude, numbers placeholders. Secondary and tertiary to level 5 for tier value times level, paid to the node's households. Output is level times (1 + 0.25 per level above one), so 1, 2.5, 4.5, 7, 10; upkeep is the level. Yearly on every node, up to 2 projects by the richest of the node's private enterprise, the nation's companies and its state enterprise: payback within 8 years, output at or above its anchor price, under 26 weeks of stock, 4 weeks of inputs at hand, 24 facilities a node, primary only in villages. This widens DEC-076, which kept villages from tertiary work, for investment only. Groups now pass on 25% of dividends rather than 80% so they have capital. Player command: `ExpandFacilityCommand`.
- Emancipation (DEC-079) now waits 5 years and 30% open jobs; it was enacted in year 2.
- Impact: SYS-PRODUCTION, SYS-STATE-ECONOMY, SYS-INTEREST-GROUPS, 02_Data_Formats

### DEC-079 Free people and rural-urban migration
- Date: 2026-11-01
- Decision: town labor markets offer better jobs, free people may move, and villagers leave for towns, on real cases. Urbanization takes about
  fifteen years. Half of village wages still leaves (DEC-077).
- Reason: the user's decision; earlier the start population was urban with no movement
- Real cases used: Harris-Todaro (people weigh wage times the chance of a job); England and Wales about 35% urban in 1801 and 77% in 1901; town air frees after a
  year and a day; the English labor statutes of 1349 and 1351 (lords resist movement); Prussia's edict of 1807 taking effect in 1810; Russia's 49 years of redemption from 1861; the urban penalty.
- [Draft] Details by Claude, numbers placeholders. Start: 65% rural (nation 0.7M, regions 0.35M, cities 0.525M, villages 0.65M), demand the same per head. Villages 20% free. Up to 8%
  of a village moves a year on the widest gap; jobs are 100,000 per facility level; bound people run away at 1% a year, 3% from a lord who broke the contract. 20% of town jobs open brings emancipation: after 3
  years 10% more of each village is freed yearly, paying 5% of income to the noble for 49 years. Migrants carry their share of cash. Towns grow 0.3% a year less. Emancipation is automatic; the player cannot yet enact or lobby it.
- Impact: SYS-STATE-ECONOMY, SYS-MARKETS

### DEC-078 A facility short of inputs bids their price up
- Date: 2026-11-01
- Decision: what a facility could not buy counts as demand in its market, so the price rises and merchants bring the input.
- Reason: measured deadlock: raw goods piled in villages, while city prices stayed low because unmet input needs left no trace; the user's go-ahead
- [Draft] Details by Claude: it bids only while it could pay and the output is worth more than the inputs; without this limit prices ran away and overflowed within three years.
- The bid is kept apart from demand: it moves the price only, so it neither crowds households out nor removes unpaid stock.
- Impact: SYS-PRODUCTION, SYS-MERCHANTS

### DEC-077 Village wages partly leave for towns; a supply ladder of processing facilities
- Date: 2026-11-01
- Decision: part of a village facility's wages goes to the nation's towns, as absentee landlords' rents. Processing facilities
  in several categories stand at city level and above, completing a supply ladder.
- Reason: the user's decision; villages hoarded while towns lacked cash
- [Draft] Details by Claude, all numbers placeholders: 50% of village wages leaves, split by DEC-076's wage weights. Ladder: wheat to
  flour to bread; barley to malt to ale; iron ore to bars to tools; copper ore to bars to cookware; wool to yarn to clothes. Raw
  and half-made goods have no household demand, except wheat at 10. Cities have 6 secondary and 8 tertiary slots.
- [Draft] Later, by the user: raw output doubled, processing up 50%. Claude scaled processing inputs by the same 50% to keep recipe ratios.
- Impact: SYS-PRODUCTION, SYS-MARKETS

### DEC-076 Ten-million continents, city industry, town wages, and nobles under bilateral contracts
- Date: 2026-11-01
- Decision: a continent holds ten million people, with fewer in villages and most in cities. Cities grow industries that
  work inputs. Wages rise with development at city level and above. Noble and royal houses manage villages and cities; the
  royal blood collects a city's tax first; lord and vassal are bound by a two-way contract, as in European feudalism.
- Reason: the user's decision, after measuring towns short of both income and goods
- [Draft] Details by Claude, all numbers placeholders. Sample world per continent: nation node 2.5M, regions 1M, cities
  1.25M, villages 50k, the continent node 1,000 (it has no market). Demand weights follow people (nation 8, region 3.2, city
  4, village 0.16). Villages have no tertiary slots; cities have 8. Wage weights: above region 3, region 2.5, city 2,
  village 1, for merchant and public wages and for what royal houses spend. Each nation's first city is the royal domain. Income
  tax: royal house 10% first, then the city's holder 10%, then the village's noble 10%, the state the rest. Yearly a
  vassal pays 20% of its purse as aid and its lord owes 50% of that back as protection, paid to the vassal's households; a
  lord who cannot pay it in full loses that vassal's aid for a year. Houses spend 2% of their purse a week on their fiefs.
- Impact: SYS-STATE-ECONOMY, SYS-MARKETS, SYS-PRODUCTION

### DEC-075 The map becomes hexagonal tiles, generated from the seed (before front-end work)
- Decision: like Civilization, the map is a grid of hexagonal tiles; the map and its nodes are generated from the random
  seed. To be taken up before front-end development; nothing is built yet.
- Reason: the user's decision
- Impact: the hand-written map_nodes.csv and map_routes.csv and the sample world; state land development (DEC-072), which
  stands in for tiles with the land around a node, should move onto tiles

### DEC-074 State subsidies; the army's safety; reserved construction
- Decision: a nation can enact a subsidy on a good, paid by output, so existing producers take it up and new companies and
  guilds appear. Army spending makes routes safer in proportion; a war system comes later. State projects are planned
  yearly and built in order. Population growth follows Victorian Europe.
- Reason: the reserve must return to the market; the user's decision
- [Draft] Details by Claude, all numbers placeholders: with money above target a nation enacts on its dearest good, at most
  2 at a time for 3 years, paying 20% of the output's value out of 30% of the excess a year. Each year of a policy one
  producer opens at the node shortest of necessities, under a group already making the good or a newly founded company;
  a guild is founded where the site's city has none of that trade. Safety is yearly defense per route over 15,000, at
  most 60%. Up to 3 projects are planned a year, 16 weeks each; those not started by the next plan are dropped.
  Population: 0.8% at full supply (Europe's rate, from memory, not checked), at most 1.3% (England and Wales 1851-1901,
  births 35 and deaths 22 per thousand, checked), at most -4% in a famine (Ireland, from memory).
- Impact: SYS-MARKETS, SYS-PRODUCTION, SYS-ROUTE-HAZARDS, SYS-INFRASTRUCTURE

### DEC-073 (number unused)
- No decision has this number; references skip from DEC-072 to DEC-074.

### DEC-072 The state defends, develops land and draws on state facilities; population follows necessity supply
- Decision: a nation spends a fixed 5% of its budget on defense (standing army and mercenaries), with maintenance on
  top, directly in its own markets. It runs a land development plan (ports, coast-to-inland highways, paved roads
  between towns, mines, farmland), choosing from what holds production back and what it spends on imports. These
  projects give a bonus to land outside the nodes, and state facilities pay the state part of their profit.
  Population growth rises and falls with the supply of civilian necessities.
- Reason: the reserve must return to the economy and surplus should go where it helps; the user's decision
- [Draft] Details by Claude, all numbers placeholders: the budget is the reserve at the year's start less the player's
  deposits; maintenance is 20% on top; purchases every 4 weeks from all stock by value, with no effect from the
  army yet. Half the reserve above target funds up to 3 projects a year, one of each kind, done in 26 weeks. The
  map has no tiles, so the land around a node stands for them: +10% primary output per level, at most 5; harbors -10% per
  level on water routes from there; highways -20% on the route's charge. Farmland and mines also open a facility of
  the state enterprise; the state collects 50% of its dividends. Growth numbers moved to DEC-074 (previously: 1% at full supply, 6% less per
  full shortfall, within 4%).
- Impact: SYS-BANKING, SYS-INFRASTRUCTURE, SYS-PRODUCTION, SYS-MARKETS

### DEC-071 A yearly income tax on households and companies
- Decision: every node's households and every facility and merchant group pay 10% of the year's income, once a
  year, into the bank reserve of their nation.
- Reason: the state needs income to spend; the user's decision
- [Draft] Details by Claude: households' income is the measured income the income-per-head rule uses; a company's
  profit is its change in cash plus dividends paid; losses carry no relief; nobody pays beyond the cash held; a
  new operator starts new books; bank-held facilities, interest groups and the player's own purse pay nothing.
- Impact: SYS-MARKETS, SYS-BANKING

### DEC-070 A labor market takes merchants' surplus; employment follows the economy
- Decision: merchant groups pay wages into a labor market from their surplus cash, and the employment rate
  rises and falls with an economic indicator.
- Reason: with merchants paid at last (producers leave them half the float), group cash grew from 97k to 1,134k
  with no way back; the user's decision
- [Draft] Details by Claude: one rate per nation, starting at 90%, within 50 to 100%; the indicator is real
  household spending per head against its own long-run level, read each quarter; wages are 10% a week of cash above
  starting cash, times the rate, paid to the nation's households by population. Facility upkeep stays as it was.
- Impact: SYS-MARKETS, SYS-MERCHANTS

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
