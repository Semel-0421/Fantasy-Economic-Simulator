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
    public class ForecastTests
    {
        // A quarter's planning on empty markets where nothing has lately sold, so only what consumers want can lift a plan.
        static GameSimulation AtQuarter()
        {
            var sim = TestWorlds.BuildSample();
            do sim.AdvanceTurn(); while (sim.Context.Turn % OrderSystem.QuarterWeeks != 0);
            var markets = sim.World.Get<MarketStore>();
            var forecast = sim.World.Get<DemandForecast>();
            for (int c = 0; c < markets.CellCount; c++)
            {
                markets.Stock[c] = 0;
                forecast.Forecast[c] = 0;
                forecast.StockAtStart[c] = 0;
                forecast.Produced[c] = 0;
            }
            return sim;
        }

        static void Plan(GameSimulation sim) => sim.Systems.OfType<DemandForecastSystem>().Single().Execute(sim.Context);

        // The cells the NPC facilities of a kind read their plans at, one per market.
        static int[] PlanCells(GameSimulation sim, string facility)
        {
            var facilities = sim.World.Get<FacilityTable>();
            var flows = sim.World.Get<MarketFlows>();
            var cells = new System.Collections.Generic.List<int>();
            for (int f = 0; f < facilities.Count; f++)
            {
                var def = sim.Database.Facilities[facilities.DefIndex[f]];
                if (def.Key != facility || facilities.Owner[f] != OwnerIds.Npc) continue;
                Assert.IsTrue(flows.TryFindNearestMarket(facilities.Node[f], def.Recipe.Outputs[0].Good, out _, out int cell));
                if (!cells.Contains(cell)) cells.Add(cell);
            }
            Assert.IsNotEmpty(cells, $"no {facility}");
            return cells.ToArray();
        }

        static Fx PlanOf(GameSimulation sim, string facility) => Fx.FromRaw(sim.World.Get<DemandForecast>().Plan[PlanCells(sim, facility)[0]]);

        static void Stock(GameSimulation sim, string good, QualityGrade grade, int quantity)
        {
            Assert.IsTrue(sim.Database.TryGetGood(good, grade, out var id));
            var markets = sim.World.Get<MarketStore>();
            for (int c = 0; c < markets.CellCount; c++)
                if (markets.CellGood[c] == id.Value) markets.Stock[c] = quantity;
        }

        [Test]
        public void Plan_FollowsWhatHouseholdsWant_ThoughNothingSold()
        {
            using var sim = AtQuarter();
            var config = sim.Database.Config;
            Plan(sim);
            Assert.Greater(PlanOf(sim, "bakery").Raw, config.ForecastMinPlan.Raw);
            config.ForecastHouseholdWeight = Fx.Zero;
            Plan(sim);
            Assert.AreEqual(config.ForecastMinPlan, PlanOf(sim, "bakery"), "by sales alone an empty market reads as no demand");
        }

        [Test]
        public void Plan_CarriesDemandUpTheChain_ToRawGoods()
        {
            using var sim = AtQuarter();
            var config = sim.Database.Config;
            Plan(sim);
            Assert.Greater(PlanOf(sim, "mill").Raw, config.ForecastMinPlan.Raw, "flour for the bread households want");
            Assert.Greater(PlanOf(sim, "wheat_farm").Raw, config.ForecastMinPlan.Raw, "wheat for that flour");
            Assert.Greater(PlanOf(sim, "barley_farm").Raw, config.ForecastMinPlan.Raw);
            Assert.Greater(PlanOf(sim, "iron_mine").Raw, config.ForecastMinPlan.Raw, "ore for the tools everyone works with");
        }

        [Test]
        public void Plan_StockAtTheMarket_CutsItToTheLeast()
        {
            using var sim = AtQuarter();
            var config = sim.Database.Config;
            Stock(sim, "wheat", QualityGrade.Mid, 10_000_000);
            Plan(sim);
            Assert.AreEqual(config.ForecastMinPlan, PlanOf(sim, "wheat_farm"));
            Assert.Greater(PlanOf(sim, "mill").Raw, config.ForecastMinPlan.Raw, "the mills still have flour to make");
        }

        [Test]
        public void Plan_StockElsewhere_DoesNotStopMakersServingTheirOwnMarket()
        {
            using var sim = AtQuarter();
            var config = sim.Database.Config;
            config.ForecastCover = Fx.Zero;
            var markets = sim.World.Get<MarketStore>();
            var forecast = sim.World.Get<DemandForecast>();
            var cells = PlanCells(sim, "dairy");
            Assert.Greater(cells.Length, 1);
            Plan(sim);
            long before = forecast.Plan[cells[1]];
            // The whole output of the first market's dairies is in stock.
            int glut = cells[0], node = Enumerable.Range(0, sim.World.Get<NodeTable>().Count).First(n => glut >= markets.MarketCellStart[n] && glut < markets.MarketCellStart[n] + markets.MarketCellCount[n]);
            foreach (QualityGrade grade in System.Enum.GetValues(typeof(QualityGrade)))
            {
                Assert.IsTrue(sim.Database.TryGetGood("cheese", grade, out var cheese));
                if (markets.TryFindCell(node, cheese, out int c)) markets.Stock[c] = 10_000_000;
            }
            Plan(sim);
            Assert.Less(forecast.Plan[glut], forecast.Plan[cells[1]]);
            Assert.Greater(forecast.Plan[cells[1]], config.ForecastMinPlan.Raw, "its own market still wants cheese");
            Assert.LessOrEqual(forecast.Plan[cells[1]], before, "no more than before: others hold plenty for the nation");
        }

        [Test]
        public void Plan_DemandOfMarketsWithoutMakers_FallsToMakersWithCapacityToSpare()
        {
            using var sim = AtQuarter();
            var config = sim.Database.Config;
            config.ForecastCover = Fx.Zero;
            var nodes = sim.World.Get<NodeTable>();
            var population = sim.World.Get<HouseholdState>().Population;
            Plan(sim);
            var before = PlanOf(sim, "dairy");
            Assert.Less(before.Raw, Fx.One.Raw);
            for (int n = 0; n < nodes.Count; n++)
                if (nodes.IsSeat(n)) population[n] *= 20;
            Plan(sim);
            Assert.Greater(PlanOf(sim, "dairy").Raw, before.Raw);
        }

        [Test]
        public void Investors_DoNotBuild_ForGoodsWhoseMakersRunFarBelowCapacity()
        {
            using var sim = TestWorlds.BuildSample();
            var config = sim.Database.Config;
            config.InvestmentMinPlan = Fx.FromPermille(800);
            var facilities = sim.World.Get<FacilityTable>();
            var groups = sim.World.Get<GroupTable>();
            var forecast = sim.World.Get<DemandForecast>();
            while (!sim.Context.Clock.IsNewYear || sim.Context.Turn == 0) sim.AdvanceTurn();
            for (int g = 0; g < groups.Count; g++) groups.Cash[g] += 1_000_000;
            for (int c = 0; c < forecast.Plan.Length; c++) forecast.Plan[c] = config.ForecastMinPlan.Raw;
            var investment = sim.Systems.OfType<InvestmentSystem>().Single();
            int count = facilities.Count;
            long levels = Enumerable.Range(0, count).Sum(f => (long)facilities.Level[f]);
            investment.Execute(sim.Context);
            Assert.AreEqual(count, facilities.Count);
            Assert.AreEqual(levels, Enumerable.Range(0, count).Sum(f => (long)facilities.Level[f]));
            for (int c = 0; c < forecast.Plan.Length; c++) forecast.Plan[c] = Fx.OneRaw;
            investment.Execute(sim.Context);
            Assert.Greater(facilities.Count + Enumerable.Range(0, facilities.Count).Sum(f => (long)facilities.Level[f]), count + levels);
        }

        [Test]
        public void Plan_LeavesPrimaryProducersRunning_WhenTheyDoNotPlan()
        {
            using var sim = TestWorlds.BuildSample();
            sim.Database.Config.ForecastPrimary = false;
            for (int t = 0; t <= OrderSystem.QuarterWeeks; t++) sim.AdvanceTurn();
            Assert.AreEqual(Fx.One, PlanOf(sim, "wheat_farm"));
        }

        [Test]
        public void Plan_AddsOpenOrders_FromAnywhereInTheNation()
        {
            using var sim = AtQuarter();
            var config = sim.Database.Config;
            Assert.IsTrue(sim.Database.TryGetGood("cart", QualityGrade.Mid, out var cart));
            Plan(sim);
            Assert.AreEqual(config.ForecastMinPlan, PlanOf(sim, "cartwright"));
            var facilities = sim.World.Get<FacilityTable>();
            int f = Enumerable.Range(0, facilities.Count).First(i => sim.Database.Facilities[facilities.DefIndex[i]].Key == "cartwright");
            var markets = sim.World.Get<MarketStore>();
            var purses = sim.World.Get<PurseState>();
            int nation = purses.NodeFloat[facilities.Node[f]];
            int orderNode = purses.NationNodes[nation].First(n => markets.TryFindCell(n, cart, out _));
            markets.TryFindCell(orderNode, cart, out int cell);
            sim.World.Get<OrderTable>().Add(-1, orderNode, cell, 100_000, sim.Context.Turn, 0);
            Plan(sim);
            Assert.AreEqual(Fx.One, PlanOf(sim, "cartwright"));
        }

        [Test]
        public void Ale_IsASemiNecessity_CloserToLuxuryFromMid()
        {
            using var sim = TestWorlds.BuildSample();
            var db = sim.Database;
            Fx Share(string good, QualityGrade grade)
            {
                Assert.IsTrue(db.TryGetGood(good, grade, out var id));
                return db.Config.LuxuryShare(db.Good(id));
            }
            var half = Fx.FromPermille(500);
            Assert.Less(Share("ale", QualityGrade.Low).Raw, half.Raw);
            Assert.Greater(Share("ale", QualityGrade.Mid).Raw, half.Raw);
            Assert.Greater(Share("ale", QualityGrade.High).Raw, Share("ale", QualityGrade.Mid).Raw);
            Assert.AreEqual(Fx.One, Share("ale", QualityGrade.Best));
            Assert.AreEqual(Fx.Zero, Share("bread", QualityGrade.Mid), "bread stays a necessity");
            Assert.AreEqual(Fx.One, Share("linen", QualityGrade.Low), "linen stays a luxury");

            int demand = 0;
            foreach (QualityGrade grade in System.Enum.GetValues(typeof(QualityGrade)))
            {
                Assert.IsTrue(db.TryGetGood("ale", grade, out var id));
                demand += db.Good(id).BaseDemand;
            }
            Assert.AreEqual(25, demand, "demand splits by grade");
            var brewery = db.Facilities.First(f => f.Key == "brewery");
            Assert.Greater(brewery.Recipe.Outputs.Length, 1, "breweries make more than one grade");
        }

        [Test]
        public void Population_HoldsAtFullSupply_InTownAndVillage()
        {
            using var sim = TestWorlds.BuildSample();
            sim.Database.Config.MigrationMaxShare = Fx.Zero;
            var households = sim.World.Get<HouseholdState>();
            var nodes = sim.World.Get<NodeTable>();
            int town = Enumerable.Range(0, nodes.Count).First(n => nodes.LevelOf(n) == WorldLevel.City);
            int village = Enumerable.Range(0, nodes.Count).First(n => nodes.LevelOf(n) == WorldLevel.Village);
            int shortOf = Enumerable.Range(0, nodes.Count).Last(n => nodes.LevelOf(n) == WorldLevel.City);
            int plenty = Enumerable.Range(0, nodes.Count).Last(n => nodes.LevelOf(n) == WorldLevel.Village);
            while (!sim.Context.Clock.IsNewYear || sim.Context.Turn == 0) sim.AdvanceTurn();
            long townBefore = households.Population[town], villageBefore = households.Population[village];
            long shortBefore = households.Population[shortOf], plentyBefore = households.Population[plenty];
            void Supply(int node, long met)
            {
                households.NecessityWanted[node] = 1000;
                households.NecessityMet[node] = met;
            }
            Supply(town, 1000);
            Supply(village, 1000);
            Supply(shortOf, 900);
            Supply(plenty, 1100);
            sim.Systems.OfType<HouseholdGrowthSystem>().Single().Execute(sim.Context);
            Assert.AreEqual(townBefore, households.Population[town]);
            Assert.AreEqual(villageBefore, households.Population[village]);
            Assert.Less(households.Population[shortOf], shortBefore);
            Assert.Greater(households.Population[plenty], plentyBefore);
        }
    }
}
