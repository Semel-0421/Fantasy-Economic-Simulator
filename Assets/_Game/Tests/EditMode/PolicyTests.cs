using System.IO;
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
    public class PolicyTests
    {
        const string City = "aur_n1_r1_c1";
        const string OtherNationCity = "bor_n1_r1_c1";

        static GameSimulation Build() => SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile,
            "key,value\nfinance.event_chance_permille,0\nmarket.credit_share_permille,0\nmarket.momentum_permille,0\n"), 42).Build();

        static NodeId Node(GameSimulation sim, string key)
        {
            Assert.IsTrue(sim.View.TryFindNode(key, out var node), key);
            return node;
        }

        static NodeId NationOf(GameSimulation sim, string key) =>
            new NodeId(sim.World.Get<NodeTable>().AncestorAt(Node(sim, key).Value, WorldLevel.Nation));

        static PlayerState PlaceAt(GameSimulation sim, string node, long coins)
        {
            var player = sim.World.Get<PlayerState>();
            System.Array.Clear(player.Coins, 0, player.Coins.Length);
            player.Node = Node(sim, node).Value;
            player.LocalCoins = coins;
            return player;
        }

        static int Rejected(GameSimulation sim) => sim.LastTurnEvents.OfType<CommandRejected>().Count();

        static void ToFirstReview(GameSimulation sim)
        {
            while (sim.Turn <= SimClock.WeeksPerYear) sim.AdvanceTurn();
        }

        static GoodId Good(GameSimulation sim, string key)
        {
            Assert.IsTrue(sim.Database.TryGetGood(key, QualityGrade.Mid, out var good), key);
            return good;
        }

        [Test]
        public void Review_RunsYearly_AndSetsTollRatesInTheNation()
        {
            using var sim = Build();
            var tolls = sim.World.Get<NodeTolls>();
            int city = Node(sim, City).Value;
            long baseTariff = tolls.Tariff[city];
            ToFirstReview(sim);
            var reviews = sim.LastTurnEvents.OfType<PolicyReviewed>().ToList();
            Assert.AreEqual(sim.World.Get<NodeBanks>().NationCount, reviews.Count);
            var review = reviews.Single(r => r.Nation == NationOf(sim, City));
            Assert.AreEqual(0, review.Year);
            Assert.AreEqual(review.TollStep, sim.View.TollStep(NationOf(sim, City)));
            Assert.AreEqual(PolicyRules.Scale(sim.Database.Config, review.TollStep).MulInt(baseTariff), tolls.Tariff[city]);
            Assert.Greater(sim.View.PolicyInfluence(NationOf(sim, City)), 0);

            // Nothing moves between reviews.
            int step = review.TollStep;
            for (int t = 0; t < 10; t++) sim.AdvanceTurn();
            Assert.IsEmpty(sim.LastTurnEvents.OfType<PolicyReviewed>());
            Assert.AreEqual(step, sim.View.TollStep(NationOf(sim, City)));
        }

        [Test]
        public void Lobbying_IsDoneInTheCompanysName_InsideTheNation()
        {
            using var sim = Build();
            var nation = NationOf(sim, City);
            PlaceAt(sim, City, 10_000);
            sim.Actions.Lobby(nation, PolicyKind.Tolls, 1, 100); // no company yet
            sim.AdvanceTurn();
            Assert.AreEqual(1, Rejected(sim));

            sim.Actions.FoundCompany(Node(sim, City), GuidanceAxis.Trade, 0);
            sim.AdvanceTurn();
            sim.Actions.Lobby(nation, PolicyKind.Tolls, 1, 100, baseGood: 0); // a good only for licensing
            sim.Actions.Lobby(nation, PolicyKind.LicensedGoods, 1, 100); // licensing needs a good
            sim.Actions.Lobby(NationOf(sim, OtherNationCity), PolicyKind.Tolls, 1, 100); // another nation
            sim.Actions.Lobby(nation, PolicyKind.Tolls, 2, 100);
            sim.AdvanceTurn();
            Assert.AreEqual(4, Rejected(sim));

            long reserve = sim.View.BankReserve(nation);
            sim.Actions.Lobby(nation, PolicyKind.Tolls, 1, 400);
            sim.AdvanceTurn();
            Assert.AreEqual(0, Rejected(sim));
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<Lobbied>().Count());
            Assert.AreEqual(10_000 - 400, sim.World.Get<PlayerState>().LocalCoins);
            Assert.GreaterOrEqual(sim.View.BankReserve(nation), reserve + 400 - 1_000); // the week's NPC loans aside
            Assert.AreEqual(1, sim.World.Get<PolicyState>().Lobbying.Count);
        }

        [Test]
        public void HeavyLobbying_TipsTheReview()
        {
            using var calm = Build();
            using var lobbied = Build();
            var nation = NationOf(lobbied, City);
            PlaceAt(lobbied, City, 100_000_000);
            lobbied.Actions.FoundCompany(Node(lobbied, City), GuidanceAxis.Trade, 0);
            lobbied.AdvanceTurn();
            calm.AdvanceTurn();
            var wheat = Good(lobbied, "wheat");
            lobbied.Actions.Lobby(nation, PolicyKind.Tolls, 1, 10_000_000);
            lobbied.Actions.Lobby(nation, PolicyKind.LicensedGoods, 1, 10_000_000, wheat.BaseIndex);
            ToFirstReview(calm);
            ToFirstReview(lobbied);

            Assert.AreEqual(1, lobbied.View.TollStep(nation));
            Assert.Less(calm.View.TollStep(nation), 1);
            Assert.IsTrue(lobbied.View.IsLicensed(nation, wheat.BaseIndex));
            Assert.IsEmpty(lobbied.World.Get<PolicyState>().Lobbying); // spent for the year

            // Wheat now needs a license in that nation only; the player can buy one there.
            PlaceAt(lobbied, City, 10_000);
            Assert.IsFalse(lobbied.View.MaySell(wheat));
            PlaceAt(lobbied, OtherNationCity, 10_000);
            Assert.AreEqual(lobbied.View.IsLicensed(NationOf(lobbied, OtherNationCity), wheat.BaseIndex), !lobbied.View.MaySell(wheat));
            PlaceAt(lobbied, City, 10_000);
            lobbied.Actions.BuyLicense(wheat);
            lobbied.AdvanceTurn();
            Assert.AreEqual(0, Rejected(lobbied));
            Assert.IsTrue(lobbied.View.MaySell(wheat));
        }

        [Test]
        public void LicensePrices_FollowTheNationsStep()
        {
            using var sim = Build();
            var tools = Good(sim, "iron_tools");
            PlaceAt(sim, City, 0);
            long price = sim.View.LicensePrice(tools);
            sim.World.Get<PolicyState>().LicensePriceStep[sim.World.Get<NodeBanks>().NationIndex(NationOf(sim, City).Value)] = 2;
            Assert.AreEqual(PolicyRules.Scale(sim.Database.Config, 2).MulInt(price), sim.View.LicensePrice(tools));
        }

        [Test]
        public void MintingPolicy_SetsTheContinentsRate()
        {
            using var sim = Build();
            ToFirstReview(sim);
            var currency = sim.World.Get<CurrencyState>();
            for (int c = 0; c < currency.ContinentCount; c++)
                Assert.AreEqual(PolicyRules.Scale(sim.Database.Config, sim.View.MintStep(c)), currency.MintRateMultiplier[c]);
            // State enterprises are the only side on minting, and they push it up (DEC-059).
            Assert.GreaterOrEqual(sim.View.MintStep(0), 0);
        }

        [Test]
        public void Inflation_TurnsOtherGroupsAgainstMinting()
        {
            using var opposed = Build();
            using var indifferent = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile,
                "key,value\nfinance.event_chance_permille,0\npolicy.inflation_opposition_permille,1000000000\n"), 42).Build();
            while (opposed.Turn <= 3 * SimClock.WeeksPerYear)
            {
                opposed.AdvanceTurn();
                indifferent.AdvanceTurn();
            }
            // The first year's minting raised prices from the second year's statistics on (DEC-062); the
            // third review weighs that (DEC-060).
            Assert.Greater(opposed.View.PublishedContinent(0).Inflation.Raw, 0);
            Assert.Less(opposed.View.MintStep(0), indifferent.View.MintStep(0));
        }

        [Test]
        public void StatusQuoBias_StopsPoliciesShortOfTheLimit()
        {
            using var sim = Build();
            for (int y = 0; y < 6; y++)
                for (int t = 0; t < SimClock.WeeksPerYear; t++) sim.AdvanceTurn();
            int max = sim.Database.Config.PolicyMaxSteps;
            var banks = sim.World.Get<NodeBanks>();
            for (int n = 0; n < banks.NationCount; n++)
            {
                var nation = new NodeId(banks.NationNode(n));
                Assert.Less(System.Math.Abs(sim.View.TollStep(nation)), max);
                Assert.Less(System.Math.Abs(sim.View.LicensePriceStep(nation)), max);
            }
        }

        [Test]
        public void TollPolicy_SurvivesSaveAndLoad()
        {
            using var sim = Build();
            ToFirstReview(sim);
            int city = Node(sim, City).Value;
            using var buffer = new MemoryStream();
            sim.Save(buffer);
            using var copy = Build();
            buffer.Position = 0;
            copy.Load(buffer);
            Assert.AreEqual(sim.World.Get<NodeTolls>().Tariff[city], copy.World.Get<NodeTolls>().Tariff[city]);
            Assert.AreEqual(sim.View.TollStep(NationOf(sim, City)), copy.View.TollStep(NationOf(copy, City)));
            for (int t = 0; t < 5; t++)
            {
                sim.AdvanceTurn();
                copy.AdvanceTurn();
            }
            Assert.AreEqual(sim.ComputeHash(), copy.ComputeHash());
        }

        [Test]
        public void CurrencyStateV1Save_IsMigrated()
        {
            using var sim = Build();
            var currency = sim.World.Get<CurrencyState>();
            using var full = new MemoryStream();
            using (var writer = new BinaryWriter(full, System.Text.Encoding.UTF8, true)) currency.Write(writer);
            // Version 1 ended before the minting multipliers.
            var v1 = full.ToArray().Take((int)full.Length - currency.ContinentCount * sizeof(long)).ToArray();
            var migrated = sim.Migrations.Upgrade(CurrencyState.SectionKey, 1, 2, v1);
            var copy = new CurrencyState(currency.ContinentCount);
            copy.Read(new BinaryReader(new MemoryStream(migrated)));
            Assert.AreEqual(currency.CoinSupply[0], copy.CoinSupply[0]);
            Assert.AreEqual(Fx.One, copy.MintRateMultiplier[0]);
        }
    }
}
