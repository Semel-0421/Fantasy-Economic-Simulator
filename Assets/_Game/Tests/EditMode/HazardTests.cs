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
    public class HazardTests
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

        /// <summary>The player at a node with all coins in its continent's currency and the given cart.</summary>
        static PlayerState PlaceAt(GameSimulation sim, string node, long coins, params (string good, int quantity)[] cart)
        {
            var player = sim.World.Get<PlayerState>();
            System.Array.Clear(player.Coins, 0, player.Coins.Length);
            player.Node = Node(sim, node).Value;
            player.LocalCoins = coins;
            foreach (var (good, quantity) in cart)
                sim.World.Get<CargoTable>().Change(CargoHolders.PlayerCart, Good(sim, good), quantity);
            return player;
        }

        static GameSimulation WithConfig(string rows) =>
            SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile, "key,value\n" + rows), 42).Build();

        static int Route(GameSimulation sim, string from, string to) =>
            new TravelCosts(sim.World, sim.Database).FindRoute(Node(sim, from).Value, Node(sim, to).Value);

        [Test]
        public void HardTerrain_AddsTurnsAndCost()
        {
            using var sim = TestWorlds.BuildSample();
            var routes = sim.World.Get<RouteTable>();
            int mountain = Route(sim, "aur_n1_r1_c1", "aur_n1_r2_c1");
            Assert.AreEqual(RouteTerrain.Mountain, (RouteTerrain)routes.Terrain[mountain]);
            Assert.AreEqual(3 + 1, routes.TravelTurns[mountain]);
            Assert.AreEqual(30, routes.BaseFee[mountain]); // 20 x 1.5
            Assert.AreEqual(6, routes.CostPerUnit[mountain]); // 4 x 1.5
            int road = Route(sim, "aur_n1_r1_c1_v1", "aur_n1_r1_c1");
            Assert.AreEqual(RouteTerrain.Road, (RouteTerrain)routes.Terrain[road]);
            Assert.AreEqual(1, routes.TravelTurns[road]);
            Assert.IsTrue(((RouteTerrain)routes.Terrain[Route(sim, "aur_n1_r2_c1", "bor_n1_r1_c2")]).IsWater());

            var nodes = TestWorlds.Sample().ReadText(CsvMapSource.NodesFile)
                .Replace("aur_n1_r1_c1,Aurelia City 1-1,City,aur_n1_r1,smithing,50,100,1,", "aur_n1_r1_c1,Aurelia City 1-1,City,aur_n1_r1,smithing,50,100,1,900");
            Assert.Throws<System.FormatException>(() => SimulationBuilder.FromData(TestWorlds.SampleWith(CsvMapSource.NodesFile, nodes), 1).Build());
        }

        [Test]
        public void EncounterChance_FollowsTerrainRegionValue_AndGuards()
        {
            using var sim = TestWorlds.BuildSample();
            var config = sim.Database.Config;
            PlaceAt(sim, "aur_n1_r1_c1", 10_000, ("wheat", 20));
            var target = Node(sim, "aur_n1_r2_c1");

            Assert.IsTrue(sim.View.TryQuoteTrip(target, out var plain));
            var danger = config.TerrainDanger[(int)RouteTerrain.Mountain] * Fx.FromPermille(1500); // the region's own factor
            var expected = danger * (Fx.One + Fx.FromRatio(plain.CargoValue, config.DangerValueScale));
            Assert.AreEqual(expected.Raw, plain.EncounterChance.Raw);
            Assert.AreEqual(0, plain.Guards);

            Assert.IsTrue(sim.View.TryQuoteTrip(target, true, true, out var guarded));
            Assert.AreEqual((expected * (Fx.One - config.GuardReduction)).Raw, guarded.EncounterChance.Raw);
            Assert.AreEqual(config.GuardBaseCost + config.GuardCostShare.MulInt(plain.CargoValue), guarded.Guards);
            Assert.AreEqual(config.InsurancePremium.MulInt(plain.CargoValue), guarded.Insurance);
            Assert.AreEqual(plain.DepartureCost + guarded.Guards + guarded.Insurance, guarded.DepartureCost);
        }

        [Test]
        public void Encounter_TakesCashOrCargo_AndInsuranceRefundsPartOfTheCargo()
        {
            // Certain encounters on roads.
            using var sim = WithConfig("terrain.road.danger_permille,1000\n");
            var player = PlaceAt(sim, "aur_n1_r1_c1_v1", 10_000, ("wheat", 20));
            long coins = player.LocalCoins;
            var city = Node(sim, "aur_n1_r1_c1");
            Assert.IsTrue(sim.View.TryQuoteTrip(city, false, true, out var quote));
            Assert.AreEqual(Fx.One, quote.EncounterChance);

            sim.Actions.Travel(city, insurance: true);
            sim.AdvanceTurn();
            var encounter = sim.LastTurnEvents.OfType<TripEncounter>().Single();
            long covered = encounter.Loss == EncounterLoss.Cargo ? encounter.LossValue : 0;
            Assert.AreEqual(sim.Database.Config.InsuranceRefund.MulInt(covered), encounter.Refund);
            long paid = quote.DepartureCost + quote.Toll;
            if (encounter.Loss == EncounterLoss.Cash)
            {
                Assert.AreEqual(sim.Database.Config.EncounterCashLoss.MulInt(coins - paid), encounter.LossValue);
                Assert.AreEqual(coins - paid - encounter.LossValue + encounter.Refund, player.LocalCoins);
            }
            else
            {
                Assert.AreEqual(14, sim.World.Get<CargoTable>().QuantityOf(CargoHolders.PlayerCart, Good(sim, "wheat"))); // 30% of 20 lost
                Assert.AreEqual(coins - paid + encounter.Refund, player.LocalCoins);
            }
        }

        // The premium is on the cargo, so the policy covers no coins, as for merchants.
        [Test]
        public void Insurance_RefundsNoCoins()
        {
            using var sim = WithConfig("terrain.road.danger_permille,1000\n");
            PlaceAt(sim, "aur_n1_r1_c1_v1", 100_000, ("wheat", 20));
            var there = Node(sim, "aur_n1_r1_c1");
            var back = Node(sim, "aur_n1_r1_c1_v1");
            TripEncounter cash = null;
            for (int trip = 0; trip < 40 && cash == null; trip++)
            {
                sim.Actions.Travel(trip % 2 == 0 ? there : back, insurance: true);
                sim.AdvanceTurn();
                cash = sim.LastTurnEvents.OfType<TripEncounter>().FirstOrDefault(e => e.Loss == EncounterLoss.Cash);
                while (sim.World.Get<PlayerState>().IsTravelling) sim.AdvanceTurn();
            }
            Assert.IsNotNull(cash, "a cash encounter within forty trips");
            Assert.Greater(cash.LossValue, 0);
            Assert.AreEqual(0, cash.Refund);
        }

        [Test]
        public void SafeRoads_HaveNoEncounters()
        {
            using var sim = WithConfig("terrain.road.danger_permille,0\n");
            PlaceAt(sim, "aur_n1_r1_c1_v1", 10_000, ("wheat", 20));
            sim.Actions.Travel(Node(sim, "aur_n1_r1_c1"));
            sim.AdvanceTurn();
            Assert.IsTrue(sim.View.IsTravelling);
            Assert.AreEqual(0, sim.LastTurnEvents.OfType<TripEncounter>().Count());
        }

        [Test]
        public void WaterRoutes_NeedPassage_OrAShipThatCarriesMore()
        {
            using var sim = WithConfig("terrain.sea.danger_permille,0\nterrain.road.danger_permille,0\n");
            var config = sim.Database.Config;
            var player = PlaceAt(sim, "aur_n1_r2_c1", 20_000, ("iron_ore", 40)); // 480 weight: more than the cart's 400
            player.Coins[sim.View.ContinentOf(Node(sim, "bor_n1_r1_c2"))] = 20_000;
            var abroad = Node(sim, "bor_n1_r1_c2");

            Assert.IsTrue(sim.View.TryQuoteTrip(abroad, out var passage));
            Assert.AreEqual(config.PassageBaseFee + 40 * config.PassagePerUnit, passage.Passage);
            sim.Actions.Travel(abroad);
            sim.AdvanceTurn();
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<CommandRejected>().Count()); // passage carries only the cart's load

            sim.Actions.BuyShip();
            sim.AdvanceTurn();
            Assert.IsTrue(sim.View.OwnsShip);
            Assert.IsTrue(sim.View.TryQuoteTrip(abroad, out var shipped));
            Assert.AreEqual(0, shipped.Passage);

            sim.Actions.Travel(Node(sim, "aur_n1_r2_c1")); // a land route: still the cart's limits
            sim.AdvanceTurn();
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<CommandRejected>().Count());
            sim.Actions.Travel(abroad);
            sim.AdvanceTurn();
            Assert.IsTrue(sim.View.IsTravelling);
        }

        [Test]
        public void Ship_IsBoughtAtPorts_AndCountsAsCollateral()
        {
            using var sim = TestWorlds.BuildSample();
            var config = sim.Database.Config;
            PlaceAt(sim, "aur_n1_r1_c1", config.ShipPrice);
            sim.Actions.BuyShip();
            sim.AdvanceTurn();
            Assert.IsFalse(sim.View.OwnsShip); // a city, but not a port

            PlaceAt(sim, "aur_n1_r2_c1", config.ShipPrice);
            sim.Actions.BuyShip();
            sim.Actions.BuyShip();
            sim.AdvanceTurn();
            Assert.IsTrue(sim.View.OwnsShip);
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<CommandRejected>().Count());
            Assert.AreEqual(config.ShipPrice, sim.View.CollateralValue);
        }
    }
}
