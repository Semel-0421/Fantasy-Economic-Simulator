using FES.Core;
using FES.Data;
using FES.Economy;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace FES.Agents
{
    public struct MerchantLevelLimits
    {
        public long MaxWeight;
        public long MaxSize;
        public long Upkeep;
    }

    /// <summary>
    /// Every merchant group of a detailed node, every group based at one wherever it is, and every group on a
    /// trip, in row order, so results are independent of Burst (SYS-MERCHANTS, DEC-003). A group arrives and sells,
    /// picks the single most profitable load for one route out of its node within the range of its personality,
    /// sets off, and pays its weekly upkeep. Coins go to the floats and
    /// households directly; tolls and bankruptcies wait in <see cref="TollsByNode"/> and <see cref="Bankrupt"/> for
    /// the managed step after the job. Cargo rows emptied here keep a zero quantity until the job ends.
    /// </summary>
    [BurstCompile(CompileSynchronously = true)]
    public struct DetailedMerchantJob : IJob
    {
        public ulong Seed;
        public int Turn;
        public uint EncounterStream;
        public uint LicenseStream;
        public int LicensedPermille;
        public int BaseGoodCount;
        public int CategoryCount;
        public int BankruptWeeks;
        public int MerchantHolderBase;
        public long CashLossRaw;
        public long CargoLossRaw;
        public long DangerValueScale;
        /// <summary>Groups judge only nodes they know; others at the anchor price (P13d, reference). Off: all known.</summary>
        public bool Rumors;

        [ReadOnly] public NativeArray<byte> NodeDetail;
        [ReadOnly] public NativeArray<int> NodeContinent;
        [ReadOnly] public NativeArray<int> NodeParent;
        [ReadOnly] public NativeArray<byte> NodeLevel;
        [ReadOnly] public NativeArray<byte> NodeCategorySpecialty;
        [ReadOnly] public NativeArray<long> Tariff;
        [ReadOnly] public NativeArray<long> SpecialtySurcharge;
        [ReadOnly] public NativeArray<int> NodeLicenseRow;
        [ReadOnly] public NativeArray<byte> Licensed;
        [ReadOnly] public NativeArray<int> NodeFloat;
        [ReadOnly] public NativeArray<long> ContinentPriceIndex;

        [ReadOnly] public NativeArray<int> OutStart;
        [ReadOnly] public NativeArray<int> OutCount;
        [ReadOnly] public NativeArray<int> RouteIndex;
        [ReadOnly] public NativeArray<int> RouteTo;
        [ReadOnly] public NativeArray<int> RouteTurns;
        [ReadOnly] public NativeArray<int> RouteBaseFee;
        [ReadOnly] public NativeArray<long> RouteCostPerUnitRaw;
        [ReadOnly] public NativeArray<long> RouteDangerRaw;

        [ReadOnly] public NativeArray<int> MarketCellStart;
        [ReadOnly] public NativeArray<int> MarketCellCount;
        [ReadOnly] public NativeArray<int> CellGood;
        [ReadOnly] public NativeArray<int> GoodBasePrice;
        [ReadOnly] public NativeArray<int> GoodWeight;
        [ReadOnly] public NativeArray<int> GoodSize;
        [ReadOnly] public NativeArray<int> GoodCategory;
        [ReadOnly] public NativeArray<int> Price;

        /// <summary>Per level: the capacity and upkeep of a group based there.</summary>
        [ReadOnly] public NativeArray<MerchantLevelLimits> Levels;
        /// <summary>Per trait: see <see cref="MerchantTraitTable"/>.</summary>
        [ReadOnly] public NativeArray<long> TraitSpendRaw;
        [ReadOnly] public NativeArray<long> TraitMinMarginRaw;
        [ReadOnly] public NativeArray<long> TraitCapacityRaw;
        /// <summary>Per vehicle: how far it multiplies cargo (raw Fx, DEC-084).</summary>
        [ReadOnly] public NativeArray<long> VehicleCapacityRaw;
        [ReadOnly] public NativeArray<byte> AgentVehicle;
        /// <summary>Per vehicle, raw Fx (DEC-085): running cost, encounter chance and premium multipliers.</summary>
        [ReadOnly] public NativeArray<long> VehicleCostRaw;
        [ReadOnly] public NativeArray<long> VehicleDangerRaw;
        [ReadOnly] public NativeArray<long> VehiclePremiumRaw;
        public long GuardBaseCost;
        public int RiskAversion;
        public long GuardCostShareRaw;
        public long GuardReductionRaw;
        public long InsurancePremiumRaw;
        public long InsuranceRefundRaw;
        /// <summary>Trips, guarded trips, insured trips, encounters.</summary>
        public NativeArray<long> TripStats;
        [ReadOnly] public NativeArray<long> TraitUpkeepRaw;
        [ReadOnly] public NativeArray<int> TraitDistancePower;
        [ReadOnly] public NativeArray<int> TraitRangeLevel;

        [ReadOnly] public NativeArray<int> AgentHome;
        [ReadOnly] public NativeArray<byte> AgentMode;
        [ReadOnly] public NativeArray<byte> AgentScale;
        [ReadOnly] public NativeArray<byte> AgentTrait;
        public NativeArray<NodeMemory> AgentMemory;
        public NativeArray<int> AgentNode;
        public NativeArray<int> AgentDestination;
        public NativeArray<int> AgentArrivalTurn;
        public NativeArray<int> AgentNegativeWeeks;
        public NativeArray<long> AgentCash;

        public NativeArray<int> Stock;
        public NativeArray<int> Supply;
        public NativeArray<int> Demand;
        public NativeArray<long> Float;
        public NativeArray<long> HouseholdCash;
        public NativeArray<long> TollsByNode;
        public NativeList<int> Bankrupt;

        public NativeList<int> CargoHolder;
        public NativeList<int> CargoGood;
        public NativeList<int> CargoQuantity;

        /// <summary>Scratch. Per group: its first cargo row, or -1; per row: the group's next one.</summary>
        public NativeArray<int> FirstRow;
        public NativeList<int> NextRow;
        /// <summary>Scratch, per node: what groups there told each other this turn (rumors).</summary>
        public NativeArray<NodeMemory> Board;
        public NativeList<int> Rows;
        /// <summary>Scratch, per node: where its stocked cells start in <see cref="StockedCells"/>, or -1 before anyone looked.</summary>
        public NativeArray<int> StockedStart;
        public NativeArray<int> StockedCount;
        public NativeList<int> StockedCells;

        public void Execute()
        {
            for (int n = 0; n < StockedStart.Length; n++) StockedStart[n] = -1;
            StockedCells.Clear();
            for (int a = 0; a < FirstRow.Length; a++) FirstRow[a] = -1;
            NextRow.Clear();
            for (int r = 0; r < CargoHolder.Length; r++)
            {
                int a = CargoHolder[r] - MerchantHolderBase;
                bool merchant = a >= 0 && a < FirstRow.Length;
                NextRow.Add(merchant ? FirstRow[a] : -1);
                if (merchant) FirstRow[a] = r;
            }
            if (Rumors)
                for (int n = 0; n < Board.Length; n++) Board[n] = NodeMemory.Empty;

            for (int a = 0; a < AgentNode.Length; a++)
            {
                bool travelling = AgentDestination[a] >= 0;
                if (!travelling && NodeDetail[AgentNode[a]] != (byte)SimDetail.Detailed
                    && NodeDetail[AgentHome[a]] != (byte)SimDetail.Detailed) continue;
                if (AgentMode[a] != (byte)ControlMode.Automated) continue;
                bool arrived = travelling && Turn >= AgentArrivalTurn[a];
                if (arrived) Arrive(a);
                if (AgentDestination[a] < 0)
                {
                    if (Rumors) Listen(a);
                    if (!arrived) SellHere(a, AgentNode[a]); // what no one could pay for last time
                    TryStartTrip(a);
                }
                PayUpkeep(a);
            }

            for (int r = CargoQuantity.Length - 1; r >= 0; r--)
            {
                if (CargoQuantity[r] != 0) continue;
                CargoHolder.RemoveAtSwapBack(r);
                CargoGood.RemoveAtSwapBack(r);
                CargoQuantity.RemoveAtSwapBack(r);
            }
        }

        // What the group knows of its node, and what others here know, passes around.
        void Listen(int a)
        {
            int node = AgentNode[a];
            var memory = AgentMemory[a];
            memory.Learn(node);
            var board = Board[node];
            for (int i = 0; i < NodeMemory.Slots; i++) memory.Learn(board[i]);
            for (int i = 0; i < NodeMemory.Slots; i++) board.Learn(memory[i]);
            AgentMemory[a] = memory;
            Board[node] = board;
        }

        // The group's cargo rows with goods in them, sorted by good.
        void CollectRows(int a)
        {
            Rows.Clear();
            for (int r = FirstRow[a]; r >= 0; r = NextRow[r])
            {
                if (CargoQuantity[r] <= 0) continue;
                int at = Rows.Length;
                Rows.Add(r);
                while (at > 0 && CargoGood[Rows[at - 1]] > CargoGood[r])
                {
                    Rows[at] = Rows[at - 1];
                    at--;
                }
                Rows[at] = r;
            }
        }

        // Sells the whole load at the destination's current price (DEC-006), as far as the traders there can pay
        // (DEC-061); unsellable goods stay on board.
        void Arrive(int a)
        {
            int node = AgentDestination[a];
            AgentNode[a] = node;
            AgentDestination[a] = -1;
            AgentArrivalTurn[a] = -1;
            if (Rumors)
            {
                var memory = AgentMemory[a];
                memory.Learn(node);
                AgentMemory[a] = memory;
            }
            SellHere(a, node);
        }

        void SellHere(int a, int node)
        {
            CollectRows(a);
            for (int i = 0; i < Rows.Length; i++)
            {
                int row = Rows[i], good = CargoGood[row];
                if (NeedsLicense(node, good) && !HoldsLicenses(a)) continue;
                int cell = FindCell(node, good);
                if (cell < 0) continue;
                long price = Price[cell];
                long units = CargoQuantity[row];
                if (price > 0)
                {
                    long payable = Float[NodeFloat[node]] > 0 ? Float[NodeFloat[node]] / price : 0;
                    if (units > payable) units = payable;
                }
                if (units <= 0) continue;
                Supply[cell] = MarketStore.ClampToNonNegativeInt(Supply[cell] + units);
                AgentCash[a] += units * price;
                Float[NodeFloat[node]] -= units * price;
                CargoQuantity[row] -= (int)units;
            }
        }

        void TryStartTrip(int a)
        {
            int node = AgentNode[a];
            long cash = AgentCash[a];
            if (cash <= 0)
            {
                StartSellingTrip(a, node);
                return;
            }
            int trait = AgentTrait[a];
            long spendRaw = TraitSpendRaw[trait];
            long budget = spendRaw == Fx.OneRaw ? cash : Fx.MulIntRaw(cash, spendRaw);
            long minMarginRaw = TraitMinMarginRaw[trait];
            int power = TraitDistancePower[trait];
            int scale = AgentScale[a];
            long maxWeight = Fx.MulIntRaw(Fx.MulIntRaw(Levels[scale].MaxWeight, TraitCapacityRaw[trait]), VehicleCapacityRaw[AgentVehicle[a]]);
            long maxSize = Fx.MulIntRaw(Fx.MulIntRaw(Levels[scale].MaxSize, TraitCapacityRaw[trait]), VehicleCapacityRaw[AgentVehicle[a]]);
            long loadWeight = 0, loadSize = 0;
            for (int r = FirstRow[a]; r >= 0; r = NextRow[r])
            {
                loadWeight += (long)CargoQuantity[r] * GoodWeight[CargoGood[r]];
                loadSize += (long)CargoQuantity[r] * GoodSize[CargoGood[r]];
            }
            bool licensed = HoldsLicenses(a);
            var memory = AgentMemory[a];
            long indexRaw = ContinentPriceIndex[NodeContinent[node]];
            int anchor = RangeAnchor(a);
            bool inRange = Within(node, anchor); // a group outside its range (an older save) may still leave

            int bestRoute = -1, bestCell = -1;
            long bestQuantity = 0, bestScore = 0;
            int firstRoute = OutStart[node], lastRoute = firstRoute + OutCount[node];
            // Only stocked goods can be bought, and most cells have no stock: the first group at a node lists those
            // (stock only falls during the job), then each looks up its destinations' cells.
            if (StockedStart[node] < 0)
            {
                StockedStart[node] = StockedCells.Length;
                int cellEnd = MarketCellStart[node] + MarketCellCount[node];
                for (int c = MarketCellStart[node]; c < cellEnd; c++)
                    if (Stock[c] > 0) StockedCells.Add(c);
                StockedCount[node] = StockedCells.Length - StockedStart[node];
            }
            int stockedEnd = StockedStart[node] + StockedCount[node];
            for (int s = StockedStart[node]; s < stockedEnd; s++)
            {
                int cell = StockedCells[s];
                if (Stock[cell] <= 0) continue;
                int good = CellGood[cell];
                long buy = Price[cell];
                long fit = int.MaxValue;
                if (GoodWeight[good] > 0) fit = Min(fit, (maxWeight - loadWeight) / GoodWeight[good]);
                if (GoodSize[good] > 0) fit = Min(fit, (maxSize - loadSize) / GoodSize[good]);
                long quantityCap = Min(Stock[cell], fit < 0 ? 0 : fit);
                if (quantityCap <= 0) continue;
                for (int k = firstRoute; k < lastRoute; k++)
                {
                    int r = RouteIndex[k];
                    int to = RouteTo[r];
                    if (NodeContinent[to] != NodeContinent[node]) continue;
                    if (inRange && !Within(to, anchor)) continue;
                    int toCell = FindCell(to, good);
                    if (toCell < 0) continue;
                    if (!licensed && NeedsLicense(to, good)) continue;
                    long baseFee = Fx.MulIntRaw(RouteBaseFee[r], VehicleCostRaw[AgentVehicle[a]]);
                    long perUnitRaw = Fx.RoundShift(RouteCostPerUnitRaw[r] * VehicleCostRaw[AgentVehicle[a]]);
                    // A node nobody told the group about is judged at the anchor price.
                    long sell = !Rumors || memory.Knows(to) ? Price[toCell] : Fx.RoundShift(GoodBasePrice[good] * indexRaw);
                    long toll = Fx.MulIntRaw(sell, TollRate(to, good));
                    long marginRaw = ((sell - buy - toll) << Fx.FractionBits) - perUnitRaw;
                    if (marginRaw <= 0 || marginRaw < minMarginRaw * buy) continue;
                    long unitCost = buy + toll + Fx.MulIntRaw(1, perUnitRaw);
                    if (unitCost < 1) unitCost = 1;
                    long quantity = Min(quantityCap, (budget - baseFee) / unitCost);
                    if (quantity <= 0) continue;
                    long profit = Fx.MulIntRaw(quantity, marginRaw) - baseFee;
                    long score = profit;
                    if (profit > 0 && power > 0)
                    {
                        long turns = RouteTurns[r] < 1 ? 1 : RouteTurns[r];
                        long divisor = power == 1 ? turns : turns * turns;
                        score = (profit << Fx.FractionBits) / divisor;
                    }
                    if (score <= bestScore) continue;
                    bestRoute = r;
                    bestCell = cell;
                    bestQuantity = quantity;
                    bestScore = score;
                }
            }
            if (bestRoute < 0)
            {
                StartSellingTrip(a, node);
                return;
            }

            int boughtGood = CellGood[bestCell];
            Stock[bestCell] -= (int)bestQuantity;
            Supply[bestCell] = MarketStore.ClampToNonNegativeInt(Supply[bestCell] + bestQuantity);
            Demand[bestCell] = MarketStore.ClampToNonNegativeInt(Demand[bestCell] + bestQuantity);
            long cost = bestQuantity * Price[bestCell];
            AgentCash[a] -= cost;
            Float[NodeFloat[node]] += cost;
            AddCargo(a, boughtGood, (int)bestQuantity);
            Depart(a, node, bestRoute);
        }

        // With nothing worth buying, a group carries what it holds to where it sells for most after the trip's costs.
        void StartSellingTrip(int a, int node)
        {
            CollectRows(a);
            if (Rows.Length == 0) return;
            bool licensed = HoldsLicenses(a);
            int anchor = RangeAnchor(a);
            bool inRange = Within(node, anchor);
            int bestRoute = -1;
            long bestNet = 0;
            int firstRoute = OutStart[node], lastRoute = firstRoute + OutCount[node];
            for (int k = firstRoute; k < lastRoute; k++)
            {
                int r = RouteIndex[k];
                int to = RouteTo[r];
                if (NodeContinent[to] != NodeContinent[node]) continue;
                if (inRange && !Within(to, anchor)) continue;
                long net = -Fx.MulIntRaw(RouteBaseFee[r], VehicleCostRaw[AgentVehicle[a]]), units = 0;
                for (int i = 0; i < Rows.Length; i++)
                {
                    int good = CargoGood[Rows[i]];
                    units += CargoQuantity[Rows[i]];
                    if (!licensed && NeedsLicense(to, good)) continue;
                    int cell = FindCell(to, good);
                    if (cell < 0) continue;
                    long value = (long)CargoQuantity[Rows[i]] * Price[cell];
                    net += value - Fx.MulIntRaw(value, TollRate(to, good));
                }
                net -= Fx.MulIntRaw(Fx.MulIntRaw(units, RouteCostPerUnitRaw[r]), VehicleCostRaw[AgentVehicle[a]]);
                if (net <= bestNet) continue;
                bestRoute = r;
                bestNet = net;
            }
            if (bestRoute >= 0) Depart(a, node, bestRoute);
        }

        void Depart(int a, int node, int route)
        {
            // The same trip cost as the player's (DEC-015): merchants need no ship (DEC-034), and buy no guards.
            int bestRoute = route;
            int destination = RouteTo[bestRoute];
            CollectRows(a);
            long units = 0, tollTotal = 0, value = 0;
            for (int i = 0; i < Rows.Length; i++)
            {
                int row = Rows[i], good = CargoGood[row];
                units += CargoQuantity[row];
                int cell = FindNearestCell(destination, good);
                if (cell < 0) continue;
                long itemValue = (long)CargoQuantity[row] * Price[cell];
                tollTotal += Fx.MulIntRaw(itemValue, TollRate(destination, good));
                value += itemValue;
            }
            long fee = Fx.MulIntRaw(RouteBaseFee[bestRoute] + Fx.MulIntRaw(units, RouteCostPerUnitRaw[bestRoute]), VehicleCostRaw[AgentVehicle[a]]);
            AgentCash[a] -= fee + tollTotal;
            HouseholdCash[node] += fee; // fees are wages at the origin (DEC-061)
            TollsByNode[destination] += tollTotal;
            AgentDestination[a] = destination;
            AgentArrivalTurn[a] = Turn + RouteTurns[bestRoute];

            // Detailed merchants roll encounters like the player; bigger vehicles draw more of them (DEC-085).
            long chanceRaw = (RouteDangerRaw[bestRoute] * (Fx.OneRaw + (value << Fx.FractionBits) / DangerValueScale)) >> Fx.FractionBits;
            chanceRaw = Fx.RoundShift(chanceRaw * VehicleDangerRaw[AgentVehicle[a]]);
            if (chanceRaw > Fx.OneRaw) chanceRaw = Fx.OneRaw;
            if (chanceRaw < 0) chanceRaw = 0;
            TripStats[0]++;
            // A group hires guards and insures when the expected cargo loss they save, half of encounters at the cargo share, beats their cost.
            long expectedLoss = Fx.MulIntRaw(Fx.MulIntRaw(value, CargoLossRaw), chanceRaw) / 2;
            long guardCost = GuardBaseCost + Fx.MulIntRaw(value, GuardCostShareRaw);
            if (Fx.MulIntRaw(expectedLoss, GuardReductionRaw) * RiskAversion > guardCost && AgentCash[a] >= guardCost)
            {
                AgentCash[a] -= guardCost;
                HouseholdCash[node] += guardCost;
                chanceRaw -= Fx.RoundShift(chanceRaw * GuardReductionRaw);
                expectedLoss -= Fx.MulIntRaw(expectedLoss, GuardReductionRaw);
                TripStats[1]++;
            }
            long premium = Fx.MulIntRaw(Fx.MulIntRaw(value, InsurancePremiumRaw), VehiclePremiumRaw[AgentVehicle[a]]);
            bool insured = Fx.MulIntRaw(expectedLoss, InsuranceRefundRaw) > premium && AgentCash[a] >= premium;
            if (insured)
            {
                AgentCash[a] -= premium;
                HouseholdCash[node] += premium;
                TripStats[2]++;
            }
            var rng = DetRandom.For(Seed, Turn, EncounterStream, a, 1);
            if (rng.NextFx01().Raw >= chanceRaw) return;
            TripStats[3]++;
            if (rng.NextInt(2) == 0)
            {
                // [Draft] Stolen coins end up spent where the trip began.
                long stolen = Fx.MulIntRaw(AgentCash[a] > 0 ? AgentCash[a] : 0, CashLossRaw);
                AgentCash[a] -= stolen;
                HouseholdCash[node] += stolen;
                return;
            }
            for (int i = 0; i < Rows.Length; i++)
                CargoQuantity[Rows[i]] -= (int)Fx.MulIntRaw(CargoQuantity[Rows[i]], CargoLossRaw);
            if (!insured) return;
            // [Draft] The households at the origin stand behind the policy, as far as they hold.
            long refund = Fx.MulIntRaw(Fx.MulIntRaw(value, CargoLossRaw), InsuranceRefundRaw);
            if (refund > HouseholdCash[node]) refund = HouseholdCash[node] > 0 ? HouseholdCash[node] : 0;
            HouseholdCash[node] -= refund;
            AgentCash[a] += refund;
        }

        void AddCargo(int a, int good, int quantity)
        {
            for (int r = FirstRow[a]; r >= 0; r = NextRow[r])
            {
                if (CargoGood[r] != good) continue;
                CargoQuantity[r] += quantity;
                return;
            }
            CargoHolder.Add(MerchantHolderBase + a);
            CargoGood.Add(good);
            CargoQuantity.Add(quantity);
            NextRow.Add(FirstRow[a]);
            FirstRow[a] = CargoQuantity.Length - 1;
        }

        // Upkeep can push cash below zero; enough such weeks and a new group replaces this one (DEC-011).
        void PayUpkeep(int a)
        {
            long upkeep = Fx.MulIntRaw(Fx.MulIntRaw(Levels[AgentScale[a]].Upkeep, TraitUpkeepRaw[AgentTrait[a]]), VehicleCostRaw[AgentVehicle[a]]);
            AgentCash[a] -= upkeep;
            HouseholdCash[AgentNode[a]] += upkeep;
            if (AgentCash[a] >= 0)
            {
                AgentNegativeWeeks[a] = 0;
                return;
            }
            AgentNegativeWeeks[a]++;
            if (AgentNegativeWeeks[a] >= BankruptWeeks) Bankrupt.Add(a);
        }

        // The home's ancestor at the personality's level, or the home itself when it sits above that level.
        int RangeAnchor(int a)
        {
            int m = AgentHome[a], level = TraitRangeLevel[AgentTrait[a]];
            while (NodeLevel[m] > level) m = NodeParent[m];
            return m;
        }

        bool Within(int node, int anchor)
        {
            for (int m = node; m >= 0; m = NodeParent[m])
                if (m == anchor) return true;
            return false;
        }

        bool HoldsLicenses(int a) => DetRandom.For(Seed, 0, LicenseStream, a).ChancePermille(LicensedPermille);

        bool NeedsLicense(int node, int good) =>
            Licensed[NodeLicenseRow[node] * BaseGoodCount + good / QualityGrades.Count] != 0;

        long TollRate(int node, int good)
        {
            long rate = Tariff[node];
            if (NodeCategorySpecialty[node * CategoryCount + GoodCategory[good]] != 0) rate += SpecialtySurcharge[node];
            return rate;
        }

        int FindNearestCell(int node, int good)
        {
            for (int m = node; m >= 0; m = NodeParent[m])
            {
                int cell = FindCell(m, good);
                if (cell >= 0) return cell;
            }
            return -1;
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

        static long Min(long a, long b) => a < b ? a : b;
    }
}
