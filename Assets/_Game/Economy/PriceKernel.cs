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

    /// <summary>
    /// One market cell's price step. Integer-only with symmetric rounding (no drift toward cheaper or
    /// dearer), so the Burst-compiled job and managed code produce identical results on every platform.
    /// </summary>
    public static class PriceKernel
    {
        /// <param name="anchorMulRaw">Multiplier on the base price for the anchor (events x currency inflation).</param>
        /// <param name="elapsedTurns">Turns since this market last repriced; rates scale with it so slow tiers keep pace.</param>
        public static void Step(in PriceParams p, int basePrice, long anchorMulRaw, int elapsedTurns,
            ref int price, ref int prevPrice, ref int stock, ref int supply, ref int demand)
        {
            long s = supply;
            long d = demand;
            long st = stock;

            // Pressure in [-1, 1]: positive when demand outruns supply (stock counts partially as supply).
            // Integer division truncates toward zero, which is symmetric.
            long effectiveSupply = s + Fx.RoundShift(st * p.StockPressureRaw);
            long total = d + effectiveSupply;
            long pressureRaw = total == 0 ? 0 : ((d - effectiveSupply) << Fx.FractionBits) / total;

            long maxStep = p.MaxStepRaw * elapsedTurns;
            long stepRaw = Fx.RoundShift(pressureRaw * p.AdjustRaw) * elapsedTurns;
            if (stepRaw > maxStep) stepRaw = maxStep;
            if (stepRaw < -maxStep) stepRaw = -maxStep;

            // Pull toward the anchor; at least one coin per update so cheap goods have no dead band.
            long anchor = Fx.RoundShift(basePrice * anchorMulRaw);
            long current = price;
            long gap = anchor - current;
            long pull = Fx.RoundShift(gap * p.AnchorRaw * elapsedTurns);
            if (pull == 0 && gap != 0 && p.AnchorRaw != 0) pull = gap > 0 ? 1 : -1;

            long next = current + Fx.RoundShift(current * stepRaw) + pull;
            if (next < p.MinPrice) next = p.MinPrice;
            if (next > int.MaxValue) next = int.MaxValue;

            long available = s + st;
            long traded = d < available ? d : available;
            long remaining = available - traded;

            prevPrice = price;
            price = (int)next;
            stock = remaining > int.MaxValue ? int.MaxValue : (int)remaining;
            supply = 0;
            demand = 0;
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
        /// <summary>See <see cref="HouseholdCash"/>.</summary>
        [NativeDisableParallelForRestriction] public NativeArray<long> Cash;
        [NativeDisableParallelForRestriction] public NativeArray<long> Spent;
        [NativeDisableParallelForRestriction] public NativeArray<long> Desired;
        [NativeDisableParallelForRestriction] public NativeArray<int> PendingUnits;
        [NativeDisableParallelForRestriction] public NativeArray<int> LastOffered;

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

        public static long Deliverable(long wished, int supply, int stock, int demand)
        {
            long left = (long)supply + stock - demand;
            if (left < 0) return 0;
            return left < wished ? left : wished;
        }

        public void Execute(int market)
        {
            int level = MarketLevel[market];
            if ((DueLevelMask & (1 << level)) == 0) return;

            int elapsed = LevelElapsed[level];
            int start = MarketCellStart[market];
            int end = start + MarketCellCount[market];
            long anchorMul = MarketAnchorMul[market];
            long levelWeight = LevelWeight[level];
            long necessityWeight = MarketNecessityWeight[market];
            long luxuryWeight = MarketLuxuryWeight[market];
            long wanted = 0, met = 0, desired = 0, spent = 0;
            long budget = MarketBudget[market];
            bool inline = (InlineLevelMask & (1 << level)) != 0;
            int modifierBase = market * CategoryCount;
            for (int c = start; c < end; c++)
            {
                int good = CellGood[c];
                int k = (modifierBase + GoodCategory[good]) * ModifierTargets.Count;
                long valueMul = Fx.RoundShift(ModifierMul[k + (int)ModifierTarget.Value] * anchorMul);
                if (CellSpecialty[c] != 0) valueMul = Fx.RoundShift(valueMul * SpecialtyAnchorMulRaw);

                int pending;
                if (inline)
                {
                    long weekly = HouseholdDemand.CellBase(GoodBaseDemand[good], GoodBasePrice[good], levelWeight,
                        ModifierMul[k + (int)ModifierTarget.Demand], valueMul, out long anchor);
                    long consumed = HouseholdDemand.Weekly(Consumption, weekly, Price[c], anchor, GoodLuxuryShare[good],
                        necessityWeight, luxuryWeight, out long cellWanted, out long cellMet);
                    desired += Deliverable(consumed, Supply[c], Stock[c], Demand[c]) * Price[c];
                    if (budget != Fx.OneRaw)
                    {
                        consumed = Fx.RoundShift(consumed * budget);
                        cellMet = Fx.RoundShift(cellMet * budget);
                    }
                    if ((long)Supply[c] + Stock[c] > 0) LastOffered[c] = Turn;
                    if (LastOffered[c] >= Turn - NeedMemoryWeeks)
                    {
                        wanted += cellWanted;
                        met += Supplied(cellMet, Supply[c], Stock[c], Demand[c]);
                    }
                    Demand[c] = MarketStore.ClampToNonNegativeInt(Demand[c] + consumed);
                    // Markets that reprice every turn settle the same turn, so nothing waits in PendingUnits.
                    pending = MarketStore.ClampToNonNegativeInt(consumed);
                }
                else
                {
                    pending = PendingUnits[c];
                    if (pending > 0) PendingUnits[c] = 0;
                }

                if (pending > 0)
                {
                    long others = (long)Demand[c] - pending;
                    if (others < 0) others = 0;
                    long delivered = (long)Supply[c] + Stock[c] - others;
                    if (delivered > pending) delivered = pending;
                    if (delivered > 0) spent += delivered * Price[c];
                }

                int price = Price[c], prev = PrevPrice[c], stock = Stock[c], supply = Supply[c], demand = Demand[c];
                PriceKernel.Step(Params, GoodBasePrice[good], valueMul, elapsed,
                    ref price, ref prev, ref stock, ref supply, ref demand);
                Price[c] = price;
                PrevPrice[c] = prev;
                Stock[c] = stock;
                Supply[c] = supply;
                Demand[c] = demand;
            }
            NecessityWanted[market] += wanted;
            NecessityMet[market] += met;
            if (inline) Desired[market] = desired;
            // Households never pay more than they hold.
            long cash = Cash[market];
            if (spent > cash) spent = cash > 0 ? cash : 0;
            Cash[market] = cash - spent;
            Spent[market] += spent;
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
            long budget = MarketBudget[market];
            long wanted = 0, met = 0, desired = 0;
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
                long consumed = HouseholdDemand.Weekly(Consumption, weekly, Price[c], anchor, GoodLuxuryShare[good],
                    MarketNecessityWeight[market], MarketLuxuryWeight[market], out long cellWanted, out long cellMet);
                desired += PriceUpdateJob.Deliverable(consumed, Supply[c], Stock[c], Demand[c]) * Price[c];
                if (budget != Fx.OneRaw)
                {
                    consumed = Fx.RoundShift(consumed * budget);
                    cellMet = Fx.RoundShift(cellMet * budget);
                }
                if ((long)Supply[c] + Stock[c] > 0) LastOffered[c] = Turn;
                if (LastOffered[c] >= Turn - NeedMemoryWeeks)
                {
                    wanted += cellWanted;
                    met += PriceUpdateJob.Supplied(cellMet, Supply[c], Stock[c], Demand[c]);
                }
                Demand[c] = MarketStore.ClampToNonNegativeInt(Demand[c] + consumed);
                PendingUnits[c] = MarketStore.ClampToNonNegativeInt(PendingUnits[c] + consumed);
            }
            NecessityWanted[market] += wanted;
            NecessityMet[market] += met;
            Desired[market] = desired;
        }
    }
}
