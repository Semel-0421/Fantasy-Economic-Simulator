using FES.Core;
using FES.Data;
using FES.World;

namespace FES.Agents
{
    public sealed class RoadBuilt : ISimEvent
    {
        public readonly NodeId From;
        public readonly NodeId To;
        public readonly int Turn;

        public RoadBuilt(NodeId from, NodeId to, int turn)
        {
            From = from;
            To = to;
            Turn = turn;
        }
    }

    /// <summary>A road decayed to nothing because nobody paid its upkeep.</summary>
    public sealed class RoadLost : ISimEvent
    {
        public readonly NodeId From;
        public readonly NodeId To;
        public readonly int Turn;

        public RoadLost(NodeId from, NodeId to, int turn)
        {
            From = from;
            To = to;
            Turn = turn;
        }
    }

    /// <summary>
    /// Weekly road keeping (DEC-054): finishes roads under construction and charges every paved route's
    /// upkeep to its maintainer. A nation pays from its bank's reserve; the player from their deposit with
    /// the bank of the road's nation, then the coins they carry in its currency. Unpaid roads lose condition
    /// and are unpaved again at zero. Roads without a maintainer (outside any nation) keep as they are.
    /// </summary>
    public sealed class RoadSystem : ISimSystem
    {
        GameDatabase _db;
        NodeTable _nodes;
        NodeBanks _banks;
        RouteTable _routes;
        RoadState _roads;
        BankState _bank;
        PlayerState _player;
        PurseState _purses;

        public string Id => "agents.roads";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.Roads;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _nodes = ctx.World.Get<NodeTable>();
            _banks = ctx.World.Get<NodeBanks>();
            _routes = ctx.World.Get<RouteTable>();
            _roads = ctx.World.Get<RoadState>();
            _bank = ctx.World.Get<BankState>();
            _player = ctx.World.Get<PlayerState>();
            _purses = ctx.World.Get<PurseState>();
        }

        public void Execute(SimContext ctx)
        {
            // Roads to the core towns are kept first (DEC-104).
            for (int pass = 0; pass < 2; pass++)
                for (int r = 0; r < _routes.Count; r++)
                {
                    bool seat = _nodes.IsSeat(_routes.From[r]) || _nodes.IsSeat(_routes.To[r]);
                    if (seat == (pass == 0)) StepRoad(ctx, r);
                }
        }

        void StepRoad(SimContext ctx, int r)
        {
            if (_roads.BuildDoneTurn[r] >= 0 && ctx.Turn >= _roads.BuildDoneTurn[r])
            {
                _roads.BuildDoneTurn[r] = -1;
                _roads.Condition[r] = RoadState.MaxCondition;
                _roads.Owner[r] = RoadState.PlayerOwner;
                ctx.Events.Publish(new RoadBuilt(new NodeId(_routes.From[r]), new NodeId(_routes.To[r]), ctx.Turn));
                return;
            }
            if (!_roads.IsPaved(r) || _roads.Owner[r] == RoadState.NoOwner || PayUpkeep(r)) return;
            _roads.Condition[r] -= _db.Config.RoadDecayPermille;
            if (_roads.Condition[r] > 0) return;
            _roads.Condition[r] = 0;
            _roads.Owner[r] = RoadState.NoOwner;
            ctx.Events.Publish(new RoadLost(new NodeId(_routes.From[r]), new NodeId(_routes.To[r]), ctx.Turn));
        }

        // Upkeep is wages where the road starts (DEC-061).
        bool PayUpkeep(int r)
        {
            if (!TakeUpkeep(r)) return false;
            _purses.PayHouseholds(_routes.From[r], _db.Config.RoadUpkeep);
            return true;
        }

        bool TakeUpkeep(int r)
        {
            long upkeep = _db.Config.RoadUpkeep;
            int owner = _roads.Owner[r];
            if (owner >= 0)
            {
                if (_bank.Spendable(owner) < upkeep) return false;
                _bank.Reserve[owner] -= upkeep;
                return true;
            }
            int nationNode = _nodes.AncestorAt(_routes.From[r], WorldLevel.Nation);
            if (nationNode >= 0)
            {
                int nation = _banks.NationIndex(nationNode);
                if (_bank.PlayerDeposit[nation] >= upkeep && _bank.Reserve[nation] >= upkeep)
                {
                    _bank.PlayerDeposit[nation] -= upkeep;
                    _bank.Reserve[nation] -= upkeep;
                    return true;
                }
            }
            int continent = _nodes.Continent[_routes.From[r]];
            if (_player.Coins[continent] < upkeep) return false;
            _player.Coins[continent] -= upkeep;
            return true;
        }
    }

    /// <summary>
    /// Pays to pave the land route from the player's node to <see cref="Destination"/>, and its way back
    /// (DEC-054). The cost grows with the route's travel turns; the road is the player's when it is done.
    /// </summary>
    public sealed class BuildRoadCommand : ICommand
    {
        public readonly NodeId Destination;

        public BuildRoadCommand(NodeId destination) => Destination = destination;

        public CommandResult Validate(SimContext ctx)
        {
            var player = ctx.World.Get<PlayerState>();
            if (player.IsTravelling) return CommandResult.Fail("Roads are commissioned on the spot, not on the road.");
            var costs = new TravelCosts(ctx.World, ctx.Services.Get<GameDatabase>());
            int route = Destination.IsValid && Destination.Value < ctx.World.Get<NodeTable>().Count ? costs.FindRoute(player.Node, Destination.Value) : -1;
            if (route < 0) return CommandResult.Fail("No route to destination.");
            if (costs.IsWater(route)) return CommandResult.Fail("Water routes cannot be paved.");
            var roads = ctx.World.Get<RoadState>();
            if (roads.BuildDoneTurn[route] >= 0) return CommandResult.Fail("A road is already being built there.");
            if (roads.Condition[route] == RoadState.MaxCondition) return CommandResult.Fail("The road is already paved.");
            return player.LocalCoins >= Cost(ctx, route) ? CommandResult.Success : CommandResult.Fail("Not enough cash.");
        }

        public void Apply(SimContext ctx)
        {
            var player = ctx.World.Get<PlayerState>();
            var costs = new TravelCosts(ctx.World, ctx.Services.Get<GameDatabase>());
            int route = costs.FindRoute(player.Node, Destination.Value);
            player.LocalCoins -= Cost(ctx, route);
            ctx.World.Get<PurseState>().PayHouseholds(player.Node, Cost(ctx, route)); // builders' wages (DEC-061)
            int done = ctx.Turn + ctx.Services.Get<GameDatabase>().Config.RoadBuildWeeks;
            var roads = ctx.World.Get<RoadState>();
            roads.BuildDoneTurn[route] = done;
            int back = costs.FindRoute(Destination.Value, player.Node);
            if (back >= 0 && roads.BuildDoneTurn[back] < 0) roads.BuildDoneTurn[back] = done;
        }

        static long Cost(SimContext ctx, int route) =>
            ctx.Services.Get<GameDatabase>().Config.RoadBuildCostPerTurn * ctx.World.Get<RouteTable>().TravelTurns[route];
    }
}
