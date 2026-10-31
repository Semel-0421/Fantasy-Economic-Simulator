using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.Simulation;
using NUnit.Framework;

namespace FES.Tests
{
    public class InvestmentTests
    {
        [Test]
        public void Investment_FoundsAndExpands_WithoutMakingCoins()
        {
            using var sim = TestWorlds.BuildSample();
            var nodes = sim.World.Get<FES.World.NodeTable>();
            int continent = nodes.Continent[sim.World.Get<PlayerState>().Node];
            var facilities = sim.World.Get<FacilityTable>();
            int atStart = facilities.Count, founded = 0;
            for (int t = 0; t < 3 * SimClock.WeeksPerYear; t++)
            {
                sim.AdvanceTurn();
                founded += sim.LastTurnEvents.OfType<FacilityFounded>().Count();
            }
            Assert.Greater(founded, 0);
            Assert.GreaterOrEqual(facilities.Count, atStart + founded);
            Assert.Greater(MoneyLedger.ContinentMoney(sim.World, continent), 0);
            foreach (var e in Enumerable.Range(0, facilities.Count))
                Assert.LessOrEqual(facilities.Level[e], sim.Database.Config.FacilityMaxLevel);
        }

        [Test]
        public void Expansion_OutputGrowsFasterThanTheLevel()
        {
            using var sim = TestWorlds.BuildSample();
            var config = sim.Database.Config;
            config.FacilityExpansionBonus = Fx.FromPermille(250);
            Assert.AreEqual(Fx.One, FacilityExpansion.OutputScale(1, config));
            Assert.AreEqual(Fx.FromPermille(2500), FacilityExpansion.OutputScale(2, config));
            Assert.AreEqual(Fx.FromInt(10), FacilityExpansion.OutputScale(5, config));
        }

        [Test]
        public void PublicWages_GoToTownsShortOfMoney()
        {
            using var sim = TestWorlds.BuildSample();
            var nodes = sim.World.Get<FES.World.NodeTable>();
            var households = sim.World.Get<HouseholdCash>();
            int nation = 0;
            int town = sim.World.Get<PurseState>().NationNodes[nation].First(n => nodes.LevelOf(n) == WorldLevel.City);
            int village = sim.World.Get<PurseState>().NationNodes[nation].First(n => nodes.LevelOf(n) == WorldLevel.Village);
            households.DesiredNecessity[town] = 1000;
            long moved = households.Cash[town];
            households.Cash[town] = 0;
            households.Cash[village] += moved;
            long townBefore = households.Cash[town], villageBefore = households.Cash[village];
            MoneyLedger.PayNationTownWages(sim.World, sim.Database.Config, nation, 500);
            Assert.Greater(households.Cash[town], townBefore);
            Assert.AreEqual(villageBefore, households.Cash[village]);
        }

        [Test]
        public void Households_BuyNecessitiesFirst_ThenLuxuriesAsMoneyGrows()
        {
            var cash = new HouseholdCash(1, 1);
            try
            {
                cash.DesiredNecessity[0] = 100;
                cash.Desired[0] = 200;
                cash.Cash[0] = 50;
                Assert.AreEqual(Fx.FromPermille(500).Raw, cash.NecessityBudgetFactor(0, 1));
                Assert.AreEqual(0, cash.LuxuryBudgetFactor(0, 1));
                cash.Cash[0] = 150;
                Assert.AreEqual(Fx.OneRaw, cash.NecessityBudgetFactor(0, 1));
                Assert.AreEqual(Fx.FromPermille(500).Raw, cash.LuxuryBudgetFactor(0, 1));
                cash.Cash[0] = 200;
                Assert.AreEqual(Fx.OneRaw, cash.LuxuryBudgetFactor(0, 1));
            }
            finally
            {
                cash.Dispose();
            }
        }

        [Test]
        public void Merchants_BuyBetterVehicles_FromTheMarket_KeepingCoinsConserved()
        {
            using var sim = TestWorlds.BuildSample();
            var agents = sim.World.Get<AgentTable>();
            var config = sim.Database.Config;
            Assert.Less(config.MerchantVehicleCapacity[MerchantVehicles.Pack].Raw, config.MerchantVehicleCapacity[MerchantVehicles.Wagon].Raw);
            int bought = 0;
            for (int t = 0; t < 3 * SimClock.WeeksPerYear; t++)
            {
                sim.AdvanceTurn();
                bought += sim.LastTurnEvents.OfType<MerchantVehicleBought>().Count();
            }
            Assert.Greater(bought, 0);
            var kinds = Enumerable.Range(0, agents.Count).Select(a => agents.Vehicle[a]).Distinct().Count();
            Assert.Greater(kinds, 1, "merchants differ in what they carry with");
            var currency = sim.World.Get<CurrencyState>();
            for (int c = 0; c < currency.ContinentCount; c++)
                Assert.AreEqual(currency.CoinSupply[c], MoneyLedger.ContinentMoney(sim.World, c));
        }

        [Test]
        public void Order_ReservesStock_ThenIsCollectedAfterTheLeadTime()
        {
            using var sim = TestWorlds.BuildSample();
            var markets = sim.World.Get<MarketStore>();
            var agents = sim.World.Get<AgentTable>();
            var orders = sim.World.Get<OrderTable>();
            Assert.IsTrue(sim.Database.TryGetGood("cart", QualityGrade.Mid, out var cart));
            int home = agents.Home[0];
            int m = home;
            while (!markets.TryFindCell(m, cart, out _)) m = sim.World.Get<FES.World.NodeTable>().Parent[m];
            markets.TryFindCell(m, cart, out int cell);
            markets.Stock[cell] = 5;
            agents.Cash[0] = 1_000_000;
            var currency = sim.World.Get<CurrencyState>();
            int continent = sim.World.Get<FES.World.NodeTable>().Continent[home];
            long money = MoneyLedger.ContinentMoney(sim.World, continent);
            agents.Vehicle[0] = MerchantVehicles.Pack;
            orders.Add(0, home, cell, 1, 0, MerchantVehicles.Cart);
            int lead = sim.Database.Config.OrderLeadWeeks;
            sim.AdvanceTurn();
            Assert.AreEqual(MerchantVehicles.Pack, agents.Vehicle[0], "not before the lead time");
            for (int t = 0; t < lead + 1; t++) sim.AdvanceTurn();
            Assert.AreEqual(MerchantVehicles.Cart, agents.Vehicle[0]);
            Assert.AreEqual(money, MoneyLedger.ContinentMoney(sim.World, continent));
        }

        [Test]
        public void VehicleCosts_RiseWithGrade()
        {
            using var sim = TestWorlds.BuildSample();
            var config = sim.Database.Config;
            Assert.Less(config.MerchantVehicleCost[0].Raw, config.MerchantVehicleCost[2].Raw);
            Assert.Less(config.MerchantVehicleDanger[0].Raw, config.MerchantVehicleDanger[2].Raw);
            Assert.Less(config.MerchantVehiclePremium[0].Raw, config.MerchantVehiclePremium[2].Raw);
        }

        [Test]
        public void RawProducers_ListToolsAsAids()
        {
            using var sim = TestWorlds.BuildSample();
            foreach (var def in sim.Database.Facilities)
                if (def.Tier == IndustryTier.Primary)
                {
                    Assert.IsEmpty(def.Recipe.Inputs, def.Key);
                    Assert.IsNotEmpty(def.Recipe.Aids, def.Key);
                }
        }
    }
}
