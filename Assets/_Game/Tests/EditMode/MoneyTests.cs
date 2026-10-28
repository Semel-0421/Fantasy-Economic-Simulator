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
    /// <summary>The money loop is closed (DEC-061): a continent's money changes only when coins are minted.</summary>
    public class MoneyTests
    {
        const string City = "aur_n1_r1_c1"; // a branch and the international bank

        static NodeId Node(GameSimulation sim, string key)
        {
            Assert.IsTrue(sim.View.TryFindNode(key, out var node), key);
            return node;
        }

        static void AssertConserved(GameSimulation sim, string when)
        {
            var currency = sim.World.Get<CurrencyState>();
            for (int c = 0; c < currency.ContinentCount; c++)
                Assert.AreEqual(currency.CoinSupply[c], MoneyLedger.ContinentMoney(sim.World, c), $"continent {c}, {when}");
        }

        static void Step(GameSimulation sim, string what)
        {
            sim.AdvanceTurn();
            Assert.IsFalse(sim.LastTurnEvents.OfType<CommandRejected>().Any(), what + ": " +
                string.Join("; ", sim.LastTurnEvents.OfType<CommandRejected>().Select(r => r.Reason)));
            AssertConserved(sim, what);
        }

        /// <summary>Puts the player at <paramref name="node"/> with only these local coins; households absorb the difference.</summary>
        static void SetCoins(GameSimulation sim, NodeId node, long coins)
        {
            var player = sim.World.Get<PlayerState>();
            var nodes = sim.World.Get<NodeTable>();
            var purses = sim.World.Get<PurseState>();
            for (int c = 0; c < player.Coins.Length; c++)
            {
                int anyNode = Enumerable.Range(0, nodes.Count).First(n => nodes.Continent[n] == c);
                purses.PayHouseholds(anyNode, player.Coins[c]);
                player.Coins[c] = 0;
            }
            player.Node = node.Value;
            player.LocalCoins = coins;
            purses.PayHouseholds(node.Value, -coins);
        }

        static int NationOf(GameSimulation sim, NodeId node) =>
            sim.World.Get<PurseState>().NodeFloat[node.Value];

        /// <summary>Moves a float's money to households of the same nation, so nothing is created.</summary>
        static void EmptyFloat(GameSimulation sim, NodeId node)
        {
            var purses = sim.World.Get<PurseState>();
            int p = NationOf(sim, node);
            purses.PayHouseholds(node.Value, purses.Float[p]);
            purses.Float[p] = 0;
        }

        [Test]
        public void Seeding_MakesEveryContinentsMoneyItsCoinSupply()
        {
            using var sim = TestWorlds.BuildSample();
            AssertConserved(sim, "start");
            var purses = sim.World.Get<PurseState>();
            for (int p = 0; p < purses.Count; p++) Assert.GreaterOrEqual(purses.Float[p], 0);
            var households = sim.World.Get<HouseholdCash>();
            var population = sim.World.Get<HouseholdState>().Population;
            for (int n = 0; n < households.Cash.Length; n++)
                if (population[n] > 0) Assert.Greater(households.Cash[n], 0, $"node {n}");
            var nation = new NodeId(sim.World.Get<NodeTable>().AncestorAt(Node(sim, City).Value, WorldLevel.Nation));
            Assert.Greater(sim.View.TraderFloat(nation), 0);
            // Households hold most of it: the floats get a tenth of the money nobody holds.
            Assert.Greater(sim.View.HouseholdCash(Node(sim, City)), 0);
            Assert.Greater(sim.World.Get<BankState>().InternationalReserve[0], 0);
        }

        [Test]
        public void NpcEconomy_ConservesMoney_EveryTurn_ForYears()
        {
            // Frequent financial events: bank runs, crunches and spikes all move money too.
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile,
                "key,value\nfinance.event_chance_permille,500\n"), 7).Build();
            int defaults = 0, bankruptcies = 0;
            var purses = sim.World.Get<PurseState>();
            var households = sim.World.Get<HouseholdCash>();
            for (int t = 0; t < 3 * SimClock.WeeksPerYear + 2; t++)
            {
                sim.AdvanceTurn();
                defaults += sim.LastTurnEvents.OfType<NpcLoanDefaulted>().Count();
                bankruptcies += sim.LastTurnEvents.OfType<FacilityBankrupt>().Count() + sim.LastTurnEvents.OfType<MerchantBankrupt>().Count();
                AssertConserved(sim, $"turn {sim.Turn - 1}");
                // Markets never pay out more than the traders hold, and spending is handed over every turn.
                for (int p = 0; p < purses.Count; p++) Assert.GreaterOrEqual(purses.Float[p], 0, $"float {p}, turn {sim.Turn - 1}");
                Assert.IsTrue(households.Spent.All(s => s == 0));
            }
            Assert.Greater(defaults + bankruptcies, 0, "the run should exercise defaults or bankruptcies");
        }

        [Test]
        public void PlayerActions_ConserveMoney()
        {
            using var sim = TestWorlds.BuildSample();
            var player = sim.World.Get<PlayerState>();
            var city = Node(sim, City);
            SetCoins(sim, city, 50_000);
            AssertConserved(sim, "setup");

            Assert.IsTrue(sim.Database.TryGetGood("iron_tools", QualityGrade.Mid, out var tools));
            var facilities = sim.World.Get<FacilityTable>();
            int facility = Enumerable.Range(0, facilities.Count).First(f => facilities.Node[f] == city.Value);

            // Goods, not money: stock put on the shelf changes no continent's money.
            var markets = sim.World.Get<MarketStore>();
            Assert.IsTrue(sim.Database.TryGetGood("wheat", QualityGrade.Mid, out var good));
            Assert.IsTrue(markets.TryFindCell(city.Value, good, out int cell));
            markets.Stock[cell] += 100;
            sim.Actions.Deposit(5_000);
            sim.Actions.BuyGoods(good, 1);
            Step(sim, "deposit and buy");
            sim.Actions.SellGoods(good, 1);
            sim.Actions.BuyLicense(tools);
            sim.Actions.FoundCompany(city, GuidanceAxis.Trade, 1_000);
            Step(sim, "sell, license, company");
            sim.Actions.BuyFacility(new FacilityId(facility), 2_000, GuidanceAxis.Industry);
            var groups = sim.World.Get<GroupTable>();
            sim.Actions.JoinGuild(Enumerable.Range(0, groups.Count).First(g => groups.KindOf(g) == GroupKind.Guild && groups.Node[g] == city.Value));
            Step(sim, "facility and guild");
            sim.Actions.TakeLoan(500);
            sim.Actions.Lobby(new NodeId(sim.World.Get<NodeTable>().AncestorAt(city.Value, WorldLevel.Nation)), PolicyKind.Tolls, 1, 300);
            Step(sim, "loan and lobbying");
            sim.Actions.Exchange(player.LocalContinent, 1 - player.LocalContinent, 1_000);
            Step(sim, "exchange");
            sim.Actions.BuildRoad(Node(sim, "aur_n1_r1_c1_v1"));
            Step(sim, "road");
            sim.Actions.Travel(Node(sim, "aur_n1_r1"), guards: true, insurance: true);
            Step(sim, "travel");
            for (int t = 0; t < 12; t++) Step(sim, $"after, week {t}");
        }

        [Test]
        public void Households_PayForWhatTheyConsume_IntoTheFloat()
        {
            using var sim = TestWorlds.BuildSample();
            var households = sim.World.Get<HouseholdCash>();
            var city = Node(sim, City);
            long cash = households.Cash.Sum();
            for (int t = 0; t < 4; t++) sim.AdvanceTurn();
            // They spend more than the wages they earn until dividends and state spending come (P13c).
            Assert.Less(households.Cash.Sum(), cash);
            Assert.Greater(households.Desired[city.Value], 0);
            AssertConserved(sim, "after spending");
        }

        [Test]
        public void Households_WithoutCash_BuyLess()
        {
            using var broke = TestWorlds.BuildSample();
            using var paid = TestWorlds.BuildSample();
            var city = Node(broke, City);
            broke.AdvanceTurn();
            paid.AdvanceTurn();
            // The city's households hand all their coins to the traders; only this week's wages come back.
            var households = broke.World.Get<HouseholdCash>();
            broke.World.Get<PurseState>().ChangeFloat(city.Value, households.Cash[city.Value]);
            households.Cash[city.Value] = 0;
            long Met(GameSimulation sim) => sim.World.Get<HouseholdState>().NecessityMet[city.Value];
            long brokeMet = Met(broke), paidMet = Met(paid);
            broke.AdvanceTurn();
            paid.AdvanceTurn();
            Assert.Less(Met(broke) - brokeMet, Met(paid) - paidMet);
            Assert.GreaterOrEqual(households.Cash[city.Value], 0);
            AssertConserved(broke, "broke households");
        }

        [Test]
        public void Selling_IsCappedByTheTradersFloat()
        {
            using var sim = TestWorlds.BuildSample();
            var city = Node(sim, City);
            SetCoins(sim, city, 0);
            Assert.IsTrue(sim.Database.TryGetGood("wheat", QualityGrade.Mid, out var wheat));
            sim.World.Get<CargoTable>().Change(CargoHolders.PlayerCart, wheat, 10);
            EmptyFloat(sim, city);
            sim.Actions.SellGoods(wheat, 10);
            sim.AdvanceTurn();
            StringAssert.Contains("no money", sim.LastTurnEvents.OfType<CommandRejected>().Single().Reason);
            Assert.AreEqual(10, sim.World.Get<CargoTable>().QuantityOf(CargoHolders.PlayerCart, wheat));

            // With coins for three units the traders buy three.
            var purses = sim.World.Get<PurseState>();
            Assert.IsTrue(sim.World.Get<MarketStore>().TryFindCell(city.Value, wheat, out int cell));
            EmptyFloat(sim, city);
            long price = sim.World.Get<MarketStore>().Price[cell];
            purses.Float[NationOf(sim, city)] = 3 * price;
            purses.PayHouseholds(city.Value, -3 * price);
            new SellGoodsCommand(wheat, 10).Apply(sim.Context);
            Assert.AreEqual(7, sim.World.Get<CargoTable>().QuantityOf(CargoHolders.PlayerCart, wheat));
            Assert.AreEqual(3 * price, sim.World.Get<PlayerState>().LocalCoins);
            Assert.AreEqual(0, purses.Float[NationOf(sim, city)]);
        }

        [Test]
        public void Exchange_NeedsCoinsAtTheInternationalBanks()
        {
            using var sim = TestWorlds.BuildSample();
            var player = sim.World.Get<PlayerState>();
            SetCoins(sim, Node(sim, City), 1_000);
            var bank = sim.World.Get<BankState>();
            int other = 1 - player.LocalContinent;
            bank.InternationalReserve[other] = 0;
            sim.Actions.Exchange(player.LocalContinent, other, 1_000);
            sim.AdvanceTurn();
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<CommandRejected>().Count());
        }
    }
}
