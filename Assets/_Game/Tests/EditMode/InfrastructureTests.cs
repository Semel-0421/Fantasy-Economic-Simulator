using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Data;
using FES.Simulation;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    public class InfrastructureTests
    {
        static NodeId Node(GameSimulation sim, string key)
        {
            Assert.IsTrue(sim.View.TryFindNode(key, out var node), key);
            return node;
        }

        static PlayerState PlaceAt(GameSimulation sim, string node, long coins)
        {
            var player = sim.World.Get<PlayerState>();
            System.Array.Clear(player.Coins, 0, player.Coins.Length);
            player.Node = Node(sim, node).Value;
            player.LocalCoins = coins;
            return player;
        }

        static int Route(GameSimulation sim, string from, string to) =>
            new TravelCosts(sim.World, sim.Database).FindRoute(Node(sim, from).Value, Node(sim, to).Value);

        static int Rejected(GameSimulation sim) => sim.LastTurnEvents.OfType<CommandRejected>().Count();

        static GameSimulation SafeRoads() => SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile,
            "key,value\nterrain.road.danger_permille,0\nterrain.sea.danger_permille,0\n"), 42).Build();

        [Test]
        public void WaterRoutes_RunBetweenPorts()
        {
            using var sim = TestWorlds.BuildSample();
            Assert.IsTrue(sim.View.IsPort(Node(sim, "aur_n1_r2")));
            Assert.IsFalse(sim.View.IsPort(Node(sim, "aur_n1_r1")));

            var nodes = TestWorlds.Sample().ReadText(CsvMapSource.NodesFile);
            int at = nodes.IndexOf("aur_n1_r2,Aurelia Region 2");
            int end = nodes.IndexOf('\n', at);
            string line = nodes.Substring(at, end - at).TrimEnd('\r');
            string noPort = nodes.Replace(line, line.Substring(0, line.Length - 1)); // drop the port flag
            Assert.AreNotEqual(nodes, noPort);
            var e = Assert.Throws<System.FormatException>(() =>
                SimulationBuilder.FromData(TestWorlds.SampleWith(CsvMapSource.NodesFile, noPort), 1).Build());
            StringAssert.Contains("ports", e.Message);
        }

        [Test]
        public void Ship_StaysDockedWhereThePlayerLands()
        {
            using var sim = SafeRoads();
            var config = sim.Database.Config;
            var player = PlaceAt(sim, "aur_n1_r2", config.ShipPrice + 1_000);
            var home = Node(sim, "aur_n1_r2");
            var abroad = Node(sim, "bor_n1_r1");
            player.Coins[sim.View.ContinentOf(abroad)] = 1_000;
            sim.Actions.BuyShip();
            sim.AdvanceTurn();
            Assert.AreEqual(home, sim.View.ShipDockedAt);

            Assert.IsTrue(sim.View.TryQuoteTrip(abroad, out var sailing));
            Assert.AreEqual(0, sailing.Passage);
            sim.Actions.Travel(abroad);
            sim.AdvanceTurn();
            Assert.AreEqual(abroad, sim.View.ShipDockedAt);
            while (sim.View.IsTravelling) sim.AdvanceTurn();

            // Back at the home port without the ship: passage again.
            player.Node = home.Value;
            Assert.IsTrue(sim.View.TryQuoteTrip(abroad, out var passage));
            Assert.Greater(passage.Passage, 0);
            sim.Actions.SellShip();
            sim.AdvanceTurn();
            Assert.AreEqual(1, Rejected(sim)); // the ship is not docked here

            player.Node = abroad.Value;
            long coins = player.LocalCoins;
            sim.Actions.SellShip();
            sim.AdvanceTurn();
            Assert.IsFalse(sim.View.OwnsShip);
            Assert.AreEqual(coins + config.ShipResaleShare.MulInt(config.ShipPrice), player.LocalCoins);
        }

        [Test]
        public void PavedRoads_AreCheaperAndSafer()
        {
            using var sim = TestWorlds.BuildSample();
            var config = sim.Database.Config;
            var costs = new TravelCosts(sim.World, sim.Database);
            int paved = Route(sim, "aur_n1_r1_c1", "aur_n1_r1");
            int unpaved = Route(sim, "aur_n1_r1_c1", "aur_n1_r1_c1_v1");
            var roads = sim.World.Get<RoadState>();
            Assert.AreEqual(RoadState.MaxCondition, roads.Condition[paved]);
            Assert.AreEqual(0, roads.Condition[unpaved]);

            var routes = sim.World.Get<RouteTable>();
            Assert.AreEqual((Fx.FromInt(routes.CostPerUnit[paved]) * (Fx.One - config.RoadCostReduction)).Raw, costs.PerUnitCharge(paved).Raw);
            Assert.AreEqual(Fx.FromInt(routes.CostPerUnit[unpaved]).Raw, costs.PerUnitCharge(unpaved).Raw);
            var hazards = sim.World.Get<RouteHazards>();
            Assert.AreEqual((Fx.FromRaw(hazards.BaseDanger[paved]) * (Fx.One - config.RoadDangerReduction)).Raw, costs.RouteDanger(paved).Raw);

            var half = roads.Condition[paved] = RoadState.MaxCondition / 2;
            Assert.AreEqual((Fx.FromInt(routes.CostPerUnit[paved]) * (Fx.One - config.RoadCostReduction * Fx.FromPermille(half))).Raw,
                costs.PerUnitCharge(paved).Raw);
        }

        [Test]
        public void NationRoads_AreKeptFromTheReserve_OrDecay()
        {
            // Without state spending or state enterprises' dividends the reserve moves only by road upkeep.
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile,
                "key,value\nbank.state_spending_permille,0\nfacility.dividend_permille,0\nstate.defense_permille,0\nstate.project_share_permille,0\n"), 42).Build();
            var config = sim.Database.Config;
            var banks = sim.World.Get<NodeBanks>();
            var bank = sim.World.Get<BankState>();
            var roads = sim.World.Get<RoadState>();
            int nation = banks.NationIndex(sim.World.Get<NodeTable>().AncestorAt(Node(sim, "aur_n1_r1").Value, WorldLevel.Nation));
            int ownRoads = Enumerable.Range(0, roads.Condition.Length).Count(r => roads.IsPaved(r) && roads.Owner[r] == nation);
            Assert.AreEqual(8, ownRoads);

            long reserve = bank.Reserve[nation];
            sim.AdvanceTurn();
            Assert.AreEqual(reserve - ownRoads * config.RoadUpkeep, bank.Reserve[nation]);

            int paved = Route(sim, "aur_n1_r1_c1", "aur_n1_r1");
            bank.Reserve[nation] = 0;
            sim.AdvanceTurn();
            Assert.AreEqual(RoadState.MaxCondition - config.RoadDecayPermille, roads.Condition[paved]);

            roads.Condition[paved] = config.RoadDecayPermille;
            sim.AdvanceTurn();
            Assert.AreEqual(0, roads.Condition[paved]);
            Assert.AreEqual(RoadState.NoOwner, roads.Owner[paved]);
            Assert.IsTrue(sim.LastTurnEvents.OfType<RoadLost>().Any(e => e.From == Node(sim, "aur_n1_r1_c1")));
        }

        [Test]
        public void Player_BuildsARoad_AndKeepsItUp()
        {
            using var sim = TestWorlds.BuildSample();
            var config = sim.Database.Config;
            var player = PlaceAt(sim, "aur_n1_r1_c1_v1", 10_000);
            var city = Node(sim, "aur_n1_r1_c1");
            sim.Actions.BuildRoad(city);
            sim.Actions.BuildRoad(city); // already under construction
            sim.AdvanceTurn();
            Assert.AreEqual(1, Rejected(sim));
            Assert.AreEqual(10_000 - config.RoadBuildCostPerTurn * 1, player.LocalCoins);

            var roads = sim.World.Get<RoadState>();
            int there = Route(sim, "aur_n1_r1_c1_v1", "aur_n1_r1_c1"), back = Route(sim, "aur_n1_r1_c1", "aur_n1_r1_c1_v1");
            var built = sim.LastTurnEvents.OfType<RoadBuilt>().ToList();
            for (int t = 0; t < config.RoadBuildWeeks && built.Count == 0; t++)
            {
                sim.AdvanceTurn();
                built = sim.LastTurnEvents.OfType<RoadBuilt>().ToList();
            }
            Assert.AreEqual(2, built.Count);
            Assert.AreEqual(RoadState.MaxCondition, roads.Condition[there]);
            Assert.AreEqual(RoadState.PlayerOwner, roads.Owner[back]);

            long coins = player.LocalCoins;
            sim.AdvanceTurn();
            Assert.AreEqual(coins - 2 * config.RoadUpkeep, player.LocalCoins); // no deposit: from the coins carried

            sim.Actions.BuildRoad(city);
            sim.AdvanceTurn();
            Assert.AreEqual(1, Rejected(sim)); // already paved
        }

        [Test]
        public void WaterRoutes_CannotBePaved()
        {
            using var sim = TestWorlds.BuildSample();
            PlaceAt(sim, "aur_n1_r2", 100_000);
            sim.Actions.BuildRoad(Node(sim, "bor_n1_r1"));
            sim.AdvanceTurn();
            Assert.AreEqual(1, Rejected(sim));
        }
    }
}
