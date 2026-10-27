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
    public class DemandTests
    {
        const string Village = "aur_n1_r1_c1_v2"; // mountain: ore is its specialty, grain and textiles are not
        const string OtherVillage = "aur_n1_r1_c1_v1";

        static NodeId Node(GameSimulation sim, string key)
        {
            Assert.IsTrue(sim.View.TryFindNode(key, out var node), key);
            return node;
        }

        static GoodId Good(GameSimulation sim, string key, QualityGrade grade = QualityGrade.Mid)
        {
            Assert.IsTrue(sim.Database.TryGetGood(key, grade, out var good), key);
            return good;
        }

        /// <summary>Weekly consumption of 100 units at the anchor, priced at <paramref name="price"/> against an anchor of 100.</summary>
        static long Weekly(SimulationConfig config, int price, long luxuryShare, long necessityWeight = 0x10000, long luxuryWeight = 0x10000)
        {
            long weekly = HouseholdDemand.Weekly(DemandParams.From(config), 100, price, 100, luxuryShare, necessityWeight, luxuryWeight,
                out _, out _);
            return weekly;
        }

        [Test]
        public void DemandCurve_FallsAboveTheAnchor_ToNothingAtTheChokePrice()
        {
            long half = Fx.FromPermille(500).Raw, max = Fx.FromInt(2).Raw;
            Assert.AreEqual(Fx.One.Raw, DemandCurve.Factor(half, 0, max));
            Assert.AreEqual(half, DemandCurve.Factor(half, Fx.One.Raw, max)); // twice the anchor
            Assert.AreEqual(0, DemandCurve.Factor(half, Fx.FromInt(2).Raw, max)); // three times: choke
            Assert.AreEqual(0, DemandCurve.Factor(half, Fx.FromInt(50).Raw, max));
            Assert.AreEqual(Fx.FromPermille(1250).Raw, DemandCurve.Factor(half, -half, max)); // half price
            Assert.AreEqual(max, DemandCurve.Factor(Fx.FromInt(5).Raw, -half, max));

            // Permille shares are not exact in Q16, so allow a few raw units.
            Assert.AreEqual(Fx.One.Raw, DemandCurve.QuintileSensitivity(Fx.FromPermille(200)).Raw, 16);
            Assert.AreEqual(Fx.FromPermille(500).Raw, DemandCurve.QuintileSensitivity(Fx.FromPermille(800)).Raw, 16);
            Assert.AreEqual(Fx.FromInt(2).Raw, DemandCurve.QuintileSensitivity(Fx.FromPermille(50)).Raw, 16);
        }

        [Test]
        public void Goods_AreNecessitiesOrLuxuries()
        {
            using var sim = TestWorlds.BuildSample();
            Assert.IsFalse(sim.Database.Good(Good(sim, "wheat")).IsLuxury);
            Assert.IsTrue(sim.Database.Good(Good(sim, "linen")).IsLuxury);
            var bad = TestWorlds.SampleWith("goods.csv",
                "key,category,base_price,base_demand,class\nwheat,grain,10,40,staple\n");
            Assert.Throws<System.FormatException>(() => SimulationBuilder.FromData(bad, 1).Build().Dispose());
        }

        [Test]
        public void Consumption_AtTheAnchor_IsTheBaseDemand_AndNothingAboveTheChoke()
        {
            var config = SimulationConfig.Defaults();
            long necessity = 0, luxury = Fx.One.Raw;
            Assert.AreEqual(100, Weekly(config, 100, necessity));
            Assert.AreEqual(100, Weekly(config, 100, luxury));
            Assert.AreEqual(0, Weekly(config, 10_000, necessity));
            Assert.AreEqual(0, Weekly(config, 10_000, luxury));
            // Luxuries fall away sooner: at twice the anchor a luxury is gone, a necessity is not.
            Assert.AreEqual(0, Weekly(config, 200, luxury));
            Assert.Greater(Weekly(config, 200, necessity), 0);
            // Cheaper than the anchor, households buy more, up to the cap.
            Assert.Greater(Weekly(config, 80, necessity), 100);
            Assert.AreEqual(config.MaxCheapDemand.MulInt(100), Weekly(config, 1, luxury));
        }

        [Test]
        public void HigherGrades_OfANecessity_AreBoughtLikeLuxuries()
        {
            var config = SimulationConfig.Defaults();
            Assert.AreEqual(Fx.Zero, config.GradeLuxuryShare[(int)QualityGrade.Mid]);
            Assert.Greater(config.GradeLuxuryShare[(int)QualityGrade.High].Raw, 0);
            Assert.AreEqual(Fx.One, config.GradeLuxuryShare[(int)QualityGrade.Best]);
            // A half-luxury: twice the income adds half again, twice the people add the other half.
            long half = config.GradeLuxuryShare[(int)QualityGrade.High].Raw;
            Assert.AreEqual(150, Weekly(config, 100, half, luxuryWeight: 2 * 0x10000));
            Assert.AreEqual(150, Weekly(config, 100, half, necessityWeight: 2 * 0x10000));
        }

        [Test]
        public void MapPopulation_ScalesNecessityDemand()
        {
            var nodes = TestWorlds.Sample().ReadText("map_nodes.csv").Split('\n')
                .Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            nodes[0] += ",population";
            for (int i = 1; i < nodes.Count; i++) nodes[i] += nodes[i].StartsWith(Village + ",") ? ",1000" : ",";
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleWith("map_nodes.csv", string.Join("\n", nodes) + "\n"), 42).Build();
            using var plain = TestWorlds.BuildSample();
            Assert.AreEqual(1000, sim.View.Population(Node(sim, Village)));
            Assert.AreEqual(sim.Database.Config.DefaultPopulation[(int)WorldLevel.Village], sim.View.Population(Node(sim, OtherVillage)));

            sim.AdvanceTurn();
            plain.AdvanceTurn();
            int village = Node(sim, Village).Value;
            long wanted = plain.World.Get<HouseholdState>().NecessityWanted[village];
            Assert.Greater(wanted, 0);
            Assert.AreEqual(2 * wanted, sim.World.Get<HouseholdState>().NecessityWanted[village]);
        }

        [Test]
        public void UnsuppliedGoods_StayBelowTheirChokePrice()
        {
            using var sim = TestWorlds.BuildSample();
            var markets = sim.World.Get<MarketStore>();
            for (int t = 0; t < 3 * SimClock.WeeksPerYear; t++) sim.AdvanceTurn();
            var index = sim.View.PriceIndex(0);
            for (int c = 0; c < markets.CellCount; c++)
            {
                long anchorBound = index.MulInt(20L * markets.GoodBasePrice[markets.CellGood[c]]);
                Assert.Less(markets.Price[c], anchorBound, sim.Database.Goods[markets.CellGood[c]].Key);
            }
        }

        [Test]
        public void Households_ChangeYearly_WithAffordability_AndRealMoneyGrowth()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile,
                "key,value\nfinance.event_chance_permille,0\n"), 42).Build();
            var config = sim.Database.Config;
            var households = sim.World.Get<HouseholdState>();
            while (sim.Turn < 2 * SimClock.WeeksPerYear) sim.AdvanceTurn();

            int starved = Node(sim, Village).Value, fed = Node(sim, OtherVillage).Value;
            long starvedPop = households.Population[starved], fedPop = households.Population[fed];
            var city = Node(sim, "aur_n1_r1_c1");
            var income = sim.View.IncomePerHead(city);
            // Overwhelm the week's own consumption: one node could afford nothing, the other twice its need.
            households.NecessityWanted[starved] = 1L << 40;
            households.NecessityMet[starved] = 0;
            households.NecessityWanted[fed] = 1L << 40;
            households.NecessityMet[fed] = 1L << 41;

            var finance = sim.World.Get<FinanceState>();
            var latest = finance.Records[finance.Records.Count - 1];
            var before = finance.Records.First(r => r.Turn == latest.Turn - SimClock.WeeksPerYear);
            int nation = sim.World.Get<NodeBanks>().NationIndex(sim.World.Get<NodeTable>().AncestorAt(city.Value, WorldLevel.Nation));
            var real = Fx.FromRatio(latest.NationTotal[nation], before.NationTotal[nation])
                / Fx.FromRatio(latest.PriceIndex[0], before.PriceIndex[0]) - Fx.One;
            var change = Fx.Clamp(config.IncomeResponse * real, -config.IncomeMaxChange, config.IncomeMaxChange);

            sim.AdvanceTurn();
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<HouseholdsUpdated>().Count());
            Assert.AreEqual(starvedPop - config.PopulationMaxChange.MulInt(starvedPop), households.Population[starved]);
            Assert.AreEqual(fedPop + config.PopulationMaxChange.MulInt(fedPop), households.Population[fed]);
            Assert.AreEqual(income + income * change, sim.View.IncomePerHead(city));
            Assert.AreEqual(0, households.NecessityWanted[starved]);
        }
    }
}
