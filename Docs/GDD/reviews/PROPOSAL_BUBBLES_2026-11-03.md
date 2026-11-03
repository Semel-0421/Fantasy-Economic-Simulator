# Proposal: credit bubbles, a stock market and a property market (2026-11-03)
Decided 2026-11-03: start with step 1, land, on these values (DEC-128). Built: steps 1 to 6 (DEC-128 to DEC-133), and what they left out (DEC-134). Korean copy: [PROPOSAL_BUBBLES_2026-11-03.ko.md](PROPOSAL_BUBBLES_2026-11-03.ko.md). Nothing here is in the code. Code as of `1d56283`.

## What the owner decided
1. Three assets for speculation: shares in facilities (A), shares in trading ventures (C), and land (D).
2. A bubble bursts by indicators, and the burst can be foreseen.
3. A bubble's reach follows how far its market is shared.
4. The player may take part, and may sell short once a condition is met.
5. A stock market and a property market are added.

## What exists to build on
- A facility's price follows its earnings: its value, two years of earnings at today's prices, and its cash (DEC-114).
- The bank lends to facilities and merchants against their value (NPC loans), and seizes and auctions what is not repaid (DEC-037, DEC-039).
- Corporate and state bonds, sold to households' and groups' idle money (DEC-097).
- Three financial events, bank run, credit crunch and rate spike, today drawn at random each quarter (DEC-043).
- The traders' coins, one purse per nation, and one coin per continent (DEC-061).
- Idle money: the top quintile holds 15 to 17M of the households' coins at year 30 (DEC-115, DEC-127).
- There is no land and no share: a facility belongs whole to one group or the player.

## Design

### 1. The assets
**Facility shares (A).** Each facility is split into a fixed number of shares (say 100). Its group keeps those not sold. A share's fundamental value is the DEC-114 price divided by the shares; dividends (DEC-061) are paid per share. Groups list shares to raise money, as they now sell bonds.

**Venture shares (C).** A venture is a company with no facility: it fits out trading voyages along routes (DEC-034), and pays out when a voyage returns. Its fundamental value is its expected voyage earnings. It is the asset furthest from production, so its bubble can be the largest and its burst hurts production least.

**Land (D).** Each node has plots: a number set by its level, bought and sold, and needed to found a facility (a facility stands on a plot). Rent: a plot under a facility pays its owner a share of the facility's earnings; a plot under houses pays a share of the households' income, so land in growing towns earns more. Its fundamental value is its rent over some years.

### 2. The markets, and how far each is shared
A market lives at a node and is shared by the nodes whose trade reaches it, as goods markets are now. [Draft] By default:

| Market | Lives at | Shared by | Coin |
|---|---|---|---|
| Stock exchange | each nation's capital | the nation's nodes and groups | the continent's |
| Continental exchange (ventures) | each continent's seat | the continent | the continent's |
| Property market | each node with land | that node's households, groups and the player | the continent's |

A bubble spreads as far as its market is shared: a property bubble in one town touches the nation through the bank's loans on it; a stock bubble the nation; a venture bubble the continent.

### 3. How a bubble forms
Each market has a price and a fundamental value per asset. Buyers are the top quintile's idle money (above some weeks of spending), groups with cash, the bank's margin loans, and the player.

- **Expectation.** Buyers extrapolate the market's recent price change: demand rises with the last quarters' gain.
- **Credit.** The bank lends against shares and land at a loan-to-value on the market price, not the fundamental. A rising price raises what may be borrowed, which buys more.
- **Supply.** Groups list more shares, and land is bought to resell, as the price rises.

Price then pulls away from the fundamental, so long as credit grows.

### 4. Indicators, and a burst that can be foreseen
Per market, published each quarter:
- **Price over fundamental** (for land: price over rent years).
- **Credit growth:** the year's change in loans secured on the market's assets.
- **Leverage:** loans on the market's assets over their market value.
- **Bank cover:** the lending nation's reserve over its loans (exists, DEC-043).

[Draft] A market is *heated* above a first threshold on price over fundamental, and *bursts* when any of: price over fundamental above a second threshold, credit growth turning negative while leverage is above a limit, or bank cover below a floor. The thresholds are in the config and shown in game; the player sees the indicators move toward them. No dice: the same state always gives the same burst.

The three financial events become the consequences of a burst instead of random draws: a burst brings a credit crunch in the markets sharing it; bank cover under its floor brings a bank run; the crunch lifts rates (rate spike).

### 5. The burst
- Expectation turns: demand falls with the last quarters' loss, so the price falls past the fundamental.
- Margin calls: a loan above its loan-to-value must be topped up or its shares and land are sold at market, which pushes the price down further.
- Defaults: what cannot be sold covers less than the loan; the bank loses the rest, its cover falls, and the crunch follows.
- Facilities whose owners fail go to auction (DEC-039), with the rule of DEC-125.

### 6. The player
- Buys and sells shares on any exchange they stand at, and land at the node they stand at; may borrow on them at the bank (margin).
- **Short selling [Draft condition]:** opens when the player holds a licence for the exchange, granted for a fee once the player has traded there for a year and has a deposit at its bank as collateral. A short borrows shares from a group for a fee per week, sells them, and must buy them back; a margin call applies if the price rises past a limit.
- Sees each market's indicators and thresholds, so a burst can be foreseen.

## Order of work
1. **Land.** Plots, rent, the property market, and land needed to found facilities. Measure the economy with land and no speculation.
2. **Facility shares and the stock exchange.** Shares, dividends per share, groups listing shares, households and groups buying.
3. **Credit on assets.** Margin loans at market loan-to-value, margin calls.
4. **Indicators and bursts.** The published indicators, the thresholds, financial events caused by bursts instead of random.
5. **Ventures and the continental exchange.**
6. **The player:** trading, margin, short selling and its licence; the screens.

Each step ends with six seeds over 30 years and a web turn measurement.

## Risks
- The economy is already fragile: many merchant and facility failures, level 3 short of necessities. A bubble on top makes causes harder to tell apart; step 1 measures before speculation.
- Turn time is over budget on the web (1.01 s on average, DEC-110). Asset markets should clear weekly in one pass per market, and indicators quarterly.
- Land changes founding: today groups found facilities freely; with land they must buy a plot.

## Decisions needed
1. Markets and their reach: the table in section 2, or another (for example a stock exchange per continent).
2. Land: plots per node level, and whether every facility needs one, primary producers included.
3. The burst thresholds' first values: price over fundamental heated 1.5, burst 2.5; leverage limit 0.6; bank cover floor as now. To be tuned by measurement.
4. The short-selling condition: a year of trading and a deposit, or another.
5. Whether ventures come with this work or later (step 5).
6. Start with step 1, land.

## Limits
Nothing is measured. All numbers are first values to try.
