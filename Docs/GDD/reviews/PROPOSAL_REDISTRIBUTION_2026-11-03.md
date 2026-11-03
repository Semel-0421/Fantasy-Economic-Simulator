# Proposal: redistribution that keeps the 80% income share (2026-11-03)
Decided 2026-11-03: B and D, with a tax on luxury purchases (DEC-127). Korean copy: [PROPOSAL_REDISTRIBUTION_2026-11-03.ko.md](PROPOSAL_REDISTRIBUTION_2026-11-03.ko.md). Nothing here is in the code: the trial was temporary and removed. Code as of `c4470e2`.

## The problem
Income goes by quintile 2/3/5/10/80% (DEC-115), and the owner keeps the 80%. In every level 3 market looked at, the four lower quintiles hold nothing and the top one everything (seed 1, one market: 0 / 0 / 0 / 0 / 175,262 coins), so only a fifth of the people buy necessities there. Two things keep it so:
- Every coin that reaches a node, wages, dividends, state pay or anything else, is split again 2/3/5/10/80%. Money handed to a node goes back to the top.
- The top fifth's spending does not grow with what it holds: luxury demand follows income, not wealth, so its savings pile up (17M of the 20M households hold at year 30).

## Trial
A weekly transfer from the top quintile's holdings, split evenly among the other four, straight to them and not through the node's income. It stands for any of the directions below, which move money the same way. Sample map, three seeds, year 30.

| Weekly share of the top's holdings | People (M) | Growth a year | Met, level 3 / level 4 | Household spending in year 30 (M) | Holdings by quintile at year 30 (M) |
|---|---|---|---|---|---|
| None (now) | 19.78 to 20.08 | -0.34 to -0.42% | 18-21 / 62-78% | 1.36 to 1.49 | 0.0 / 0.2 / 0.6 / 1.6 / 17 |
| 0.5% | 20.37 to 20.89 | -0.30 to -0.42% | 16-19 / 77-99% | 1.18 to 1.83 | 3 / 3.4 / 3.8 / 4.9 / 3.4 |
| 2% | 20.50 to 21.16 | -0.27 to -0.29% | 16-21 / 87-101% | 1.17 to 1.47 | 3.9 / 4.1 / 4.6 / 5.7 / 0.9 |

What it shows: the people of level 4 are then fed and their numbers rise by 0.6 to 1.1M. Level 3 does not move, and households spend no more: the lower quintiles now hoard too. In level 3 money was the first limit and the goods are the next. Households there can only buy what is in their market (on hand covered about two thirds of their wish at year 10), so more money does not fill level 3 until goods reach it. Facility and merchant failures stayed about the same.

## Directions
All keep the 80% income share. Each sends money to the lower quintiles directly, not through the node's income.

**A. Service and patronage.** The top quintile pays a share of what it holds beyond some weeks of its own spending as wages for servants, craftsmen and retainers, to the lower quintiles. It reads as consumption by the rich, of services. One rule in the household settlement, a weekly share and a reserve in weeks.

**B. Alms for the hungry (recommended).** Where a lower quintile cannot pay for its fifth of the necessities this week, the top quintile of the same node pays the gap, up to a share of what it holds. Money goes only where people go short, so it does not build new hoards, and in a node where everyone is fed nothing moves. Cost: a little more code, as it reads the week's necessities; it does nothing for consumption beyond necessities.

**C. A tax on hoards, paid out to the poor.** The state takes a yearly share of the top quintile's holdings above some weeks of spending and pays it to the lower quintiles of the same nation by head, straight to them. Uses the state's books, so the player sees it. Cost: crosses nodes, so coins move from rich towns to poor ones; a political rule as much as an economic one.

**D. Rich spending grows with wealth.** The top quintile's luxury demand grows with what it holds, not only with income. Its money then reaches the markets, but there it is income again and goes 80% back to the top, so little reaches the poor. Not tried; the weakest lever of the four.

## Recommendation
B, with a share of 2% a week of the top's holdings as its limit, then measure six seeds over 30 years. And separately, since money alone does not reach level 3: find out why goods do not reach level 3 markets when their households can pay.

## Decisions needed
1. Direction: A, B, C or D, or a mix.
2. The share (and for A and C, the reserve in weeks).

## Limits
Three seeds. The trial moved money evenly to the four lower quintiles each week, without any of the conditions above. Levels 3 and 4 are the code's numbers.
