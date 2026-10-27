using System.Collections.Generic;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// What one trip along a route costs (SYS-TRAVEL-COSTS). Fees are paid in the departure node's coin,
    /// the toll in the coin of the node it is paid to; with no automatic exchange (DEC-031) the two can
    /// differ on a route between continents.
    /// </summary>
    public readonly struct TripQuote
    {
        public readonly int Route;
        /// <summary>Base fee plus the per-unit charge (DEC-007, DEC-017).</summary>
        public readonly long Fee;
        public readonly int FeeContinent;
        /// <summary>Toll of the entered node on the cargo's value there (DEC-014, DEC-027).</summary>
        public readonly long Toll;
        public readonly int TollContinent;
        public readonly int CargoUnits;

        public TripQuote(int route, long fee, int feeContinent, long toll, int tollContinent, int cargoUnits)
        {
            Route = route;
            Fee = fee;
            FeeContinent = feeContinent;
            Toll = toll;
            TollContinent = tollContinent;
            CargoUnits = cargoUnits;
        }

        /// <summary>Whether coins per continent cover the fee and the toll together.</summary>
        public bool AffordableWith(long[] coins) =>
            FeeContinent == TollContinent
                ? coins[FeeContinent] >= Fee + Toll
                : coins[FeeContinent] >= Fee && coins[TollContinent] >= Toll;
    }

    /// <summary>
    /// The one trip cost function shared by the player and, later, merchants (DEC-015). A route is one edge,
    /// so a trip enters one node, its destination.
    /// </summary>
    public sealed class TravelCosts
    {
        readonly GameDatabase _db;
        readonly NodeTable _nodes;
        readonly RouteTable _routes;
        readonly NodeTolls _tolls;
        readonly NodeSpecialties _specialties;
        readonly MarketStore _markets;
        readonly MarketFlows _flows;

        public TravelCosts(WorldState world, GameDatabase db)
        {
            _db = db;
            _nodes = world.Get<NodeTable>();
            _routes = world.Get<RouteTable>();
            _tolls = world.Get<NodeTolls>();
            _specialties = world.Get<NodeSpecialties>();
            _markets = world.Get<MarketStore>();
            _flows = world.Get<MarketFlows>();
        }

        /// <summary>The route from <paramref name="from"/> to <paramref name="to"/>, or -1.</summary>
        public int FindRoute(int from, int to)
        {
            int start = _routes.OutStart[from];
            for (int k = start; k < start + _routes.OutCount[from]; k++)
                if (_routes.To[_routes.RouteIndex[k]] == to) return _routes.RouteIndex[k];
            return -1;
        }

        public TripQuote Quote(int route, IReadOnlyList<CargoItem> cargo)
        {
            int to = _routes.To[route];
            long units = 0, toll = 0;
            foreach (var item in cargo)
            {
                units += item.Quantity;
                // Valued where it enters, or at the nearest ancestor trading it; no market prices it at zero.
                if (!_flows.TryFindNearestMarket(to, item.Good, out _, out int cell)) continue;
                long rate = _tolls.Tariff[to];
                if (IsSpecialtyCategory(to, _db.Good(item.Good).CategoryIndex)) rate += _tolls.SpecialtySurcharge[to];
                toll += Fx.FromRaw(rate).MulInt((long)item.Quantity * _markets.Price[cell]);
            }
            long fee = _routes.BaseFee[route] + units * _routes.CostPerUnit[route];
            return new TripQuote(route, fee, _nodes.Continent[_routes.From[route]], toll, _nodes.Continent[to],
                (int)System.Math.Min(units, int.MaxValue));
        }

        /// <summary>A category is protected at a node when one of the node's specialty goods is in it.</summary>
        bool IsSpecialtyCategory(int node, int category)
        {
            int end = _specialties.Start[node] + _specialties.Count[node];
            for (int k = _specialties.Start[node]; k < end; k++)
                if (_db.Goods[GoodId.From(_specialties.BaseGood[k], QualityGrade.Low).Value].CategoryIndex == category) return true;
            return false;
        }
    }
}
