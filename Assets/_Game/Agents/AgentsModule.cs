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
        static readonly uint TraitStream = DetHash.Of("agents.merchant_trait");
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
            var purses = builder.World.Add(new PurseState(nodes, banks, builder.World.Get<HouseholdCash>()));
            builder.World.Add(new LaborState(purses.Count, db.Config.LaborBaseEmployment));
            builder.World.Add(new IncomeTaxState(nodes.ContinentCount));
            builder.World.Add(new DevelopmentState(nodes.Count, builder.World.Get<RouteTable>().Count, banks.NationCount, purses.Count, builder.World.Get<MarketStore>().GoodBasePrice.Length));
            builder.World.Add(new NobleState(nodes));
            builder.World.Add(new OrderTable());
            builder.World.Add(new ContractTable());
            builder.World.Add(new DemandForecast(builder.World.Get<MarketStore>().CellCount));
            builder.World.Add(new MerchantTripStats());
            builder.World.Add(new TraderLoans(nodes.Count, banks.NationCount));
            builder.World.Add(new BillTable());
            builder.World.Add(new BillRoom(purses.Count));
            builder.World.Add(new BondTable());
            builder.World.Add(new CreditStats());
            builder.World.Add(new BondOffers());
            builder.World.Add(new MigrationState(nodes, banks.NationCount, db.Config));
            builder.World.Add(new FinanceState(banks.NationCount, nodes.ContinentCount, db.Config.LoanInterestPerYear));
            builder.World.Add(new MoneyTotals(banks.NationCount, nodes.ContinentCount));

            var facilities = builder.World.Add(new FacilityTable());
            SpawnFacilities(facilities, nodes, db, seed);
            var merchants = builder.World.Add(new AgentTable());
            SpawnMerchants(merchants, nodes, db.Config, seed);
            var groups = builder.World.Add(new GroupTable());
            GroupSetup.Found(groups, nodes, db, facilities);
            GroupSetup.AssignOwners(groups, nodes, db, facilities, merchants);
            builder.World.Add(new GuildMembershipTable());
            builder.World.Add(new PolicyState(banks.NationCount, nodes.ContinentCount, db));
            builder.World.Add(new GuidanceState());
            builder.World.Add(new MerchantGenetics(nodes.ContinentCount));
            MoneyLedger.Seed(builder.World, db.Config);
            builder.World.Add(new LodState(nodes.Count));

            builder.Services.SetDefault<ILodPolicy>(new DefaultLodPolicy());
            builder.Services.SetDefault<IMerchantBehavior>(new ArbitrageMerchantBehavior());

            builder.AddMigration(new PlayerStateV1Migration());
            builder.AddMigration(new PlayerStateV2Migration());
            builder.AddMigration(new FacilityTableV1Migration());
            builder.AddMigration(new AgentTableV1Migration());
            builder.AddMigration(new PlayerShipV1Migration());
            builder.AddMigration(new PurseStateV1Migration());
            builder.AddMigration(new AgentTableV3Migration());
            builder.AddMigration(new AgentTableV4Migration());
            builder.AddMigration(new AgentTableV5Migration());
            builder.AddMigration(new AgentTableV6Migration());
            builder.AddMigration(new ContractTableV1Migration());
            builder.AddMigration(new AuctionTableV1Migration());
            builder.AddMigration(new GroupColumnMigration(FacilityTable.SectionKey, 2));
            builder.AddMigration(new GroupColumnMigration(AgentTable.SectionKey, 2));

            builder.AddSystem(new TravelSystem());
            builder.AddSystem(new LodSystem());
            builder.AddSystem(new ProductionSystem());
            builder.AddSystem(new MerchantSystem());
            builder.AddSystem(new HouseholdSpendingSystem());
            builder.AddSystem(new GuidanceSystem());
            builder.AddSystem(new LicenseSystem());
            builder.AddSystem(new BankSystem());
            builder.AddSystem(new StateSpendingSystem());
            builder.AddSystem(new LaborMarketSystem());
            builder.AddSystem(new DefenseSystem());
            builder.AddSystem(new FinanceSystem());
            builder.AddSystem(new IncomeTaxSystem());
            builder.AddSystem(new NobleSystem());
            builder.AddSystem(new MigrationSystem());
            builder.AddSystem(new StateProjectSystem());
            builder.AddSystem(new SubsidySystem());
            builder.AddSystem(new InvestmentSystem());
            builder.AddSystem(new MerchantVehicleSystem());
            builder.AddSystem(new OrderSystem());
            builder.AddSystem(new ContractSystem());
            builder.AddSystem(new StrategicSubsidySystem());
            builder.AddSystem(new CreditSystem());
            builder.AddSystem(new TraderLoanSystem());
            builder.AddSystem(new MerchantMembershipSystem());
            builder.AddSystem(new DemandForecastSystem());
            builder.AddSystem(new HouseholdGrowthSystem());
            builder.AddSystem(new PolicySystem());
            builder.AddSystem(new MerchantGenerationSystem());
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

        // Producers are drawn at random per node and tier. A facility with inputs is drawn only from the types
        // whose every input some facility makes in the node's subtree, so a bakery stands where wheat grows and
        // a smithy above iron mines (P13d, [Draft]); with none such, a primary producer takes the slot.
        static void SpawnFacilities(FacilityTable table, NodeTable nodes, GameDatabase db, ulong seed)
        {
            var defsByTier = new List<FacilityDef>[IndustryTiers.Count];
            for (int t = 0; t < IndustryTiers.Count; t++) defsByTier[t] = new List<FacilityDef>();
            foreach (var def in db.Facilities)
                if (def.Continent == null) defsByTier[(int)def.Tier].Add(def);

            var made = new HashSet<int>[nodes.Count];
            for (int n = 0; n < nodes.Count; n++) made[n] = new HashSet<int>();
            // Nodes run from the top down, so a town's own slots see the village output below only on a second look.
            for (int pass = 0; pass < 2; pass++)
            for (int n = nodes.Count - 1; n >= 0; n--)
                for (int t = 0; t < IndustryTiers.Count; t++)
                    for (int k = 0; t != (int)IndustryTier.Tertiary && k < db.Config.ProducersPerNode[nodes.Level[n], t]; k++)
                    {
                        var def = Draw(defsByTier, made, n, t, k, seed);
                        if (def == null) continue;
                        foreach (var line in def.Recipe.Outputs)
                            for (int m = n; m >= 0; m = nodes.Parent[m]) made[m].Add(line.Good.Value);
                    }

            for (int n = 0; n < nodes.Count; n++)
            {
                int level = nodes.Level[n];
                for (int t = 0; t < IndustryTiers.Count; t++)
                {
                    int count = db.Config.ProducersPerNode[level, t];
                    for (int k = 0; k < count; k++)
                    {
                        var def = Draw(defsByTier, made, n, t, k, seed);
                        if (def == null) continue;
                        table.Add(n, def.Index, OwnerIds.Npc, ControlMode.Automated, 1, Fx.One, db.Config.FacilityStartCash[(int)def.Tier]);
                    }
                }
            }
            SpawnContinental(table, nodes, db);
        }

        // A continent's own goods start with a few producers, spread over its villages (DEC-096).
        static void SpawnContinental(FacilityTable table, NodeTable nodes, GameDatabase db)
        {
            var villages = new List<int>();
            foreach (var def in db.Facilities)
            {
                if (def.Continent == null) continue;
                if (!nodes.TryFind(def.Continent, out var continent))
                    throw new System.FormatException($"facilities.csv: continent '{def.Continent}' of '{def.Key}' is not on the map.");
                villages.Clear();
                for (int n = 0; n < nodes.Count; n++)
                    if (nodes.LevelOf(n) == WorldLevel.Village && nodes.IsAncestorOrSelf(continent.Value, n)) villages.Add(n);
                for (int k = 0; k < db.Config.ContinentalProducers && villages.Count > 0; k++)
                    table.Add(villages[(def.Index + k * villages.Count / db.Config.ContinentalProducers) % villages.Count], def.Index, OwnerIds.Npc,
                        ControlMode.Automated, 1, Fx.One, db.Config.FacilityStartCash[(int)def.Tier]);
            }
        }

        static FacilityDef Draw(List<FacilityDef>[] defsByTier, HashSet<int>[] made, int node, int tier, int k, ulong seed)
        {
            var eligible = defsByTier[tier].FindAll(def => System.Array.TrueForAll(def.Recipe.Inputs, line => made[node].Contains(line.Good.Value)));
            if (eligible.Count == 0 && defsByTier[tier].Count > 0) eligible = defsByTier[(int)IndustryTier.Primary];
            if (eligible.Count == 0) return null;
            // Salt packs (tier, index) without collisions for up to 2^24 producers per tier and node.
            var rng = DetRandom.For(seed, 0, SpawnStream, node, ((uint)tier << 24) | (uint)k);
            return eligible[rng.NextInt(eligible.Count)];
        }

        static void SpawnMerchants(AgentTable table, NodeTable nodes, SimulationConfig config, ulong seed)
        {
            for (int n = 0; n < nodes.Count; n++)
            {
                var level = nodes.LevelOf(n);
                for (int k = 0; k < config.MerchantsPerNode[(int)level]; k++)
                {
                    var id = table.Add(n, OwnerIds.Npc, level, ControlMode.Automated, config.MerchantStartCash[(int)level]);
                    if (config.MerchantTraitsEnabled) table.Trait[id.Value] = (byte)DetRandom.For(seed, 0, TraitStream, id.Value).NextInt(MerchantTraits.Count);
                    table.Vehicle[id.Value] = MerchantVehicles.AtStart(config, seed, id.Value);
                }
            }
        }
    }
}
