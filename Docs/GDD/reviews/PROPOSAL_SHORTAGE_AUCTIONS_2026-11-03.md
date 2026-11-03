# Proposal: necessities that do not reach the starving, and facilities sold round and round (2026-11-03)
Decided 2026-11-03: shortage C as a constant-elasticity curve (DEC-126; the C written here was already in), auctions C and D. The figure 0.97 below is wrong: the elasticity is 0.6 since DEC-112. Korean copy: [PROPOSAL_SHORTAGE_AUCTIONS_2026-11-03.ko.md](PROPOSAL_SHORTAGE_AUCTIONS_2026-11-03.ko.md). Nothing here is in the code: the measurements were temporary and removed. Code as of `feb6c41`.

## 1. Necessities do not reach the starving level

### The problem
After DEC-124, level 3 markets still meet 18 to 24% of their necessities (six seeds, year 30), while level 4 meets 58 to 91% and holds the stock.

### What was measured
Seeds 1 and 2, year 2, weights per week.

| Measure | Value |
|---|---|
| Level 3 necessities wished at the reference price | 11,600 |
| Level 3 necessities wished at today's price, not covered by stock, stores or supply | 1,000 to 1,250 |
| What all merchant groups carry on one trip | 84,000 |
| Level 4 necessity stock | 9.3M to 9.6M |
| Price of stocked necessities, level 3 / level 4 | 326% / 49% of base |

Carrying is not short. At three times the base price, households wish for a tenth of what they need, as the necessity demand curve falls steeply with price (0.97 per 100% above the anchor; the step 5 proposal's decision 4). DEC-124 sells into that small wish. What is left of a load finds no traders with coins, so it is not carried; so no stock builds up there, so the price stays high, so the wish stays small. Not a bug: each rule does what it says.

### Directions
**A. Merchants lower the price to clear their load (recommended).** When the households' wish at the price is less than the load, the group sells to them at the lower price where their wish takes it, but not below what the load cost it. The households' lower price then reaches the market through the demand it meets. A change inside DEC-124 and the merchant job only. Cost: a search for the price per sale; households pay less than the list price.

**B. Leave the rest with the traders on credit.** What the households do not take goes into the market's stock, and the traders pay the group as they sell it. Stock builds up where it is short, so the price kernel lowers the price. Cost: a new table of what traders owe each group, paid from the float, like bills but tied to goods; a group's coins arrive late.

**C. Flatten the necessity curve.** Treat 0.5 as the mean elasticity and normalise the quintiles (the step 5 proposal's decision 4, recommended there and not tried). Hungry households then keep buying at high prices. Cost: changes every market's demand; all balance moves.

**D. Leave it.** Level 3 stays at about a fifth of its needs.

### Recommendation
A, measured with six seeds over 30 years. If level 3 stays below half, add C.

## 2. Facilities sold round and round

### The problem
Over 30 years, 4,071 to 6,230 auctions closed (six seeds), against 0 to 219 facility bankruptcies counted. A facility with a bank loan that fails is seized and auctioned (DEC-037) without the bankruptcy event, so the count missed them.

### What was measured
Seeds 1 and 2, ten years.

| Measure | Seed 1 | Seed 2 |
|---|---|---|
| Auctions | 975 | 1,518 |
| Facilities auctioned at least once | 222 of 292 | 258 of 297 |
| Median weeks between two auctions of the same facility | 40 | 33 |
| Shortest | 15 | 12 |
| Most auctions of one facility | 16 | 18 |
| By tier: primary / secondary / tertiary | 127 / 53 / 42 | 145 / 50 / 63 |
| Average price | 3,636 | 3,320 |

The loop: a facility that loses money borrows from the bank as soon as its cash is below zero (as far as its value allows), defaults, is seized and auctioned. Without a player's bid a local family must buy it at the reserve price (DEC-039), and what the family lacks comes from the households around (DEC-107). The facility still loses money, so it goes round again. In ten years that moves about 3.5M (seed 1) to 5.0M (seed 2) coins from households to the bank, besides each new operator's starting cash. Not a bug: each step follows its decision. The cause underneath is that most facilities lose money year after year (DEC-120's measurement), which has still not been looked into.

### Directions
**A. No forced purchase, and no loans to losers (recommended).** An auction without a buyer who wants the facility at the reserve closes it (level 0, as DEC-120 does), instead of a family paying with the households' money. A family buys only from its own cash, and only when the facility would pay its way at today's prices (the investment test). The bank lends to a facility only if it earned money in its last tax year. Cost: many facilities close at first, as in DEC-120's trial (about 90% within 30 years at two losing years), so output falls until the losses are understood.

**B. Only stop the loans to losers.** The bank lends only to a facility that earned money in its last tax year. Losing facilities then fail through ordinary bankruptcy and stay with their group under a new manager. Smaller: one condition. The households still fund new managers.

**C. Count it and leave it.** Publish the bankruptcy event on seizure, so the counts are right, and change nothing else.

**D. Find out why facilities lose money first.** Look into the chronic losses before changing the loop. Slowest, and the other directions may turn out not to be needed.

### Recommendation
C at once, since the count is wrong either way, then D before A or B: the loop is a symptom, and A closes facilities whose losses may come from a fault elsewhere.

## Decisions needed
1. Shortage: A, B, C or D.
2. Auctions: A, B, C or D; C can go with any of them.

## Limits
Two seeds for the details, six for the totals. Levels 3 and 4 are the code's numbers, not checked against the map's names. No direction was tried.
