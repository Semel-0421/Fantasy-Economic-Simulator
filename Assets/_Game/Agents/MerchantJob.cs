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
    /// trip, in row order, so results are independent of Burst (SYS-MERCHANTS, DEC-003). A group arrives, sells or
    /// carries its load on, picks the single most profitable load for one route out of its node within the range of its
    /// personality, sets off, and pays its weekly upkeep. Coins go to the floats and
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
        /// <summary>Groups weigh selling here against carrying the load on (DEC-093). Off: they sell all on arrival.</summary>
        public bool WeighSales;
        /// <summary>How many markets ahead a group judges a load (DEC-095).</summary>
        public int JudgeHops;
        /// <summary>A wish a group cannot fill bids the price up (DEC-095); per cell: <see cref="MarketStore.InputBid"/>.</summary>
        public bool Bids;
        /// <summary>A group leaving with one load still bids for a better one it could not find (DEC-096).</summary>
        public bool BidsWhileLoaded;
        public NativeArray<int> InputBid;
        /// <summary>A group stranded above its home with nothing to carry goes back empty (DEC-095).</summary>
        public bool ReturnHome;
        /// <summary>The lowest level whose groups trade between continents; below 0 none (DEC-096).</summary>
        public int CrossLevel;
        public int ContinentCount;
        /// <summary>Per (from, to) continent: coins of to for one of from (raw Fx).</summary>
        [ReadOnly] public NativeArray<long> CoinRateRaw;
        public long ExchangeFeeRaw;
        /// <summary>Per continent: what its international bank holds (DEC-031).</summary>
        public NativeArray<long> International;
        /// <summary>Per (node, continent): the first route of the way there, or -1.</summary>
        [ReadOnly] public NativeArray<int> RouteToward;
        /// <summary>Per node: a large enough group based here may sail whatever its personality.</summary>
        [ReadOnly] public NativeArray<byte> NodePort;
        /// <summary>A market whose traders cannot pay for this many units is no market to a group judging a load; 0 off.</summary>
        public int JudgeCoinUnits;
        /// <summary>Per float: bills it may still give, and coins its bank may still and did pay on them (DEC-097).</summary>
        public NativeArray<long> BillRoom;
        public NativeArray<long> BillBankRoom;
        public NativeArray<long> BillBankSpent;
        public long BillDiscountRaw;
        public int BillWeeks;
        public NativeList<int> BillHolder;
        public NativeList<int> BillPurse;
        public NativeList<long> BillFace;
        public NativeList<int> BillDue;
        /// <summary>Faces issued, and coins paid on them by banks.</summary>
        public NativeArray<long> BillStats;

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
        public long MemberPremiumRaw;
        [ReadOnly] public NativeArray<int> AgentAssociation;
        public int RiskAversion;
        public long GuardCostShareRaw;
        public long GuardReductionRaw;
        public long InsurancePremiumRaw;
        public long InsuranceRefundRaw;
        /// <summary>Trips, guarded trips, insured trips, encounters, then <see cref="MerchantTripStats"/> on sales.</summary>
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
        public NativeArray<long> AgentLoadCost;

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
                if (travelling && Turn >= AgentArrivalTurn[a]) Arrive(a);
                if (AgentDestination[a] < 0)
                {
                    if (Rumors) Listen(a);
                    int carryOn = SellOrCarry(a, AgentNode[a]); // an idle group tries what no one could pay for last time
                    if (carryOn >= 0) Depart(a, AgentNode[a], carryOn);
                    else TryStartTrip(a);
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
        }

        // The group's profit and loss at this node (DEC-093). What the goods cost is gone either way, so what counts is
        // what they bring here against what they bring elsewhere after the trip. It sells here when that is the better
        // of the two, even below cost, or when it would not last the trip. Returns the route to carry the load on, or -1.
        int SellOrCarry(int a, int node)
        {
            CollectRows(a);
            if (Rows.Length == 0) return -1;
            if (WeighSales)
            {
                int route = BestCarry(a, node, out long net, out long tripCost);
                if (route >= 0 && net > ValueHere(a, node))
                {
                    if (LastsTheTrip(a, route, tripCost))
                    {
                        TripStats[6]++;
                        return route;
                    }
                    TripStats[7]++;
                }
            }
            SellHere(a, node);
            return -1;
        }

        // What the load brings here now.
        long ValueHere(int a, int node)
        {
            bool licensed = HoldsLicenses(a);
            long value = 0;
            for (int i = 0; i < Rows.Length; i++)
            {
                int good = CargoGood[Rows[i]];
                if (!licensed && NeedsLicense(node, good)) continue;
                int cell = FindCell(node, good);
                if (cell >= 0) value += (long)CargoQuantity[Rows[i]] * Price[cell];
            }
            return Earn(a, node, Payable(a, node, value));
        }

        bool Abroad(int a, int node) => NodeContinent[node] != NodeContinent[AgentHome[a]];

        long CoinsIn(long amount, int from, int to) => from == to ? amount : Fx.MulIntRaw(amount, CoinRateRaw[from * ContinentCount + to]);

        // What local coins at a node come to in the group's own, which it gets after the exchange fee when it sells abroad (DEC-008).
        long Earn(int a, int node, long local)
        {
            if (!Abroad(a, node)) return local;
            long own = CoinsIn(local, NodeContinent[node], NodeContinent[AgentHome[a]]);
            return own - Fx.MulIntRaw(own, ExchangeFeeRaw);
        }

        // What local coins at a node cost in the group's own when it buys abroad.
        long Spend(int a, int node, long local)
        {
            if (!Abroad(a, node)) return local;
            long own = CoinsIn(local, NodeContinent[node], NodeContinent[AgentHome[a]]);
            return own + Fx.MulIntRaw(own, ExchangeFeeRaw);
        }

        // The group pays its own coins for something at a node. Abroad they go to its home's international bank, and
        // the local coins come out of the bank there as far as it holds them. Returns the local coins.
        long PayThere(int a, int node, long own)
        {
            if (!Abroad(a, node)) return own;
            int home = NodeContinent[AgentHome[a]], here = NodeContinent[node];
            long local = CoinsIn(own, home, here);
            local -= Fx.MulIntRaw(local, ExchangeFeeRaw);
            if (local > International[here]) local = International[here] > 0 ? International[here] : 0;
            International[home] += own;
            International[here] -= local;
            return local;
        }

        // Whether a group may take a route: within its range at home, anywhere once abroad, and between continents only
        // if it is large enough and bold enough (DEC-066, DEC-096).
        bool MayGo(int a, int at, int to, int anchor, bool inRange)
        {
            if (NodeContinent[to] != NodeContinent[at])
                return CrossLevel >= 0 && AgentScale[a] <= CrossLevel
                    && (TraitRangeLevel[AgentTrait[a]] <= (int)WorldLevel.Nation || NodePort[AgentHome[a]] != 0);
            return !inRange || Abroad(a, at) || Within(to, anchor);
        }

        bool HasCargo(int a)
        {
            for (int r = FirstRow[a]; r >= 0; r = NextRow[r])
                if (CargoQuantity[r] > 0) return true;
            return false;
        }

        // A group with nothing worth carrying and nothing to buy does not stay for good where it stranded: abroad it sails
        // home with what it holds (DEC-096), and above its home it comes back down empty to where the goods are (DEC-095).
        void HeadHome(int a, int node)
        {
            if (AgentDestination[a] >= 0) return;
            if (Abroad(a, node))
            {
                int route = RouteToward[node * ContinentCount + NodeContinent[AgentHome[a]]];
                if (route >= 0) Depart(a, node, route);
                return;
            }
            if (HasCargo(a)) return;
            int home = AgentHome[a];
            if (!ReturnHome || node == home || NodeLevel[node] >= NodeLevel[home]) return;
            int first = OutStart[node], last = first + OutCount[node];
            for (int k = first; k < last; k++)
            {
                int down = RouteTo[RouteIndex[k]];
                if (NodeLevel[down] <= NodeLevel[node] || !Within(home, down)) continue;
                Depart(a, node, RouteIndex[k]);
                return;
            }
        }

        // As much of a sum as the traders of a node can pay in coins, then in bills at home (DEC-097).
        long Payable(int a, int node, long value)
        {
            long held = Float[NodeFloat[node]];
            long coins = Min(value, held > 0 ? held : 0);
            if (Abroad(a, node)) return coins;
            long billed = Min(value - coins, BillRoom[NodeFloat[node]]);
            return coins + billed - Fx.MulIntRaw(billed, BillDiscountRaw);
        }

        // Traders who cannot pay give a bill, which goes to market with the goods; the bank pays the seller at a discount
        // as far as it has room, or the seller holds it to maturity (DEC-097). Returns the coins received now.
        long SellForBill(int a, int purse, long face)
        {
            BillRoom[purse] -= face;
            BillStats[0] += face;
            long discounted = face - Fx.MulIntRaw(face, BillDiscountRaw);
            bool bank = BillBankRoom[purse] >= discounted;
            BillHolder.Add(bank ? BillHolders.Bank : a);
            BillPurse.Add(purse);
            BillFace.Add(face);
            BillDue.Add(Turn + BillWeeks);
            if (!bank) return 0;
            BillBankRoom[purse] -= discounted;
            BillBankSpent[purse] += discounted;
            BillStats[1] += discounted;
            return discounted;
        }

        // Whether the traders of a market can pay for a fair lot at this price, in coins or, at home, in bills (DEC-096).
        // A price means nothing where no one has the money: that is how groups stranded on top of the world.
        bool TradersPay(int a, int node, long price)
        {
            if (JudgeCoinUnits <= 0) return true;
            long lot = JudgeCoinUnits * price;
            int purse = NodeFloat[node];
            return Float[purse] >= lot || (!Abroad(a, node) && BillRoom[purse] >= lot);
        }

        void BookSale(int a, long units, long proceeds, ref long carried)
        {
            long cost = AgentLoadCost[a] * units / carried;
            AgentLoadCost[a] -= cost;
            carried -= units;
            if (proceeds >= cost) return;
            TripStats[4]++;
            TripStats[5] += cost - proceeds;
        }

        // Whether the group gets there before its weeks below zero end it.
        bool LastsTheTrip(int a, int route, long tripCost) =>
            AgentCash[a] >= tripCost || AgentNegativeWeeks[a] + RouteTurns[route] < BankruptWeeks;

        // Sells at the current price (DEC-006), as far as the traders here can pay (DEC-061); unsellable goods stay on board.
        void SellHere(int a, int node)
        {
            CollectRows(a);
            long carried = 0;
            for (int i = 0; i < Rows.Length; i++) carried += CargoQuantity[Rows[i]];
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
                bool abroad = Abroad(a, node);
                int home = NodeContinent[AgentHome[a]];
                long proceeds = Earn(a, node, units * price);
                // Foreign coins change only as far as the home bank holds its own (DEC-031).
                if (abroad && proceeds > International[home])
                {
                    units = International[home] > 0 && proceeds > 0 ? units * International[home] / proceeds : 0;
                    proceeds = Earn(a, node, units * price);
                }
                if (units > 0)
                {
                    Supply[cell] = MarketStore.ClampToNonNegativeInt(Supply[cell] + units);
                    AgentCash[a] += proceeds;
                    Float[NodeFloat[node]] -= units * price;
                    if (abroad)
                    {
                        International[NodeContinent[node]] += units * price;
                        International[home] -= proceeds;
                    }
                    CargoQuantity[row] -= (int)units;
                    TripStats[8] += proceeds;
                    BookSale(a, units, proceeds, ref carried);
                }
                int purse = NodeFloat[node];
                if (abroad || price <= 0 || CargoQuantity[row] <= 0) continue;
                long billed = Min(CargoQuantity[row], BillRoom[purse] / price);
                if (billed <= 0) continue;
                long received = SellForBill(a, purse, billed * price);
                Supply[cell] = MarketStore.ClampToNonNegativeInt(Supply[cell] + billed);
                AgentCash[a] += received;
                CargoQuantity[row] -= (int)billed;
                TripStats[8] += billed * price;
                BookSale(a, billed, billed * price - Fx.MulIntRaw(billed * price, BillDiscountRaw), ref carried);
            }
        }

        void TryStartTrip(int a)
        {
            int node = AgentNode[a];
            long cash = AgentCash[a];
            if (cash <= 0)
            {
                StartSellingTrip(a, node);
                HeadHome(a, node);
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
            long bestQuantity = 0, bestScore = 0, bestWanted = 0, bestProfit = 0;
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
                long buy = Spend(a, node, Price[cell]);
                long fit = int.MaxValue;
                if (GoodWeight[good] > 0) fit = Min(fit, (maxWeight - loadWeight) / GoodWeight[good]);
                if (GoodSize[good] > 0) fit = Min(fit, (maxSize - loadSize) / GoodSize[good]);
                long quantityCap = Min(Stock[cell], fit < 0 ? 0 : fit);
                if (quantityCap <= 0) continue;
                for (int k = firstRoute; k < lastRoute; k++)
                {
                    int r = RouteIndex[k];
                    int to = RouteTo[r];
                    if (!MayGo(a, node, to, anchor, inRange)) continue;
                    int toCell = FindCell(to, good);
                    if (toCell < 0) continue;
                    if (!licensed && NeedsLicense(to, good)) continue;
                    long baseFee = Fx.MulIntRaw(RouteBaseFee[r], VehicleCostRaw[AgentVehicle[a]]);
                    long perUnitRaw = Fx.RoundShift(RouteCostPerUnitRaw[r] * VehicleCostRaw[AgentVehicle[a]]);
                    // A node nobody told the group about is judged at the anchor price.
                    long sell = !Rumors || memory.Knows(to) ? Earn(a, to, Price[toCell]) : Fx.RoundShift(GoodBasePrice[good] * indexRaw);
                    long toll = Fx.MulIntRaw(sell, TollRate(to, good));
                    if (!TradersPay(a, to, Price[toCell])) sell = 0;
                    sell = Max(sell, Beyond(a, to, node, good, licensed, anchor, inRange, JudgeHops - 1));
                    long marginRaw = ((sell - buy - toll) << Fx.FractionBits) - perUnitRaw;
                    if (marginRaw <= 0 || marginRaw < minMarginRaw * buy) continue;
                    long unitCost = buy + toll + Fx.MulIntRaw(1, perUnitRaw);
                    if (unitCost < 1) unitCost = 1;
                    long affordable = (budget - baseFee) / unitCost;
                    long quantity = Min(quantityCap, affordable);
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
                    bestWanted = Min(fit < 0 ? 0 : fit, affordable);
                    bestProfit = profit;
                }
            }
            if (bestRoute < 0)
            {
                if (Bids) BidForWhatIsMissing(a, node, 0, budget, maxWeight - loadWeight, maxSize - loadSize, minMarginRaw, licensed, anchor, inRange);
                StartSellingTrip(a, node);
                HeadHome(a, node);
                return;
            }
            if (Bids && bestWanted > bestQuantity) AddBid(bestCell, bestWanted - bestQuantity);
            if (BidsWhileLoaded) BidForWhatIsMissing(a, node, bestProfit, budget, maxWeight - loadWeight, maxSize - loadSize, minMarginRaw, licensed, anchor, inRange);

            // Abroad, what it pays comes out of the bank there as local coins (DEC-031).
            bool abroad = Abroad(a, node);
            if (abroad && bestQuantity * Price[bestCell] > International[NodeContinent[node]])
            {
                bestQuantity = International[NodeContinent[node]] > 0 && Price[bestCell] > 0 ? International[NodeContinent[node]] / Price[bestCell] : 0;
                if (bestQuantity <= 0)
                {
                    StartSellingTrip(a, node);
                    HeadHome(a, node);
                    return;
                }
            }

            int boughtGood = CellGood[bestCell];
            Stock[bestCell] -= (int)bestQuantity;
            Supply[bestCell] = MarketStore.ClampToNonNegativeInt(Supply[bestCell] + bestQuantity);
            Demand[bestCell] = MarketStore.ClampToNonNegativeInt(Demand[bestCell] + bestQuantity);
            long cost = bestQuantity * Price[bestCell];
            long own = Spend(a, node, cost);
            AgentCash[a] -= own;
            AgentLoadCost[a] += own;
            Float[NodeFloat[node]] += cost;
            if (abroad)
            {
                International[NodeContinent[AgentHome[a]]] += own;
                International[NodeContinent[node]] -= cost;
            }
            AddCargo(a, boughtGood, (int)bestQuantity);
            Depart(a, node, bestRoute);
        }

        // What a unit brings past a node, within the hops left, after each hop's charge and toll (DEC-095). It may pass
        // through a market that does not trade the good.
        long Beyond(int a, int at, int from, int good, bool licensed, int anchor, bool inRange, int hops)
        {
            if (hops <= 0) return 0;
            long best = 0;
            int first = OutStart[at], last = first + OutCount[at];
            for (int k = first; k < last; k++)
            {
                int r = RouteIndex[k];
                int next = RouteTo[r];
                if (next == from || !MayGo(a, at, next, anchor, inRange)) continue;
                int cell = FindCell(next, good);
                long here = cell >= 0 ? Earn(a, next, Price[cell]) : 0;
                long direct = (licensed || !NeedsLicense(next, good)) && cell >= 0 && TradersPay(a, next, Price[cell]) ? here : 0;
                long worth = Max(direct, Beyond(a, next, at, good, licensed, anchor, inRange, hops - 1));
                if (worth <= 0) continue;
                long net = worth - Fx.MulIntRaw(cell >= 0 ? here : worth, TollRate(next, good))
                    - Fx.MulIntRaw(1, Fx.RoundShift(RouteCostPerUnitRaw[r] * VehicleCostRaw[AgentVehicle[a]]));
                best = Max(best, net);
            }
            return best;
        }

        void AddBid(int cell, long units) => InputBid[cell] = MarketStore.ClampToNonNegativeInt(InputBid[cell] + units);

        // A group says what it would carry if the market had it, when that beats what it found (DEC-096). The bid draws
        // the good here, and stops by itself when the price has closed the margin (DEC-095).
        void BidForWhatIsMissing(int a, int node, long beat, long budget, long roomWeight, long roomSize, long minMarginRaw, bool licensed, int anchor, bool inRange)
        {
            int bestCell = -1;
            long bestProfit = beat, bestUnits = 0;
            int cellEnd = MarketCellStart[node] + MarketCellCount[node];
            int firstRoute = OutStart[node], lastRoute = firstRoute + OutCount[node];
            for (int cell = MarketCellStart[node]; cell < cellEnd; cell++)
            {
                if (Stock[cell] > 0) continue;
                int good = CellGood[cell];
                long fit = int.MaxValue;
                if (GoodWeight[good] > 0) fit = Min(fit, roomWeight / GoodWeight[good]);
                if (GoodSize[good] > 0) fit = Min(fit, roomSize / GoodSize[good]);
                if (fit <= 0) continue;
                long buy = Spend(a, node, Price[cell]);
                for (int k = firstRoute; k < lastRoute; k++)
                {
                    int r = RouteIndex[k];
                    int to = RouteTo[r];
                    if (!MayGo(a, node, to, anchor, inRange)) continue;
                    int toCell = FindCell(to, good);
                    if (toCell < 0 || (!licensed && NeedsLicense(to, good))) continue;
                    long baseFee = Fx.MulIntRaw(RouteBaseFee[r], VehicleCostRaw[AgentVehicle[a]]);
                    long perUnitRaw = Fx.RoundShift(RouteCostPerUnitRaw[r] * VehicleCostRaw[AgentVehicle[a]]);
                    long there = Earn(a, to, Price[toCell]);
                    long toll = Fx.MulIntRaw(there, TollRate(to, good));
                    if (!TradersPay(a, to, Price[toCell])) there = 0;
                    long sell = Max(there, Beyond(a, to, node, good, licensed, anchor, inRange, JudgeHops - 1));
                    long marginRaw = ((sell - buy - toll) << Fx.FractionBits) - perUnitRaw;
                    if (marginRaw <= 0 || marginRaw < minMarginRaw * buy) continue;
                    long unitCost = buy + toll + Fx.MulIntRaw(1, perUnitRaw);
                    if (unitCost < 1) unitCost = 1;
                    long units = Min(fit, (budget - baseFee) / unitCost);
                    if (units <= 0) continue;
                    long profit = Fx.MulIntRaw(units, marginRaw) - baseFee;
                    if (profit <= bestProfit) continue;
                    bestProfit = profit;
                    bestCell = cell;
                    bestUnits = units;
                }
            }
            if (bestCell >= 0) AddBid(bestCell, bestUnits);
        }

        // With nothing worth buying, a group carries what it holds to where it sells for most after the trip's costs.
        void StartSellingTrip(int a, int node)
        {
            CollectRows(a);
            if (Rows.Length == 0) return;
            int route = BestCarry(a, node, out _, out long tripCost);
            if (route >= 0 && (!WeighSales || LastsTheTrip(a, route, tripCost))) Depart(a, node, route);
        }

        // The route where the collected rows bring most after the trip's fee and toll, or -1. When sales are weighed
        // (DEC-093) a load is also worth only what the traders there can pay, less the trip's weeks of upkeep and what
        // the road is expected to take.
        int BestCarry(int a, int node, out long bestNet, out long bestCost)
        {
            bool licensed = HoldsLicenses(a);
            int anchor = RangeAnchor(a);
            bool inRange = Within(node, anchor);
            int bestRoute = -1;
            bestNet = 0;
            bestCost = 0;
            int firstRoute = OutStart[node], lastRoute = firstRoute + OutCount[node];
            for (int k = firstRoute; k < lastRoute; k++)
            {
                int r = RouteIndex[k];
                int to = RouteTo[r];
                if (!MayGo(a, node, to, anchor, inRange)) continue;
                long cost = Fx.MulIntRaw(RouteBaseFee[r], VehicleCostRaw[AgentVehicle[a]]), units = 0, value = 0, further = 0;
                for (int i = 0; i < Rows.Length; i++)
                {
                    int good = CargoGood[Rows[i]];
                    units += CargoQuantity[Rows[i]];
                    if (WeighSales) further += CargoQuantity[Rows[i]] * Beyond(a, to, node, good, licensed, anchor, inRange, JudgeHops - 1);
                    if (!licensed && NeedsLicense(to, good)) continue;
                    int cell = FindCell(to, good);
                    if (cell < 0) continue;
                    long itemValue = (long)CargoQuantity[Rows[i]] * Price[cell];
                    value += itemValue;
                    cost += Spend(a, to, Fx.MulIntRaw(itemValue, TollRate(to, good)));
                }
                value = Earn(a, to, value);
                cost += Fx.MulIntRaw(Fx.MulIntRaw(units, RouteCostPerUnitRaw[r]), VehicleCostRaw[AgentVehicle[a]]);
                long worth = value;
                if (WeighSales)
                {
                    cost += RouteTurns[r] * Upkeep(a);
                    long chanceRaw = EncounterChance(a, r, value);
                    worth = Min(Max(value, further), Earn(a, to, Payable(a, to, long.MaxValue / 2)))
                        - Fx.MulIntRaw(Fx.MulIntRaw(value, CargoLossRaw), chanceRaw) / 2;
                }
                if (worth - cost <= bestNet) continue;
                bestRoute = r;
                bestNet = worth - cost;
                bestCost = cost;
            }
            return bestRoute;
        }

        // The chance of an encounter on a route with a load of this value; bigger vehicles draw more of them (DEC-085).
        long EncounterChance(int a, int route, long value)
        {
            long chanceRaw = (RouteDangerRaw[route] * (Fx.OneRaw + (value << Fx.FractionBits) / DangerValueScale)) >> Fx.FractionBits;
            chanceRaw = Fx.RoundShift(chanceRaw * VehicleDangerRaw[AgentVehicle[a]]);
            if (chanceRaw > Fx.OneRaw) chanceRaw = Fx.OneRaw;
            if (chanceRaw < 0) chanceRaw = 0;
            return chanceRaw;
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
            bool abroad = Abroad(a, node);
            value = Earn(a, destination, value);
            tollTotal = Spend(a, destination, tollTotal);
            AgentCash[a] -= fee + tollTotal;
            HouseholdCash[node] += PayThere(a, node, fee); // fees are wages at the origin (DEC-061)
            TollsByNode[destination] += PayThere(a, destination, tollTotal);
            AgentDestination[a] = destination;
            AgentArrivalTurn[a] = Turn + RouteTurns[bestRoute];

            // Detailed merchants roll encounters like the player.
            long chanceRaw = EncounterChance(a, bestRoute, value);
            TripStats[0]++;
            // A group hires guards and insures when the expected cargo loss they save, half of encounters at the cargo share, beats their cost.
            long expectedLoss = Fx.MulIntRaw(Fx.MulIntRaw(value, CargoLossRaw), chanceRaw) / 2;
            long guardCost = GuardBaseCost + Fx.MulIntRaw(value, GuardCostShareRaw);
            // [Draft] Abroad a group hires no guards and buys no cover.
            if (!abroad && Fx.MulIntRaw(expectedLoss, GuardReductionRaw) * RiskAversion > guardCost && AgentCash[a] >= guardCost)
            {
                AgentCash[a] -= guardCost;
                HouseholdCash[node] += guardCost;
                chanceRaw -= Fx.RoundShift(chanceRaw * GuardReductionRaw);
                expectedLoss -= Fx.MulIntRaw(expectedLoss, GuardReductionRaw);
                TripStats[1]++;
            }
            long premium = Fx.MulIntRaw(Fx.MulIntRaw(value, InsurancePremiumRaw), VehiclePremiumRaw[AgentVehicle[a]]);
            if (AgentAssociation[a] >= 0 && AgentVehicle[a] > MerchantVehicles.Pack) premium = Fx.MulIntRaw(premium, MemberPremiumRaw);
            bool insured = !abroad && Fx.MulIntRaw(expectedLoss, InsuranceRefundRaw) > premium && AgentCash[a] >= premium;
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
                HouseholdCash[node] += PayThere(a, node, stolen);
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
            long upkeep = Upkeep(a);
            AgentCash[a] -= upkeep;
            HouseholdCash[AgentNode[a]] += PayThere(a, AgentNode[a], upkeep);
            if (AgentCash[a] >= 0)
            {
                AgentNegativeWeeks[a] = 0;
                return;
            }
            AgentNegativeWeeks[a]++;
            if (AgentNegativeWeeks[a] >= BankruptWeeks) Bankrupt.Add(a);
        }

        long Upkeep(int a) => Fx.MulIntRaw(Fx.MulIntRaw(Levels[AgentScale[a]].Upkeep, TraitUpkeepRaw[AgentTrait[a]]), VehicleCostRaw[AgentVehicle[a]]);

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
        static long Max(long a, long b) => a > b ? a : b;
    }
}
