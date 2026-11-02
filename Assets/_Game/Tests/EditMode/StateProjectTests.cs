using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.Simulation;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>The state's defense, land development and state facilities (DEC-072).</summary>
    public class StateProjectTests
    {
        static void AssertConserved(GameSimulation sim, long[] start)
        {
            var currency = sim.World.Get<CurrencyState>();
            for (int c = 0; c < start.Length; c++)
                Assert.AreEqual(currency.CoinSupply[c], MoneyLedger.ContinentMoney(sim.World, c), $"continent {c}");
        }

        [Test]
        public void Defense_BuysFromTheNationsMarkets_AtItsFixedShare()
        {
            using var sim = TestWorlds.BuildSample();
            var development = sim.World.Get<DevelopmentState>();
            var config = sim.Database.Config;
            for (int t = 0; t < SimClock.WeeksPerYear; t++) sim.AdvanceTurn();
            for (int n = 0; n < development.YearBudget.Length; n++)
            {
                long yearly = config.DefenseShare.MulInt(development.YearBudget[n]);
                yearly += config.DefenseMaintenance.MulInt(yearly);
                Assert.Greater(development.DefenseSpent[n], 0);
                Assert.LessOrEqual(development.DefenseSpent[n], yearly);
            }
            Assert.AreEqual(sim.World.Get<CurrencyState>().CoinSupply[0], MoneyLedger.ContinentMoney(sim.World, 0));
        }

        // A nation buying a twentieth of its stock from cells holding ten units still buys. Its coins go on whichever
        // cells a whole unit falls to, so with stocks this even it falls somewhat short of the share, never over it.
        [Test]
        public void Defense_SpendsItsShare_WhereEachCellHoldsLessThanAWholeUnitOfIt()
        {
            using var sim = TestWorlds.BuildSample();
            var markets = sim.World.Get<MarketStore>();
            var purses = sim.World.Get<PurseState>();
            for (int c = 0; c < markets.Stock.Length; c++) markets.Stock[c] = 10;
            int nations = purses.NationCount;
            using var spend = new global::Unity.Collections.NativeArray<long>(nations, global::Unity.Collections.Allocator.TempJob);
            using var paid = new global::Unity.Collections.NativeArray<long>(nations, global::Unity.Collections.Allocator.TempJob);
            using var value = new global::Unity.Collections.NativeArray<long>(nations, global::Unity.Collections.Allocator.TempJob);
            using var carry = new global::Unity.Collections.NativeArray<long>(nations, global::Unity.Collections.Allocator.TempJob);
            var worth = new long[nations];
            for (int node = 0; node < purses.NodeFloat.Length; node++)
            {
                int nation = purses.NodeFloat[node], end = markets.MarketCellStart[node] + markets.MarketCellCount[node];
                if (nation >= nations) continue;
                for (int c = markets.MarketCellStart[node]; c < end; c++) worth[nation] += (long)markets.Stock[c] * markets.Price[c];
            }
            var spendable = spend;
            for (int n = 0; n < nations; n++) spendable[n] = worth[n] / 20;

            new DefensePurchaseJob
            {
                NodeFloat = purses.NodeFloat,
                MarketCellStart = markets.MarketCellStart,
                MarketCellCount = markets.MarketCellCount,
                Price = markets.Price,
                Stock = markets.Stock,
                Supply = markets.Supply,
                Demand = markets.Demand,
                Spend = spend,
                Paid = paid,
                Value = value,
                Carry = carry,
            }.Execute();

            for (int n = 0; n < nations; n++)
            {
                Assert.LessOrEqual(paid[n], spend[n], $"nation {n}");
                Assert.GreaterOrEqual(paid[n], spend[n] / 2, $"nation {n}");
            }
        }

        [Test]
        public void Defense_PassesWhatItBuysThroughSupply_SoStockFallsOnce()
        {
            using var sim = TestWorlds.BuildSample();
            var markets = sim.World.Get<MarketStore>();
            var bank = sim.World.Get<BankState>();
            for (int n = 0; n < bank.Reserve.Length; n++) bank.Reserve[n] = 1_000_000_000_000;
            for (int c = 0; c < markets.Stock.Length; c++) markets.Stock[c] = 1000;
            var stock = markets.Stock.ToArray();
            var supply = markets.Supply.ToArray();
            var demand = markets.Demand.ToArray();
            sim.Systems.OfType<DefenseSystem>().Single().Execute(sim.Context);
            long bought = 0;
            for (int c = 0; c < stock.Length; c++)
            {
                int units = stock[c] - markets.Stock[c];
                bought += units;
                Assert.AreEqual(supply[c] + units, markets.Supply[c], $"supply of cell {c}");
                Assert.AreEqual(demand[c] + units, markets.Demand[c], $"demand of cell {c}");
            }
            Assert.Greater(bought, 0);
        }

        [Test]
        public void Projects_AreFundedAndFinished_AndConserveMoney()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile, "key,value\nfacility.bankrupt_weeks,100000\nbank.npc_max_reserve_share_permille,0\nbank.state_reserve_target_permille,0\nstate.project_weeks,4\nstate.subsidy_max_per_nation,0\n"), 42).Build();
            var facilities = sim.World.Get<FacilityTable>();
            int before = facilities.Count;
            int finished = 0;
            for (int t = 0; t < 3 * SimClock.WeeksPerYear; t++)
            {
                sim.AdvanceTurn();
                finished += sim.LastTurnEvents.OfType<StateProjectFinished>().Count();
            }
            Assert.Greater(finished, 0);
            for (int c = 0; c < sim.World.Get<NodeTable>().ContinentCount; c++)
                Assert.AreEqual(sim.World.Get<CurrencyState>().CoinSupply[c], MoneyLedger.ContinentMoney(sim.World, c), $"continent {c}");
            var development = sim.World.Get<DevelopmentState>();
            int land = development.LandLevel.Sum();
            Assert.AreEqual(land, facilities.Count - before, "each land project opens one state facility");
            Assert.GreaterOrEqual(Enumerable.Range(before, facilities.Count - before).Count(f => facilities.Group[f] >= 0
                && sim.World.Get<GroupTable>().KindOf(facilities.Group[f]) == GroupKind.StateEnterprise), land);
        }

        [Test]
        public void Projects_OfAYear_RunOneAtATime_InAQueue()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile, "key,value\nbank.state_reserve_target_permille,0\n"), 42).Build();
            var development = sim.World.Get<DevelopmentState>();
            bool queued = false;
            for (int t = 0; t < 2 * SimClock.WeeksPerYear + 2; t++)
            {
                sim.AdvanceTurn();
                foreach (var group in development.Projects.GroupBy(p => p.Nation))
                {
                    Assert.LessOrEqual(group.Count(p => p.DoneTurn >= 0), 1, "one under way");
                    if (group.Any(p => p.DoneTurn < 0)) queued = true;
                }
            }
            Assert.IsTrue(queued, "later projects wait their turn");
        }

        [Test]
        public void Subsidy_PaysProducersByOutput_AndOpensProducers_ConservingMoney()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile, "key,value\nbank.state_reserve_target_permille,0\n"), 42).Build();
            var development = sim.World.Get<DevelopmentState>();
            var facilities = sim.World.Get<FacilityTable>();
            int enacted = 0, facilitiesBefore = facilities.Count;
            for (int t = 0; t < 4 * SimClock.WeeksPerYear; t++)
            {
                sim.AdvanceTurn();
                enacted += sim.LastTurnEvents.OfType<SubsidyEnacted>().Count();
                for (int c = 0; c < sim.World.Get<NodeTable>().ContinentCount; c++)
                    Assert.AreEqual(sim.World.Get<CurrencyState>().CoinSupply[c], MoneyLedger.ContinentMoney(sim.World, c), $"continent {c}, turn {t}");
            }
            Assert.Greater(enacted, 0);
            Assert.Greater(facilities.Count, facilitiesBefore);
        }

        [Test]
        public void SubsidisedWorkshop_HasItsFamilyAtTheCoreItStandsAt()
        {
            using var sim = TestWorlds.BuildSample();
            var nodes = sim.World.Get<NodeTable>();
            var groups = sim.World.Get<GroupTable>();
            var facilities = sim.World.Get<FacilityTable>();
            var households = sim.World.Get<HouseholdState>();
            var banks = sim.World.Get<NodeBanks>();
            var development = sim.World.Get<DevelopmentState>();
            sim.AdvanceTurn();
            while (!sim.Context.Clock.IsNewYear) sim.AdvanceTurn();

            int core = nodes.Core[banks.NationNode(0)];
            bool HasFamily(int node) => Enumerable.Range(0, groups.Count).Any(g => groups.KindOf(g) == GroupKind.PrivateEnterprise && groups.Node[g] == node);
            var nationNodes = sim.World.Get<PurseState>().NationNodes[0];
            int site = nationNodes.First(n => n != core && nodes.LevelOf(n) == WorldLevel.City);
            for (int g = 0; g < groups.Count; g++) // no family lives at either yet
                if (groups.KindOf(g) == GroupKind.PrivateEnterprise && (groups.Node[g] == core || groups.Node[g] == site)) groups.Kind[g] = (byte)GroupKind.Company;
            foreach (int node in nationNodes)
            {
                households.NecessityWanted[node] = 100;
                households.NecessityMet[node] = node == site ? 0 : 100; // the shortest of necessities, so the subsidy looks here first
            }
            Assert.IsTrue(sim.Database.TryGetGood("iron_tools", QualityGrade.Mid, out var tools));
            sim.Database.Config.SubsidyMaxPerNation = 1;
            development.Subsidies.Clear();
            development.Subsidies.Add(new SubsidyPolicy { Nation = 0, Good = tools.Value, EndYear = 9_999 });
            sim.World.Get<BankState>().Reserve[0] += 10_000_000;
            int before = facilities.Count;

            sim.Systems.OfType<SubsidySystem>().Single().Execute(sim.Context);

            Assert.Greater(facilities.Count, before);
            Assert.AreEqual(core, facilities.Node[facilities.Count - 1], "a smithy stands at a core");
            Assert.IsTrue(HasFamily(core));
            Assert.IsFalse(HasFamily(site), "no family is staked where nothing was built");
        }

        [Test]
        public void SubsidyRate_AddsToWhatAProducerIsPaid()
        {
            long Cash(bool subsidy)
            {
                using var sim = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile, "key,value\nbank.state_reserve_target_permille,0\n"), 42).Build();
                var development = sim.World.Get<DevelopmentState>();
                var facilities = sim.World.Get<FacilityTable>();
                var def = sim.Database.Facilities.First(d => d.Recipe.Inputs.Length == 0);
                int f = Enumerable.Range(0, facilities.Count).First(i => facilities.DefIndex[i] == def.Index && facilities.Owner[i] == OwnerIds.Npc
                    && sim.World.Get<PurseState>().NodeFloat[facilities.Node[i]] < development.YearBudget.Length);
                int nation = sim.World.Get<PurseState>().NodeFloat[facilities.Node[f]];
                if (subsidy)
                {
                    foreach (var line in def.Recipe.Outputs) development.Subsidies.Add(new SubsidyPolicy { Nation = nation, Good = line.Good.Value, EndYear = 99 });
                    development.RefreshSubsidyRates(sim.Database.Config.SubsidyRate);
                }
                sim.Systems.OfType<ProductionSystem>().Single().Execute(sim.Context);
                return facilities.Cash[f];
            }
            Assert.Greater(Cash(true), Cash(false));
        }

        [Test]
        public void Army_MakesRoutesSafer_InProportionToWhatItCosts()
        {
            using var sim = TestWorlds.BuildSample();
            var costs = new TravelCosts(sim.World, sim.Database);
            var development = sim.World.Get<DevelopmentState>();
            var purses = sim.World.Get<PurseState>();
            var routes = sim.World.Get<RouteTable>();
            sim.AdvanceTurn();
            int r = Enumerable.Range(0, routes.Count).First(i => purses.NodeFloat[routes.From[i]] < development.Security.Length);
            int nation = purses.NodeFloat[routes.From[r]];
            Assert.Greater(development.Security[nation], 0);
            Assert.LessOrEqual(development.Security[nation], sim.Database.Config.DefenseMaxSafety.Raw);
            var armed = costs.RouteDanger(r);
            development.Security[nation] = 0;
            Assert.Greater(costs.RouteDanger(r).Raw, armed.Raw);
        }

        [Test]
        public void Highway_CutsTheRoutesCharge()
        {
            using var sim = TestWorlds.BuildSample();
            var costs = new TravelCosts(sim.World, sim.Database);
            var development = sim.World.Get<DevelopmentState>();
            int r = Enumerable.Range(0, sim.World.Get<RouteTable>().Count).First(i => !costs.IsWater(i));
            var before = costs.PerUnitCharge(r);
            development.RouteGrade[r] = 1;
            Assert.AreEqual((before * (Fx.One - sim.Database.Config.StateHighwayBonus)).Raw, costs.PerUnitCharge(r).Raw);
        }

        [Test]
        public void Population_FollowsNecessitySupply()
        {
            using var sim = TestWorlds.BuildSample();
            sim.Database.Config.MigrationMaxShare = Fx.Zero;
            sim.Database.Config.PopulationUrbanPenalty = Fx.Zero;
            var households = sim.World.Get<HouseholdState>();
            var nodes = sim.World.Get<NodeTable>();
            var config = sim.Database.Config;
            Assert.IsTrue(sim.View != null);
            int full = Enumerable.Range(0, nodes.Count).First(n => households.Population[n] > 10_000 || households.Population[n] > 1_000);
            int none = Enumerable.Range(0, nodes.Count).Last(n => households.Population[n] > 1_000);
            Assert.AreNotEqual(full, none);
            while (sim.Turn < SimClock.WeeksPerYear) sim.AdvanceTurn();
            long fullBefore = households.Population[full], noneBefore = households.Population[none];
            households.NecessityWanted[full] = 1000;
            households.NecessityMet[full] = 1000;
            households.NecessityWanted[none] = 1000;
            households.NecessityMet[none] = 0;
            sim.Systems.OfType<HouseholdGrowthSystem>().Single().Execute(sim.Context);
            Assert.AreEqual(fullBefore + config.PopulationBaseGrowth.MulInt(fullBefore), households.Population[full]);
            Assert.Less(households.Population[none], noneBefore);
        }
    }
}
