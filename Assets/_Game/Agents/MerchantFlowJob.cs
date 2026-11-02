using System;
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
    /// Aggregate merchant trade (DEC-003): for every aggregate node, along every route within its continent,
    /// each good both markets trade moves when its destination price beats the origin price plus the
    /// per-unit charge and toll. The amount is a share of the origin's stock proportional to the margin,
    /// licensed goods scaled by the licensed share, all within the node's pooled cargo capacity spread over
    /// its routes and their travel turns, goods taken in cell order. The units leave the origin's stock (through its supply into its
    /// demand, like a purchase) and reach the destination's supply the same week, less the route's expected
    /// encounter loss ([Draft]: no transit time, no base fee). Between nations, the destination's traders pay the
    /// origin's for the units at the origin price, and move no more than their float pays for (DEC-061). Between
    /// continents the flow runs at a share of the normal one and the payment changes coins at both international
    /// banks, as far as they hold them (DEC-066).
    /// Nodes run in order, so results are independent of Burst.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct MerchantFlowJob : IJob
    {
        public int CategoryCount;
        public long FlowShareRaw;
        public long LicensedShareRaw;
        public int BaseGoodCount;
        /// <summary>Share of the normal flow between continents (raw Fx); zero is none.</summary>
        public long CrossFlowRaw;
        public int ContinentCount;
        /// <summary>Per (from, to) continent: coins of to for one of from (raw Fx).</summary>
        [ReadOnly] public NativeArray<long> CoinRateRaw;
        public long ExchangeFeeRaw;
        /// <summary>Per continent: what its international bank holds.</summary>
        public NativeArray<long> International;
        /// <summary>Share of a flow's margin the traders pay as wages at the origin (raw Fx, DEC-098).</summary>
        public long WageRaw;
        public NativeArray<long> HouseholdCash;

        [ReadOnly] public NativeArray<byte> NodeDetail;
        [ReadOnly] public NativeArray<int> NodeContinent;
        [ReadOnly] public NativeArray<long> NodeCapacity;
        /// <summary>Per node: its groups' average share of stock they dare move, and least margin over the buying price (raw Q16).</summary>
        [ReadOnly] public NativeArray<long> NodeSpendRaw;
        [ReadOnly] public NativeArray<long> NodeMinMarginRaw;
        /// <summary>Per node, raw Fx: its groups' average vehicle cost and danger multipliers (DEC-085).</summary>
        [ReadOnly] public NativeArray<long> NodeVehicleCostRaw;
        [ReadOnly] public NativeArray<long> NodeVehicleDangerRaw;
        [ReadOnly] public NativeArray<byte> NodeCategorySpecialty;
        [ReadOnly] public NativeArray<long> Tariff;
        [ReadOnly] public NativeArray<long> SpecialtySurcharge;
        [ReadOnly] public NativeArray<int> OutStart;
        [ReadOnly] public NativeArray<int> OutCount;
        [ReadOnly] public NativeArray<int> RouteIndex;
        [ReadOnly] public NativeArray<int> RouteTo;
        [ReadOnly] public NativeArray<int> RouteTurns;
        /// <summary>Per route: charge per cargo unit after paving (raw Fx).</summary>
        [ReadOnly] public NativeArray<long> RouteCostPerUnitRaw;
        /// <summary>Scratch, per route: weight the node's merchants can still move along it this week.</summary>
        public NativeArray<long> RouteBudget;
        /// <summary>Per route: expected share of the goods lost on the way (raw Fx).</summary>
        [ReadOnly] public NativeArray<long> RouteExpectedLoss;
        [ReadOnly] public NativeArray<int> MarketCellStart;
        [ReadOnly] public NativeArray<int> MarketCellCount;
        [ReadOnly] public NativeArray<int> CellGood;
        [ReadOnly] public NativeArray<int> GoodCategory;
        [ReadOnly] public NativeArray<int> GoodWeight;
        /// <summary>Per node: its licensing row; per (row, base good): 1 when selling needs a license (DEC-058).</summary>
        [ReadOnly] public NativeArray<int> NodeLicenseRow;
        [ReadOnly] public NativeArray<byte> Licensed;
        [ReadOnly] public NativeArray<int> Price;
        [ReadOnly] public NativeArray<int> NodeFloat;
        public NativeArray<long> Float;
        /// <summary>Per float: value paid to other floats for goods flowing in, for state planning.</summary>
        public NativeArray<long> Imports;

        public NativeArray<int> Stock;
        public NativeArray<int> Supply;
        public NativeArray<int> Demand;

        public void Execute()
        {
            for (int n = 0; n < NodeDetail.Length; n++)
            {
                if (NodeDetail[n] == (byte)SimDetail.Detailed || NodeCapacity[n] <= 0 || !ShareCapacity(n)) continue;
                // Most cells hold no stock: scan the node's cells once and look up each destination's cell.
                int cellEnd = MarketCellStart[n] + MarketCellCount[n];
                for (int c = MarketCellStart[n]; c < cellEnd; c++) MoveCell(n, c);
            }
        }

        // Each route open to the node's groups gets its part of their capacity, less the longer it takes. False when none is open.
        bool ShareCapacity(int n)
        {
            int first = OutStart[n], last = first + OutCount[n];
            int routes = 0;
            for (int k = first; k < last; k++)
                if (CrossFlowRaw > 0 || NodeContinent[RouteTo[RouteIndex[k]]] == NodeContinent[n]) routes++;
            if (routes == 0) return false;
            for (int k = first; k < last; k++)
            {
                int r = RouteIndex[k];
                RouteBudget[r] = CrossFlowRaw <= 0 && NodeContinent[RouteTo[r]] != NodeContinent[n]
                    ? 0
                    : NodeCapacity[n] / ((long)routes * Math.Max(1, RouteTurns[r]));
            }
            return true;
        }

        void MoveCell(int n, int c)
        {
            int first = OutStart[n], last = first + OutCount[n];
            for (int k = first; k < last && Stock[c] > 0; k++)
            {
                int r = RouteIndex[k];
                if (RouteBudget[r] <= 0) continue;
                int toCell = FindCell(RouteTo[r], CellGood[c]);
                if (toCell < 0) continue;
                long budget = RouteBudget[r];
                Move(n, c, toCell, r, ref budget);
                RouteBudget[r] = budget;
            }
        }

        int FindCell(int market, int good)
        {
            int lo = MarketCellStart[market], hi = lo + MarketCellCount[market] - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int g = CellGood[mid];
                if (g == good) return mid;
                if (g < good) lo = mid + 1;
                else hi = mid - 1;
            }
            return -1;
        }

        void Move(int from, int fromCell, int toCell, int r, ref long budget)
        {
            int to = RouteTo[r], good = CellGood[fromCell];
            bool cross = NodeContinent[from] != NodeContinent[to];
            long sell = InOriginCoin(from, to, Price[toCell]);
            if (cross && sell <= 0) return;
            long perUnitRaw = Fx.RoundShift(RouteCostPerUnitRaw[r] * NodeVehicleCostRaw[from]);
            long marginRaw = ((sell - Price[fromCell] - Fx.MulIntRaw(sell, TollRate(to, good))) << Fx.FractionBits) - perUnitRaw;
            if (marginRaw <= 0 || Stock[fromCell] <= 0 || marginRaw < NodeMinMarginRaw[from] * Price[fromCell]) return;

            long buy = Price[fromCell];
            long pay = PayInDestinationCoin(from, to, buy);
            long quantity = FlowQuantity(from, fromCell, to, sell, marginRaw, budget);
            quantity = WithinCoins(from, to, quantity, buy, pay);
            if (quantity <= 0) return;
            budget -= quantity * GoodWeight[good];
            Settle(from, to, quantity, buy, pay);
            if (WageRaw > 0) PayWage(from, to, Fx.MulIntRaw(Fx.MulIntRaw(quantity, marginRaw), WageRaw));

            long expectedLossRaw = Fx.RoundShift(RouteExpectedLoss[r] * NodeVehicleDangerRaw[from]);
            Stock[fromCell] -= (int)quantity;
            Supply[fromCell] = MarketStore.ClampToNonNegativeInt(Supply[fromCell] + quantity);
            Demand[fromCell] = MarketStore.ClampToNonNegativeInt(Demand[fromCell] + quantity);
            Supply[toCell] = MarketStore.ClampToNonNegativeInt(Supply[toCell] + quantity - Fx.MulIntRaw(quantity, expectedLossRaw));
        }

        // A price at the destination, valued in the origin's coin after the exchange fee.
        long InOriginCoin(int from, int to, long price)
        {
            int fromCoin = NodeContinent[from], toCoin = NodeContinent[to];
            if (fromCoin == toCoin) return price;
            long value = Fx.MulIntRaw(price, CoinRateRaw[toCoin * ContinentCount + fromCoin]);
            return value - Fx.MulIntRaw(value, ExchangeFeeRaw);
        }

        long PayInDestinationCoin(int from, int to, long buy)
        {
            int fromCoin = NodeContinent[from], toCoin = NodeContinent[to];
            if (fromCoin == toCoin) return buy;
            long pay = Fx.MulIntRaw(buy, CoinRateRaw[fromCoin * ContinentCount + toCoin]);
            return pay + Fx.MulIntRaw(pay, ExchangeFeeRaw);
        }

        long TollRate(int to, int good)
        {
            long rate = Tariff[to];
            if (NodeCategorySpecialty[to * CategoryCount + GoodCategory[good]] != 0) rate += SpecialtySurcharge[to];
            return rate;
        }

        long FlowQuantity(int from, int fromCell, int to, long sell, long marginRaw, long budget)
        {
            int good = CellGood[fromCell];
            long quantity = Fx.MulIntRaw(Stock[fromCell], Fx.RoundShift(FlowShareRaw * NodeSpendRaw[from])) * marginRaw / (sell << Fx.FractionBits);
            if (Licensed[NodeLicenseRow[to] * BaseGoodCount + good / QualityGrades.Count] != 0) quantity = Fx.MulIntRaw(quantity, LicensedShareRaw);
            if (GoodWeight[good] > 0) quantity = Math.Min(quantity, budget / GoodWeight[good]);
            if (NodeContinent[from] != NodeContinent[to]) quantity = Fx.MulIntRaw(quantity, CrossFlowRaw);
            return Math.Min(quantity, Stock[fromCell]); // good vehicles can ask for more than there is
        }

        long WithinCoins(int from, int to, long quantity, long buy, long pay)
        {
            if (NodeContinent[from] != NodeContinent[to] && buy > 0)
                quantity = Math.Min(quantity, Math.Max(0, International[NodeContinent[from]]) / buy);
            int payer = NodeFloat[to];
            if (payer != NodeFloat[from] && pay > 0) quantity = Math.Min(quantity, Math.Max(0, Float[payer]) / pay);
            return quantity;
        }

        void Settle(int from, int to, long quantity, long buy, long pay)
        {
            int payer = NodeFloat[to], payee = NodeFloat[from];
            if (payer != payee)
            {
                Float[payer] -= quantity * pay;
                Float[payee] += quantity * buy;
                Imports[payer] += quantity * pay;
            }
            if (NodeContinent[from] == NodeContinent[to]) return;
            International[NodeContinent[to]] += quantity * pay;
            International[NodeContinent[from]] -= quantity * buy;
        }

        void PayWage(int from, int to, long wage)
        {
            int purse = NodeContinent[from] != NodeContinent[to] ? NodeFloat[from] : NodeFloat[to];
            if (wage > Float[purse]) wage = Float[purse] > 0 ? Float[purse] : 0;
            Float[purse] -= wage;
            HouseholdCash[from] += wage;
        }
    }
}
