# Proposal: who pays the merchants (2026-11-03)
Decided 2026-11-03: direction A, necessities to households first (DEC-124). Korean copy: [PROPOSAL_TRADER_COINS_2026-11-03.ko.md](PROPOSAL_TRADER_COINS_2026-11-03.ko.md). Nothing here is in the code: the measurements were temporary and removed.

## The problem
After DEC-123 (bills no more than the traders' coins), merchant failures over 30 years nearly doubled (7,352 to 8,550, against 3,918 to 5,352, six seeds), and no group keeps a cart or wagon. The cause is not a bug. The traders' coins, which pay every merchant sale, are nearly empty all the time. Before DEC-123, bills hid this: merchants were paid in bills that about 5% of were ever paid.

## What was measured
Sample map, seeds 1 and 2, current code, unless stated.

| Measure | Value |
|---|---|
| Coins in all, year 1 | 10.0M |
| Held by households | 8.0M |
| Households' spending a year | 0.65M to 0.77M (about 9% of what they hold) |
| Traders' coins at the end of each year | 0 to 2 |
| Traders' coins, average over two years, per nation | 9,400 to 10,000 |
| Unsold stock in markets | 7.5M at year 1, 9.8M at year 3 |
| Merchant sales, two years | 142,000 to 188,000 (1.09M to 1.34M before DEC-123) |

By market level, seed 1, year 2:

| Level | Markets | People | Needs met | Necessity cells empty | Necessity stock | Price of stocked necessities |
|---|---|---|---|---|---|---|
| 3 | 8 | 7.0M | 18% | 293 of 312 | 15,000 | 326% of base |
| 4 | 20 | 13.2M | 115% | 316 of 520 | 8.66M | 49% of base |

So necessities pile up at half price where they are made, and the other level starves at three times the price with 1.2M coins in households' hands. Prices answer supply as they should, and the gap pays for any trip. The merchants who would carry it cannot be paid: a sale is paid only out of the traders' coins of the nation (DEC-061), and those coins fill only when households buy, which the starving level cannot do, as it has nothing to buy. Producers are paid out of the same coins first, up to half of what is there as the week starts (`markets.producer_float_permille`); the coins are gone before the merchants arrive.

Turning on loans to the traders (DEC-092, now off) does not help: at 5% and 20% the traders' coins rose to 10,200 to 14,800 and merchant sales fell to 122,000 to 148,000; failures stayed the same (394 to 456 over two years).

## Directions
**A. Merchants sell to households directly (recommended).** At its destination a group may sell what the households there wish to buy this week, paid from their cash, at the price; the traders' coins pay only for the rest, as now. This is the step that is missing: the starving markets have buyers with coins and no traders with coins. It keeps DEC-061 for wholesale and DEC-123 for bills. Cost: a second way of selling in the merchant job, and its Burst copy; household spending now also happens on a merchant's arrival.

**B. Households order necessities.** Extend the made-to-order book (DEC-085) to necessities: households where a necessity is missing place orders, which merchants fill and the households pay on collection. Uses a mechanism that exists. Cost: orders arrive late, households pay only on collection, and merchants must learn to fill orders; slower to show effect.

**C. Give the traders working coins.** Seed and keep the traders' coins at weeks of the trade they serve, from households' savings or the bank, repaid as goods sell. Keeps one way of paying. Cost: the trial of DEC-092 loans raised the coins without raising sales, so this needs its own rule for where the coins go, and the first trial says it may not help.

**D. Take DEC-123 back.** Bills again without limit. Merchants are paid in paper that is almost never paid; failures fall back, banks and households lose 450,000 to 620,000 in two years. Not recommended: it hides the problem.

## Recommendation
A, then measure with six seeds over 30 years: failures, vehicles, needs met by level, and population. If A alone does not keep groups alive, add C.

## Decisions needed
1. Direction: A, B, C or D.
2. For A: may a merchant sell to households beyond what is in the market that week, or only up to what households wish to buy this week? Recommended: only up to the week's wish.
3. The failing test `InvestmentTests.Merchants_BuyBetterVehicles_FromTheMarket_KeepingCoinsConserved` stays failing until a direction is in; nothing is committed meanwhile.

## Limits
Two or three seeds per measure, two years for the flows. Level 3 and 4 are named by the code's levels; which of them are cities and which villages was not checked against the map. Direction A was not tried.
