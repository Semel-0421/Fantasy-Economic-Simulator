using System.Collections.Generic;
using System.IO;
using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.Simulation;
using FES.World;
using NUnit.Framework;
using Unity.Collections;

namespace FES.Tests
{
    public class MerchantTests
    {
        static IEnumerable<int> DetailedMerchants(GameSimulation sim)
        {
            var agents = sim.World.Get<AgentTable>();
            var lod = sim.World.Get<LodState>();
            return Enumerable.Range(0, agents.Count).Where(a => lod.IsDetailed(agents.Node[a]));
        }

        [Test]
        public void DetailedMerchant_BuysTravelsAndSells()
        {
            using var sim = TestWorlds.BuildSample();
            var agents = sim.World.Get<AgentTable>();
            var cargo = sim.World.Get<CargoTable>();
            sim.AdvanceTurn();
            int trader = -1;
            for (int t = 0; t < 10 && trader < 0; t++)
            {
                sim.AdvanceTurn();
                trader = DetailedMerchants(sim).Where(agents.IsTravelling).DefaultIfEmpty(-1).First();
            }
            Assert.GreaterOrEqual(trader, 0, "no detailed merchant set off");

            var load = new List<CargoItem>();
            cargo.Collect(CargoHolders.Merchant(new AgentId(trader)), load);
            Assert.AreEqual(1, load.Count);
            int destination = agents.Destination[trader];
            int arrival = agents.ArrivalTurn[trader];
            long cash = agents.Cash[trader];
            while (sim.Turn <= arrival) sim.AdvanceTurn();

            // It sold the load on arrival; it may already have set off again from there.
            Assert.AreEqual(destination, agents.Node[trader]);
            Assert.AreNotEqual(cash, agents.Cash[trader]);
        }

        [Test]
        public void Merchants_StayOnTheirContinent_AndCarryLicensedGoodsOnlyWithLicenses()
        {
            using var sim = TestWorlds.BuildSample();
            var agents = sim.World.Get<AgentTable>();
            var cargo = sim.World.Get<CargoTable>();
            var nodes = sim.World.Get<NodeTable>();
            int trips = 0;
            for (int t = 0; t < 40; t++)
            {
                sim.AdvanceTurn();
                for (int a = 0; a < agents.Count; a++)
                {
                    Assert.AreEqual(nodes.Continent[agents.Home[a]], nodes.Continent[agents.Node[a]]);
                    if (!agents.IsTravelling(a)) continue;
                    trips++;
                    Assert.AreEqual(nodes.Continent[agents.Home[a]], nodes.Continent[agents.Destination[a]]);
                    bool licensed = LicenseRules.MerchantHoldsLicenses(sim.Context.Seed, new AgentId(a), sim.Database.Config.LicensedMerchantPermille);
                    var load = new List<CargoItem>();
                    cargo.Collect(CargoHolders.Merchant(new AgentId(a)), load);
                    Assert.IsTrue(licensed || load.All(i => !sim.Database.Good(i.Good).RequiresLicense));
                }
            }
            Assert.Greater(trips, 0);
        }

        [Test]
        public void Smithies_GetOre_OnceMerchantsCarryIt()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            var config = sim.Database.Config;
            var smithies = Enumerable.Range(0, facilities.Count)
                .Where(f => sim.Database.Facilities[facilities.DefIndex[f]].Tier == IndustryTier.Secondary).ToList();
            const int turns = 30;
            for (int t = 0; t < turns; t++) sim.AdvanceTurn();
            long idle = config.FacilityStartCash[(int)IndustryTier.Secondary] - turns * config.FacilityUpkeep[(int)IndustryTier.Secondary];
            Assert.IsTrue(smithies.Any(f => facilities.Cash[f] != idle));
        }

        [Test]
        public void MerchantBelowZeroCash_IsReplacedAtHome()
        {
            using var sim = TestWorlds.BuildSample();
            var agents = sim.World.Get<AgentTable>();
            sim.AdvanceTurn(); // LOD is set on the first turn
            int a = DetailedMerchants(sim).First(m => !agents.IsTravelling(m));
            agents.Cash[a] = -1;
            var bankrupt = new List<MerchantBankrupt>();
            for (int t = 0; t < sim.Database.Config.MerchantBankruptWeeks; t++)
            {
                sim.AdvanceTurn();
                bankrupt.AddRange(sim.LastTurnEvents.OfType<MerchantBankrupt>().Where(b => b.Merchant.Value == a));
            }
            Assert.AreEqual(1, bankrupt.Count);
            Assert.AreEqual(agents.Home[a], agents.Node[a]);
            Assert.AreEqual(sim.Database.Config.MerchantStartCash[agents.Scale[a]], agents.Cash[a]);
        }

        [Test]
        public void MerchantBehavior_BurstMatchesManaged()
        {
            using var burst = TestWorlds.BuildSample(seed: 3);
            using var managed = TestWorlds.BuildSample(seed: 3);
            ((ArbitrageMerchantBehavior)managed.Context.Services.Get<IMerchantBehavior>()).ForceManaged = true;
            for (int t = 0; t < 25; t++)
            {
                burst.AdvanceTurn();
                managed.AdvanceTurn();
            }
            Assert.AreEqual(managed.ComputeHash(), burst.ComputeHash());
        }

        [Test]
        public void AggregateFlow_MovesStockTowardTheHigherPrice()
        {
            using var detail = new NativeArray<byte>(new byte[] { 0, 0 }, Allocator.Temp);
            using var continent = new NativeArray<int>(new[] { 0, 0 }, Allocator.Temp);
            using var capacity = new NativeArray<long>(new long[] { 1000, 0 }, Allocator.Temp);
            using var specialty = new NativeArray<byte>(new byte[] { 0, 0 }, Allocator.Temp);
            using var rates = new NativeArray<long>(new long[] { 0, 0 }, Allocator.Temp);
            using var outStart = new NativeArray<int>(new[] { 0, 1 }, Allocator.Temp);
            using var outCount = new NativeArray<int>(new[] { 1, 0 }, Allocator.Temp);
            using var routeIndex = new NativeArray<int>(new[] { 0 }, Allocator.Temp);
            using var routeTo = new NativeArray<int>(new[] { 1 }, Allocator.Temp);
            using var routeTurns = new NativeArray<int>(new[] { 1 }, Allocator.Temp);
            using var routeCost = new NativeArray<int>(new[] { 1 }, Allocator.Temp);
            using var routeBudget = new NativeArray<long>(1, Allocator.Temp);
            using var cellStart = new NativeArray<int>(new[] { 0, 1 }, Allocator.Temp);
            using var cellCount = new NativeArray<int>(new[] { 1, 1 }, Allocator.Temp);
            using var cellGood = new NativeArray<int>(new[] { 0, 0 }, Allocator.Temp);
            using var goodZero = new NativeArray<int>(new[] { 0 }, Allocator.Temp);
            using var goodWeight = new NativeArray<int>(new[] { 1 }, Allocator.Temp);
            using var licensed = new NativeArray<byte>(new byte[] { 0 }, Allocator.Temp);
            using var price = new NativeArray<int>(new[] { 10, 30 }, Allocator.Temp);
            using var stock = new NativeArray<int>(new[] { 100, 0 }, Allocator.Temp);
            using var supply = new NativeArray<int>(2, Allocator.Temp);
            using var demand = new NativeArray<int>(2, Allocator.Temp);
            new MerchantFlowJob
            {
                CategoryCount = 1,
                FlowShareRaw = Fx.FromPermille(500).Raw,
                LicensedShareRaw = Fx.FromPermille(200).Raw,
                NodeDetail = detail, NodeContinent = continent, NodeCapacity = capacity, NodeCategorySpecialty = specialty,
                Tariff = rates, SpecialtySurcharge = rates,
                OutStart = outStart, OutCount = outCount, RouteIndex = routeIndex, RouteTo = routeTo,
                RouteTurns = routeTurns, RouteCostPerUnit = routeCost, RouteBudget = routeBudget,
                MarketCellStart = cellStart, MarketCellCount = cellCount, CellGood = cellGood,
                GoodCategory = goodZero, GoodWeight = goodWeight, GoodLicensed = licensed,
                Price = price, Stock = stock, Supply = supply, Demand = demand,
            }.Execute();

            // margin 30 - 10 - 1 = 19; half the stock times 19/30 = 31 units.
            Assert.AreEqual(69, stock[0]);
            Assert.AreEqual(31, supply[0]);
            Assert.AreEqual(31, demand[0]);
            Assert.AreEqual(31, supply[1]);
        }

        [Test]
        public void AgentTableV1Save_StartsEveryGroupAtHome()
        {
            using var sim = TestWorlds.BuildSample();
            var agents = sim.World.Get<AgentTable>();
            using var v1 = new MemoryStream();
            using (var writer = new BinaryWriter(v1))
            {
                agents.Node.Write(writer);
                agents.Owner.Write(writer);
                agents.Scale.Write(writer);
                agents.Mode.Write(writer);
                agents.Cash.Write(writer);
            }
            var migrated = sim.Migrations.Upgrade(AgentTable.SectionKey, 1, 2, v1.ToArray());
            agents.Read(new BinaryReader(new MemoryStream(migrated)));
            for (int a = 0; a < agents.Count; a++)
            {
                Assert.AreEqual(agents.Node[a], agents.Home[a]);
                Assert.IsFalse(agents.IsTravelling(a));
            }
        }
    }
}
