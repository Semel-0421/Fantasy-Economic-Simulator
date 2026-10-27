using System.Collections.Generic;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>Registers the player, merchants, facilities, LOD and guidance. Requires the world and economy modules.</summary>
    public sealed class AgentsModule : ISimModule
    {
        static readonly uint SpawnStream = DetHash.Of("agents.spawn");
        static readonly uint StartStream = DetHash.Of("agents.player.start");
        static readonly string[] RequiredModules = { "world", "economy" };

        public string Id => "agents";
        public IReadOnlyList<string> Requires => RequiredModules;

        public void Register(SimBuilder builder)
        {
            var db = builder.Services.Get<GameDatabase>();
            var nodes = builder.World.Get<NodeTable>();
            var seed = builder.World.Get<SimClock>().Seed;

            var player = builder.World.Add(new PlayerState(nodes));
            player.Node = ResolveStartNode(db.Config, nodes, seed);
            player.LocalCoins = db.Config.PlayerStartCash;
            builder.World.Add(new CargoTable());
            builder.World.Add(new LicenseTable());
            builder.World.Add(new PlayerShip());
            var banks = builder.World.Get<NodeBanks>();
            var bank = builder.World.Add(new BankState(banks.NationCount, nodes.ContinentCount));
            BankSystem.SetUpReserves(bank, banks, nodes, builder.World.Get<CurrencyState>(), db.Config);
            builder.World.Add(new LoanTable());
            builder.World.Add(new AuctionTable());
            builder.World.Add(new NpcLoanTable());
            builder.World.Add(new FinanceState(banks.NationCount, nodes.ContinentCount, db.Config.LoanInterestPerYear));
            builder.World.Add(new MoneyTotals(banks.NationCount, nodes.ContinentCount));

            var facilities = builder.World.Add(new FacilityTable());
            SpawnFacilities(facilities, nodes, db, seed);
            var merchants = builder.World.Add(new AgentTable());
            SpawnMerchants(merchants, nodes, db.Config);
            var groups = builder.World.Add(new GroupTable());
            GroupSetup.Found(groups, nodes, db, facilities);
            GroupSetup.AssignOwners(groups, nodes, db, facilities, merchants);
            builder.World.Add(new GuildMembershipTable());
            builder.World.Add(new GuidanceState());
            builder.World.Add(new LodState(nodes.Count));

            builder.Services.SetDefault<ILodPolicy>(new DefaultLodPolicy());
            builder.Services.SetDefault<IMerchantBehavior>(new ArbitrageMerchantBehavior());

            builder.AddMigration(new PlayerStateV1Migration());
            builder.AddMigration(new PlayerStateV2Migration());
            builder.AddMigration(new FacilityTableV1Migration());
            builder.AddMigration(new AgentTableV1Migration());
            builder.AddMigration(new PlayerShipV1Migration());
            builder.AddMigration(new AuctionTableV1Migration());
            builder.AddMigration(new GroupColumnMigration(FacilityTable.SectionKey, 2));
            builder.AddMigration(new GroupColumnMigration(AgentTable.SectionKey, 2));

            builder.AddSystem(new TravelSystem());
            builder.AddSystem(new LodSystem());
            builder.AddSystem(new ProductionSystem());
            builder.AddSystem(new MerchantSystem());
            builder.AddSystem(new GuidanceSystem());
            builder.AddSystem(new LicenseSystem());
            builder.AddSystem(new BankSystem());
            builder.AddSystem(new FinanceSystem());
            builder.AddSystem(new HouseholdGrowthSystem());
            builder.AddSystem(new RoadSystem());
            builder.AddSystem(new GroupSystem());
        }

        /// <summary>A random village from the world seed (DEC-040), unless config names a start node.</summary>
        static int ResolveStartNode(SimulationConfig config, NodeTable nodes, ulong seed)
        {
            if (!string.IsNullOrEmpty(config.PlayerStartNode))
                return nodes.TryFind(config.PlayerStartNode, out var start)
                    ? start.Value
                    : throw new System.FormatException($"player.start_node '{config.PlayerStartNode}' is not on the map.");
            int villages = nodes.LevelCount(WorldLevel.Village);
            if (villages == 0) return 0;
            return nodes.LevelStart(WorldLevel.Village) + DetRandom.For(seed, 0, StartStream, 0).NextInt(villages);
        }

        static void SpawnFacilities(FacilityTable table, NodeTable nodes, GameDatabase db, ulong seed)
        {
            var defsByTier = new List<FacilityDef>[IndustryTiers.Count];
            for (int t = 0; t < IndustryTiers.Count; t++) defsByTier[t] = new List<FacilityDef>();
            foreach (var def in db.Facilities) defsByTier[(int)def.Tier].Add(def);

            for (int n = 0; n < nodes.Count; n++)
            {
                int level = nodes.Level[n];
                for (int t = 0; t < IndustryTiers.Count; t++)
                {
                    var defs = defsByTier[t];
                    if (defs.Count == 0) continue;
                    int count = db.Config.ProducersPerNode[level, t];
                    for (int k = 0; k < count; k++)
                    {
                        // Salt packs (tier, index) without collisions for up to 2^24 producers per tier and node.
                        var rng = DetRandom.For(seed, 0, SpawnStream, n, ((uint)t << 24) | (uint)k);
                        var def = defs[rng.NextInt(defs.Count)];
                        table.Add(n, def.Index, OwnerIds.Npc, ControlMode.Automated, 1, Fx.One, db.Config.FacilityStartCash[t]);
                    }
                }
            }
        }

        static void SpawnMerchants(AgentTable table, NodeTable nodes, SimulationConfig config)
        {
            for (int n = 0; n < nodes.Count; n++)
            {
                var level = nodes.LevelOf(n);
                for (int k = 0; k < config.MerchantsPerNode[(int)level]; k++)
                    table.Add(n, OwnerIds.Npc, level, ControlMode.Automated, config.MerchantStartCash[(int)level]);
            }
        }
    }
}
