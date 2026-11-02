using System.Collections.Generic;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// What one trip along a route costs and risks (SYS-TRAVEL-COSTS, SYS-ROUTE-HAZARDS). Everything is paid
    /// in the departure node's coin except the toll, paid in the coin of the node it is paid to; with no
    /// automatic exchange (DEC-031) the two can differ on a route between continents.
    /// </summary>
    public readonly struct TripQuote
    {
        public readonly int Route;
        /// <summary>Base fee plus the per-unit charge, scaled by the terrain (DEC-007, DEC-017, DEC-025).</summary>
        public readonly long Fee;
        public readonly int FeeContinent;
        /// <summary>Toll of the entered node on the cargo's value there (DEC-014, DEC-027).</summary>
        public readonly long Toll;
        public readonly int TollContinent;
        public readonly int CargoUnits;
        /// <summary>Fare on a water route without an own ship (DEC-034); 0 otherwise.</summary>
        public readonly long Passage;
        public readonly long Guards;
        public readonly long Insurance;
        /// <summary>The cargo's value at the destination's prices, in the toll's coin.</summary>
        public readonly long CargoValue;
        /// <summary>Chance of an encounter on this trip (DEC-026).</summary>
        public readonly Fx EncounterChance;

        public TripQuote(int route, in TripCharges charges, int feeContinent, int tollContinent, int cargoUnits, long cargoValue, Fx encounterChance)
        {
            Route = route;
            Fee = charges.Fee;
            FeeContinent = feeContinent;
            Toll = charges.Toll;
            TollContinent = tollContinent;
            CargoUnits = cargoUnits;
            Passage = charges.Passage;
            Guards = charges.Guards;
            Insurance = charges.Insurance;
            CargoValue = cargoValue;
            EncounterChance = encounterChance;
        }

        /// <summary>Everything paid in the departure coin.</summary>
        public long DepartureCost => Fee + Passage + Guards + Insurance;

        /// <summary>Whether coins per continent cover the departure costs and the toll together.</summary>
        public bool AffordableWith(long[] coins) =>
            FeeContinent == TollContinent
                ? coins[FeeContinent] >= DepartureCost + Toll
                : coins[FeeContinent] >= DepartureCost && coins[TollContinent] >= Toll;
    }

    /// <summary>What a trip charges; see <see cref="TripQuote"/>.</summary>
    public readonly struct TripCharges
    {
        public readonly long Fee, Toll, Passage, Guards, Insurance;

        public TripCharges(long fee, long toll, long passage, long guards, long insurance)
        {
            Fee = fee;
            Toll = toll;
            Passage = passage;
            Guards = guards;
            Insurance = insurance;
        }
    }

    /// <summary>Countermeasures bought with a trip (DEC-026).</summary>
    public readonly struct TripOptions
    {
        public static readonly TripOptions None = default;

        public readonly bool Guards;
        public readonly bool Insurance;
        /// <summary>The traveller owns a ship, so a water route needs no passage.</summary>
        public readonly bool OwnShip;
        /// <summary>A member of a guild where the trip starts insures for less (DEC-057).</summary>
        public readonly bool GuildMember;

        public TripOptions(bool guards, bool insurance, bool ownShip, bool guildMember = false)
        {
            Guards = guards;
            Insurance = insurance;
            OwnShip = ownShip;
            GuildMember = guildMember;
        }
    }

    /// <summary>
    /// The one trip cost function shared by the player and merchants (DEC-015). A route is one edge, so a trip
    /// enters one node, its destination.
    /// </summary>
    public sealed class TravelCosts
    {
        readonly GameDatabase _db;
        readonly NodeTable _nodes;
        readonly RouteTable _routes;
        readonly NodeTolls _tolls;
        readonly NodeSpecialties _specialties;
        readonly RouteHazards _hazards;
        readonly RoadState _roads;
        readonly MarketStore _markets;
        readonly MarketFlows _flows;
        readonly CurrencyState _currency;
        readonly DevelopmentState _development;
        readonly PurseState _purses;

        public TravelCosts(WorldState world, GameDatabase db)
        {
            _db = db;
            _development = world.Get<DevelopmentState>();
            _purses = world.Get<PurseState>();
            _nodes = world.Get<NodeTable>();
            _routes = world.Get<RouteTable>();
            _tolls = world.Get<NodeTolls>();
            _specialties = world.Get<NodeSpecialties>();
            _hazards = world.Get<RouteHazards>();
            _roads = world.Get<RoadState>();
            _markets = world.Get<MarketStore>();
            _flows = world.Get<MarketFlows>();
            _currency = world.Get<CurrencyState>();
        }

        /// <summary>The route from <paramref name="from"/> to <paramref name="to"/>, or -1.</summary>
        public int FindRoute(int from, int to)
        {
            int start = _routes.OutStart[from];
            for (int k = start; k < start + _routes.OutCount[from]; k++)
                if (_routes.To[_routes.RouteIndex[k]] == to) return _routes.RouteIndex[k];
            return -1;
        }

        public bool IsWater(int route) => ((RouteTerrain)_routes.Terrain[route]).IsWater();

        public TripQuote Quote(int route, IReadOnlyList<CargoItem> cargo) => Quote(route, cargo, TripOptions.None);

        public TripQuote Quote(int route, IReadOnlyList<CargoItem> cargo, TripOptions options)
        {
            var config = _db.Config;
            int to = _routes.To[route];
            int fromContinent = _nodes.Continent[_routes.From[route]], toContinent = _nodes.Continent[to];
            long units = 0, toll = 0, value = 0;
            foreach (var item in cargo)
            {
                units += item.Quantity;
                // Valued where it enters, or at the nearest ancestor trading it; no market prices it at zero.
                if (!_flows.TryFindNearestMarket(to, item.Good, out _, out int cell)) continue;
                long itemValue = (long)item.Quantity * _markets.Price[cell];
                long rate = _tolls.Tariff[to];
                if (IsSpecialtyCategory(to, _db.Good(item.Good).CategoryIndex)) rate += _tolls.SpecialtySurcharge[to];
                toll += Fx.FromRaw(rate).MulInt(itemValue);
                value += itemValue;
            }
            long fee = _routes.BaseFee[route] + PerUnitCharge(route).MulInt(units);
            long passage = IsWater(route) && !options.OwnShip ? config.PassageBaseFee + units * config.PassagePerUnit : 0;
            long valueHere = _currency.Convert(value, toContinent, fromContinent);
            long guards = options.Guards ? config.GuardBaseCost + config.GuardCostShare.MulInt(valueHere) : 0;
            long insurance = options.Insurance ? config.InsurancePremium.MulInt(valueHere) : 0;
            if (options.GuildMember) insurance -= config.GuildInsuranceDiscount.MulInt(insurance);
            return new TripQuote(route, new TripCharges(fee, toll, passage, guards, insurance), fromContinent, toContinent,
                (int)System.Math.Min(units, int.MaxValue), value, EncounterChance(route, value, options.Guards));
        }

        /// <summary>The route's charge per cargo unit, cut by paving in proportion to the road's condition (DEC-054).</summary>
        public Fx PerUnitCharge(int route) =>
            Fx.FromInt(_routes.CostPerUnit[route]) * (Fx.One - _roads.Effect(route, _db.Config.RoadCostReduction)) * DevelopmentFactor(route);

        // Highways and harbor works the states have built (DEC-072).
        Fx DevelopmentFactor(int route)
        {
            var development = _development;
            if (development.RouteGrade[route] > 0) return Fx.One - _db.Config.StateHighwayBonus;
            int harbor = development.HarborLevel[_routes.From[route]];
            return harbor > 0 && IsWater(route) ? Fx.One - _db.Config.StateHarborBonus * Fx.FromInt(harbor) : Fx.One;
        }

        /// <summary>The terrain and region danger of a route, cut by paving (DEC-054).</summary>
        public Fx RouteDanger(int route) =>
            Fx.FromRaw(_hazards.BaseDanger[route]) * (Fx.One - _roads.Effect(route, _db.Config.RoadDangerReduction)) * ArmyFactor(route);

        // A nation's army makes its routes safer (DEC-074).
        Fx ArmyFactor(int route)
        {
            int nation = _purses.NodeFloat[_routes.From[route]];
            return nation < _development.Security.Length ? Fx.One - Fx.FromRaw(_development.Security[nation]) : Fx.One;
        }

        /// <summary>
        /// Route danger growing linearly with the cargo's value and cut by guards; at most 1 (DEC-026).
        /// </summary>
        public Fx EncounterChance(int route, long cargoValue, bool guards)
        {
            var config = _db.Config;
            var chance = RouteDanger(route) * (Fx.One + Fx.FromRatio(cargoValue, config.DangerValueScale));
            if (guards) chance *= Fx.One - config.GuardReduction;
            return chance.Raw > Fx.OneRaw ? Fx.One : chance.Raw < 0 ? Fx.Zero : chance;
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

    public enum EncounterLoss : byte
    {
        None = 0,
        Cash = 1,
        Cargo = 2,
    }

    /// <summary>One roll per trip (DEC-026): whether an encounter happens and whether it takes cash or cargo.</summary>
    public static class Encounters
    {
        public static readonly uint Stream = DetHash.Of("agents.encounters");

        /// <param name="salt">Separates travellers that share an entity index (player 0, merchants 1).</param>
        public static EncounterLoss Roll(ulong seed, int turn, int entity, uint salt, Fx chance)
        {
            var rng = DetRandom.For(seed, turn, Stream, entity, salt);
            if (rng.NextFx01().Raw >= chance.Raw) return EncounterLoss.None;
            return rng.NextInt(2) == 0 ? EncounterLoss.Cash : EncounterLoss.Cargo;
        }

        /// <summary>Takes the loss share of every carried good from a holder; returns what was lost.</summary>
        public static void TakeCargo(CargoTable cargo, int holder, Fx share, List<CargoItem> lost)
        {
            lost.Clear();
            cargo.Collect(holder, lost);
            for (int i = 0; i < lost.Count; i++)
            {
                int quantity = (int)share.MulInt(lost[i].Quantity);
                cargo.Change(holder, lost[i].Good, -quantity);
                lost[i] = new CargoItem(lost[i].Good, quantity);
            }
            lost.RemoveAll(item => item.Quantity == 0);
        }
    }
}
