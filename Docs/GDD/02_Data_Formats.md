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
| class | `necessity` or `luxury` (optional, default necessity); sets the price elasticity ([Draft]) |

Each row expands to 4 items (`key@low`, `@mid`, `@high`, `@best`) with prices scaled by
`grade.<grade>.price_permille` (defaults 600 / 1000 / 1800 / 3500).
Good id = `baseIndex * 4 + grade`.

## recipes.csv

`recipe,role,good,grade,qty` — one row per input (`in`) or output (`out`), weekly at level 1.

## facilities.csv

`key,tier,recipe,state_owned` — tier is `Primary`, `Secondary` or `Tertiary`; `state_owned` (optional, 1)
gives facilities of the type to their nation's state enterprise. Spawn counts per node come from
`producers.<level>.<tier>` in config; the facility type is picked deterministically among that tier's defs.

## events.csv

`key,level,category,target,mul_permille,duration,chance_permille` — rolled per node of `level` each
turn; `category` may be `*`; `target` is `Supply`, `Demand` or `Value`. Supply/Demand modifiers scale
weekly flows, so any duration >= 1 works. A `Value` event must last at least the longest repricing
period among its level and the levels below it, or loading fails.

## market_profiles.csv (required)

`level,category,grades` — which goods markets of a level carry. `category` and `grades` accept `*`;
grades may be a list such as `Low|Mid`. Drives the sparse market layout and therefore memory.

## map_nodes.csv / map_routes.csv

- Nodes: `key,name,level,parent,tags`. Continents have an empty parent. A node's parent must be one level
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
| households.<level>.population, households.<level>.income | continent 20000 / 4 ... village 500 / 2 | default population and weekly income per head ([Draft]) |
| households.q1..q5.income_share_permille | 20, 30, 50, 100, 800 | income share per quintile, poorest first; must sum to 1000 ([Draft]) |
| households.base_growth_permille, households.growth_response_permille, households.max_change_permille | 5, 50, 30 | yearly population change ([Draft]) |
| households.income_response_permille, households.income_max_change_permille | 500, 500 | yearly income change toward measured income: share of the gap, cap as a share of the larger value ([Draft]; cap was 50) |
| households.budget_weeks | 26 | households spend at most their cash over this many weeks a week ([Draft]) |
| markets.trader_float_permille | 100 | share of the starting money nobody holds that goes to the traders' floats; households get the rest ([Draft]) |
| merchants.<level> | 50/50/30/10/3 | merchant groups per node |
| merchants.<level>.start_cash | 5000/4000/3000/2000/1000 | starting cash per merchant group |
| merchants.<level>.max_weight, merchants.<level>.max_size | 4000/4000/2000/1000/400, 3200/3200/1600/800/300 | cargo limits per merchant group ([Draft]) |
| merchants.<level>.upkeep, merchants.bankrupt_weeks | 50/40/30/20/10, 5 | weekly upkeep of a detailed group, weeks below zero before bankruptcy ([Draft]) |
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
| price.adjust_permille / max_step_permille / anchor_permille / stock_pressure_permille / min | 250 / 150 / 20 / 250 / 1 | pricing |
| currency.mint_permille, gold_reserve, silver_reserve, coin_supply, coins_per_gold, coins_per_silver | 30, 1e6, 1e7, 5e6, 10, 1 | currency |
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
