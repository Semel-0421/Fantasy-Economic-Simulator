using FES.Core;
using FES.Economy;

namespace FES.Agents
{
    // How a group chooses a load and a route (SYS-MERCHANTS, DEC-093, DEC-095, DEC-096).
    public partial struct DetailedMerchantJob
    {
        /// <summary>Where a group may go from here, and whether it may sell licensed goods.</summary>
        readonly struct GroupReach
        {
            public readonly bool Licensed, InRange;
            public readonly int Anchor;

            public GroupReach(bool licensed, int anchor, bool inRange)
            {
                Licensed = licensed;
                Anchor = anchor;
                InRange = inRange;
            }
        }

        /// <summary>What a group asks of a load, and the best it has found.</summary>
        struct TripSearch
        {
            public int Node, Power;
            public GroupReach Reach;
            public NodeMemory Memory;
            public long Budget, RoomWeight, RoomSize, MinMarginRaw, IndexRaw;
            public int Route, Cell;
            public long Quantity, Score, Wanted, Profit;
        }

        /// <summary>A good a group could take from its node, what a unit costs it, and how many would fit.</summary>
        readonly struct CellOffer
        {
            public readonly int Cell, Good;
            public readonly long Buy, Fit, Cap;

            public CellOffer(int cell, int good, long buy, long fit, long cap)
            {
                Cell = cell;
                Good = good;
                Buy = buy;
                Fit = fit;
                Cap = cap;
            }
        }

        /// <summary>What a unit brings on a route.</summary>
        struct UnitOnRoute
        {
            public long Sell, BaseFee, MarginRaw, UnitCost;
        }

        struct BestBid
        {
            public int Cell;
            public long Profit, Units;
        }

        GroupReach ReachOf(int a, int node)
        {
            int anchor = RangeAnchor(a);
            return new GroupReach(HoldsLicenses(a), anchor, Within(node, anchor)); // a group outside its range (an older save) may still leave
        }

        void TryStartTrip(int a)
        {
            int node = AgentNode[a];
            if (AgentCash[a] <= 0)
            {
                SellOrGoHome(a, node);
                return;
            }
            var search = NewSearch(a, node);
            ListStockedCells(node);
            int stockedEnd = StockedStart[node] + StockedCount[node];
            for (int s = StockedStart[node]; s < stockedEnd; s++)
                WeighCell(a, StockedCells[s], ref search);
            if (search.Route < 0)
            {
                if (Bids) BidForWhatIsMissing(a, 0, in search);
                SellOrGoHome(a, node);
                return;
            }
            if (Bids && search.Wanted > search.Quantity) AddBid(search.Cell, search.Wanted - search.Quantity);
            if (BidsWhileLoaded) BidForWhatIsMissing(a, search.Profit, in search);

            long quantity = WithinTheBankThere(a, node, search.Cell, search.Quantity);
            if (quantity <= 0)
            {
                SellOrGoHome(a, node);
                return;
            }
            BuyLoad(a, node, search.Cell, quantity);
            Depart(a, node, search.Route);
        }

        void SellOrGoHome(int a, int node)
        {
            StartSellingTrip(a, node);
            HeadHome(a, node);
        }

        TripSearch NewSearch(int a, int node)
        {
            int trait = AgentTrait[a];
            long cash = AgentCash[a], spendRaw = TraitSpendRaw[trait];
            long loadWeight = 0, loadSize = 0;
            for (int r = FirstRow[a]; r >= 0; r = NextRow[r])
            {
                loadWeight += (long)CargoQuantity[r] * GoodWeight[CargoGood[r]];
                loadSize += (long)CargoQuantity[r] * GoodSize[CargoGood[r]];
            }
            return new TripSearch
            {
                Node = node,
                Power = TraitDistancePower[trait],
                Reach = ReachOf(a, node),
                Memory = AgentMemory[a],
                Budget = spendRaw == Fx.OneRaw ? cash : Fx.MulIntRaw(cash, spendRaw),
                RoomWeight = Capacity(a, Levels[AgentScale[a]].MaxWeight) - loadWeight,
                RoomSize = Capacity(a, Levels[AgentScale[a]].MaxSize) - loadSize,
                MinMarginRaw = TraitMinMarginRaw[trait],
                IndexRaw = ContinentPriceIndex[NodeContinent[node]],
                Route = -1,
                Cell = -1,
            };
        }

        long Capacity(int a, long atLevel) => Fx.MulIntRaw(Fx.MulIntRaw(atLevel, TraitCapacityRaw[AgentTrait[a]]), VehicleCapacityRaw[AgentVehicle[a]]);

        // Only stocked goods can be bought, and most cells have no stock: the first group at a node lists those
        // (stock only falls during the job), then each looks up its destinations' cells.
        void ListStockedCells(int node)
        {
            if (StockedStart[node] >= 0) return;
            StockedStart[node] = StockedCells.Length;
            int cellEnd = MarketCellStart[node] + MarketCellCount[node];
            for (int c = MarketCellStart[node]; c < cellEnd; c++)
                if (Stock[c] > 0) StockedCells.Add(c);
            StockedCount[node] = StockedCells.Length - StockedStart[node];
        }

        void WeighCell(int a, int cell, ref TripSearch search)
        {
            if (Stock[cell] <= 0) return;
            int good = CellGood[cell];
            long fit = Fit(good, search.RoomWeight, search.RoomSize);
            if (fit < 0) fit = 0;
            long cap = Min(Stock[cell], fit);
            if (cap <= 0) return;
            var offer = new CellOffer(cell, good, Spend(a, search.Node, Price[cell]), fit, cap);
            int firstRoute = OutStart[search.Node], lastRoute = firstRoute + OutCount[search.Node];
            for (int k = firstRoute; k < lastRoute; k++)
                WeighRoute(a, RouteIndex[k], in offer, ref search);
        }

        long Fit(int good, long roomWeight, long roomSize)
        {
            long fit = int.MaxValue;
            if (GoodWeight[good] > 0) fit = Min(fit, roomWeight / GoodWeight[good]);
            if (GoodSize[good] > 0) fit = Min(fit, roomSize / GoodSize[good]);
            return fit;
        }

        void WeighRoute(int a, int r, in CellOffer offer, ref TripSearch search)
        {
            int to = RouteTo[r];
            if (!MayGo(a, search.Node, to, in search.Reach)) return;
            int toCell = FindCell(to, offer.Good);
            if (toCell < 0 || (!search.Reach.Licensed && NeedsLicense(to, offer.Good))) return;
            // A node nobody told the group about is judged at the anchor price.
            long there = !Rumors || search.Memory.Knows(to) || NodeCoreStrength[to] > 0
                ? Earn(a, to, OfferAt(toCell))
                : Fx.RoundShift(GoodBasePrice[offer.Good] * search.IndexRaw);
            if (!WeighUnit(a, r, toCell, there, in offer, in search, out var unit)) return;
            long affordable = (search.Budget - unit.BaseFee) / unit.UnitCost;
            long quantity = Min(offer.Cap, affordable);
            if (quantity <= 0) return;
            long profit = Fx.MulIntRaw(quantity, unit.MarginRaw) - unit.BaseFee;
            if (CountTripCosts)
            {
                long worth = quantity * unit.Sell;
                profit -= RouteTurns[r] * Upkeep(a) + Fx.MulIntRaw(Fx.MulIntRaw(worth, CargoLossRaw), EncounterChance(a, r, worth)) / 2;
                if (profit <= 0) return;
            }
            long score = TripScore(profit, RouteTurns[r], search.Power);
            if (score <= search.Score) return;
            search.Route = r;
            search.Cell = offer.Cell;
            search.Quantity = quantity;
            search.Score = score;
            search.Wanted = Min(offer.Fit, affordable);
            search.Profit = profit;
        }

        static long TripScore(long profit, int routeTurns, int power)
        {
            if (profit <= 0 || power <= 0) return profit;
            long turns = routeTurns < 1 ? 1 : routeTurns;
            long divisor = power == 1 ? turns : turns * turns;
            return (profit << Fx.FractionBits) / divisor;
        }

        // What a unit brings on a route at this price there; false when that falls short of what the group asks.
        bool WeighUnit(int a, int r, int toCell, long there, in CellOffer offer, in TripSearch search, out UnitOnRoute unit)
        {
            int to = RouteTo[r];
            long perUnitRaw = Fx.RoundShift(RouteCostPerUnitRaw[r] * VehicleCostRaw[AgentVehicle[a]]);
            long toll = Fx.MulIntRaw(there, TollRate(to, offer.Good));
            if (!TradersPay(a, to, Price[toCell])) there = 0;
            unit.Sell = Max(there, Beyond(a, to, search.Node, offer.Good, in search.Reach, JudgeHops - 1));
            unit.BaseFee = Fx.MulIntRaw(RouteBaseFee[r], VehicleCostRaw[AgentVehicle[a]]);
            unit.MarginRaw = ((unit.Sell - offer.Buy - toll) << Fx.FractionBits) - perUnitRaw;
            unit.UnitCost = offer.Buy + toll + Fx.MulIntRaw(1, perUnitRaw);
            if (unit.UnitCost < 1) unit.UnitCost = 1;
            return unit.MarginRaw > 0 && unit.MarginRaw >= search.MinMarginRaw * offer.Buy;
        }

        // Abroad, what it pays comes out of the bank there as local coins (DEC-031).
        long WithinTheBankThere(int a, int node, int cell, long quantity)
        {
            long bank = International[NodeContinent[node]];
            if (!Abroad(a, node) || quantity * Price[cell] <= bank) return quantity;
            return bank > 0 && Price[cell] > 0 ? bank / Price[cell] : 0;
        }

        void BuyLoad(int a, int node, int cell, long quantity)
        {
            Stock[cell] -= (int)quantity;
            Supply[cell] = MarketStore.ClampToNonNegativeInt(Supply[cell] + quantity);
            Demand[cell] = MarketStore.ClampToNonNegativeInt(Demand[cell] + quantity);
            long cost = quantity * Price[cell];
            long own = Spend(a, node, cost);
            AgentCash[a] -= own;
            AgentLoadCost[a] += own;
            TripStats[9] += own;
            Float[NodeFloat[node]] += cost;
            if (Abroad(a, node))
            {
                International[NodeContinent[AgentHome[a]]] += own;
                International[NodeContinent[node]] -= cost;
            }
            AddCargo(a, CellGood[cell], (int)quantity);
        }

        // What a unit brings past a node, within the hops left, after each hop's charge and toll (DEC-095). It may pass
        // through a market that does not trade the good.
        long Beyond(int a, int at, int from, int good, in GroupReach reach, int hops)
        {
            if (hops <= 0) return 0;
            long best = 0;
            int first = OutStart[at], last = first + OutCount[at];
            for (int k = first; k < last; k++)
            {
                int r = RouteIndex[k];
                int next = RouteTo[r];
                if (next == from || !MayGo(a, at, next, in reach)) continue;
                int cell = FindCell(next, good);
                long here = cell >= 0 ? Earn(a, next, OfferAt(cell)) : 0;
                long direct = (reach.Licensed || !NeedsLicense(next, good)) && cell >= 0 && TradersPay(a, next, Price[cell]) ? here : 0;
                long worth = Max(direct, Beyond(a, next, at, good, in reach, hops - 1));
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
        void BidForWhatIsMissing(int a, long beat, in TripSearch search)
        {
            var bid = new BestBid { Cell = -1, Profit = beat };
            int node = search.Node;
            int cellEnd = MarketCellStart[node] + MarketCellCount[node];
            for (int cell = MarketCellStart[node]; cell < cellEnd; cell++)
            {
                if (Stock[cell] > 0) continue;
                int good = CellGood[cell];
                long fit = Fit(good, search.RoomWeight, search.RoomSize);
                if (fit > 0) WeighBid(a, new CellOffer(cell, good, Spend(a, node, Price[cell]), fit, fit), in search, ref bid);
            }
            if (bid.Cell >= 0) AddBid(bid.Cell, bid.Units);
        }

        void WeighBid(int a, in CellOffer offer, in TripSearch search, ref BestBid bid)
        {
            int firstRoute = OutStart[search.Node], lastRoute = firstRoute + OutCount[search.Node];
            for (int k = firstRoute; k < lastRoute; k++)
            {
                int r = RouteIndex[k];
                int to = RouteTo[r];
                if (!MayGo(a, search.Node, to, in search.Reach)) continue;
                int toCell = FindCell(to, offer.Good);
                if (toCell < 0 || (!search.Reach.Licensed && NeedsLicense(to, offer.Good))) continue;
                if (!WeighUnit(a, r, toCell, Earn(a, to, OfferAt(toCell)), in offer, in search, out var unit)) continue;
                long units = Min(offer.Fit, (search.Budget - unit.BaseFee) / unit.UnitCost);
                if (units <= 0) continue;
                long profit = Fx.MulIntRaw(units, unit.MarginRaw) - unit.BaseFee;
                if (profit <= bid.Profit) continue;
                bid.Profit = profit;
                bid.Cell = offer.Cell;
                bid.Units = units;
            }
        }

        // With nothing worth buying, a group carries what it holds to where it sells for most after the trip's costs.
        void StartSellingTrip(int a, int node)
        {
            CollectRows(a);
            if (Rows.Length == 0) return;
            int route = BestCarry(a, node, out _, out long tripCost);
            if (route >= 0 && (!WeighSales || LastsTheTrip(a, route, tripCost))) Depart(a, node, route);
        }

        // The route where the collected rows bring most after the trip's fee and toll, or -1.
        int BestCarry(int a, int node, out long bestNet, out long bestCost)
        {
            var reach = ReachOf(a, node);
            int bestRoute = -1;
            bestNet = 0;
            bestCost = 0;
            int firstRoute = OutStart[node], lastRoute = firstRoute + OutCount[node];
            for (int k = firstRoute; k < lastRoute; k++)
            {
                int r = RouteIndex[k];
                if (!MayGo(a, node, RouteTo[r], in reach)) continue;
                long worth = CarryWorth(a, node, r, in reach, out long cost);
                if (worth - cost <= bestNet) continue;
                bestRoute = r;
                bestNet = worth - cost;
                bestCost = cost;
            }
            return bestRoute;
        }

        // What the collected rows bring on a route, and the trip's costs. When sales are weighed (DEC-093) a load is also
        // worth only what the traders there can pay, less the trip's weeks of upkeep and what the road is expected to take.
        long CarryWorth(int a, int node, int r, in GroupReach reach, out long cost)
        {
            int to = RouteTo[r];
            cost = Fx.MulIntRaw(RouteBaseFee[r], VehicleCostRaw[AgentVehicle[a]]);
            long units = 0, value = 0, further = 0;
            for (int i = 0; i < Rows.Length; i++)
            {
                int good = CargoGood[Rows[i]];
                units += CargoQuantity[Rows[i]];
                if (WeighSales) further += CargoQuantity[Rows[i]] * Beyond(a, to, node, good, in reach, JudgeHops - 1);
                if (!reach.Licensed && NeedsLicense(to, good)) continue;
                int cell = FindCell(to, good);
                if (cell < 0) continue;
                long itemValue = (long)CargoQuantity[Rows[i]] * OfferAt(cell);
                value += itemValue;
                cost += Spend(a, to, Fx.MulIntRaw(itemValue, TollRate(to, good)));
            }
            value = Earn(a, to, value);
            cost += Fx.MulIntRaw(Fx.MulIntRaw(units, RouteCostPerUnitRaw[r]), VehicleCostRaw[AgentVehicle[a]]);
            if (!WeighSales) return value;
            cost += RouteTurns[r] * Upkeep(a);
            long chanceRaw = EncounterChance(a, r, value);
            return Min(Max(value, further), Earn(a, to, Payable(a, to, long.MaxValue / 2)))
                - Fx.MulIntRaw(Fx.MulIntRaw(value, CargoLossRaw), chanceRaw) / 2;
        }
    }
}
