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
        public void DetailedJob_BurstMatchesManaged_WithRumors()
        {
            var data = TestWorlds.SampleWith(GameDatabase.ConfigFile, "key,value\nmerchants.rumors,1\n");
            using var burst = SimulationBuilder.FromData(data, 3).Build();
            using var managed = SimulationBuilder.FromData(data, 3).Build();
            ((ArbitrageMerchantBehavior)managed.Context.Services.Get<IMerchantBehavior>()).ForceManaged = true;
            for (int t = 0; t < 2 * SimClock.WeeksPerYear + 2; t++)
            {
                burst.AdvanceTurn();
                managed.AdvanceTurn();
            }
            Assert.AreEqual(managed.ComputeHash(), burst.ComputeHash());
        }

        [Test]
        public void Traits_AreDrawnAtStart_AndNeutralWhenOff()
        {
            using var on = TestWorlds.BuildSample();
            var traits = on.World.Get<AgentTable>().Trait.AsArray().ToArray();
            Assert.Greater(traits.Distinct().Count(), 5, "nine combinations, drawn at random");

            using var off = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile, "key,value\nmerchants.traits,0\n"), 42).Build();
            Assert.IsTrue(off.World.Get<AgentTable>().Trait.AsArray().All(t => t == MerchantTraits.Neutral));
            for (int t = 0; t < SimClock.WeeksPerYear + 2; t++) off.AdvanceTurn();
            Assert.IsTrue(off.World.Get<AgentTable>().Trait.AsArray().All(t => t == MerchantTraits.Neutral));
        }

        [Test]
        public void Personality_LimitsWhatAGroupRisks()
        {
            var config = SimulationConfig.Defaults();
            using var table = new MerchantTraitTable(config);
            int cautious = MerchantTraits.Of(MerchantPersonality.Cautious, MerchantOccupation.Local);
            int aggressive = MerchantTraits.Of(MerchantPersonality.Aggressive, MerchantOccupation.Peddler);
            Assert.Less(table.SpendRaw[cautious], table.SpendRaw[aggressive]);
            Assert.Greater(table.MinMarginRaw[cautious], table.MinMarginRaw[aggressive]);
            Assert.Greater(table.DistancePower[cautious], table.DistancePower[aggressive]);
            Assert.Less(table.CapacityRaw[aggressive], table.CapacityRaw[cautious]);
        }

        [Test]
        public void UnsoldCargo_IsSoldOnceTheTradersCanPay()
        {
            using var sim = TestWorlds.BuildSample();
            sim.AdvanceTurn();
            var agents = sim.World.Get<AgentTable>();
            var cargo = sim.World.Get<CargoTable>();
            var lod = sim.World.Get<LodState>();
            var markets = sim.World.Get<MarketStore>();
            Assert.IsTrue(sim.Database.TryGetGood("wheat", QualityGrade.Mid, out var wheat));
            int a = Enumerable.Range(0, agents.Count).First(i => lod.IsDetailed(agents.Node[i]) && !agents.IsTravelling(i)
                && markets.TryFindCell(agents.Node[i], wheat, out _));
            var holder = CargoHolders.Merchant(new AgentId(a));
            cargo.Clear(holder);
            cargo.Change(holder, wheat, 10);
            // Plenty in the float: the load goes in the group's next idle week, without a trip.
            sim.World.Get<PurseState>().Float[sim.World.Get<PurseState>().NodeFloat[agents.Node[a]]] += 1_000_000;
            sim.World.Get<HouseholdCash>().Cash[agents.Node[a]] -= 1_000_000;
            long cash = agents.Cash[a];
            sim.AdvanceTurn();
            Assert.Greater(agents.Cash[a], cash - sim.Database.Config.MerchantUpkeep[agents.Scale[a]]);
        }

        [Test]
        public void GroupWithCargoAndNoCash_CarriesItToWhereItSells()
        {
            using var sim = TestWorlds.BuildSample();
            sim.AdvanceTurn();
            var agents = sim.World.Get<AgentTable>();
            var cargo = sim.World.Get<CargoTable>();
            var lod = sim.World.Get<LodState>();
            var purses = sim.World.Get<PurseState>();
            Assert.IsTrue(sim.Database.TryGetGood("wheat", QualityGrade.Mid, out var wheat));
            int a = Enumerable.Range(0, agents.Count).First(i => lod.IsDetailed(agents.Node[i]) && !agents.IsTravelling(i));
            var holder = CargoHolders.Merchant(new AgentId(a));
            cargo.Clear(holder);
            cargo.Change(holder, wheat, 50);
            sim.World.Get<HouseholdCash>().Cash[agents.Node[a]] += agents.Cash[a];
            agents.Cash[a] = 0;
            int p = purses.NodeFloat[agents.Node[a]];
            sim.World.Get<HouseholdCash>().Cash[agents.Node[a]] += purses.Float[p];
            purses.Float[p] = 0;
            sim.AdvanceTurn();
            Assert.IsTrue(agents.IsTravelling(a), "it set off to sell");
        }

        [Test]
        public void CautiousGroups_StayInTheirHomeRegion()
        {
            using var sim = TestWorlds.BuildSample();
            var agents = sim.World.Get<AgentTable>();
            var nodes = sim.World.Get<NodeTable>();
            for (int a = 0; a < agents.Count; a++)
                agents.Trait[a] = MerchantTraits.Of(MerchantPersonality.Cautious, MerchantOccupation.Expedition);
            int trips = 0;
            for (int t = 0; t < 30; t++)
            {
                sim.AdvanceTurn();
                for (int a = 0; a < agents.Count; a++)
                {
                    if (!agents.IsTravelling(a) || nodes.Level[agents.Home[a]] < (byte)WorldLevel.Region) continue;
                    trips++;
                    Assert.AreEqual(nodes.AncestorAt(agents.Home[a], WorldLevel.Region), nodes.AncestorAt(agents.Destination[a], WorldLevel.Region));
                }
            }
            Assert.Greater(trips, 0);
        }

        [Test]
        public void GroupBasedInTheDetailedArea_IsSteppedAwayFromIt()
        {
            using var sim = TestWorlds.BuildSample();
            sim.AdvanceTurn();
            var agents = sim.World.Get<AgentTable>();
            var nodes = sim.World.Get<NodeTable>();
            var lod = sim.World.Get<LodState>();
            int a = Enumerable.Range(0, agents.Count).First(i => lod.IsDetailed(agents.Home[i]) && !agents.IsTravelling(i));
            int away = Enumerable.Range(0, nodes.Count).First(n => !lod.IsDetailed(n) && nodes.Continent[n] == nodes.Continent[agents.Home[a]]);
            agents.Node[a] = away;
            long cash = agents.Cash[a];
            sim.AdvanceTurn();
            Assert.AreNotEqual(cash, agents.Cash[a]);
        }

        [Test]
        public void NewYear_SetsWeightsByFitness_AndAShareOfGroupsHandsOver()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile,
                "key,value\nmerchants.generation_permille,500\nmerchants.mutation_permille,0\n"), 42).Build();
            var agents = sim.World.Get<AgentTable>();
            var genetics = sim.World.Get<MerchantGenetics>();
            var nodes = sim.World.Get<NodeTable>();
            int continent = nodes.Continent[sim.World.Get<PlayerState>().Node];
            int first = continent * MerchantTraits.Count;
            long uniform = genetics.Weight[first];
            for (int t = 0; t < SimClock.WeeksPerYear; t++) sim.AdvanceTurn();
            var before = agents.Trait.AsArray().ToArray();
            var cash = agents.Cash.AsArray().ToArray();
            genetics.YearBankruptcies[first] = 1000; // trait 0 failed everywhere this year
            sim.AdvanceTurn();
            Assert.Less(genetics.Weight[first], uniform, "a failing trait is drawn less");
            Assert.AreEqual(0, genetics.YearBankruptcies[first]);
            var after = agents.Trait.AsArray();
            int changed = Enumerable.Range(0, before.Length).Count(a => before[a] != after[a]);
            Assert.Greater(changed, before.Length / 5, "about half hand over, most to another trait");
            Assert.IsTrue(sim.World.Get<CurrencyState>().CoinSupply[continent] > 0);
        }

        [Test]
        public void NodeMemory_KeepsTheLastFourNodes()
        {
            var memory = NodeMemory.Empty;
            Assert.IsFalse(memory.Knows(1));
            for (int n = 1; n <= 5; n++) memory.Learn(n);
            Assert.IsFalse(memory.Knows(1), "the oldest went");
            for (int n = 2; n <= 5; n++) Assert.IsTrue(memory.Knows(n));
            memory.Learn(3); // already known: nothing moves
            memory.Learn(6);
            Assert.IsFalse(memory.Knows(2));
            Assert.IsTrue(memory.Knows(3));
        }

        [Test]
        public void Rumors_ConserveMoney_AndTeachGroupsNodes()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile, "key,value\nmerchants.rumors,1\n"), 42).Build();
            var currency = sim.World.Get<CurrencyState>();
            for (int t = 0; t < 30; t++)
            {
                sim.AdvanceTurn();
                for (int c = 0; c < currency.ContinentCount; c++)
                    Assert.AreEqual(currency.CoinSupply[c], MoneyLedger.ContinentMoney(sim.World, c));
            }
            Assert.IsTrue(sim.World.Get<AgentTable>().Memory.AsArray().Any(m => m.B >= 0), "someone knows more than one node");
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
            using var routeCost = new NativeArray<long>(new[] { Fx.One.Raw }, Allocator.Temp);
            using var routeBudget = new NativeArray<long>(1, Allocator.Temp);
            using var routeLoss = new NativeArray<long>(new[] { Fx.FromPermille(100).Raw }, Allocator.Temp);
            using var cellStart = new NativeArray<int>(new[] { 0, 1 }, Allocator.Temp);
            using var cellCount = new NativeArray<int>(new[] { 1, 1 }, Allocator.Temp);
            using var cellGood = new NativeArray<int>(new[] { 0, 0 }, Allocator.Temp);
            using var goodZero = new NativeArray<int>(new[] { 0 }, Allocator.Temp);
            using var goodWeight = new NativeArray<int>(new[] { 1 }, Allocator.Temp);
            using var licenseRow = new NativeArray<int>(new[] { 0, 0 }, Allocator.Temp);
            using var licensed = new NativeArray<byte>(new byte[] { 0 }, Allocator.Temp);
            using var price = new NativeArray<int>(new[] { 10, 30 }, Allocator.Temp);
            using var stock = new NativeArray<int>(new[] { 100, 0 }, Allocator.Temp);
            using var supply = new NativeArray<int>(2, Allocator.Temp);
            using var demand = new NativeArray<int>(2, Allocator.Temp);
            using var nodeFloat = new NativeArray<int>(new[] { 0, 0 }, Allocator.Temp);
            using var floats = new NativeArray<long>(1, Allocator.Temp);
            using var nodeSpend = new NativeArray<long>(new[] { Fx.OneRaw, Fx.OneRaw }, Allocator.Temp);
            using var nodeMargin = new NativeArray<long>(2, Allocator.Temp);
            new MerchantFlowJob
            {
                CategoryCount = 1,
                FlowShareRaw = Fx.FromPermille(500).Raw,
                LicensedShareRaw = Fx.FromPermille(200).Raw,
                NodeDetail = detail, NodeContinent = continent, NodeCapacity = capacity, NodeSpendRaw = nodeSpend, NodeMinMarginRaw = nodeMargin, NodeCategorySpecialty = specialty,
                Tariff = rates, SpecialtySurcharge = rates,
                OutStart = outStart, OutCount = outCount, RouteIndex = routeIndex, RouteTo = routeTo,
                RouteTurns = routeTurns, RouteCostPerUnitRaw = routeCost, RouteBudget = routeBudget, RouteExpectedLoss = routeLoss,
                MarketCellStart = cellStart, MarketCellCount = cellCount, CellGood = cellGood,
                GoodCategory = goodZero, GoodWeight = goodWeight, NodeLicenseRow = licenseRow, Licensed = licensed, BaseGoodCount = 1,
                Price = price, NodeFloat = nodeFloat, Float = floats, Stock = stock, Supply = supply, Demand = demand,
            }.Execute();

            // margin 30 - 10 - 1 = 19; half the stock times 19/30 = 31 units, 10% (3) lost on the way.
            Assert.AreEqual(69, stock[0]);
            Assert.AreEqual(31, supply[0]);
            Assert.AreEqual(31, demand[0]);
            Assert.AreEqual(28, supply[1]);
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
            var migrated = sim.Migrations.Upgrade(AgentTable.SectionKey, 1, 4, v1.ToArray());
            agents.Read(new BinaryReader(new MemoryStream(migrated)));
            for (int a = 0; a < agents.Count; a++)
            {
                Assert.AreEqual(agents.Node[a], agents.Home[a]);
                Assert.IsFalse(agents.IsTravelling(a));
            }
        }
    }
}
