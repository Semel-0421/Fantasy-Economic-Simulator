using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    public sealed class MerchantVehicleBought : ISimEvent
    {
        public readonly AgentId Merchant;
        public readonly byte Vehicle;

        public MerchantVehicleBought(AgentId merchant, byte vehicle)
        {
            Merchant = merchant;
            Vehicle = vehicle;
        }
    }

    /// <summary>What a merchant group carries with (DEC-084): pack animals, then carts, then wagons.</summary>
    public static class MerchantVehicles
    {
        public const byte Pack = 0, Cart = 1, Wagon = 2;
        public const int Count = 3;
        static readonly uint StartStream = DetHash.Of("agents.merchant_vehicle_start");

        public static byte AtStart(SimulationConfig config, ulong seed, int merchant)
        {
            long roll = DetRandom.For(seed, 0, StartStream, merchant).NextFx01().Raw;
            long wagon = config.MerchantVehicleStartShare[Wagon].Raw;
            if (roll < wagon) return Wagon;
            return roll < wagon + config.MerchantVehicleStartShare[Cart].Raw ? Cart : Pack;
        }

        /// <summary>The good that makes a vehicle; false for packs, or where the data has no such good.</summary>
        public static bool TryGood(GameDatabase db, byte vehicle, out GoodId good)
        {
            good = default;
            return vehicle == Cart ? db.TryGetGood("cart", QualityGrade.Mid, out good)
                : vehicle == Wagon && db.TryGetGood("wagon", QualityGrade.Mid, out good);
        }
    }

    /// <summary>
    /// Once a year automated merchant groups invest in their carrying (DEC-084). A vehicle may wear down one step. A
    /// group with enough cash then orders the next vehicle (DEC-085), which <see cref="OrderSystem"/> delivers.
    /// </summary>
    public sealed class MerchantVehicleSystem : ISimSystem
    {
        static readonly uint WearStream = DetHash.Of("agents.merchant_vehicle_wear");

        GameDatabase _db;
        AgentTable _agents;
        MarketStore _markets;
        MarketFlows _flows;
        OrderTable _orders;
        NodeTable _nodes;
        bool[] _ordering = System.Array.Empty<bool>();

        public string Id => "agents.merchant_vehicles";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.MerchantVehicles;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _agents = ctx.World.Get<AgentTable>();
            _markets = ctx.World.Get<MarketStore>();
            _flows = ctx.World.Get<MarketFlows>();
            _orders = ctx.World.Get<OrderTable>();
            _nodes = ctx.World.Get<NodeTable>();
        }

        public void Execute(SimContext ctx)
        {
            if (!ctx.Clock.IsNewYear || ctx.Turn == 0) return;
            var config = _db.Config;
            if (_ordering.Length != _agents.Count) _ordering = new bool[_agents.Count];
            System.Array.Clear(_ordering, 0, _ordering.Length);
            for (int r = 0; r < _orders.Count; r++)
                if (_orders.Buyer[r] >= 0) _ordering[_orders.Buyer[r]] = true;
            for (int a = 0; a < _agents.Count; a++)
            {
                if (_agents.Mode[a] != (byte)ControlMode.Automated) continue;
                if (_agents.Vehicle[a] > MerchantVehicles.Pack && DetRandom.For(ctx.Seed, ctx.Turn, WearStream, a).NextFx01().Raw < config.MerchantVehicleWear.Raw)
                    _agents.Vehicle[a]--;
                OrderNextVehicle(ctx, a);
            }
        }

        void OrderNextVehicle(SimContext ctx, int a)
        {
            byte next = (byte)(_agents.Vehicle[a] + 1);
            if (next >= MerchantVehicles.Count || !MerchantVehicles.TryGood(_db, next, out var good)) return;
            if (_ordering[a]) return;
            int home = _agents.Home[a];
            if (!_flows.TryFindNearestMarket(home, good, out _, out int nearest)) return;
            if (_agents.Cash[a] < _markets.Price[nearest] * _db.Config.MerchantVehicleCashTimes[next]) return;
            _orders.Add(a, home, CellToOrderFrom(home, good, nearest), 1, ctx.Turn, next);
        }

        // The nearest node with one in stock takes the order, else the nearest market, where factories will see it.
        int CellToOrderFrom(int home, GoodId good, int nearest)
        {
            for (int m = home; m >= 0; m = _nodes.Parent[m])
                if (_markets.TryFindCell(m, good, out int c) && _markets.Stock[c] > 0) return c;
            return nearest;
        }
    }

    /// <summary>Version 4 had no vehicles: every group starts on pack animals and buys its way up.</summary>
    public sealed class AgentTableV4Migration : ISaveMigration
    {
        public string SectionKey => AgentTable.SectionKey;
        public int FromVersion => 4;

        public byte[] Migrate(byte[] section)
        {
            int rows = new BinaryReader(new MemoryStream(section)).ReadInt32();
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(rows);
                for (int r = 0; r < rows; r++) writer.Write(MerchantVehicles.Pack);
            }
            return output.ToArray();
        }
    }
}
