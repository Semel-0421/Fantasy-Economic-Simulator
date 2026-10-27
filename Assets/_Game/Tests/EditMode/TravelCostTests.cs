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
    public class TravelCostTests
    {
        static NodeId Node(GameSimulation sim, string key)
        {
            Assert.IsTrue(sim.View.TryFindNode(key, out var node), key);
            return node;
        }

        static GoodId Good(GameSimulation sim, string key)
        {
            Assert.IsTrue(sim.Database.TryGetGood(key, QualityGrade.Mid, out var good), key);
            return good;
        }

        static int PriceAt(GameSimulation sim, NodeId node, GoodId good)
        {
            Assert.IsTrue(sim.World.Get<MarketFlows>().TryFindNearestMarket(node.Value, good, out _, out int cell));
            return sim.World.Get<MarketStore>().Price[cell];
        }

        /// <summary>Moves the player there with all their coins in that continent's currency (the start village is random).</summary>
        static void PlacePlayer(GameSimulation sim, string node, params (string good, int quantity)[] cart)
        {
            var player = sim.World.Get<PlayerState>();
            long coins = player.Coins.Sum();
            System.Array.Clear(player.Coins, 0, player.Coins.Length);
            player.Node = Node(sim, node).Value;
            player.LocalCoins = coins;
            foreach (var (good, quantity) in cart)
                sim.World.Get<CargoTable>().Change(CargoHolders.PlayerCart, Good(sim, good), quantity);
        }

        [Test]
        public void Toll_ChargesTheNodesTariff_PlusSurchargeOnItsSpecialtyCategories()
        {
            using var sim = TestWorlds.BuildSample();
            // The city's own rates are 50 / 100 permille, and it is a 'smithing' node (tools).
            PlacePlayer(sim, "aur_n1_r1_c1_v1", ("iron_tools", 2), ("wheat", 10));
            var city = Node(sim, "aur_n1_r1_c1");

            Assert.IsTrue(sim.View.TryQuoteTrip(city, out var quote));
            long expectedToll = Fx.FromPermille(150).MulInt(2L * PriceAt(sim, city, Good(sim, "iron_tools")))
                                + Fx.FromPermille(50).MulInt(10L * PriceAt(sim, city, Good(sim, "wheat")));
            Assert.AreEqual(expectedToll, quote.Toll);
            Assert.AreEqual(2 + 12 * 1, quote.Fee); // base fee + 12 units x 1
            Assert.AreEqual(12, quote.CargoUnits);
            Assert.AreEqual(quote.FeeContinent, quote.TollContinent);
        }

        [Test]
        public void Travel_PaysFeeAndToll_Upfront()
        {
            using var sim = TestWorlds.BuildSample();
            PlacePlayer(sim, "aur_n1_r1_c1_v1", ("wheat", 10));
            var city = Node(sim, "aur_n1_r1_c1");
            long coins = sim.View.PlayerCash;
            Assert.IsTrue(sim.View.TryQuoteTrip(city, out var quote));
            Assert.Greater(quote.Toll, 0);

            sim.Actions.Travel(city);
            sim.AdvanceTurn();
            var started = sim.LastTurnEvents.OfType<TripStarted>().Single();
            Assert.AreEqual(quote.Fee, started.Paid.Fee);
            Assert.AreEqual(quote.Toll, started.Paid.Toll);
            Assert.AreEqual(coins - quote.Fee - quote.Toll, sim.View.PlayerCash);
            Assert.AreEqual(started.ArrivalTurn, sim.View.ArrivalTurn);
        }

        [Test]
        public void Travel_IsRejected_WithoutCoinsForTheCosts()
        {
            using var sim = TestWorlds.BuildSample();
            PlacePlayer(sim, "aur_n1_r1_c1_v1", ("wheat", 10));
            var city = Node(sim, "aur_n1_r1_c1");
            Assert.IsTrue(sim.View.TryQuoteTrip(city, out var quote));
            sim.World.Get<PlayerState>().LocalCoins = quote.Fee + quote.Toll - 1;

            sim.Actions.Travel(city);
            sim.AdvanceTurn();
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<CommandRejected>().Count());
            Assert.IsFalse(sim.View.IsTravelling);
            Assert.AreEqual(quote.Fee + quote.Toll - 1, sim.View.PlayerCash);
        }

        [Test]
        public void TripAcrossContinents_PaysTheTollInTheDestinationsCoin()
        {
            using var sim = TestWorlds.BuildSample();
            PlacePlayer(sim, "aur_n1_r2", ("wheat", 10));
            var player = sim.World.Get<PlayerState>();
            var abroad = Node(sim, "bor_n1_r1");
            int home = sim.View.ContinentOf(sim.View.PlayerNode), away = sim.View.ContinentOf(abroad);
            Assert.AreNotEqual(home, away);

            Assert.IsTrue(sim.View.TryQuoteTrip(abroad, out var quote));
            Assert.AreEqual(home, quote.FeeContinent);
            Assert.AreEqual(away, quote.TollContinent);
            Assert.AreEqual(Fx.FromPermille(30).MulInt(10L * PriceAt(sim, abroad, Good(sim, "wheat"))), quote.Toll);

            sim.Actions.Travel(abroad); // no coins of the other continent yet
            sim.AdvanceTurn();
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<CommandRejected>().Count());

            player.Coins[away] = 1000;
            long homeCoins = player.Coins[home];
            Assert.IsTrue(sim.View.TryQuoteTrip(abroad, out quote));
            sim.Actions.Travel(abroad);
            sim.AdvanceTurn();
            Assert.IsTrue(sim.View.IsTravelling);
            Assert.Greater(quote.Passage, 0); // a sea route without an own ship
            var encounter = sim.LastTurnEvents.OfType<TripEncounter>().SingleOrDefault();
            long cashLost = encounter != null && encounter.Loss == EncounterLoss.Cash ? encounter.LossValue : 0;
            Assert.AreEqual(homeCoins - quote.DepartureCost - cashLost, player.Coins[home]);
            Assert.AreEqual(1000 - quote.Toll, player.Coins[away]);
        }

        [Test]
        public void NegativeTollRates_AreRejected()
        {
            var nodes = TestWorlds.Sample().ReadText(CsvMapSource.NodesFile).Replace(",smithing,50,100", ",smithing,-5,100");
            var e = Assert.Throws<System.FormatException>(() =>
                SimulationBuilder.FromData(TestWorlds.SampleWith(CsvMapSource.NodesFile, nodes), 1).Build());
            StringAssert.Contains("toll", e.Message);
        }
    }
}
