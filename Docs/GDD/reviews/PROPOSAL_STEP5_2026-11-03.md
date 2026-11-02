# Proposal: step 5, margin and balance (2026-11-03)
[Draft] For the owner's decision. Korean copy: [PROPOSAL_STEP5_2026-11-03.ko.md](PROPOSAL_STEP5_2026-11-03.ko.md). Nothing here is in the code: the trials were temporary and removed.

## The problem
The 25% traders' margin stops at the sale. Producers sell at 75% of the price but decide at 100%, so investors build what loses money (C20, C21). Separately, groups hold about five times what households hold (C23), and people keep falling (C18).

## Trials
Sample map, seeds 1 to 3, year 30, mean of the three, after DEC-111. "Processed" is ten goods: flour, malt, bars, yarn, clothes, ale, bread, tools, cookware. Rows starting with "+" add to "decisions see the offer".

| Trial | People (M) | Growth a year | Met, city / village | Groups / households (M) | Failures in 30 years |
|---|---|---|---|---:|---|
| Now | 15.73 | -0.29% | 83 / 153% | 12.5 / 2.6 | 7 |
| Decisions see the offer | 15.62 | -0.27% | 82 / 151% | 12.6 / 2.3 | 12 |
| + processed yield x4/3 | 16.98 | -0.15% | 88 / 156% | 13.0 / 2.6 | 9 |
| + processed yield, payout 50% | 16.99 | -0.17% | 86 / 158% | 10.9 / 3.0 | 7 |
| + processed yield, payout 80% | 16.94 | -0.09% | 93 / 153% | 10.6 / 3.9 | 11 |
| + processed anchor x4/3 | 15.45 | -0.44% | 79 / 152% | 12.9 / 2.3 | 6 |
| Producers paid full price | 15.64 | -0.37% | 82 / 152% | 12.7 / 2.1 | 18 |
| Payout 50% only | 15.58 | -0.25% | 86 / 146% | 8.9 / 3.2 | 6 |
| Payout 80% only | 15.35 | -0.26% | 85 / 151% | 7.3 / 3.3 | 17 |

Limits: three seeds, and they differ widely (growth between seeds differs by up to 0.4 points, failures from 0 to 41), so small gaps mean little. x4/3 is one blunt number for all ten goods, not a tuning. Payout 80% left 3.2M in one seed's floats. Starting incomes (C36), elasticity (C19), inflation (C22) and the growth rule (C18) were not tried.

## Directions
**A. Keep the margin, correct decisions, raise processed yields (recommended).** Output is valued at the offer in investment, working cash, bids and aids; seized cargo and contracts pay the offer (C28); processed recipes give more per input. Keeps DEC-098 as confirmed. It is the only direction whose trials raised people and city supply together. Cost: every recipe is retuned, and more output per input is a design change.

**B. Keep the margin, correct decisions, raise processed prices.** The same, with higher anchors instead of yields. Keeps recipes as written. In the trial it did worse than now: households pay more for the same goods.

**C. Margin on the retail step only.** Producers are paid the full price, merchants keep the margin. Removes C20 at the root and needs no retuning, but reopens the confirmed DEC-098. In the trial failures more than doubled and households held less.

**D. Code only, numbers later.** Only the corrected decisions and C28, then stop. Least risk, but the trial shows no gain alone and more failures, so most of the work stays open.

## Recommendation
A in this order: corrected decisions with a test that every recipe has a non-negative margin near anchor; yields per recipe; then the dividend; then measure again with more seeds. Each step records new hashes.

## Decisions needed
1. Direction: A, B, C or D.
2. Group payout: 25% in config, 80% in the documents, or between. Recommended: 50%, then re-measure. 80% looked best on growth and supply but with more failures and a swollen float in one seed.
3. Starting income: config 5, 5, 5, 4, 2 or documents 300, 300, 300, 500, 2000. Recommended: documents, since the config values look like a unit slip; not tried.
4. Necessity elasticity: treat 0.5 as the mean and normalise the curve, or record the real 0.97. Recommended: normalise, then re-measure; not tried.
5. Steady state: is falling population a defect? The trials still mostly fall. Recommended target: within 0.1% a year of steady at year 30, with cities at 90% of necessities or more.
