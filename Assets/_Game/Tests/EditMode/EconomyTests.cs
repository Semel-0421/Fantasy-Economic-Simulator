using FES.Core;
using FES.Data;
using FES.Economy;
using FES.Simulation;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    public class EconomyTests
    {
        static PriceParams Params => new PriceParams
        {
            AdjustRaw = Fx.FromPermille(250).Raw,
            MaxStepRaw = Fx.FromPermille(150).Raw,
            AnchorRaw = 0,
            StockPressureRaw = Fx.FromPermille(250).Raw,
            MinPrice = 1,
        };

        static int StepPrice(int supply, int demand, int stock = 0, int price = 100, int elapsed = 1)
        {
            int prev = 0;
            PriceKernel.Step(Params, 100, Fx.OneRaw, elapsed, ref price, ref prev, ref stock, ref supply, ref demand);
            return price;
        }

        [Test]
        public void PriceKernel_IsSymmetric_AndScalesWithElapsedTurns()
        {
            // Same pressure up and down must move the price by the same amount (no downward ratchet).
            int up = StepPrice(supply: 10, demand: 13, price: 20) - 20;
            int down = 20 - StepPrice(supply: 13, demand: 10, price: 20);
            Assert.AreEqual(up, down);
            Assert.Greater(StepPrice(supply: 10, demand: 30, elapsed: 4), StepPrice(supply: 10, demand: 30, elapsed: 1));
        }

        [Test]
        public void PriceKernel_AnchorPullsCheapGoodsBackWithoutDeadBand()
        {
            var p = Params;
            p.AnchorRaw = Fx.FromPermille(20).Raw;
            int price = 70, prev = 0, stock = 0, supply = 0, demand = 0;
            PriceKernel.Step(p, 100, Fx.OneRaw, 1, ref price, ref prev, ref stock, ref supply, ref demand);
            Assert.Greater(price, 70);
        }

        [Test]
        public void PriceKernel_CoinBackToTheAnchor_DoesNotHideAShortageOrAGlut()
        {
            var p = Params;
            p.AnchorRaw = Fx.FromPermille(20).Raw;
            int price = 8, prev = 0, stock = 0, supply = 0, demand = 100;
            PriceKernel.Step(p, 7, Fx.OneRaw, 1, ref price, ref prev, ref stock, ref supply, ref demand);
            Assert.Greater(price, 8, "at this price the step rounds to one coin; the pull must not cancel it");
            price = 5;
            supply = 100;
            demand = 0;
            PriceKernel.Step(p, 7, Fx.OneRaw, 1, ref price, ref prev, ref stock, ref supply, ref demand);
            Assert.Less(price, 5);
        }

        [Test]
        public void PriceKernel_MovesWithPressure_AndClampsStep()
        {
            Assert.Greater(StepPrice(supply: 10, demand: 30), 100);
            Assert.Less(StepPrice(supply: 30, demand: 10), 100);
            Assert.AreEqual(100, StepPrice(supply: 20, demand: 20));
            var maxStep = Fx.FromPermille(150);
            Assert.AreEqual(100 + maxStep.MulInt(100), StepPrice(supply: 0, demand: 1000)); // capped at +15%
            Assert.AreEqual(100 + (-maxStep).MulInt(100), StepPrice(supply: 1000, demand: 0));
        }

        [Test]
        public void PriceKernel_ClearsAccumulators_AndCarriesUnsoldStock()
        {
            int price = 100, prev = 0, stock = 5, supply = 20, demand = 15;
            PriceKernel.Step(Params, 100, Fx.OneRaw, 1, ref price, ref prev, ref stock, ref supply, ref demand);
            Assert.AreEqual(100, prev);
            Assert.AreEqual(10, stock); // 20 + 5 available, 15 sold
            Assert.AreEqual(0, supply);
            Assert.AreEqual(0, demand);
        }

        [Test]
        public void MarketLayout_IsSparsePerLevelProfile()
        {
            using var sim = TestWorlds.BuildSample();
            var nodes = sim.World.Get<NodeTable>();
            var markets = sim.World.Get<MarketStore>();
            var db = sim.Database;

            int village = nodes.LevelStart(WorldLevel.Village);
            int city = nodes.LevelStart(WorldLevel.City);
            int region = nodes.LevelStart(WorldLevel.Region);
            Assert.AreEqual(16 * 2, markets.MarketCellCount[village]);  // 16 goods, without tools, x low/mid
            Assert.AreEqual(27 * 3, markets.MarketCellCount[city]);     // all goods x low/mid/high
            Assert.AreEqual(27 * 4, markets.MarketCellCount[region]);   // everything

            db.TryGetGood("iron_tools", QualityGrade.Mid, out var tools);
            db.TryGetGood("wheat", QualityGrade.Low, out var wheat);
            Assert.IsFalse(markets.Carries(village, tools));
            Assert.IsTrue(markets.TryFindCell(village, wheat, out int cell));
            Assert.AreEqual(wheat.Value, markets.CellGood[cell]);
        }

        [Test]
        public void Pricing_BurstJob_MatchesManagedExecution()
        {
            using var burst = TestWorlds.BuildSample(seed: 9);
            using var managed = TestWorlds.BuildSample(seed: 9);
            foreach (var s in managed.Systems)
                if (s is PricingSystem p) p.ForceManaged = true;

            for (int t = 0; t < 20; t++)
            {
                burst.AdvanceTurn();
                managed.AdvanceTurn();
            }
            Assert.AreEqual(managed.ComputeHash(), burst.ComputeHash());
        }

        [Test]
        public void Production_BurstJob_MatchesManagedExecution()
        {
            using var burst = TestWorlds.BuildSample(seed: 9);
            using var managed = TestWorlds.BuildSample(seed: 9);
            foreach (var s in managed.Systems)
                if (s is FES.Agents.ProductionSystem p) p.ForceManaged = true;

            for (int t = 0; t < 20; t++)
            {
                burst.AdvanceTurn();
                managed.AdvanceTurn();
            }
            Assert.AreEqual(managed.ComputeHash(), burst.ComputeHash());
        }

        [Test]
        public void Prices_MoveOverTime_AndStayPositive()
        {
            using var sim = TestWorlds.BuildSample();
            var markets = sim.World.Get<MarketStore>();
            for (int t = 0; t < 20; t++) sim.AdvanceTurn();

            int changed = 0;
            for (int c = 0; c < markets.CellCount; c++)
            {
                Assert.GreaterOrEqual(markets.Price[c], 1);
                if (markets.Price[c] != markets.GoodBasePrice[markets.CellGood[c]]) changed++;
            }
            Assert.Greater(changed, markets.CellCount / 3); // made-to-order goods wait for orders
        }

        [Test]
        public void Currency_MintsYearly_AndRatesDiverge()
        {
            using var sim = TestWorlds.BuildSample();
            var currency = sim.World.Get<CurrencyState>();
            long gold0 = currency.GoldReserve[0];
            long coins0 = currency.CoinSupply[0];
            for (int t = 0; t <= SimClock.WeeksPerYear; t++) sim.AdvanceTurn();
            Assert.Less(currency.GoldReserve[0], gold0);
            Assert.Greater(currency.CoinSupply[0], coins0);

            // Prices follow the published money total (DEC-062): the first year's coins count from the
            // second year's statistics on.
            while (sim.Turn <= 2 * SimClock.WeeksPerYear + 1) sim.AdvanceTurn(); // published at the new year, priced the turn after
            Assert.AreEqual(Fx.FromRatio(currency.MoneyTotal[0], currency.BaselineMoneyTotal[0]), currency.PriceIndex[0]);
            Assert.AreEqual(sim.World.Get<FES.Agents.FinanceState>().PublishedContinentTotal[0], currency.MoneyTotal[0]);
            Assert.Greater(currency.PriceIndex[0].Raw, Fx.OneRaw);
            Assert.AreNotEqual(currency.ExchangeRate[0], currency.ExchangeRate[1]);
        }

        const string EventsHeader = "key,level,category,target,mul_permille,duration,chance_permille\n";

        [Test]
        public void WorldEvents_AddModifiersThatExpire()
        {
            var data = TestWorlds.SampleWith(GameDatabase.EventsFile, EventsHeader + "quake,Continent,*,Supply,500,2,1000\n");
            using var sim = SimulationBuilder.FromData(data, 1).Build();
            var modifiers = sim.World.Get<MarketModifierTable>();
            sim.AdvanceTurn();
            Assert.AreEqual(2, modifiers.Active.Count); // one per continent
            sim.AdvanceTurn();
            Assert.AreEqual(4, modifiers.Active.Count);
            sim.AdvanceTurn(); // first wave expires at turn 2
            Assert.AreEqual(4, modifiers.Active.Count);
        }

        [Test]
        public void ShortDemandEvent_ReachesSlowTierMarkets()
        {
            // A one-turn event on a continent that only reprices every 4 turns must still change its flows.
            var data = TestWorlds.SampleWith(GameDatabase.EventsFile, EventsHeader + "boom,Continent,*,Demand,3000,1,1000\n");
            using var boom = SimulationBuilder.FromData(data, 1).Build();
            using var calm = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.EventsFile, EventsHeader), 1).Build();
            for (int t = 0; t < 2; t++)
            {
                boom.AdvanceTurn();
                calm.AdvanceTurn();
            }
            int continent = boom.World.Get<NodeTable>().LevelStart(WorldLevel.Continent);
            Assert.IsTrue(boom.Database.TryGetGood("bread", QualityGrade.Mid, out var bread));
            Assert.IsTrue(boom.World.Get<MarketStore>().TryFindCell(continent, bread, out int cell));
            Assert.Greater(boom.World.Get<MarketStore>().Demand[cell], calm.World.Get<MarketStore>().Demand[cell]);
        }

        [Test]
        public void ValueEvent_ShorterThanRepricePeriod_IsRejected()
        {
            var data = TestWorlds.SampleWith(GameDatabase.EventsFile, EventsHeader + "fad,Nation,*,Value,1500,1,10\n");
            Assert.Throws<System.FormatException>(() => GameDatabase.Load(data));
        }
    }
}
