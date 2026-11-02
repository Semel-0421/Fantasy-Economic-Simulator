using System.Collections.Generic;
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
    public class ProductionTests
    {
        static IEnumerable<int> FacilitiesOf(GameSimulation sim, IndustryTier tier)
        {
            var facilities = sim.World.Get<FacilityTable>();
            return Enumerable.Range(0, facilities.Count).Where(f => sim.Database.Facilities[facilities.DefIndex[f]].Tier == tier);
        }

        // A farm for sale with a single output; buying it takes the player there.
        static int FarmForSale(GameSimulation sim)
        {
            var facilities = sim.World.Get<FacilityTable>();
            return FacilitiesOf(sim, IndustryTier.Primary).First(f =>
            {
                var def = sim.Database.Facilities[facilities.DefIndex[f]];
                return !def.StateOwned && def.Recipe.Outputs.Length == 1;
            });
        }

        static List<FacilityBankrupt> RunCollecting(GameSimulation sim, int turns)
        {
            var bankrupt = new List<FacilityBankrupt>();
            for (int t = 0; t < turns; t++)
            {
                sim.AdvanceTurn();
                bankrupt.AddRange(sim.LastTurnEvents.OfType<FacilityBankrupt>());
            }
            return bankrupt;
        }

        [Test]
        public void Facilities_StartWithTheirTiersCash()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            var config = sim.Database.Config;
            for (int f = 0; f < facilities.Count; f++)
                Assert.AreEqual(config.FacilityStartCash[(int)sim.Database.Facilities[facilities.DefIndex[f]].Tier], facilities.Cash[f]);
        }

        static long Upkeep(GameSimulation sim, IndustryTier tier) => sim.Database.Config.FacilityUpkeep[(int)tier];

        [Test]
        public void NpcOutput_IsSoldAtTheCurrentPrice()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            long start = sim.Database.Config.FacilityStartCash[(int)IndustryTier.Primary];
            sim.AdvanceTurn();
            foreach (int f in FacilitiesOf(sim, IndustryTier.Primary))
                Assert.Greater(facilities.Cash[f], start, $"facility {f}");
        }

        [Test]
        public void FacilityWithoutInputStock_ProducesNothing_ButPaysUpkeep()
        {
            // Smithies sit in cities, which no mine supplies with ore while merchants stay idle.
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("credit.working_weeks,0"), 42)
                .ConfigureServices(s => s.Set<IMerchantBehavior>(new IdleMerchantBehavior())).Build();
            var facilities = sim.World.Get<FacilityTable>();
            var smithies = FacilitiesOf(sim, IndustryTier.Secondary).ToList();
            Assume.That(smithies, Is.Not.Empty);
            for (int t = 0; t < 10; t++) sim.AdvanceTurn();

            var markets = sim.World.Get<MarketStore>();
            Assert.IsTrue(sim.Database.TryGetGood("iron_tools", QualityGrade.Mid, out var tools));
            long expected = sim.Database.Config.FacilityStartCash[(int)IndustryTier.Secondary] - 10 * Upkeep(sim, IndustryTier.Secondary);
            foreach (int f in smithies)
            {
                Assert.AreEqual(expected, facilities.Cash[f]);
                Assert.IsTrue(markets.TryFindCell(facilities.Node[f], tools, out int cell));
                Assert.AreEqual(0, markets.Stock[cell]);
            }
        }

        [Test]
        public void FacilityWithInputStock_BuysAndProduces()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            long idle = sim.Database.Config.FacilityStartCash[(int)IndustryTier.Tertiary] - 10 * Upkeep(sim, IndustryTier.Tertiary);
            var markets = sim.World.Get<FES.Economy.MarketStore>();
            foreach (int f in FacilitiesOf(sim, IndustryTier.Tertiary))
                foreach (var line in sim.Database.Facilities[facilities.DefIndex[f]].Recipe.Inputs)
                    if (markets.TryFindCell(facilities.Node[f], line.Good, out int cell)) markets.Stock[cell] = 100000;
            for (int t = 0; t < 10; t++) sim.AdvanceTurn();
            Assert.IsTrue(FacilitiesOf(sim, IndustryTier.Tertiary).Any(f => facilities.Cash[f] != idle));
        }

        [Test]
        public void FacilityShortOfInput_BidsItsPriceUp()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            var markets = sim.World.Get<FES.Economy.MarketStore>();
            int f = FacilitiesOf(sim, IndustryTier.Tertiary).First();
            var input = sim.Database.Facilities[facilities.DefIndex[f]].Recipe.Inputs[0].Good;
            Assert.IsTrue(markets.TryFindCell(facilities.Node[f], input, out int cell));
            markets.Stock[cell] = 0;
            int before = markets.Price[cell];
            sim.AdvanceTurn();
            Assert.Greater(markets.Price[cell], before);
            Assert.AreEqual(0, markets.InputBid[cell], "a price update uses the bid up");
        }

        [Test]
        public void FacilityBelowZeroCash_GoesBankruptAfterTheGraceWeeks()
        {
            // Without NPC loans, which would cover the shortfall.
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile,
                "key,value\nbank.npc_max_reserve_share_permille,0\n"), 42).Build();
            var facilities = sim.World.Get<FacilityTable>();
            var bakeries = FacilitiesOf(sim, IndustryTier.Tertiary).ToList();
            foreach (int f in bakeries) facilities.Cash[f] = 0; // cannot buy inputs, so upkeep keeps it below zero
            int weeks = sim.Database.Config.FacilityBankruptWeeks;

            var early = RunCollecting(sim, weeks - 1);
            Assert.IsEmpty(early);
            Assert.IsTrue(bakeries.All(f => facilities.Cash[f] < 0 && facilities.NegativeCashWeeks[f] == weeks - 1));

            var bankrupt = RunCollecting(sim, 1);
            CollectionAssert.AreEquivalent(bakeries, bankrupt.Select(b => b.Facility.Value));
            Assert.IsTrue(bankrupt.All(b => !b.WasPlayerOwned && b.WarehouseClaimUntil == -1));
            long start = sim.Database.Config.FacilityStartCash[(int)IndustryTier.Tertiary];
            Assert.IsTrue(bakeries.All(f => facilities.Cash[f] == start && facilities.NegativeCashWeeks[f] == 0));
        }

        [Test]
        public void PlayerOutput_GoesToTheWarehouse()
        {
            using var sim = TestWorlds.BuildSample();
            sim.Database.Config.FacilityOutputWage = Fx.Zero;
            var facilities = sim.World.Get<FacilityTable>();
            int farm = FarmForSale(sim);
            long cash = facilities.Cash[farm];
            TestWorlds.BuyFacility(sim, farm, GuidanceAxis.Agriculture);
            sim.AdvanceTurn();

            var stored = new List<CargoItem>();
            sim.View.GetWarehouse(new FacilityId(farm), stored);
            var output = sim.Database.Facilities[facilities.DefIndex[farm]].Recipe.Outputs[0];
            Assert.AreEqual(output.Good.BaseIndex, stored.Single().Good.BaseIndex); // a specialty may come out a grade higher
            Assert.Greater(stored.Single().Quantity, 0);
            Assert.AreEqual(cash - Upkeep(sim, IndustryTier.Primary), facilities.Cash[farm]); // nothing sold: the warehouse had room
        }

        [Test]
        public void PlayerOutput_OverflowIsSold()
        {
            const string config = "key,value\ncargo.warehouse.max_weight,20\ncargo.warehouse.max_size,20\n";
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile, config), 42).Build();
            var facilities = sim.World.Get<FacilityTable>();
            int farm = FarmForSale(sim);
            long cash = facilities.Cash[farm];
            TestWorlds.BuyFacility(sim, farm, GuidanceAxis.Agriculture);
            sim.AdvanceTurn();

            var load = sim.View.WarehouseLoad(new FacilityId(farm));
            Assert.LessOrEqual(load.Weight, 20);
            Assert.LessOrEqual(load.Size, 20);
            Assert.Greater(load.Weight, 0);
            Assert.Greater(facilities.Cash[farm], cash - Upkeep(sim, IndustryTier.Primary));
        }

        [Test]
        public void LostPlayerFacility_LeavesItsWarehouseClaimableForAWhile()
        {
            using var sim = TestWorlds.BuildSample();
            var view = sim.View;
            var facilities = sim.World.Get<FacilityTable>();
            int bakery = FacilitiesOf(sim, IndustryTier.Tertiary).First();
            sim.World.Get<PlayerState>().Node = facilities.Node[bakery];
            var id = new FacilityId(bakery);
            TestWorlds.BuyFacility(sim, id.Value, GuidanceAxis.Trade);
            sim.AdvanceTurn();
            sim.Actions.MoveFacilityCash(id, -facilities.Cash[bakery]);
            sim.AdvanceTurn();
            Assert.Less(facilities.Cash[bakery], 0);
            var wheat = new GoodId(0);
            sim.World.Get<CargoTable>().Change(CargoHolders.Warehouse(id), wheat, 3);

            var bankrupt = RunCollecting(sim, sim.Database.Config.FacilityBankruptWeeks).Where(b => b.Facility == id).ToList();
            Assert.AreEqual(1, bankrupt.Count);
            Assert.IsTrue(bankrupt[0].WasPlayerOwned);
            Assert.AreEqual(bankrupt[0].Turn + sim.Database.Config.LostWarehouseWeeks, bankrupt[0].WarehouseClaimUntil);
            Assert.AreEqual(OwnerIds.Npc, facilities.Owner[bakery]);

            // During the claim the player may only take goods out.
            sim.Actions.MoveCargo(id, wheat, 1, toWarehouse: false);
            sim.Actions.MoveCargo(id, wheat, 1, toWarehouse: true);
            sim.Actions.MoveFacilityCash(id, 10);
            sim.AdvanceTurn();
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<CargoMoved>().Single().Moved);
            Assert.AreEqual(2, sim.LastTurnEvents.OfType<CommandRejected>().Count());
            var stored = new List<CargoItem>();
            view.GetWarehouse(id, stored);
            Assert.AreEqual(2, stored.Single(i => i.Good == wheat).Quantity);

            var cleared = new List<LostWarehouseCleared>();
            while (sim.Turn <= bankrupt[0].WarehouseClaimUntil)
            {
                sim.AdvanceTurn();
                cleared.AddRange(sim.LastTurnEvents.OfType<LostWarehouseCleared>());
            }
            Assert.AreEqual(id, cleared.Single().Facility);
            view.GetWarehouse(id, stored);
            Assert.IsEmpty(stored);
            Assert.AreEqual(-1, facilities.WarehouseClaimUntil[bakery]);
        }

        [Test]
        public void ANewOperatorsBankruptcy_KeepsThePlayersClaimOnTheWarehouse()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            int bakery = FacilitiesOf(sim, IndustryTier.Tertiary).First();
            int until = sim.Turn + 100;
            facilities.WarehouseClaimUntil[bakery] = until;
            facilities.Owner[bakery] = OwnerIds.Npc;
            facilities.Cash[bakery] = -1_000_000_000;
            facilities.NegativeCashWeeks[bakery] = sim.Database.Config.FacilityBankruptWeeks;

            var bankrupt = RunCollecting(sim, 1).Where(b => b.Facility.Value == bakery).ToList();

            Assert.AreEqual(1, bankrupt.Count);
            Assert.AreEqual(until, bankrupt[0].WarehouseClaimUntil);
            Assert.AreEqual(until, facilities.WarehouseClaimUntil[bakery]);
        }

        [Test]
        public void Player_MovesCashAndCargo_AtOwnFacility()
        {
            using var sim = TestWorlds.BuildSample();
            sim.Database.Config.FacilityOutputWage = Fx.Zero;
            var view = sim.View;
            var facilities = sim.World.Get<FacilityTable>();
            int farm = FarmForSale(sim);
            var id = new FacilityId(farm);
            long facilityCash = facilities.Cash[farm];
            sim.World.Get<PlayerState>().Node = facilities.Node[farm];
            sim.Actions.MoveFacilityCash(id, 50); // rejected: not owned yet
            TestWorlds.BuyFacility(sim, id.Value, GuidanceAxis.Agriculture);
            sim.AdvanceTurn();
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<CommandRejected>().Count());

            sim.World.Get<PlayerState>().LocalCoins += 1_000;
            long coins = view.PlayerCash;
            var stored = new List<CargoItem>();
            view.GetWarehouse(id, stored);
            var good = stored.Single().Good;
            sim.Actions.MoveFacilityCash(id, 50);
            sim.Actions.MoveCargo(id, good, 3, toWarehouse: false);
            sim.Actions.MoveCargo(id, good, 1, toWarehouse: true);
            sim.AdvanceTurn();
            Assert.AreEqual(coins - 50, view.PlayerCash);
            Assert.AreEqual(facilityCash + 50 - 2 * Upkeep(sim, IndustryTier.Primary), facilities.Cash[farm]);
            var moved = sim.LastTurnEvents.OfType<CargoMoved>().ToList();
            Assert.AreEqual(new[] { 3, 1 }, moved.Select(m => m.Moved).ToArray());
            var cart = new List<CargoItem>();
            view.GetCart(cart);
            Assert.AreEqual(2, cart.Single().Quantity);
        }

        [Test]
        public void FacilityTableV1Save_GetsStartingCash()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            var expected = Enumerable.Range(0, facilities.Count).Select(f => facilities.Cash[f]).ToArray();
            using var v1 = new MemoryStream();
            using (var writer = new BinaryWriter(v1))
            {
                facilities.Node.Write(writer);
                facilities.DefIndex.Write(writer);
                facilities.Owner.Write(writer);
                facilities.Mode.Write(writer);
                facilities.Level.Write(writer);
                facilities.Efficiency.Write(writer);
            }
            var migrated = sim.Migrations.Upgrade(FacilityTable.SectionKey, 1, 3, v1.ToArray());
            facilities.Read(new BinaryReader(new MemoryStream(migrated)));
            Assert.AreEqual(FacilityTableV1Migration.UnsetCash, facilities.Cash[0]);

            sim.Systems.OfType<ProductionSystem>().Single().OnLoaded(sim.Context);
            CollectionAssert.AreEqual(expected, Enumerable.Range(0, facilities.Count).Select(f => facilities.Cash[f]).ToArray());
        }

        [Test]
        public void Loading_KeepsACashOfMinusOne_AndConjuresNoCoins()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            int continent = sim.World.Get<NodeTable>().Continent[facilities.Node[0]];
            facilities.Cash[0] = -1;
            long money = MoneyLedger.ContinentMoney(sim.World, continent);
            using var buffer = new MemoryStream();
            sim.Save(buffer);
            buffer.Position = 0;
            sim.Load(buffer);
            Assert.AreEqual(-1, facilities.Cash[0]);
            Assert.AreEqual(money, MoneyLedger.ContinentMoney(sim.World, continent));
        }

        [Test]
        public void Recipes_RejectTheSameGoodTwiceInOneRole()
        {
            const string recipes = "recipe,role,good,grade,qty\nr,in,wheat,Mid,1\nr,in,wheat,Mid,2\nr,out,bread,Mid,1\n";
            var e = Assert.Throws<System.FormatException>(() => GameDatabase.Load(TestWorlds.SampleWith(GameDatabase.RecipesFile, recipes)));
            StringAssert.Contains("twice", e.Message);
        }
    }
}
