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

Each row expands to 4 items (`key@low`, `@mid`, `@high`, `@best`) with prices scaled by
`grade.<grade>.price_permille` (defaults 600 / 1000 / 1800 / 3500).
Good id = `baseIndex * 4 + grade`.

## recipes.csv

`recipe,role,good,grade,qty` — one row per input (`in`) or output (`out`), weekly at level 1.

## facilities.csv

`key,tier,recipe` — tier is `Primary`, `Secondary` or `Tertiary`. Spawn counts per node come from
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
  at city level or above.
- Routes: `from,to,travel_turns,cost_per_unit,base_fee` (directed; add both directions for two-way
  travel). `base_fee` is optional (default 0) and paid once per trip.

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
| merchants.<level> | 50/50/30/10/3 | merchant groups per node |
| merchants.<level>.start_cash | 5000/4000/3000/2000/1000 | starting cash per merchant group |
| merchants.<level>.max_weight, merchants.<level>.max_size | 4000/4000/2000/1000/400, 3200/3200/1600/800/300 | cargo limits per merchant group ([Draft]) |
| merchants.<level>.upkeep, merchants.bankrupt_weeks | 50/40/30/20/10, 5 | weekly upkeep of a detailed group, weeks below zero before bankruptcy ([Draft]) |
| merchants.flow_permille | 500 | aggregate trade: share of stock moved per unit of margin ([Draft]) |
| production.variance_permille | 100 | weekly output variance of detailed facilities |
| facility.<tier>.start_cash | 500/2000/1000 | starting cash of a facility and of a new operator |
| bank.initial_reserve_permille, bank.deposit_interest_permille, bank.exchange_fee_permille | 100, 20, 30 | starting reserves (share of coin supply), yearly deposit interest, exchange fee ([Draft]) |
| bank.loan_interest_permille, bank.loan_term_weeks, bank.loan_to_value_permille, bank.grace_weeks | 100, 52, 500, 4 | loans ([Draft]) |
| bank.auction_weeks, bank.auction_reserve_permille | 2, 800 | auction of seized facilities ([Draft]) |
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
| currency.endowment_spread_permille | 300 | +- variation of continents' starting reserves |
| guidance.decay_permille | 10 | weekly guidance decay |
| guidance.weight.buy_facility_permille, guidance.weight.found_company_permille | 1000, 5000 | guidance signal weights |
| lod.focus_level | Region | default LOD policy: detailed subtree around the player |
| player.start_node, player.start_cash | random village from the seed, 500 | player start; start_node is a test/debug override |
| cargo.cart.max_weight, cargo.cart.max_size | 400, 300 | player cart capacity ([Draft] placeholders) |
| cargo.warehouse.max_weight, cargo.warehouse.max_size | 2000, 1500 | warehouse at each player facility ([Draft]) |

`<level>` = continent, nation, region, city, village. `<tier>` = primary, secondary, tertiary.
`<grade>` = low, mid, high, best.
