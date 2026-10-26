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

    /// <summary>Steps every cell of every market whose level is due this turn.</summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct PriceUpdateJob : IJobParallelFor
    {
        public PriceParams Params;
        public int CategoryCount;
        /// <summary>Bit l set = markets of WorldLevel l are due.</summary>
        public int DueLevelMask;

        /// <summary>Per level: turns since that level last repriced.</summary>
        [ReadOnly] public NativeArray<int> LevelElapsed;
        [ReadOnly] public NativeArray<byte> MarketLevel;
        [ReadOnly] public NativeArray<int> MarketCellStart;
        [ReadOnly] public NativeArray<int> MarketCellCount;
        /// <summary>Per market: currency price index of its continent (raw Q16).</summary>
        [ReadOnly] public NativeArray<long> MarketAnchorMul;
        [ReadOnly] public NativeArray<int> CellGood;
        [ReadOnly] public NativeArray<int> GoodBasePrice;
        [ReadOnly] public NativeArray<int> GoodCategory;
        /// <summary><see cref="MarketFlows.Multipliers"/>; only the Value target is read here.</summary>
        [ReadOnly] public NativeArray<long> ModifierMul;

        [NativeDisableParallelForRestriction] public NativeArray<int> Price;
        [NativeDisableParallelForRestriction] public NativeArray<int> PrevPrice;
        [NativeDisableParallelForRestriction] public NativeArray<int> Stock;
        [NativeDisableParallelForRestriction] public NativeArray<int> Supply;
        [NativeDisableParallelForRestriction] public NativeArray<int> Demand;

        public void Execute(int market)
        {
            int level = MarketLevel[market];
            if ((DueLevelMask & (1 << level)) == 0) return;

            int elapsed = LevelElapsed[level];
            int start = MarketCellStart[market];
            int end = start + MarketCellCount[market];
            long anchorMul = MarketAnchorMul[market];
            int modifierBase = market * CategoryCount;
            for (int c = start; c < end; c++)
            {
                int good = CellGood[c];
                int m = (modifierBase + GoodCategory[good]) * ModifierTargets.Count + (int)ModifierTarget.Value;
                long valueMul = Fx.RoundShift(ModifierMul[m] * anchorMul);

                int price = Price[c], prev = PrevPrice[c], stock = Stock[c], supply = Supply[c], demand = Demand[c];
                PriceKernel.Step(Params, GoodBasePrice[good], valueMul, elapsed,
                    ref price, ref prev, ref stock, ref supply, ref demand);
                Price[c] = price;
                PrevPrice[c] = prev;
                Stock[c] = stock;
                Supply[c] = supply;
                Demand[c] = demand;
            }
        }
    }

    /// <summary>Adds each market's weekly base consumption (scaled by demand modifiers) to its demand accumulators.</summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct ConsumptionJob : IJobParallelFor
    {
        public int CategoryCount;
        [ReadOnly] public NativeArray<byte> MarketLevel;
        [ReadOnly] public NativeArray<int> MarketCellStart;
        [ReadOnly] public NativeArray<int> MarketCellCount;
        [ReadOnly] public NativeArray<int> CellGood;
        [ReadOnly] public NativeArray<int> GoodBaseDemand;
        [ReadOnly] public NativeArray<int> GoodCategory;
        /// <summary>Per level demand weight (raw Q16).</summary>
        [ReadOnly] public NativeArray<long> LevelWeight;
        /// <summary><see cref="MarketFlows.Multipliers"/>; only the Demand target is read here.</summary>
        [ReadOnly] public NativeArray<long> ModifierMul;

        [NativeDisableParallelForRestriction] public NativeArray<int> Demand;

        public void Execute(int market)
        {
            long weight = LevelWeight[MarketLevel[market]];
            int start = MarketCellStart[market];
            int end = start + MarketCellCount[market];
            int modifierBase = market * CategoryCount;
            for (int c = start; c < end; c++)
            {
                int good = CellGood[c];
                int m = (modifierBase + GoodCategory[good]) * ModifierTargets.Count + (int)ModifierTarget.Demand;
                long weekly = Fx.RoundShift(Fx.RoundShift(GoodBaseDemand[good] * weight) * ModifierMul[m]);
                long next = Demand[c] + weekly;
                Demand[c] = next > int.MaxValue ? int.MaxValue : next < 0 ? 0 : (int)next;
            }
        }
    }
}
