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
    /// <summary>The traders' margin, what they do with idle coins, and far flows paying their carriers (DEC-098).</summary>
    public class TraderMarginTests
    {
        static long Money(GameSimulation sim)
        {
            long money = 0;
            var currency = sim.World.Get<CurrencyState>();
            for (int c = 0; c < currency.ContinentCount; c++) money += MoneyLedger.ContinentMoney(sim.World, c);
            return money;
        }

        [Test]
        public void ThePlayer_SellsAtTheTradersOffer()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("markets.trader_spread_permille,300"), 42).Build();
            var player = sim.World.Get<PlayerState>();
            var markets = sim.World.Get<MarketStore>();
            Assert.IsTrue(sim.Database.TryGetGood("wheat", QualityGrade.Mid, out var wheat));
            Assert.IsTrue(sim.World.Get<MarketFlows>().TryFindNearestMarket(player.Node, wheat, out _, out int cell));
            sim.World.Get<CargoTable>().Change(CargoHolders.PlayerCart, wheat, 10);
            markets.Price[cell] = 100;
            long coins = player.LocalCoins;
            new SellGoodsCommand(wheat, 10).Apply(sim.Context);
            Assert.AreEqual(coins + 10 * 70, player.LocalCoins);
        }

        [Test]
        public void Producers_ArePaidTheOffer()
        {
            long Paid(string spread)
            {
                using var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("markets.trader_spread_permille," + spread, "credit.working_weeks,0"), 42).Build();
                var facilities = sim.World.Get<FacilityTable>();
                sim.AdvanceTurn();
                long cash = 0;
                for (int f = 0; f < facilities.Count; f++) cash += facilities.Cash[f];
                return cash;
            }
            Assert.Less(Paid("300"), Paid("0"));
        }

        [Test]
        public void IdleFloat_GoesToHouseholds_OnlyBeyondTheWeeksKept()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("traders.float_keep_weeks,8", "traders.float_payout_permille,500", "traders.loan_share_permille,0"), 42).Build();
            var purses = sim.World.Get<PurseState>();
            var cash = sim.World.Get<HouseholdCash>();
            var loans = sim.World.Get<TraderLoans>();
            foreach (int node in purses.NationNodes[0])
            {
                cash.Desired[node] = 0;
                loans.OwedHouseholds[node] = 0;
                loans.InterestHouseholds[node] = 0;
            }
            loans.OwedBank[0] = 0;
            loans.InterestBank[0] = 0;
            cash.Desired[purses.NationNodes[0][0]] = 1_000;
            var system = sim.Systems.OfType<TraderLoanSystem>().Single();

            purses.Float[0] = 8_000;
            long held = purses.NationNodes[0].Sum(n => cash.Cash[n]), money = Money(sim);
            system.Execute(sim.Context);
            Assert.AreEqual(8_000, purses.Float[0], "what trade needs stays");
            Assert.AreEqual(held, purses.NationNodes[0].Sum(n => cash.Cash[n]));

            purses.Float[0] = 10_000;
            money = Money(sim);
            system.Execute(sim.Context);
            Assert.AreEqual(9_000, purses.Float[0], "half of the excess this week");
            Assert.AreEqual(held + 1_000, purses.NationNodes[0].Sum(n => cash.Cash[n]));
            Assert.AreEqual(money, Money(sim));
        }

        [Test]
        public void FarFlow_PaysItsMargin_AsWagesAtTheOrigin()
        {
            long Wages(string share, out long moved)
            {
                using var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("lod.mode,2", "merchants.flow_wage_permille," + share), 42).Build();
                sim.AdvanceTurn();
                var nodes = sim.World.Get<NodeTable>();
                var markets = sim.World.Get<MarketStore>();
                var cash = sim.World.Get<HouseholdCash>();
                var routes = sim.World.Get<RouteTable>();
                var purses = sim.World.Get<PurseState>();
                Assert.IsTrue(sim.Database.TryGetGood("bread", QualityGrade.Mid, out var bread));
                int route = Enumerable.Range(0, routes.Count).First(r => nodes.LevelOf(routes.From[r]) == WorldLevel.Village && nodes.LevelOf(routes.To[r]) == WorldLevel.City
                    && markets.TryFindCell(routes.From[r], bread, out _) && markets.TryFindCell(routes.To[r], bread, out _));
                int from = routes.From[route], to = routes.To[route];
                Assert.IsTrue(markets.TryFindCell(from, bread, out int cell));
                Assert.IsTrue(markets.TryFindCell(to, bread, out int dear));
                int start = markets.MarketCellStart[from];
                for (int c = start; c < start + markets.MarketCellCount[from]; c++) markets.Stock[c] = 0;
                markets.Stock[cell] = 1_000;
                markets.Price[cell] = 10;
                markets.Price[dear] = 1_000;
                purses.Float[purses.NodeFloat[from]] = 1_000_000_000;
                long before = cash.Cash[from], money = Money(sim);
                sim.Context.Services.Get<IMerchantBehavior>().RunAggregate(sim.Context);
                moved = 1_000 - markets.Stock[cell];
                Assert.AreEqual(money, Money(sim));
                return cash.Cash[from] - before;
            }

            long paid = Wages("1000", out long units);
            Assert.Greater(units, 0);
            Assert.Greater(paid, 0);
            Assert.AreEqual(0, Wages("0", out _));
        }
    }
}
