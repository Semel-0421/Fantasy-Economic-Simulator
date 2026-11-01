---
doc: DATA-FORMATS
title: Data formats and config keys
status: final
updated: 2026-11-01
---

# 02 — Data Formats

All files live in `Assets/StreamingAssets/Data/`. CSV with a header row; lines starting with `#` and blank lines
are ignored; quote fields containing commas (a quote only opens a field as its first character).
Integers outside the int range, numeric enum values and unterminated quotes are load errors. Keys are the stable identity; dense ids are assigned in
file order at load. Any change to these files changes the data hash, so older saves are rejected —
except `config.csv`, which is hashed by its effective values (see below).

## goods.csv (required)

| column | meaning |
|---|---|
| key | base good key (unique) |
| category | category key; categories are created in order of first appearance |
| base_price | Mid-grade reference price in coins |
| base_demand | weekly demand of a village market (scaled by level weight) |
| weight | weight of one unit, shared by all grades (optional, default 1) |
| size | size (volume) of one unit, shared by all grades (optional, default 1) |
| requires_license | 1 if selling needs a license of the nation (optional, default 0) |
| class | `necessity`, `semi` or `luxury` (optional, default necessity); sets the price elasticity ([Draft], `semi` DEC-091) |

Each row expands to 4 items (`key@low`, `@mid`, `@high`, `@best`) with prices scaled by
`grade.<grade>.price_permille` (defaults 600 / 1000 / 1800 / 3500).
Good id = `baseIndex * 4 + grade`.

## recipes.csv

`recipe,role,good,grade,qty` — one row per input (`in`) or output (`out`), weekly at level 1.

## facilities.csv

`key,tier,recipe,state_owned,strategic,continent` — tier is `Primary`, `Secondary` or `Tertiary`; `state_owned` (optional, 1)
gives facilities of the type to their nation's state enterprise. Spawn counts per node come from
`producers.<level>.<tier>` in config; the facility type is picked deterministically among that tier's defs
whose inputs are all made in the node's subtree, or among the primary defs when none qualifies (DEC-069).
`continent` (optional, a continent key; DEC-096) means the type stands only there: it is left out of the picking
above, `producers.per_continent` of it (default 3) spawn in villages there, and no one builds it elsewhere.

## events.csv

`key,level,category,target,mul_permille,duration,chance_permille` — rolled per node of `level` each
turn; `category` may be `*`; `target` is `Supply`, `Demand` or `Value`. Supply/Demand modifiers scale
weekly flows, so any duration >= 1 works. A `Value` event must last at least the longest repricing
period among its level and the levels below it, or loading fails.

## market_profiles.csv (required)

`level,category,grades` — which goods markets of a level carry. `category` and `grades` accept `*`;
grades may be a list such as `Low|Mid`. Drives the sparse market layout and therefore memory.

## map_nodes.csv / map_routes.csv

- Nodes: `key,name,level,parent,tags`. `core` (optional, DEC-101) names the town or village at the heart of a continent, nation or region; empty takes its first town. Continents have an empty parent. A node's parent must be one level
  up, except villages, which may hang off a city or a region. `tags` (optional) lists terrain tags
  separated by `|`; every tag must appear in `specialties.csv`. `tariff_permille` and
  `specialty_surcharge_permille` (optional, empty = config default) set the node's toll rates.
  `international_bank` (optional, 1) marks the continent's international bank: at most one per continent,
  at city level or above. `danger_permille` (optional, region nodes only) is the region's road danger
  factor. `port` (optional, 1) marks a port; water routes must run between ports. `population` (optional, empty = the
  level's default) sets the node's starting population.
- Routes: `from,to,travel_turns,cost_per_unit,base_fee` (directed; add both directions for two-way
  travel). `base_fee` is optional (default 0) and paid once per trip. `terrain` (optional) is `Road`,
  `Mountain`, `River` or `Sea`; river and sea routes are water. `paved` (optional, 1) paves a land route
  from the start.

## specialties.csv

`tag,category,good` — a terrain tag makes `good` (a base good of `category`) a specialty of every node
carrying the tag; an empty `good` means every base good of the category. Several rows per tag add up.
Optional; the sample tags and goods are [Draft] placeholders.

## config.csv

`key,value` pairs; every key has a code default (`SimulationConfig`). Unknown keys are load errors
(typos would otherwise fall back to the default silently). The data hash uses the effective value of
every key, defaults included: comments, row order and restating a default do not affect saves, while
changing a default in code does.

| key | default | meaning |
|---|---|---|
| tier.<level>.period / .offset | see 00_Architecture | price-update cadence |
| demand.<level>.weight_permille | continent 40000 ... village 1000 | base demand weight |
| demand.necessity_elasticity_permille, demand.luxury_elasticity_permille, demand.max_cheap_permille | 500, 1500, 2000 | demand curve ([Draft]) |
| demand.<grade>.luxury_share_permille | 0 / 0 / 500 / 1000 | how far each grade of a necessity is bought like a luxury ([Draft]) |
| households.<level>.population, households.<level>.income_permille | 20000 / 10000 / 4000 / 2000 / 500; 300 / 300 / 300 / 500 / 2000 | default population and weekly income per head in thousandths of a coin ([Draft]; P13d replaced `households.<level>.income`, 4 / 4 / 3 / 3 / 2 coins) |
| households.q1..q5.income_share_permille | 20, 30, 50, 100, 800 | income share per quintile, poorest first; must sum to 1000 ([Draft]) |
| households.income_response_permille, households.income_max_change_permille | 500, 500 | yearly income change toward measured income: share of the gap, cap as a share of the larger value ([Draft]; cap was 50) |
| households.need_memory_weeks | 52 | a necessity counts toward population change if offered within this many weeks (DEC-067, [Draft] value) |
| households.budget_weeks | 26, sample data 13 (DEC-095) | households spend at most their cash over this many weeks a week ([Draft]) |
| markets.trader_float_permille | 100, sample data 900 (DEC-094) | share of the starting money nobody holds that goes to the traders' floats; households get the rest ([Draft]) |
| markets.producer_float_permille | 500 | share of a float, as the week starts, that producers may be paid from |
| labor.base_employment_permille, labor.min_employment_permille, labor.max_employment_permille | 900, 500, 1000 | employment rate at long-run activity, and its limits ([Draft]) |
| labor.response_permille, labor.long_run_response_permille | 500, 125 | quarterly share of the way toward the target rate, and of the long-run level toward activity ([Draft]) |
| labor.merchant_wage_permille, labor.merchant_buffer_permille | 100, 1000 | weekly wage share of a group's cash above the buffer, at full employment; the buffer as a share of starting cash ([Draft]) |
| labor.<level>.wage_weight_permille | 3000 / 3000 / 2500 / 2000 / 1000 | wage per head by level (DEC-076) |
| noble.royal_share_permille, noble.city_share_permille, noble.village_share_permille, noble.aid_permille, noble.protection_permille, noble.spend_permille | 100, 100, 100, 200, 500, 20 | houses' tax shares, yearly aid, protection owed, weekly spending (DEC-076) |
| labor.village_wage_away_permille | 500 | share of village facility wages paid to the nation's towns (DEC-077) |
| migration.max_share_permille, migration.start_free_permille, migration.flee_permille, migration.broken_contract_factor, migration.jobs_per_facility_level | 0, 200, 10, 3, 100000 | migration limits and jobs (DEC-079) |
| migration.vacancy_permille, migration.emancipation_lead_years, migration.freed_per_year_permille, migration.redemption_years, migration.redemption_permille, households.urban_penalty_permille | 200, 3, 100, 49, 50, 0 | emancipation, redemption and the urban penalty (DEC-079) |
| facility.expansion_bonus_permille, facility.max_level, facility.unaided_output_permille | 0, 5, 700 | expansion and output without tools (DEC-080, DEC-081) |
| facility.output_wage_permille, state.public_wage_permille | 0, 0 | wages by output and public town wages (DEC-082) |
| merchants.vehicle{0,1,2}.capacity_permille, merchants.vehicle{0,1,2}.start_permille, merchants.vehicle_cash_times, merchants.vehicle_wear_permille | 1000, 0, 3, 100 | merchant vehicles (DEC-084) |
| merchants.vehicle{0,1,2}.cost_permille, .danger_permille, .premium_permille, .cash_times; merchants.risk_aversion; orders.lead_weeks, orders.expiry_weeks; production.forecast_enabled, production.forecast_min_permille | 1000, 1000, 1000, 3; 1; 8, 52; 0, 100 | DEC-085; goods.csv gains made_to_order |
| core.absorb, core.stacking, core.demand_weighted | 1, 0, 1 (DEC-104) | classes hold no market; strength by highest or sum; cores keep their classes' demand per head ([Draft]) |
| core.continent.weight, core.nation.weight, core.region.weight, core.wage_permille, core.demand_permille | 3, 2, 1, 100, 50 (DEC-104) | a core's strength, and what a point is worth ([Draft]) |
| lod.mode | 1 (DEC-100) | 0 detail around the player, 1 every node detailed, 2 every node aggregate; for measuring |
| merchants.join_permille, merchants.member_fee_permille, merchants.support_floor_permille, merchants.support_share_permille, merchants.member_premium_permille | 0, 50, 200, 100, 500 | merchant associations (DEC-086) |
| state.strategic_subsidy_permille; contract.per_nation, contract.min_stock, contract.stock_weeks, contract.term_weeks, contract.base_discount_permille, contract.discount_per_unit_permille, contract.max_discount_permille | 0; 0, 200, 26, 52, 50, 1, 250 | DEC-087; facilities.csv gains strategic |
| contract.min_weeks, contract.max_weeks, contract.deposit_permille (replacing contract.term_weeks) | 8, 104, 200 | contract term and deposit (DEC-088) |
| contract.demand_share_permille, contract.credit_weeks | 500, 8 | contract volume and credit check (DEC-087) |
| food.{low,mid,high,best}.price_permille, food.{low,mid,high,best}.demand_permille | 0 (off) | food and drink grade prices and demand split (DEC-090, DEC-091) |
| production.forecast_primary, production.forecast_household_permille, production.forecast_cover_permille; investment.min_plan_permille | 0, 1000, 1000; 0 | market plans that reach raw goods; investors skip goods whose makers run below the share (DEC-091) |
| demand.semi.{low,mid,high,best}.luxury_share_permille | 250, 650, 900, 1000 | how far a semi-necessity is bought like a luxury (DEC-091) |
| traders.loan_share_permille, traders.loan_keep_weeks, traders.bank_loan_share_permille, traders.loan_interest_permille, traders.loan_weeks, traders.loan_repay_permille | 0 (sample data 10, DEC-094), 26, 0, 50, 52, 250 | loans to the traders' float, off while no share is set (DEC-092) |
| investment.payback_years, investment.max_per_node, investment.projects_per_node | 0, 24, 2 | NPC investment (DEC-080) |
| migration.emancipation_earliest_years | 0 | years before emancipation can be enacted (DEC-080) |
| tax.household_permille, tax.company_permille | 100, 100 | yearly income tax (DEC-071) |
| state.defense_permille, state.defense_maintenance_permille, state.defense_interval_weeks | 50, 200, 4 | defense as a share of the yearly budget, maintenance on top, weeks between purchases (DEC-072) |
| state.project_share_permille, state.projects_per_year, state.project_weeks, state.road_score_permille, state.trade_score_permille | 500, 3, 16, 120, 100 | share of the reserve above target for the year's plan; most per year; weeks each; what paving, and harbors and highways beyond their import bill, score against other needs ([Draft]) |
| state.defense_safety_cost_per_route, state.defense_max_safety_permille | 15000, 600 | yearly defense per route for a full danger cut, and the most it cuts (DEC-074) |
| state.subsidy_share_permille, state.subsidy_rate_permille, state.subsidy_years, state.subsidy_max_per_nation | 300, 200, 3, 2 | share of the excess subsidies may pay in a year; rate on output's value; length; most at once (DEC-074) |
| households.base_growth_permille, households.growth_response_permille, households.max_growth_permille, households.max_decline_permille | 8, 40, 13, 40 | yearly population change at full necessity supply, per full shortfall, and limits (DEC-074) |
| state.farmland_cost, state.mine_cost, state.harbor_cost, state.highway_cost_per_turn | 5000, 8000, 10000, 1000 | project costs ([Draft]) |
| state.land_bonus_permille, state.land_max, state.harbor_bonus_permille, state.harbor_max, state.highway_bonus_permille | 100, 5, 100, 3, 200 | output and charge bonuses of projects ([Draft]) |
| state.facility_levy_permille | 500 | share of a state enterprise's dividends the state collects ([Draft]) |
| merchants.<level> | 50/50/30/10/3 | merchant groups per node |
| merchants.<level>.start_cash | 5000/4000/3000/2000/1000 | starting cash per merchant group |
| merchants.<level>.max_weight, merchants.<level>.max_size | 4000/4000/2000/1000/400, 3200/3200/1600/800/300 | cargo limits per merchant group ([Draft]) |
| merchants.<level>.upkeep, merchants.bankrupt_weeks | 50/40/30/20/10, 5 | weekly upkeep of a detailed group, weeks below zero before bankruptcy ([Draft]) |
| merchants.traits, merchants.rumors | 1, 0 | merchant traits; judging prices by memory and rumor ([Draft]) |
| merchants.weigh_sales | 1 | groups weigh selling here against carrying the load on; 0 sells all on arrival (DEC-093) |
| merchants.judge_hops | 1, sample data 3 (DEC-095) | how many markets ahead a group values a load |
| merchants.bids | 0, sample data 2 | 1: a group with nothing to buy bids for what it would carry (DEC-095); 2: also when it leaves with something else, if the missing load pays more (DEC-096) |
| merchants.return_home | 0, sample data 1 (DEC-095) | a group stranded above its home with nothing to carry comes back down empty |
| merchants.cross_level | -1, sample data 2 (DEC-096) | the lowest level whose groups sail between continents; -1 none |
| merchants.cross_flow_permille | 0, sample data 650 (DEC-066) | share of the normal flow that aggregate trade moves between continents |
| markets.trader_spread_permille | 0, sample data 250 (DEC-098) | traders pay sellers the price less this margin |
| traders.float_keep_weeks, traders.float_payout_permille | 0 off, sample data 8; 20 (DEC-098) | weeks of households' wishes a float keeps; share of the excess paid to the nation's households a week ([Draft]) |
| merchants.flow_wage_permille | 0, sample data 1000 (DEC-098) | share of an aggregate flow's margin paid as wages at the origin ([Draft]) |
| merchants.cautious/normal/aggressive.loss_aversion | 0 uses merchants.risk_aversion; sample data 3, 2, 1 (DEC-099) | how many times a loss weighs against the same gain ([Draft]) |
| merchants.count_trip_costs | 0, sample data 1 (DEC-099) | a trip's upkeep and expected losses count when choosing a load ([Draft]) |
| merchants.hedge_fee_permille, hedge_floor_permille, hedge_fall_permille, hedge_chance_permille, hedge_bank_share_permille | 0 off, 850, 150, 300, 100; sample data 8, 900, 150, 60, 100 (DEC-099) | price cover ([Draft]) |
| merchants.judge_coin_units | 0, sample data 20 (DEC-096) | a market whose traders cannot pay for this many units is no market when judging a load; 0 off |
| merchants.port_crossers | 0, sample data 1 (DEC-096) | a large enough group based at a port sails whatever its personality |
| bank.international_balance | 0, sample data 1 (DEC-096) | nations keep their international bank between its starting share of the coin supply and twice that |
| producers.per_continent | 3 | facilities of each continental type (DEC-096) |
| credit.bill_cap_weeks | 0 off, sample data 8 (DEC-097) | a nation's traders may owe this many weeks of households' wishes in bills |
| credit.bill_weeks, credit.bill_discount_permille, credit.bill_repay_permille, credit.bill_default_weeks, credit.bill_bank_floor_permille, credit.bill_producers | 8, 20, 250, 26, 500, 0 | a bill's term; the bank's discount; share of the float that pays due bills a week; weeks overdue before protest; the share of its reserve target the bank keeps; producers take bills too ([Draft]) |
| credit.bond_buy_permille, credit.bond_keep_weeks, credit.bond_group_keep_times | 5, 26, 2 | share of idle money offered for bonds a week; weeks of spending households keep; times its starting cash a group keeps ([Draft]) |
| credit.state_debt_permille, credit.state_bond_interest_permille, credit.state_bond_weeks | 0 off, sample data 300; 40; 1040 | a nation's debt limit as a share of its coin supply; yearly interest; term ([Draft]) |
| credit.company_debt_permille, credit.company_bond_interest_permille, credit.company_bond_weeks, credit.company_default_weeks | 0 off, sample data 500; 70; 520; 26 | a group's debt limit as a share of its facilities' value; yearly interest; term; weeks behind before default ([Draft]) |
| credit.working_weeks | 0 off, sample data 4 | weeks of upkeep and inputs a group keeps its facilities in cash for ([Draft]) |
| merchants.generation_permille, merchants.mutation_permille, merchants.fitness_capital_permille | 50, 150, 700 | yearly handover share; chance of a random trait; capital's weight in fitness ([Draft]) |
| merchants.<personality>.spend_permille, merchants.<personality>.min_margin_permille | cautious 500 / 200, normal 750 / 100, aggressive 1000 / 0 | share of cash risked, least margin ([Draft]) |
| merchants.<personality>.range_level | cautious Region, normal Nation, aggressive Continent | where a group may trade, under its home's ancestor at that level |
| merchants.<occupation>.capacity_permille, .upkeep_permille, .distance_power | local 1000 / 1000 / 2, expedition 1000 / 1000 / 0, peddler 500 / 500 / 1 | capacity, upkeep, how distance counts ([Draft]) |
| merchants.flow_permille | 500 | aggregate trade: share of stock moved per unit of margin ([Draft]) |
| production.variance_permille | 100 | weekly output variance of detailed facilities |
| facility.<tier>.start_cash | 500/2000/1000 | starting cash of a facility and of a new operator |
| terrain.<terrain>.extra_turns, terrain.<terrain>.cost_permille, terrain.<terrain>.danger_permille | road 0/1000/50, mountain 1/1500/100, river 0/1200/60, sea 0/1500/80 | terrain effects on routes ([Draft]) |
| danger.region_permille, danger.value_scale | 1000, 2000 | default region danger; cargo value that doubles the chance |
| encounter.cash_loss_permille, encounter.cargo_loss_permille | 300, 300 | share lost in an encounter |
| guard.base_cost, guard.cost_permille, guard.reduction_permille | 10, 20, 700 | guards ([Draft]) |
| insurance.premium_permille, insurance.refund_permille | 30, 800 | guild insurance ([Draft]) |
| passage.base_fee, passage.per_unit | 20, 2 | passage on water routes without a ship |
| ship.price, ship.max_weight, ship.max_size, ship.resale_permille | 5000, 3000, 2400, 700 | the player's ship |
| road.cost_reduction_permille, road.danger_reduction_permille | 400, 500 | effect of a fully paved road ([Draft]) |
| road.upkeep, road.decay_permille | 5, 5 | weekly upkeep per paved route; condition lost per unpaid week |
| road.build_cost_per_turn, road.build_weeks | 300, 8 | building a road ([Draft]) |
| groups.company.start_cash, groups.guild.start_cash, groups.state.start_cash, groups.private.start_cash | 20000, 2000, 50000, 1000 | starting cash per interest group kind ([Draft]) |
| groups.companies_per_nation | 2 | NPC companies founded per nation |
| guild.fee, guild.insurance_discount_permille, guild.license_discount_permille | 200, 300, 300 | yearly guild fee and member discounts ([Draft]) |
| guild.hall_max_weight, guild.hall_max_size | 1000, 800 | a member's guild hall warehouse |
| bank.initial_reserve_permille, bank.deposit_interest_permille, bank.exchange_fee_permille | 100, 20, 30 | starting reserves (share of coin supply), yearly deposit interest, exchange fee ([Draft]) |
| bank.loan_interest_permille, bank.loan_term_weeks, bank.loan_to_value_permille, bank.grace_weeks | 100, 52, 500, 4 | loans ([Draft]) |
| bank.auction_weeks, bank.auction_reserve_permille | 2, 800 | auction of seized facilities ([Draft]) |
| bank.state_reserve_target_permille, bank.state_spending_permille | 2000, 20 | reserve target as a multiple of the nation's share of the initial reserve share of the coin supply; weekly share of the reserve above it spent as public wages ([Draft]) |
| facility.dividend_buffer_weeks, facility.dividend_permille | 26, 100 | weeks of costs a facility keeps; weekly share of the cash above it paid as dividends ([Draft] values from DEC-061) |
| groups.payout_permille | 800 | share of its dividends a group passes to its home node's households ([Draft]) |
| bank.international_reserve_permille | 20 | share of a continent's starting coin supply its international bank holds for exchange ([Draft]) |
| bank.target_reserve_permille, bank.max_rate_multiplier_permille | 500, 3000 | reserve ratio below which loan rates rise, and the cap on that rise ([Draft]) |
| bank.npc_loan_term_weeks, bank.npc_buffer_weeks, bank.npc_max_reserve_share_permille | 26, 8, 100 | NPC loans: term, weeks of upkeep borrowed beyond the shortfall, largest share of a reserve one loan takes (0 turns NPC lending off) ([Draft]) |
| finance.history_quarters | 20 | quarterly records kept (at least 4) |
| finance.event_chance_permille, finance.event_end_reserve_permille | 20, 500 | chance of a financial event per nation and quarter; reserve ratio that ends one ([Draft]) |
| finance.bank_run_drain_permille, finance.bank_run_withdraw_permille, finance.rate_spike_permille | 200, 100, 2000 | event effects: reserve drained by a run, deposit share the player may withdraw weekly during a run, rate multiplier of a spike ([Draft]) |
| facility.<tier>.value | 3000/8000/5000 | collateral value of a level-1 facility (times its level) |
| license.price_permille, license.merchant_share_permille | 20000, 200 | license price (x Mid base price), share of licensed merchant groups ([Draft]) |
| guidance.weight.buy_license_permille | 2000 | Trade signal of buying a license |
| toll.tariff_permille, toll.specialty_surcharge_permille | 30, 70 | default toll rates on entering a node ([Draft]) |
| specialty.output_permille, specialty.grade_up_permille, specialty.price_permille | 1250, 200, 800 | specialty output bonus, share one grade higher, local anchor price ([Draft]) |
| facility.<tier>.upkeep | 50/100/50 | weekly upkeep of a level-1 facility (times its level) |
| facility.bankrupt_weeks | 5 | consecutive weeks with cash below zero before bankruptcy |
| cargo.lost_warehouse_weeks | 8 | weeks a lost facility's warehouse can still be emptied |
| producers.<level>.<tier> | village primary 4, tertiary 2; city secondary 4 | producer groups per node |
| grade.<grade>.price_permille | 600/1000/1800/3500 | grade price multipliers |
| price.adjust_permille, price.max_step_permille, price.anchor_permille, price.stock_pressure_permille, price.min | 250, 150, 20, 250, 1 | pricing |
| currency.mint_permille, currency.gold_reserve, currency.silver_reserve, currency.coin_supply, currency.coins_per_gold, currency.coins_per_silver | 30, 1e6, 1e7, 5e6, 10, 1 | currency |
| policy.step_permille, policy.max_steps | 100, 5 | one policy step and the limit either way ([Draft]) |
| policy.move_threshold_permille, policy.resistance_permille | 100, 1000 | share of influence a policy needs to move, and the status-quo bias per step already taken ([Draft]) |
| policy.asset_weight_permille, policy.lobby_weight_permille, policy.influence_per_job | 1000, 3000, 50 | influence per coin of assets, per coin lobbied, per job ([Draft]) |
| policy.jobs_per_facility_level, policy.jobs_per_merchant_group | 20, 10 | jobs counted for influence ([Draft]) |
| policy.npc_lobby_share_permille, policy.loss_tilt_permille | 50, 2000 | NPC groups' yearly lobbying share of cash; lean toward protection per share of worth lost ([Draft]) |
| policy.inflation_opposition_permille | 100 | published yearly inflation at which groups other than state enterprises fully oppose minting ([Draft]) |
| currency.endowment_spread_permille | 300 | +- variation of continents' starting reserves |
| guidance.decay_permille | 10 | weekly guidance decay |
| guidance.weight.buy_facility_permille, guidance.weight.found_company_permille | 1000, 5000 | guidance signal weights |
| lod.focus_level | Region | default LOD policy: detailed subtree around the player |
| player.start_node, player.start_cash | random village from the seed, 500 | player start; start_node is a test/debug override |
| cargo.cart.max_weight, cargo.cart.max_size | 400, 300 | player cart capacity ([Draft] placeholders) |
| cargo.warehouse.max_weight, cargo.warehouse.max_size | 2000, 1500 | warehouse at each player facility ([Draft]) |

`<level>` = continent, nation, region, city, village. `<tier>` = primary, secondary, tertiary.
`<grade>` = low, mid, high, best.
