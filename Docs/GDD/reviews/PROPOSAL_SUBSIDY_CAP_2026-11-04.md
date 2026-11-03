# Proposal: subsidised producers and the cap on facilities per node (2026-11-04)
Korean copy: [PROPOSAL_SUBSIDY_CAP_2026-11-04.ko.md](PROPOSAL_SUBSIDY_CAP_2026-11-04.ko.md). Decided 2026-11-04: option B with the state's own cap, kept by its land projects too (DEC-135). Code as of `32815db`.

## What was asked
Why cities hold more facilities than investment's cap of 24 a node (`investment.max_per_node`, DEC-080).

## What was found
Measured on seeds 1 to 3 over 30 years, each new facility traced to what opened it:

| Seed | Opened by subsidies | Of them at a node already at the cap | By investment | At the cap | By state projects | Nodes over 24 at year 30 | Most at a node |
|---|---|---|---|---|---|---|---|
| 1 | 149 | 56 | 51 | 0 | 4 | 5 | 45 |
| 2 | 151 | 80 | 48 | 0 | 2 | 5 | 45 |
| 3 | 157 | 75 | 33 | 0 | 3 | 6 | 45 |

At the start no node holds more than 21. Investment keeps to its cap: none of its facilities opened at a node at the cap. The overflow comes from subsidies.

## Why: a design gap, not a coding error
- The cap was set in DEC-080 for investment only. Subsidies (DEC-074) came earlier and have none.
- Each year of a subsidy policy opens one producer at the node shortest of necessities. Cities are nearly always shortest (level 3 meets 29% to 35% of its necessities), so the producers go to cities. A good that cannot be made there goes to the nation's core city (DEC-104).
- Only land (DEC-128) stops it now: a city has 40 plots, plus those state projects open, hence the most at 45. Before land, cities reached 58.
- Subsidies open three to four times as many facilities as investment does. They are most of the economy's growth in facilities.

## Options
- **A. The cap holds for subsidies too.** A site at the cap is skipped; the producer opens at the next node shortest of necessities where the good can be made, with a free plot and room under the cap. If there is none, no producer that year (the subsidy still pays output).
- **B. A's rule, and the fallback for goods a node cannot make spreads over the nation's core towns,** the one shortest of necessities first, rather than always the capital.
- **C. No change.** Land is the limit; the cap stays investment's alone, and the documents say so.

Recommended: **B**. It keeps the one rule for every opener, and spreads the fine workshops that today pile into the capital.

## Limits
Three seeds; no other measurement. Golden hashes change with A or B. The number of subsidised openings itself (DEC-074: one a policy a year, two policies a nation) is left to the balancing step.
