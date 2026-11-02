using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.Persistence;
using FES.Simulation;
using FES.World;
using NUnit.Framework;
using Debug = UnityEngine.Debug;

namespace FES.Tests
{
    public class SimulationTests
    {
        [Test]
        public void SampleWorld_RunsOneYear()
        {
            using var sim = TestWorlds.BuildSample();
            var nodes = sim.World.Get<NodeTable>();
            Assert.AreEqual(36, nodes.Count);
            Assert.AreEqual(2, nodes.ContinentCount);

            for (int t = 0; t < SimClock.WeeksPerYear; t++) sim.AdvanceTurn();
            Assert.AreEqual(SimClock.WeeksPerYear, sim.Turn);
        }

        [Test]
        public void SpawnCounts_FollowConfig()
        {
            using var sim = TestWorlds.BuildSample();
            var nodes = sim.World.Get<NodeTable>();
            int villages = nodes.LevelCount(WorldLevel.Village), cities = nodes.LevelCount(WorldLevel.City);
            var facilities = sim.World.Get<FacilityTable>();
            // A slot whose inputs nobody makes nearby goes to a primary producer, so every slot is filled (P13d).
            int continental = sim.Database.Facilities.Count(d => d.Continent != null) * sim.Database.Config.ContinentalProducers;
            Assert.AreEqual(villages * 4 + cities * (6 + 8) + continental, facilities.Count);
            int primaries = Enumerable.Range(0, facilities.Count).Count(f => sim.Database.Facilities[facilities.DefIndex[f]].Tier == IndustryTier.Primary);
            Assert.GreaterOrEqual(primaries, villages * 4);
            Assert.Greater(facilities.Count, primaries);

            // A facility with inputs stands where its inputs are made, in its node's subtree (P13d).
            for (int f = 0; f < facilities.Count; f++)
                foreach (var input in sim.Database.Facilities[facilities.DefIndex[f]].Recipe.Inputs)
                    Assert.IsTrue(Enumerable.Range(0, facilities.Count).Any(g => sim.Database.Facilities[facilities.DefIndex[g]].Recipe.Outputs.Any(o => o.Good == input.Good)
                        && (Within(nodes, facilities.Node[g], facilities.Node[f]) || SameRegion(nodes, facilities.Node[g], facilities.Node[f]))), $"facility {f} {sim.Database.Facilities[facilities.DefIndex[f]].Key} needs {sim.Database.Good(input.Good).Key} at {nodes.LevelOf(facilities.Node[f])}");

            int expectedMerchants = villages * 2 + cities * 6 + nodes.LevelCount(WorldLevel.Region) * 8
                + nodes.LevelCount(WorldLevel.Nation) * 2 + nodes.LevelCount(WorldLevel.Continent) * 2;
            Assert.AreEqual(expectedMerchants, sim.World.Get<AgentTable>().Count);
        }

        // A class's producers stand at its core (DEC-104), so what its inputs come from may be a neighbour of that core.
        static bool SameRegion(NodeTable nodes, int a, int b) =>
            nodes.AncestorAt(a, WorldLevel.Region) == nodes.AncestorAt(b, WorldLevel.Region);

        static bool Within(NodeTable nodes, int node, int ancestor)
        {
            for (int m = node; m >= 0; m = nodes.Parent[m])
                if (m == ancestor) return true;
            return false;
        }

        [Test]
        public void SameSeed_SameHash_DifferentSeed_DifferentHash()
        {
            using var a = TestWorlds.BuildSample(seed: 1);
            using var b = TestWorlds.BuildSample(seed: 1);
            using var c = TestWorlds.BuildSample(seed: 2);
            for (int t = 0; t < 30; t++)
            {
                a.AdvanceTurn();
                b.AdvanceTurn();
                c.AdvanceTurn();
            }
            Assert.AreEqual(a.ComputeHash(), b.ComputeHash());
            Assert.AreNotEqual(a.ComputeHash(), c.ComputeHash());
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesStateAndFuture()
        {
            using var original = TestWorlds.BuildSample(seed: 5);
            for (int t = 0; t < 10; t++) original.AdvanceTurn();
            using var buffer = new MemoryStream();
            original.Save(buffer);

            using var restored = TestWorlds.BuildSample(seed: 5);
            buffer.Position = 0;
            restored.Load(buffer);
            Assert.AreEqual(10, restored.Turn);
            Assert.AreEqual(original.ComputeHash(), restored.ComputeHash());

            for (int t = 0; t < 10; t++)
            {
                original.AdvanceTurn();
                restored.AdvanceTurn();
            }
            Assert.AreEqual(original.ComputeHash(), restored.ComputeHash());
        }

        [Test]
        public void Load_CorruptSave_LeavesWorldUnchanged()
        {
            using var source = TestWorlds.BuildSample(seed: 5);
            for (int t = 0; t < 5; t++) source.AdvanceTurn();
            using var buffer = new MemoryStream();
            source.Save(buffer);
            var truncated = new MemoryStream(buffer.ToArray(), 0, (int)buffer.Length / 2);

            using var target = TestWorlds.BuildSample(seed: 5);
            target.AdvanceTurn();
            ulong before = target.ComputeHash();
            Assert.Catch(() => target.Load(truncated));
            Assert.AreEqual(before, target.ComputeHash());
            Assert.AreEqual(1, target.Turn);
        }

        [Test]
        public void Load_IntoARunningGame_RefusesASaveWithoutASection()
        {
            using var source = TestWorlds.BuildSample(seed: 5);
            for (int t = 0; t < 5; t++) source.AdvanceTurn();
            using var partial = SaveWithout(source, "agents.income_tax");

            using var target = TestWorlds.BuildSample(seed: 5);
            target.AdvanceTurn();
            ulong before = target.ComputeHash();
            Assert.Throws<InvalidDataException>(() => target.Load(partial));
            Assert.AreEqual(before, target.ComputeHash());
        }

        [Test]
        public void Load_IntoANewGame_KeepsItsFreshStateForAMissingSection()
        {
            using var source = TestWorlds.BuildSample(seed: 5);
            for (int t = 0; t < 5; t++) source.AdvanceTurn();
            using var partial = SaveWithout(source, "agents.guidance");

            using var target = TestWorlds.BuildSample(seed: 5);
            target.Load(partial);
            Assert.AreEqual(5, target.Turn);
        }

        [Test]
        public void Load_RefusesASectionNotReadToItsEnd()
        {
            using var source = TestWorlds.BuildSample(seed: 5);
            source.AdvanceTurn();
            using var full = new MemoryStream();
            source.Save(full);
            full.Position = 0;
            var header = SaveSerializer.ReadHeader(new BinaryReader(full));
            using var padded = new MemoryStream();
            SaveSerializer.Write(padded, header, source.World.PersistentComponents.Select(c => c.Key == "agents.income_tax" ? new Padded(c) : c));
            padded.Position = 0;

            using var target = TestWorlds.BuildSample(seed: 5);
            target.AdvanceTurn();
            ulong before = target.ComputeHash();
            Assert.Throws<InvalidDataException>(() => target.Load(padded));
            Assert.AreEqual(before, target.ComputeHash());
        }

        static MemoryStream SaveWithout(GameSimulation sim, string key)
        {
            using var full = new MemoryStream();
            sim.Save(full);
            full.Position = 0;
            var header = SaveSerializer.ReadHeader(new BinaryReader(full));
            var partial = new MemoryStream();
            SaveSerializer.Write(partial, header, sim.World.PersistentComponents.Where(c => c.Key != key));
            partial.Position = 0;
            return partial;
        }

        /// <summary>A component whose section ends in bytes nothing reads.</summary>
        sealed class Padded : IPersistentComponent
        {
            readonly IPersistentComponent _inner;

            public Padded(IPersistentComponent inner) => _inner = inner;

            public string Key => _inner.Key;
            public int Version => _inner.Version;
            public void Read(BinaryReader reader) => _inner.Read(reader);

            public void Write(BinaryWriter writer)
            {
                _inner.Write(writer);
                writer.Write(0L);
            }
        }

        [Test]
        public void BuyingAFacility_CostsItsValueAndTheCashItHolds()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            var player = sim.World.Get<PlayerState>();
            int f = Enumerable.Range(0, facilities.Count).First(i => facilities.Node[i] == player.Node && !sim.Database.Facilities[facilities.DefIndex[i]].StateOwned);
            long asked = BuyFacilityCommand.AskingPrice(sim.World, sim.Database, f);
            player.LocalCoins = asked * 2;

            sim.Enqueue(new BuyFacilityCommand(new FacilityId(f), 0, GuidanceAxis.Industry));
            sim.Enqueue(new BuyFacilityCommand(new FacilityId(f), asked - 1, GuidanceAxis.Industry));
            sim.AdvanceTurn();
            Assert.AreEqual(2, sim.LastTurnEvents.OfType<CommandRejected>().Count());
            Assert.AreEqual(OwnerIds.Npc, facilities.Owner[f]);

            asked = BuyFacilityCommand.AskingPrice(sim.World, sim.Database, f);
            sim.Enqueue(new BuyFacilityCommand(new FacilityId(f), asked, GuidanceAxis.Industry));
            sim.AdvanceTurn();
            Assert.AreEqual(OwnerIds.Player, facilities.Owner[f]);
        }

        [Test]
        public void Load_DiscardsQueuedCommands()
        {
            using var sim = TestWorlds.BuildSample();
            using var buffer = new MemoryStream();
            sim.Save(buffer);
            var player = sim.World.Get<PlayerState>();
            long cash = player.LocalCoins;

            sim.Enqueue(new FoundCompanyCommand(new NodeId(player.Node), GuidanceAxis.Trade, 100));
            buffer.Position = 0;
            sim.Load(buffer);
            sim.AdvanceTurn();
            Assert.AreEqual(cash, player.LocalCoins);
        }

        [Test]
        public void Commands_RejectNegativeAmounts()
        {
            using var sim = TestWorlds.BuildSample();
            var player = sim.World.Get<PlayerState>();
            long cash = player.LocalCoins;
            sim.Enqueue(new BuyFacilityCommand(new FacilityId(0), -1000, GuidanceAxis.Industry));
            sim.Enqueue(new FoundCompanyCommand(new NodeId(player.Node), GuidanceAxis.Trade, -1000));
            sim.AdvanceTurn();
            Assert.AreEqual(cash, player.LocalCoins);
            Assert.AreEqual(2, sim.LastTurnEvents.OfType<CommandRejected>().Count());
        }

        [Test]
        public void Builder_RejectsDuplicateModules_AndDefaultsAreIdempotent()
        {
            var builder = SimulationBuilder.FromData(TestWorlds.Sample(), 1).WithDefaultModules();
            Assert.Throws<System.InvalidOperationException>(() => builder.AddModule(new WorldModule()));
            using var sim = builder.Build();
            Assert.AreEqual(0, sim.Turn);
        }

        [Test]
        public void Builder_OrdersModulesByRequirements()
        {
            var source = TestWorlds.Sample();
            using var reversed = new SimulationBuilder(GameDatabase.Load(source), new CsvMapSource(source).LoadMap(), 7)
                .AddModule(new AgentsModule()).AddModule(new EconomyModule()).AddModule(new WorldModule()).Build();
            using var standard = TestWorlds.BuildSample(seed: 7);
            for (int t = 0; t < 5; t++)
            {
                reversed.AdvanceTurn();
                standard.AdvanceTurn();
            }
            Assert.AreEqual(standard.ComputeHash(), reversed.ComputeHash());
        }

        [Test]
        public void Builder_ReplacesAndRemovesDefaultSystems()
        {
            var pricing = new PricingSystem { ForceManaged = true };
            using var sim = SimulationBuilder.FromData(TestWorlds.Sample(), 1)
                .ReplaceSystem(pricing)
                .RemoveSystem("agents.merchants")
                .Build();
            Assert.IsTrue(sim.Systems.Contains(pricing));
            Assert.IsFalse(sim.Systems.Any(s => s.Id == "agents.merchants"));
            sim.AdvanceTurn();

            var unknown = SimulationBuilder.FromData(TestWorlds.Sample(), 1).RemoveSystem("no.such.system");
            Assert.Throws<System.InvalidOperationException>(() => unknown.Build());
        }

        [Test]
        public void View_ReadsState_AndActionsQueueCommands()
        {
            using var sim = TestWorlds.BuildSample();
            var view = sim.View;
            var player = sim.World.Get<PlayerState>();
            Assert.AreEqual(player.Node, view.PlayerNode.Value);
            Assert.AreEqual(WorldLevel.Village, view.NodeLevel(view.PlayerNode));
            Assert.IsTrue(view.TryFindNode(view.NodeKey(view.PlayerNode), out var found));
            Assert.AreEqual(view.PlayerNode, found);

            sim.AdvanceTurn();
            var quotes = new List<MarketQuote>();
            view.GetQuotes(view.PlayerNode, quotes);
            Assert.Greater(quotes.Count, 0);
            Assert.IsTrue(view.TryGetQuote(view.PlayerNode, quotes[0].Good, out var quote));
            Assert.AreEqual(quotes[0].Price, quote.Price);
            Assert.Greater(quote.Price, 0);

            var routes = new List<RouteInfo>();
            view.GetRoutesFrom(view.PlayerNode, routes);
            Assert.Greater(routes.Count, 0);
            long cash = view.PlayerCash;
            Assert.IsTrue(view.TryQuoteTrip(routes[0].To, out var trip));
            Assert.AreEqual(routes[0].BaseFee, trip.Fee); // empty cart: no per-unit charge, no toll
            sim.Actions.FoundCompany(view.PlayerNode, GuidanceAxis.Guild, 100);
            sim.Actions.Travel(routes[0].To);
            sim.AdvanceTurn();
            Assert.AreEqual(cash - 100 - trip.Fee, view.PlayerCash);
            Assert.AreEqual(GuidanceAxis.Guild, view.DominantGuidance);
            Assert.IsTrue(view.IsTravelling);
            Assert.AreEqual(routes[0].To, view.TravelDestination);
        }

        [Test]
        public void Travel_TakesRouteTurns_AndRejectsSecondTrip()
        {
            using var sim = TestWorlds.BuildSample();
            var view = sim.View;
            var start = view.PlayerNode;
            var routes = new List<RouteInfo>();
            view.GetRoutesFrom(start, routes);
            var route = routes[0];

            sim.Actions.Travel(route.To);
            sim.Actions.Travel(route.To);
            sim.AdvanceTurn();
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<CommandRejected>().Count());
            Assert.AreEqual(sim.Turn - 1 + route.TravelTurns, view.ArrivalTurn);

            while (view.IsTravelling)
            {
                Assert.AreEqual(start, view.PlayerNode);
                sim.AdvanceTurn();
            }
            Assert.AreEqual(route.To, view.PlayerNode);
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<PlayerArrived>().Count());
            Assert.AreEqual(route.TravelTurns + 1, sim.Turn);
        }

        [Test]
        public void PlayerStateV1Save_IsMigratedByTheAgentsModule()
        {
            using var v1 = new MemoryStream();
            using (var writer = new BinaryWriter(v1))
            {
                writer.Write(3);
                writer.Write(77L);
            }
            using var sim = TestWorlds.BuildSample();
            var migrated = sim.Migrations.Upgrade(PlayerState.SectionKey, 1, 3, v1.ToArray());

            var nodes = sim.World.Get<NodeTable>();
            var player = new PlayerState(nodes);
            player.Read(new BinaryReader(new MemoryStream(migrated)));
            Assert.AreEqual(3, player.Node);
            Assert.AreEqual(77L, player.Coins[nodes.Continent[3]]);
            Assert.AreEqual(77L, player.Coins.Sum());
            Assert.IsFalse(player.IsTravelling);
        }

        [Test]
        public void Player_StartsInARandomVillage_FixedBySeed()
        {
            var starts = new HashSet<int>();
            for (ulong seed = 1; seed <= 12; seed++)
            {
                using var sim = TestWorlds.BuildSample(seed);
                using var again = TestWorlds.BuildSample(seed);
                var view = sim.View;
                Assert.AreEqual(WorldLevel.Village, view.NodeLevel(view.PlayerNode));
                Assert.AreEqual(view.PlayerNode, again.View.PlayerNode);
                Assert.AreEqual(sim.Database.Config.PlayerStartCash, view.PlayerCash);
                starts.Add(view.PlayerNode.Value);
            }
            Assert.Greater(starts.Count, 1);
        }

        static MarketQuote MostStocked(GameSimulation sim)
        {
            var quotes = new List<MarketQuote>();
            sim.View.GetQuotes(sim.View.PlayerNode, quotes);
            return quotes.OrderByDescending(q => q.Stock).ThenBy(q => q.Good.Value).First();
        }

        [Test]
        public void BuyAndSell_MoveGoodsAndCoins()
        {
            using var sim = TestWorlds.BuildSample();
            var view = sim.View;
            for (int t = 0; t < 3; t++) sim.AdvanceTurn();
            var quote = MostStocked(sim);
            Assume.That(quote.Stock, Is.GreaterThanOrEqualTo(5));
            long coins = view.PlayerCash;

            sim.Actions.BuyGoods(quote.Good, 5);
            sim.Enqueue(new BuyGoodsCommand(quote.Good, 0));
            sim.Actions.SellGoods(quote.Good, 2);
            sim.AdvanceTurn();
            var trades = sim.LastTurnEvents.OfType<GoodsTraded>().ToList();
            Assert.AreEqual(2, trades.Count);
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<CommandRejected>().Count());
            Assert.AreEqual(5, trades[0].Filled);
            Assert.AreEqual(2, trades[1].Filled);
            Assert.AreEqual(coins - 5L * trades[0].Price + 2L * trades[1].Price, view.PlayerCash);

            var cart = new List<CargoItem>();
            view.GetCart(cart);
            Assert.AreEqual(1, cart.Count);
            Assert.AreEqual(quote.Good, cart[0].Good);
            Assert.AreEqual(3, cart[0].Quantity);
            var def = sim.Database.Good(quote.Good);
            Assert.AreEqual(3L * def.Weight, view.CartLoad.Weight);
            Assert.AreEqual(3L * def.Size, view.CartLoad.Size);

            sim.World.Get<PurseState>().ChangeFloat(view.PlayerNode.Value, 1_000_000); // the traders here have money for it
            sim.Actions.SellGoods(quote.Good, 100);
            sim.AdvanceTurn();
            Assert.AreEqual(3, sim.LastTurnEvents.OfType<GoodsTraded>().Single().Filled);
            view.GetCart(cart);
            Assert.AreEqual(0, cart.Count);
        }

        [Test]
        public void Buy_TakesStockAtOnce_AndAddsDemand()
        {
            using var sim = TestWorlds.BuildSample();
            for (int t = 0; t < 3; t++) sim.AdvanceTurn();
            var quote = MostStocked(sim);
            Assume.That(quote.Stock, Is.GreaterThanOrEqualTo(4));
            var markets = sim.World.Get<MarketStore>();
            markets.TryFindCell(sim.View.PlayerNode.Value, quote.Good, out int cell);
            int demand = markets.Demand[cell];

            var flows = sim.World.Get<MarketFlows>();
            flows.Buy(cell, 4);
            Assert.AreEqual(quote.Stock - 4, markets.Stock[cell]);
            Assert.AreEqual(demand + 4, markets.Demand[cell]);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => flows.Buy(cell, markets.Stock[cell] + 1));
        }

        [Test]
        public void Buy_FillsPartially_UpToTheSmallestLimit()
        {
            using var sim = TestWorlds.BuildSample();
            var view = sim.View;
            for (int t = 0; t < 3; t++) sim.AdvanceTurn();
            var quote = MostStocked(sim);
            Assume.That(quote.Stock, Is.GreaterThan(0));

            sim.Actions.BuyGoods(quote.Good, 1_000_000);
            sim.AdvanceTurn();
            var trade = sim.LastTurnEvents.OfType<GoodsTraded>().Single();
            var def = sim.Database.Good(quote.Good);
            long room = view.CartCapacity.MaxAdditional(new CargoLoad(0, 0), def.Weight, def.Size);
            long affordable = sim.Database.Config.PlayerStartCash / trade.Price;
            Assert.AreEqual(System.Math.Min(System.Math.Min(quote.Stock, room), affordable), trade.Filled);
            Assert.Less(trade.Filled, trade.Requested);
            Assert.LessOrEqual(view.CartLoad.Weight, view.CartCapacity.MaxWeight);
            Assert.LessOrEqual(view.CartLoad.Size, view.CartCapacity.MaxSize);
            Assert.GreaterOrEqual(view.PlayerCash, 0);
        }

        [Test]
        public void Trade_IsRejectedWhileTravelling_OrForUnknownGoods()
        {
            using var sim = TestWorlds.BuildSample();
            var view = sim.View;
            for (int t = 0; t < 2; t++) sim.AdvanceTurn();
            var quote = MostStocked(sim);
            var routes = new List<RouteInfo>();
            view.GetRoutesFrom(view.PlayerNode, routes);
            sim.Actions.BuyGoods(new GoodId(sim.Database.GoodCount), 1);
            sim.Actions.Travel(routes[0].To);
            sim.Actions.BuyGoods(quote.Good, 1);
            sim.AdvanceTurn();
            Assert.AreEqual(2, sim.LastTurnEvents.OfType<CommandRejected>().Count());
            Assert.AreEqual(0, sim.LastTurnEvents.OfType<GoodsTraded>().Count());
        }

        [Test]
        public void Cart_SurvivesSaveAndLoad()
        {
            using var sim = TestWorlds.BuildSample();
            for (int t = 0; t < 3; t++) sim.AdvanceTurn();
            var quote = MostStocked(sim);
            Assume.That(quote.Stock, Is.GreaterThanOrEqualTo(4));
            sim.Actions.BuyGoods(quote.Good, 4);
            sim.AdvanceTurn();
            using var buffer = new MemoryStream();
            sim.Save(buffer);

            using var copy = TestWorlds.BuildSample(seed: 99);
            buffer.Position = 0;
            copy.Load(buffer);
            Assert.AreEqual(sim.ComputeHash(), copy.ComputeHash());
            var cart = new List<CargoItem>();
            copy.View.GetCart(cart);
            Assert.AreEqual(4, cart.Single().Quantity);
            Assert.AreEqual(sim.View.PlayerCash, copy.View.PlayerCash);
        }

        [Test]
        public void PlayerStateV2Save_PutsCashInTheLocalContinent()
        {
            using var sim = TestWorlds.BuildSample();
            var nodes = sim.World.Get<NodeTable>();
            int node = nodes.Count - 1;
            using var v2 = new MemoryStream();
            using (var writer = new BinaryWriter(v2))
            {
                writer.Write(node);
                writer.Write(123L);
                writer.Write(-1);
                writer.Write(-1);
            }
            var migrated = sim.Migrations.Upgrade(PlayerState.SectionKey, 2, 3, v2.ToArray());
            var player = new PlayerState(nodes);
            player.Read(new BinaryReader(new MemoryStream(migrated)));
            Assert.AreEqual(node, player.Node);
            Assert.AreEqual(123L, player.LocalCoins);
            Assert.AreEqual(123L, player.Coins.Sum());
        }

        [Test]
        public void Commands_AreValidated_AndFeedGuidance()
        {
            using var sim = TestWorlds.BuildSample();
            var player = sim.World.Get<PlayerState>();
            var guidance = sim.World.Get<GuidanceState>();
            long cash = player.LocalCoins;

            sim.Enqueue(new FoundCompanyCommand(new NodeId(player.Node), GuidanceAxis.Trade, 200));
            sim.Enqueue(new FoundCompanyCommand(new NodeId(player.Node), GuidanceAxis.Trade, 1_000_000));
            sim.AdvanceTurn();

            Assert.AreEqual(cash - 200, player.LocalCoins);
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<CommandRejected>().Count());
            Assert.AreEqual(GuidanceAxis.Trade, guidance.Dominant);
            Assert.Greater(guidance[GuidanceAxis.Trade].Raw, Fx.FromInt(4).Raw);
            Assert.AreEqual(player.Node, guidance.FocusNode);
        }

        [Test]
        public void BuyingFacility_MakesItsNodeDetailed()
        {
            using var sim = TestWorlds.BuildSample();
            sim.Database.Config.LodMode = 0;
            var nodes = sim.World.Get<NodeTable>();
            var facilities = sim.World.Get<FacilityTable>();
            var lod = sim.World.Get<LodState>();
            var player = sim.World.Get<PlayerState>();

            int playerRegion = nodes.AncestorAt(player.Node, WorldLevel.Region);
            int far = Enumerable.Range(0, facilities.Count).First(f => !nodes.IsAncestorOrSelf(playerRegion, facilities.Node[f]));
            sim.AdvanceTurn();
            Assert.IsFalse(lod.IsDetailed(facilities.Node[far]));
            Assert.IsTrue(lod.IsDetailed(player.Node));

            TestWorlds.BuyFacility(sim, far, GuidanceAxis.Agriculture); // paid in the facility's continent coin
            sim.AdvanceTurn(); // Commands phase runs before the Lod phase, so this same turn sees the purchase
            Assert.AreEqual(OwnerIds.Player, facilities.Owner[far]);
            Assert.IsTrue(lod.IsDetailed(facilities.Node[far]));
        }

        /// <summary>The target on a web build, where nothing may be left out to reach it (DEC-110). The Editor runs slower than the web player, so failing here does not mean the web misses it.</summary>
        const double TurnBudgetMs = 1000;

        [Test, Explicit("Builds the full 1,580-market / 4,000-item world with the shipped rules; run manually to measure.")]
        [Category("Scale")]
        [Timeout(1800000)]
        public void FullScale_ShippedRules_StayWithinTurnBudget()
        {
            var build = Stopwatch.StartNew();
            using var sim = SimulationBuilder.FromData(TestWorlds.FullScaleShipped(), 1).Build();
            build.Stop();
            Assert.AreEqual(1, sim.Database.Config.LodMode, "Every node is computed in full.");
            var markets = sim.World.Get<MarketStore>();
            Debug.Log($"[FES scale] markets={markets.MarketCount} cells={markets.CellCount} " +
                $"marketMB={markets.NativeBytes / (1024.0 * 1024.0):0.0} " +
                $"facilities={sim.World.Get<FacilityTable>().Count} merchants={sim.World.Get<AgentTable>().Count} build={build.ElapsedMilliseconds}ms");

            // Through a new year, whose systems an ordinary turn does not run, and on into a settled quarter.
            int lastTurn = SimClock.WeeksPerYear + SimClock.WeeksPerQuarter;
            var totals = new double[sim.Systems.Count];
            double all = 0, worst = 0, newYear = 0;
            int worstTurn = 0;
            string newYearSystems = "";
            sim.AdvanceTurn(); // warm-up (Burst compile, first allocation)
            while (sim.Turn <= lastTurn)
            {
                bool isNewYear = sim.Turn % SimClock.WeeksPerYear == 0;
                int turn = sim.Turn;
                var one = Stopwatch.StartNew();
                sim.AdvanceTurn();
                double ms = one.Elapsed.TotalMilliseconds;
                all += ms;
                if (ms > worst) (worst, worstTurn) = (ms, turn);
                for (int i = 0; i < totals.Length; i++) totals[i] += sim.LastSystemDuration(i).TotalMilliseconds;
                if (isNewYear)
                {
                    newYear = ms;
                    newYearSystems = SlowestSystems(sim, i => sim.LastSystemDuration(i).TotalMilliseconds);
                }
            }
            Debug.Log($"[FES scale shipped] turns=1-{lastTurn} avgTurn={all / lastTurn:0.0}ms worst={worst:0.0}ms at turn {worstTurn} newYear={newYear:0.0}ms ({newYearSystems}) | " +
                SlowestSystems(sim, i => totals[i] / lastTurn));

            Assert.AreEqual(1580, markets.MarketCount);
            Assert.AreEqual(4000, sim.Database.GoodCount);
            Assert.LessOrEqual(worst, TurnBudgetMs, $"Worst turn {worstTurn}.");
        }

        static string SlowestSystems(GameSimulation sim, System.Func<int, double> ms) =>
            string.Join(", ", sim.Systems.Select((s, i) => (s.Id, ms: ms(i))).OrderByDescending(x => x.ms).Take(8).Select(x => $"{x.Id}={x.ms:0.0}ms"));

        [Test, Explicit("Writes the full-scale data for the web benchmark player into StreamingAssets; delete the folder after building.")]
        [Category("Scale")]
        public void FullScale_WritesWebBenchmarkData()
        {
            string folder = Path.Combine(UnityEngine.Application.streamingAssetsPath, "Benchmark");
            Directory.CreateDirectory(folder);
            var data = TestWorlds.FullScaleShipped();
            foreach (string file in DataFiles.All)
                if (data.Exists(file)) File.WriteAllText(Path.Combine(folder, file), data.ReadText(file));
        }
    }
}
