using FES.Core;
using FES.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace FES.Economy
{
    /// <summary>Pricing constants in raw Q16 fixed-point, passed by value into jobs.</summary>
    public struct PriceParams
    {
        public long AdjustRaw;
        public long MaxStepRaw;
        public long AnchorRaw;
        public long StockPressureRaw;
        public int MinPrice;

        public static PriceParams From(SimulationConfig c) => new PriceParams
        {
            AdjustRaw = c.PriceAdjustRate.Raw,
            MaxStepRaw = c.PriceMaxStep.Raw,
            AnchorRaw = c.PriceAnchorRate.Raw,
            StockPressureRaw = c.StockPressure.Raw,
            MinPrice = c.MinPrice,
        };
    }

    /// <summary>One market cell's numbers, as a price step takes and leaves them.</summary>
    public struct PriceCell
    {
        public int Price, PrevPrice, Stock, Supply, Demand;
    }

    /// <summary>
    /// One market cell's price step. Integer-only with symmetric rounding (no drift toward cheaper or
    /// dearer), so the Burst-compiled job and managed code produce identical results on every platform.
    /// </summary>
    public static class PriceKernel
    {
        /// <param name="anchorMulRaw">Multiplier on the base price for the anchor (events x currency inflation).</param>
        /// <param name="elapsedTurns">Turns since this market last repriced; rates scale with it so slow tiers keep pace.</param>
        public static void Step(in PriceParams p, int basePrice, long anchorMulRaw, int elapsedTurns, ref PriceCell cell, int inputBid = 0)
        {
            long s = cell.Supply;
            long d = cell.Demand;
            long st = cell.Stock;

            // Pressure in [-1, 1]: positive when demand outruns supply (stock counts partially as supply).
            // Integer division truncates toward zero, which is symmetric.
            long effectiveSupply = s + Fx.RoundShift(st * p.StockPressureRaw);
            long wanted = d + inputBid;
            long total = wanted + effectiveSupply;
            long pressureRaw = total == 0 ? 0 : ((wanted - effectiveSupply) << Fx.FractionBits) / total;

            long maxStep = p.MaxStepRaw * elapsedTurns;
            long stepRaw = Fx.RoundShift(pressureRaw * p.AdjustRaw) * elapsedTurns;
            if (stepRaw > maxStep) stepRaw = maxStep;
            if (stepRaw < -maxStep) stepRaw = -maxStep;

            // Pull toward the anchor; at least one coin per update so cheap goods have no dead band, unless the market
            // presses the other way: a coin is a large share of a cheap price, and it would hide a shortage or a glut.
            long anchor = Fx.RoundShift(basePrice * anchorMulRaw);
            long current = cell.Price;
            long gap = anchor - current;
            long pull = Fx.RoundShift(gap * p.AnchorRaw * elapsedTurns);
            if (pull == 0 && gap != 0 && p.AnchorRaw != 0 && (pressureRaw == 0 || pressureRaw > 0 == gap > 0)) pull = gap > 0 ? 1 : -1;

            long next = current + Fx.RoundShift(current * stepRaw) + pull;
            if (next < p.MinPrice) next = p.MinPrice;
            if (next > int.MaxValue) next = int.MaxValue;

            long available = s + st;
            long traded = d < available ? d : available;
            long remaining = available - traded;

            cell.PrevPrice = cell.Price;
            cell.Price = (int)next;
            cell.Stock = remaining > int.MaxValue ? int.MaxValue : (int)remaining;
            cell.Supply = 0;
            cell.Demand = 0;
        }
    }

    /// <summary>
    /// Steps every cell of every market whose level is due this turn. Markets that reprice every turn first
    /// add this turn's household consumption here (<see cref="HouseholdDemand"/>), which saves a pass over
    /// most cells; slower markets got theirs turn by turn from <see cref="ConsumptionJob"/>, so a short demand
    /// event still reaches them. Consumption is scaled by the households' budget (DEC-061). Before a cell
    /// steps, its households pay its price for the units the market delivers to them: every other unit of
    /// demand is a purchase already taken from the stock, so they get what supply and stock leave.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct PriceUpdateJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public NativeArray<int> InputBid;
        public PriceParams Params;
        public DemandParams Consumption;
        public int CategoryCount;
        /// <summary>Bit l set = markets of WorldLevel l are due.</summary>
        public int DueLevelMask;
        /// <summary>Bit l set = markets of WorldLevel l reprice every turn and consume here.</summary>
        public int InlineLevelMask;
        /// <summary>Anchor multiplier (raw Q16) for cells of a node's specialties.</summary>
        public long SpecialtyAnchorMulRaw;
        public int Turn;
        /// <summary>Needs count for goods the market offered within this many weeks (usually traded goods).</summary>
        public int NeedMemoryWeeks;

        /// <summary>Per level: turns since that level last repriced.</summary>
        [ReadOnly] public NativeArray<int> LevelElapsed;
        [ReadOnly] public NativeArray<byte> MarketLevel;
        [ReadOnly] public NativeArray<int> MarketCellStart;
        [ReadOnly] public NativeArray<int> MarketCellCount;
        /// <summary>Per market: currency price index of its continent (raw Q16).</summary>
        [ReadOnly] public NativeArray<long> MarketAnchorMul;
        [ReadOnly] public NativeArray<int> CellGood;
        [ReadOnly] public NativeArray<byte> CellSpecialty;
        [ReadOnly] public NativeArray<int> GoodBasePrice;
        [ReadOnly] public NativeArray<int> GoodCategory;
        /// <summary><see cref="MarketFlows.Multipliers"/>; the Value and Demand targets are read here.</summary>
        [ReadOnly] public NativeArray<long> ModifierMul;
        [ReadOnly] public NativeArray<int> GoodBaseDemand;
        /// <summary>Per good: how far it is bought like a luxury (raw Q16, 0..1).</summary>
        [ReadOnly] public NativeArray<long> GoodLuxuryShare;
        /// <summary>Per level demand weight (raw Q16).</summary>
        [ReadOnly] public NativeArray<long> LevelWeight;
        /// <summary>Per market: necessity and luxury weight of its households (raw Q16).</summary>
        [ReadOnly] public NativeArray<long> MarketNecessityWeight;
        [ReadOnly] public NativeArray<long> MarketLuxuryWeight;
        /// <summary>Per market: necessity units wanted at the anchor, and demanded at the price met.</summary>
        [NativeDisableParallelForRestriction] public NativeArray<long> NecessityWanted;
        [NativeDisableParallelForRestriction] public NativeArray<long> NecessityMet;

        /// <summary>Per market: the share of their wished consumption its households can pay for (raw Q16).</summary>
        [ReadOnly] public NativeArray<long> MarketBudget;
        /// <summary>Per market: the share of their wished luxuries left over for (raw Q16, DEC-083).</summary>
        [ReadOnly] public NativeArray<long> MarketLuxuryBudget;
        [NativeDisableParallelForRestriction] public NativeArray<long> DesiredNecessity;
        /// <summary>See <see cref="HouseholdCash"/>.</summary>
        [NativeDisableParallelForRestriction] public NativeArray<long> Cash;
        [NativeDisableParallelForRestriction] public NativeArray<long> Spent;
        [NativeDisableParallelForRestriction] public NativeArray<long> Desired;
        [NativeDisableParallelForRestriction] public NativeArray<int> PendingUnits;
        [NativeDisableParallelForRestriction] public NativeArray<int> LastOffered;
        [NativeDisableParallelForRestriction] public NativeArray<int> HomeGrown;

        [NativeDisableParallelForRestriction] public NativeArray<int> Price;
        [NativeDisableParallelForRestriction] public NativeArray<int> PrevPrice;
        [NativeDisableParallelForRestriction] public NativeArray<int> Stock;
        [NativeDisableParallelForRestriction] public NativeArray<int> Supply;
        [NativeDisableParallelForRestriction] public NativeArray<int> Demand;

        /// <summary>
        /// Of <paramref name="wished"/> household units, how many the market could deliver now: supply and stock
        /// left after the demand already in. Households budget against these, not against goods nobody sells.
        /// </summary>
        /// <summary>Of the necessities households could afford, what is there to buy, so population follows supply.</summary>
        public static long Supplied(long met, int supply, int stock, int demand)
        {
            long left = (long)supply + stock - demand;
            return left <= 0 ? 0 : left < met ? left : met;
        }

        /// <summary>Households eat what they grew before they buy (DEC-117): takes it from the cell's home crop.</summary>
        public static long EatHomeGrown(ref NativeArray<int> homeGrown, int c, long needed)
        {
            long eaten = homeGrown[c] < needed ? homeGrown[c] : needed;
            homeGrown[c] -= (int)eaten;
            return eaten;
        }

        public static long Deliverable(long wished, int supply, int stock, int demand)
        {
            long left = (long)supply + stock - demand;
            if (left < 0) return 0;
            return left < wished ? left : wished;
        }

        struct MarketTally
        {
            public long Wanted, Met, Desired, DesiredNecessity, Spent;
        }

        public void Execute(int market)
        {
            int level = MarketLevel[market];
            if ((DueLevelMask & (1 << level)) == 0) return;

            int start = MarketCellStart[market];
            int end = start + MarketCellCount[market];
            bool inline = (InlineLevelMask & (1 << level)) != 0;
            var tally = new MarketTally();
            for (int c = start; c < end; c++) StepCell(market, c, level, inline, ref tally);
            NecessityWanted[market] += tally.Wanted;
            NecessityMet[market] += tally.Met;
            if (inline)
            {
                Desired[market] = tally.Desired;
                DesiredNecessity[market] = tally.DesiredNecessity;
            }
            // Households never pay more than they hold.
            long cash = Cash[market], spent = tally.Spent;
            if (spent > cash) spent = cash > 0 ? cash : 0;
            Cash[market] = cash - spent;
            Spent[market] += spent;
        }

        void StepCell(int market, int c, int level, bool inline, ref MarketTally tally)
        {
            int good = CellGood[c];
            int k = (market * CategoryCount + GoodCategory[good]) * ModifierTargets.Count;
            long valueMul = Fx.RoundShift(ModifierMul[k + (int)ModifierTarget.Value] * MarketAnchorMul[market]);
            if (CellSpecialty[c] != 0) valueMul = Fx.RoundShift(valueMul * SpecialtyAnchorMulRaw);

            int pending = inline ? ConsumeNow(market, c, level, valueMul, ref tally) : TakePending(c);
            if (pending > 0) tally.Spent += DeliveredValue(c, pending);

            var cell = new PriceCell { Price = Price[c], PrevPrice = PrevPrice[c], Stock = Stock[c], Supply = Supply[c], Demand = Demand[c] };
            PriceKernel.Step(Params, GoodBasePrice[good], valueMul, LevelElapsed[level], ref cell, InputBid[c]);
            InputBid[c] = 0;
            Price[c] = cell.Price;
            PrevPrice[c] = cell.PrevPrice;
            Stock[c] = cell.Stock;
            Supply[c] = cell.Supply;
            Demand[c] = cell.Demand;
        }

        // Adds this turn's household consumption to the cell's demand, and returns the units; such a market settles the
        // same turn, so nothing waits in PendingUnits.
        int ConsumeNow(int market, int c, int level, long valueMul, ref MarketTally tally)
        {
            int good = CellGood[c];
            int k = (market * CategoryCount + GoodCategory[good]) * ModifierTargets.Count;
            long weekly = HouseholdDemand.CellBase(GoodBaseDemand[good], GoodBasePrice[good], LevelWeight[level],
                ModifierMul[k + (int)ModifierTarget.Demand], valueMul, out long anchor);
            var wish = HouseholdDemand.Weekly(Consumption, weekly, Price[c], anchor, GoodLuxuryShare[good],
                MarketNecessityWeight[market], MarketLuxuryWeight[market]);
            long eaten = EatHomeGrown(ref HomeGrown, c, wish.Met);
            long toBuy = wish.Met - eaten, luxuries = wish.Units - wish.Met;
            tally.Desired += Deliverable(toBuy + luxuries, Supply[c], Stock[c], Demand[c]) * Price[c];
            tally.DesiredNecessity += Deliverable(toBuy, Supply[c], Stock[c], Demand[c]) * Price[c];
            long cellMet = Fx.RoundShift(toBuy * MarketBudget[market]);
            long consumed = cellMet + Fx.RoundShift(luxuries * MarketLuxuryBudget[market]);
            if ((long)Supply[c] + Stock[c] + eaten > 0) LastOffered[c] = Turn;
            if (LastOffered[c] >= Turn - NeedMemoryWeeks)
            {
                tally.Wanted += wish.Wanted;
                tally.Met += eaten + Supplied(cellMet, Supply[c], Stock[c], Demand[c]);
            }
            Demand[c] = MarketStore.ClampToNonNegativeInt(Demand[c] + consumed);
            return MarketStore.ClampToNonNegativeInt(consumed);
        }

        int TakePending(int c)
        {
            int pending = PendingUnits[c];
            if (pending > 0) PendingUnits[c] = 0;
            return pending;
        }

        long DeliveredValue(int c, int pending)
        {
            long others = (long)Demand[c] - pending;
            if (others < 0) others = 0;
            long delivered = (long)Supply[c] + Stock[c] - others;
            if (delivered > pending) delivered = pending;
            return delivered > 0 ? delivered * Price[c] : 0;
        }
    }

    /// <summary>
    /// Adds this turn's household consumption to the demand of markets that reprice less often than every
    /// turn (<see cref="PriceUpdateJob"/> does the others), at the price they have held since their last update,
    /// scaled by the households' budget. The units are paid for when the market reprices.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct ConsumptionJob : IJobParallelFor
    {
        public DemandParams Consumption;
        public int CategoryCount;
        /// <summary>Bit l set = markets of WorldLevel l consume here.</summary>
        public int LevelMask;
        public long SpecialtyAnchorMulRaw;
        public int Turn;
        public int NeedMemoryWeeks;
        [NativeDisableParallelForRestriction] public NativeArray<int> HomeGrown;
        [ReadOnly] public NativeArray<byte> MarketLevel;
        [ReadOnly] public NativeArray<int> MarketCellStart;
        [ReadOnly] public NativeArray<int> MarketCellCount;
        [ReadOnly] public NativeArray<long> MarketAnchorMul;
        [ReadOnly] public NativeArray<long> MarketNecessityWeight;
        [ReadOnly] public NativeArray<long> MarketLuxuryWeight;
        [ReadOnly] public NativeArray<int> CellGood;
        [ReadOnly] public NativeArray<byte> CellSpecialty;
        [ReadOnly] public NativeArray<int> Price;
        [ReadOnly] public NativeArray<int> GoodBaseDemand;
        [ReadOnly] public NativeArray<int> GoodBasePrice;
        [ReadOnly] public NativeArray<int> GoodCategory;
        [ReadOnly] public NativeArray<long> GoodLuxuryShare;
        [ReadOnly] public NativeArray<long> LevelWeight;
        [ReadOnly] public NativeArray<long> ModifierMul;

        [ReadOnly] public NativeArray<long> MarketBudget;
        /// <summary>Per market: the share of their wished luxuries left over for (raw Q16, DEC-083).</summary>
        [ReadOnly] public NativeArray<long> MarketLuxuryBudget;
        [NativeDisableParallelForRestriction] public NativeArray<long> DesiredNecessity;
        [ReadOnly] public NativeArray<int> Supply;
        [ReadOnly] public NativeArray<int> Stock;

        [NativeDisableParallelForRestriction] public NativeArray<int> Demand;
        [NativeDisableParallelForRestriction] public NativeArray<long> NecessityWanted;
        [NativeDisableParallelForRestriction] public NativeArray<long> NecessityMet;
        [NativeDisableParallelForRestriction] public NativeArray<long> Desired;
        [NativeDisableParallelForRestriction] public NativeArray<int> PendingUnits;
        [NativeDisableParallelForRestriction] public NativeArray<int> LastOffered;

        public void Execute(int market)
        {
            int level = MarketLevel[market];
            if ((LevelMask & (1 << level)) == 0) return;

            long anchorMul = MarketAnchorMul[market];
            long levelWeight = LevelWeight[level];
            long budget = MarketBudget[market], luxuryBudget = MarketLuxuryBudget[market];
            long wanted = 0, met = 0, desired = 0, desiredNecessity = 0;
            int start = MarketCellStart[market];
            int end = start + MarketCellCount[market];
            int modifierBase = market * CategoryCount;
            for (int c = start; c < end; c++)
            {
                int good = CellGood[c];
                int k = (modifierBase + GoodCategory[good]) * ModifierTargets.Count;
                long valueMul = Fx.RoundShift(ModifierMul[k + (int)ModifierTarget.Value] * anchorMul);
                if (CellSpecialty[c] != 0) valueMul = Fx.RoundShift(valueMul * SpecialtyAnchorMulRaw);
                long weekly = HouseholdDemand.CellBase(GoodBaseDemand[good], GoodBasePrice[good], levelWeight,
                    ModifierMul[k + (int)ModifierTarget.Demand], valueMul, out long anchor);
                var wish = HouseholdDemand.Weekly(Consumption, weekly, Price[c], anchor, GoodLuxuryShare[good],
                    MarketNecessityWeight[market], MarketLuxuryWeight[market]);
                long eaten = PriceUpdateJob.EatHomeGrown(ref HomeGrown, c, wish.Met);
                long luxuries = wish.Units - wish.Met, toBuy = wish.Met - eaten;
                desired += PriceUpdateJob.Deliverable(toBuy + luxuries, Supply[c], Stock[c], Demand[c]) * Price[c];
                desiredNecessity += PriceUpdateJob.Deliverable(toBuy, Supply[c], Stock[c], Demand[c]) * Price[c];
                long cellMet = Fx.RoundShift(toBuy * budget);
                long consumed = cellMet + Fx.RoundShift(luxuries * luxuryBudget);
                if ((long)Supply[c] + Stock[c] + eaten > 0) LastOffered[c] = Turn;
                if (LastOffered[c] >= Turn - NeedMemoryWeeks)
                {
                    wanted += wish.Wanted;
                    met += eaten + PriceUpdateJob.Supplied(cellMet, Supply[c], Stock[c], Demand[c]);
                }
                Demand[c] = MarketStore.ClampToNonNegativeInt(Demand[c] + consumed);
                PendingUnits[c] = MarketStore.ClampToNonNegativeInt(PendingUnits[c] + consumed);
            }
            NecessityWanted[market] += wanted;
            NecessityMet[market] += met;
            Desired[market] = desired;
            DesiredNecessity[market] = desiredNecessity;
        }
    }
}
