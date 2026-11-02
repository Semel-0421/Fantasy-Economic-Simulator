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
        /// <summary><see cref="LineIsInput"/> of a tool or seed line; a required input is 1, an output 0.</summary>
        public const byte AidLine = 2;

        /// <summary>Producers take bills for what traders cannot pay (DEC-097); see <see cref="DetailedMerchantJob"/>.</summary>
        public bool BillProducers;
        /// <summary>Traders pay sellers the price less this margin (raw Fx, DEC-098).</summary>
        public long SpreadRaw;
        public NativeArray<long> BillRoom;
        public NativeArray<long> BillBankRoom;
        public NativeArray<long> BillBankSpent;
        public long BillDiscountRaw;
        public int BillWeeks;
        public NativeList<int> BillHolder;
        public NativeList<int> BillPurse;
        public NativeList<long> BillFace;
        public NativeList<int> BillDue;

        /// <summary>Per cell: <see cref="MarketStore.InputBid"/>.</summary>
        public NativeArray<int> InputBid;
        public ulong Seed;
        public int Turn;
        public uint Stream;
        public uint GradeStream;
        public int VariancePermille;
        public int GoodCount;
        public long GradeUpChanceRaw;
        public long ExpansionBonusRaw;
        public long UnaidedOutputRaw;
        public long OutputWageRaw;
        /// <summary>Per cell, raw Fx: the share of capacity factories selling there run at; and what they made (DEC-085).</summary>
        [ReadOnly] public NativeArray<long> CellPlan;
        public NativeArray<long> CellProduced;
        [ReadOnly] public NativeArray<int> GoodBasePrice;
        /// <summary>Per facility: this week's wages that follow what it made (DEC-082), paid with upkeep.</summary>
        public NativeArray<long> FacilityOutputWage;

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
            for (int f = 0; f < FacilityNode.Length; f++) RunFacility(f);
        }

        void RunFacility(int f)
        {
            FacilityOutputWage[f] = 0;
            bool detailed = NodeDetail[FacilityNode[f]] == (byte)SimDetail.Detailed;
            var output = PlannedOutput(f, detailed);
            long inputCost = InputCost(f, output);
            FacilityInputCost[f] = inputCost;
            if (!InputsInStock(f, output, out long cost))
            {
                BidForInputs(f, output, inputCost);
                return;
            }
            if (cost > FacilityCash[f]) return;

            bool aided = TryAids(f, output, cost, out bool hasAids, out long aidCost);
            if (cost + aidCost > FacilityCash[f]) aided = false;
            if (aided) cost += aidCost;
            FacilityCash[f] -= cost;
            Float[NodeFloat[FacilityNode[f]]] += cost;
            MakeGoods(f, output, detailed, aided, hasAids && !aided);
        }

        // Aggregate: expected output, no per-facility randomness or decisions.
        Fx PlannedOutput(int f, bool detailed)
        {
            var output = Fx.FromRaw(FacilityEfficiency[f]) * Fx.FromInt(FacilityLevel[f])
                * (Fx.One + Fx.FromRaw(ExpansionBonusRaw) * Fx.FromInt(FacilityLevel[f] - 1));
            if (detailed) output *= DetRandom.For(Seed, Turn, Stream, f).NextSpread(VariancePermille);
            if (FacilityOwner[f] == OwnerIds.Player) return output;
            for (int k = LineStart[f]; k < LineStart[f + 1]; k++)
            {
                if (LineIsInput[k] != 0) continue;
                if (LineCell[k] >= 0) output *= Fx.FromRaw(CellPlan[LineCell[k]]);
                break;
            }
            return output;
        }

        long Units(int k, Fx output) => Fx.MulIntRaw(LineQuantity[k], output.Raw);

        long InputCost(int f, Fx output)
        {
            long cost = 0;
            for (int k = LineStart[f]; k < LineStart[f + 1] && LineIsInput[k] != 0; k++)
                if (LineCell[k] >= 0) cost += Units(k, output) * Price[LineCell[k]];
            return cost;
        }

        bool InputsInStock(int f, Fx output, out long cost)
        {
            cost = 0;
            for (int k = LineStart[f]; k < LineStart[f + 1] && LineIsInput[k] != 0; k++)
            {
                if (LineIsInput[k] == AidLine) continue;
                int cell = LineCell[k];
                long need = Units(k, output);
                if (cell < 0 || Stock[cell] < need) return false;
                cost += need * Price[cell];
            }
            return true;
        }

        // What it could not buy still bids the price up, so merchants bring the input here, as long as
        // it could pay and the output would be worth the inputs; that keeps the bid from running away (DEC-078).
        void BidForInputs(int f, Fx output, long inputCost)
        {
            int start = LineStart[f], end = LineStart[f + 1];
            long outputValue = 0;
            for (int k = start; k < end; k++)
                if (LineIsInput[k] == 0 && LineCell[k] >= 0) outputValue += Units(k, output) * Price[LineCell[k]];
            if (inputCost >= OfferOn(outputValue) || inputCost > FacilityCash[f]) return;
            for (int k = start; k < end && LineIsInput[k] != 0; k++)
            {
                int cell = LineCell[k];
                if (cell < 0 || LineIsInput[k] == AidLine) continue;
                long need = Units(k, output);
                if (Stock[cell] < need) InputBid[cell] = MarketStore.ClampToNonNegativeInt(InputBid[cell] + need - Stock[cell]);
            }
        }

        // Tools and seed are all bought or none; without them it works at a lower output, and bids for them (DEC-081).
        // The bid stays within the output's worth and the cash, as for inputs (DEC-078). Returns whether they are all there.
        bool TryAids(int f, Fx output, long cost, out bool hasAids, out long aidCost)
        {
            int start = LineStart[f], end = LineStart[f + 1];
            bool aided = true;
            hasAids = false;
            aidCost = 0;
            bool mayBid = AidsAreWorthIt(f, output, cost);
            for (int k = start; k < end && LineIsInput[k] != 0; k++)
            {
                if (LineIsInput[k] != AidLine) continue;
                hasAids = true;
                int cell = LineCell[k];
                long need = Units(k, output);
                if (cell < 0 || Stock[cell] < need)
                {
                    aided = false;
                    if (cell >= 0 && mayBid) InputBid[cell] = MarketStore.ClampToNonNegativeInt(InputBid[cell] + need - Stock[cell]);
                }
                else aidCost += need * Price[cell];
            }
            return aided;
        }

        bool AidsAreWorthIt(int f, Fx output, long cost)
        {
            long aidValue = 0, outputWorth = 0;
            for (int k = LineStart[f]; k < LineStart[f + 1]; k++)
            {
                if (LineCell[k] < 0) continue;
                long worth = Units(k, output) * Price[LineCell[k]];
                if (LineIsInput[k] == AidLine) aidValue += worth;
                else if (LineIsInput[k] == 0) outputWorth += worth;
            }
            return aidValue < OfferOn(outputWorth) && cost + aidValue <= FacilityCash[f];
        }

        void MakeGoods(int f, Fx output, bool detailed, bool aided, bool unaided)
        {
            for (int k = LineStart[f]; k < LineStart[f + 1]; k++)
            {
                long quantity = Units(k, output);
                if (LineIsInput[k] == AidLine && !aided) continue;
                if (LineIsInput[k] != 0) UseInput(LineCell[k], quantity);
                else MakeOutput(f, k, quantity, detailed, unaided);
            }
        }

        void UseInput(int cell, long quantity)
        {
            Stock[cell] -= (int)quantity;
            Supply[cell] = MarketStore.ClampToNonNegativeInt(Supply[cell] + quantity);
            Demand[cell] = MarketStore.ClampToNonNegativeInt(Demand[cell] + quantity);
        }

        void MakeOutput(int f, int k, long quantity, bool detailed, bool unaided)
        {
            bool playerOwned = FacilityOwner[f] == OwnerIds.Player;
            quantity = Fx.MulIntRaw(quantity, LineOutputMul[k]);
            if (unaided) quantity = Fx.MulIntRaw(quantity, UnaidedOutputRaw);
            long upgraded = 0;
            if (LineUpGood[k] >= 0)
            {
                if (!detailed) upgraded = Fx.MulIntRaw(quantity, GradeUpChanceRaw);
                else if (DetRandom.For(Seed, Turn, GradeStream, f, (uint)k).NextFx01().Raw < GradeUpChanceRaw) upgraded = quantity;
                Emit(f, LineUpGood[k], LineUpCell[k], LineUpModifier[k], upgraded, playerOwned);
            }
            Emit(f, LineGood[k], LineCell[k], LineModifier[k], quantity - upgraded, playerOwned);
        }

        static long Min(long a, long b) => a < b ? a : b;

        void Emit(int f, int good, int cell, int modifier, long quantity, bool playerOwned)
        {
            if (cell >= 0) quantity = Fx.MulIntRaw(quantity, ModifierMul[modifier]);
            int clamped = MarketStore.ClampToNonNegativeInt(quantity);
            if (clamped == 0) return;
            FacilityOutputWage[f] += Fx.MulIntRaw(clamped * GoodBasePrice[good], OutputWageRaw);
            if (cell >= 0) CellProduced[cell] += clamped;
            if (playerOwned)
            {
                PlayerOutputFacility.Add(f);
                PlayerOutputGood.Add(good);
                PlayerOutputCell.Add(cell);
                PlayerOutputQuantity.Add(clamped);
            }
            else if (cell >= 0) SellToMarket(f, good, cell, clamped);
        }

        // What traders pay a seller for goods at this list worth (DEC-098).
        long OfferOn(long worth) => worth - Fx.MulIntRaw(worth, SpreadRaw);

        // All of it reaches the market; the producer is paid only as far as the traders can pay (DEC-061),
        // from what the float keeps free of this week's merchants.
        void SellToMarket(int f, int good, int cell, int clamped)
        {
            int traders = NodeFloat[FacilityNode[f]];
            long worth = clamped * Price[cell];
            worth = OfferOn(worth);
            long paid = worth;
            long payable = Float[traders] - FloatReserve[traders];
            if (paid > payable) paid = payable > 0 ? payable : 0;
            Supply[cell] = MarketStore.ClampToNonNegativeInt(Supply[cell] + clamped);
            FacilityCash[f] += paid;
            Float[traders] -= paid;
            if (BillProducers && FacilityOwner[f] != OwnerIds.Player) // only bonds are open to the player (DEC-102)
                TakeBill(f, traders, worth - paid);
            long rate = SubsidyRate[traders * GoodCount + good];
            if (rate <= 0) return;
            long grant = Fx.MulIntRaw(clamped * Price[cell], rate);
            if (grant > SubsidyLeft[traders]) grant = SubsidyLeft[traders];
            FacilityCash[f] += grant;
            SubsidyLeft[traders] -= grant;
        }

        void TakeBill(int f, int traders, long unpaid)
        {
            long face = Min(unpaid, BillRoom[traders]);
            if (face <= 0) return;
            BillRoom[traders] -= face;
            long discounted = face - Fx.MulIntRaw(face, BillDiscountRaw);
            bool bank = BillBankRoom[traders] >= discounted;
            BillHolder.Add(bank ? BillHolders.Bank : BillHolders.Facility(f));
            BillPurse.Add(traders);
            BillFace.Add(face);
            BillDue.Add(Turn + BillWeeks);
            if (!bank) return;
            BillBankRoom[traders] -= discounted;
            BillBankSpent[traders] += discounted;
            FacilityCash[f] += discounted;
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
        [ReadOnly] public NativeArray<long> FacilityOutputWage;
        /// <summary>Per group: dividends received this week.</summary>
        public NativeArray<long> GroupDividend;
        /// <summary>Per facility: dividends paid this year, which the income tax counts as profit.</summary>
        public NativeArray<long> FacilityDividendsYear;
        /// <summary>Per node, raw Fx: the share of wages paid there that leaves for the nation's towns.</summary>
        [ReadOnly] public NativeArray<long> NodeWageAwayRaw;
        /// <summary>Per node: this week's wages leaving it.</summary>
        public NativeArray<long> NodeWagesAway;

        public void Execute()
        {
            for (int f = 0; f < FacilityCash.Length; f++)
            {
                long upkeep = DefUpkeep[FacilityDefIndex[f]] * FacilityLevel[f] + FacilityOutputWage[f];
                FacilityCash[f] -= upkeep;
                long away = Fx.MulIntRaw(upkeep, NodeWageAwayRaw[FacilityNode[f]]);
                HouseholdCash[FacilityNode[f]] += upkeep - away;
                NodeWagesAway[FacilityNode[f]] += away;
                long buffer = (upkeep + FacilityInputCost[f]) * DividendBufferWeeks;
                if (FacilityOwner[f] == OwnerIds.Npc && FacilityCash[f] > buffer)
                {
                    long dividend = Fx.MulIntRaw(FacilityCash[f] - buffer, DividendShareRaw);
                    FacilityCash[f] -= dividend;
                    FacilityDividendsYear[f] += dividend;
                    if (FacilityGroup[f] >= 0) GroupDividend[FacilityGroup[f]] += dividend;
                    else HouseholdCash[FacilityNode[f]] += dividend;
                }
                // A facility held for auction has no operator to fail; its sale settles the arrears.
                if (FacilityCash[f] >= 0 || FacilityOwner[f] == OwnerIds.Bank) FacilityNegativeCashWeeks[f] = 0;
                else if (++FacilityNegativeCashWeeks[f] >= BankruptWeeks) Bankrupt.Add(f);
            }
        }
    }
}
