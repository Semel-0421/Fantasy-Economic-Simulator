using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace FES.Agents
{
    /// <summary>
    /// Weekly facility production in facility order, so results are independent of Burst. Two passes per
    /// facility: check every input's stock and the total cost, then buy, pay and produce. A purchase takes
    /// units out of the stock and passes them through supply into demand, like <see cref="MarketFlows.Buy"/>.
    /// Cells are clamped after every add, like <see cref="MarketFlows"/>. A specialty output line is
    /// multiplied, and part of it comes out one grade higher: all of it with the grade-up chance when the
    /// facility is detailed, the expected share when it is aggregate.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct ProductionJob : IJob
    {
        public ulong Seed;
        public int Turn;
        public uint Stream;
        public uint GradeStream;
        public int VariancePermille;
        public int GoodCount;
        public long GradeUpChanceRaw;

        [ReadOnly] public NativeArray<int> FacilityNode;
        [ReadOnly] public NativeArray<int> FacilityOwner;
        [ReadOnly] public NativeArray<int> FacilityLevel;
        [ReadOnly] public NativeArray<long> FacilityEfficiency;
        public NativeArray<long> FacilityCash;
        /// <summary>Per node: its traders' float; buyers pay into it and sellers are paid from it (DEC-061).</summary>
        [ReadOnly] public NativeArray<int> NodeFloat;
        public NativeArray<long> Float;
        /// <summary>Per float: what producers leave for the merchants who sell later in the week.</summary>
        [ReadOnly] public NativeArray<long> FloatReserve;
        /// <summary>Per float and good: the state's subsidy on output (DEC-074), and what it may still pay this week.</summary>
        [ReadOnly] public NativeArray<long> SubsidyRate;
        public NativeArray<long> SubsidyLeft;
        [ReadOnly] public NativeArray<byte> NodeDetail;
        [ReadOnly] public NativeArray<int> LineStart;
        [ReadOnly] public NativeArray<int> LineCell;
        [ReadOnly] public NativeArray<int> LineGood;
        [ReadOnly] public NativeArray<int> LineModifier;
        [ReadOnly] public NativeArray<int> LineQuantity;
        [ReadOnly] public NativeArray<byte> LineIsInput;
        [ReadOnly] public NativeArray<long> LineOutputMul;
        [ReadOnly] public NativeArray<int> LineUpGood;
        [ReadOnly] public NativeArray<int> LineUpCell;
        [ReadOnly] public NativeArray<int> LineUpModifier;
        [ReadOnly] public NativeArray<long> ModifierMul;
        [ReadOnly] public NativeArray<int> Price;

        public NativeArray<int> Stock;
        public NativeArray<int> Supply;
        public NativeArray<int> Demand;

        /// <summary>Output of player-owned facilities (facility, good, market cell, quantity), stored after the job.</summary>
        public NativeList<int> PlayerOutputFacility;
        public NativeList<int> PlayerOutputGood;
        public NativeList<int> PlayerOutputCell;
        public NativeList<int> PlayerOutputQuantity;
        /// <summary>Per facility: this week's inputs at current prices, bought or not (the dividend buffer counts them).</summary>
        public NativeArray<long> FacilityInputCost;

        public void Execute()
        {
            for (int f = 0; f < FacilityNode.Length; f++)
            {
                bool detailed = NodeDetail[FacilityNode[f]] == (byte)SimDetail.Detailed;
                var output = Fx.FromRaw(FacilityEfficiency[f]) * Fx.FromInt(FacilityLevel[f]);
                if (detailed) output *= DetRandom.For(Seed, Turn, Stream, f).NextSpread(VariancePermille);
                // Aggregate: expected output, no per-facility randomness or decisions.

                int start = LineStart[f], end = LineStart[f + 1];
                long inputCost = 0;
                for (int k = start; k < end && LineIsInput[k] != 0; k++)
                    if (LineCell[k] >= 0) inputCost += Fx.MulIntRaw(LineQuantity[k], output.Raw) * Price[LineCell[k]];
                FacilityInputCost[f] = inputCost;
                bool inStock = true;
                long cost = 0;
                for (int k = start; k < end && LineIsInput[k] != 0; k++)
                {
                    int cell = LineCell[k];
                    long need = Fx.MulIntRaw(LineQuantity[k], output.Raw);
                    if (cell < 0 || Stock[cell] < need)
                    {
                        inStock = false;
                        break;
                    }
                    cost += need * Price[cell];
                }
                if (!inStock || cost > FacilityCash[f]) continue;
                FacilityCash[f] -= cost;
                Float[NodeFloat[FacilityNode[f]]] += cost;

                bool playerOwned = FacilityOwner[f] == OwnerIds.Player;
                for (int k = start; k < end; k++)
                {
                    int cell = LineCell[k];
                    long quantity = Fx.MulIntRaw(LineQuantity[k], output.Raw);
                    if (LineIsInput[k] != 0)
                    {
                        Stock[cell] -= (int)quantity;
                        Supply[cell] = MarketStore.ClampToNonNegativeInt(Supply[cell] + quantity);
                        Demand[cell] = MarketStore.ClampToNonNegativeInt(Demand[cell] + quantity);
                        continue;
                    }

                    quantity = Fx.MulIntRaw(quantity, LineOutputMul[k]);
                    long upgraded = 0;
                    if (LineUpGood[k] >= 0)
                    {
                        if (!detailed) upgraded = Fx.MulIntRaw(quantity, GradeUpChanceRaw);
                        else if (DetRandom.For(Seed, Turn, GradeStream, f, (uint)k).NextFx01().Raw < GradeUpChanceRaw) upgraded = quantity;
                        Emit(f, LineUpGood[k], LineUpCell[k], LineUpModifier[k], upgraded, playerOwned);
                    }
                    Emit(f, LineGood[k], cell, LineModifier[k], quantity - upgraded, playerOwned);
                }
            }
        }

        void Emit(int f, int good, int cell, int modifier, long quantity, bool playerOwned)
        {
            if (cell >= 0) quantity = Fx.MulIntRaw(quantity, ModifierMul[modifier]);
            int clamped = MarketStore.ClampToNonNegativeInt(quantity);
            if (clamped == 0) return;
            if (playerOwned)
            {
                PlayerOutputFacility.Add(f);
                PlayerOutputGood.Add(good);
                PlayerOutputCell.Add(cell);
                PlayerOutputQuantity.Add(clamped);
            }
            else if (cell >= 0)
            {
                // All of it reaches the market; the producer is paid only as far as the traders can pay (DEC-061),
                // from what the float keeps free of this week's merchants.
                int traders = NodeFloat[FacilityNode[f]];
                long paid = clamped * Price[cell];
                long payable = Float[traders] - FloatReserve[traders];
                if (paid > payable) paid = payable > 0 ? payable : 0;
                Supply[cell] = MarketStore.ClampToNonNegativeInt(Supply[cell] + clamped);
                FacilityCash[f] += paid;
                Float[traders] -= paid;
                long rate = SubsidyRate[traders * GoodCount + good];
                if (rate > 0)
                {
                    long grant = Fx.MulIntRaw(clamped * Price[cell], rate);
                    if (grant > SubsidyLeft[traders]) grant = SubsidyLeft[traders];
                    FacilityCash[f] += grant;
                    SubsidyLeft[traders] -= grant;
                }
            }
        }
    }

    /// <summary>
    /// Weekly upkeep (wages) of every facility, scaled by its level, paid to the households of its node
    /// (DEC-061); cash may go below zero. A facility whose cash ended <see cref="BankruptWeeks"/> weeks in a
    /// row below zero is listed as bankrupt (DEC-051). An NPC facility then pays a share of its cash above
    /// <see cref="DividendBufferWeeks"/> weeks of costs (upkeep and inputs) to its group, or to the
    /// households of its node without one (DEC-061).
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct FacilityUpkeepJob : IJob
    {
        public int BankruptWeeks;
        public int DividendBufferWeeks;
        public long DividendShareRaw;

        [ReadOnly] public NativeArray<int> FacilityDefIndex;
        [ReadOnly] public NativeArray<int> FacilityLevel;
        [ReadOnly] public NativeArray<long> DefUpkeep;
        public NativeArray<long> FacilityCash;
        public NativeArray<int> FacilityNegativeCashWeeks;
        public NativeList<int> Bankrupt;
        [ReadOnly] public NativeArray<int> FacilityNode;
        /// <summary>Per node: <see cref="FES.Economy.HouseholdCash.Cash"/>.</summary>
        public NativeArray<long> HouseholdCash;
        [ReadOnly] public NativeArray<int> FacilityOwner;
        [ReadOnly] public NativeArray<int> FacilityGroup;
        [ReadOnly] public NativeArray<long> FacilityInputCost;
        /// <summary>Per group: dividends received this week.</summary>
        public NativeArray<long> GroupDividend;
        /// <summary>Per facility: dividends paid this year, which the income tax counts as profit.</summary>
        public NativeArray<long> FacilityDividendsYear;

        public void Execute()
        {
            for (int f = 0; f < FacilityCash.Length; f++)
            {
                long upkeep = DefUpkeep[FacilityDefIndex[f]] * FacilityLevel[f];
                FacilityCash[f] -= upkeep;
                HouseholdCash[FacilityNode[f]] += upkeep;
                long buffer = (upkeep + FacilityInputCost[f]) * DividendBufferWeeks;
                if (FacilityOwner[f] == OwnerIds.Npc && FacilityCash[f] > buffer)
                {
                    long dividend = Fx.MulIntRaw(FacilityCash[f] - buffer, DividendShareRaw);
                    FacilityCash[f] -= dividend;
                    FacilityDividendsYear[f] += dividend;
                    if (FacilityGroup[f] >= 0) GroupDividend[FacilityGroup[f]] += dividend;
                    else HouseholdCash[FacilityNode[f]] += dividend;
                }
                if (FacilityCash[f] >= 0) FacilityNegativeCashWeeks[f] = 0;
                else if (++FacilityNegativeCashWeeks[f] >= BankruptWeeks) Bankrupt.Add(f);
            }
        }
    }
}
