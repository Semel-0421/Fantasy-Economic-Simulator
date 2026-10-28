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

        /// <summary>Puts the player at <paramref name="node"/> with only these local coins; the purses absorb the difference.</summary>
        static void SetCoins(GameSimulation sim, NodeId node, long coins)
        {
            var player = sim.World.Get<PlayerState>();
            var nodes = sim.World.Get<NodeTable>();
            var purses = sim.World.Get<PurseState>();
            for (int c = 0; c < player.Coins.Length; c++)
            {
                int anyNode = Enumerable.Range(0, nodes.Count).First(n => nodes.Continent[n] == c);
                purses.Change(anyNode, player.Coins[c]);
                player.Coins[c] = 0;
            }
            player.Node = node.Value;
            player.LocalCoins = coins;
            purses.Change(node.Value, -coins);
        }

        [Test]
        public void Seeding_MakesEveryContinentsMoneyItsCoinSupply()
        {
            using var sim = TestWorlds.BuildSample();
            AssertConserved(sim, "start");
            var purses = sim.World.Get<PurseState>();
            for (int p = 0; p < purses.Count; p++) Assert.GreaterOrEqual(purses.Purse[p], 0);
            var nation = new NodeId(sim.World.Get<NodeTable>().AncestorAt(Node(sim, City).Value, WorldLevel.Nation));
            Assert.Greater(sim.View.NationPurse(nation), 0);
            Assert.Greater(sim.World.Get<BankState>().InternationalReserve[0], 0);
        }

        [Test]
        public void NpcEconomy_ConservesMoney_EveryTurn_ForYears()
        {
            // Frequent financial events: bank runs, crunches and spikes all move money too.
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile,
                "key,value\nfinance.event_chance_permille,500\n"), 7).Build();
            int defaults = 0, bankruptcies = 0;
            for (int t = 0; t < 3 * SimClock.WeeksPerYear + 2; t++)
            {
                sim.AdvanceTurn();
                defaults += sim.LastTurnEvents.OfType<NpcLoanDefaulted>().Count();
                bankruptcies += sim.LastTurnEvents.OfType<FacilityBankrupt>().Count() + sim.LastTurnEvents.OfType<MerchantBankrupt>().Count();
                AssertConserved(sim, $"turn {sim.Turn - 1}");
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
