using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Economy;
using FES.Simulation;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>Shares and land are pledged at their price; when they fall, the bank calls for margin and sells them (DEC-130).</summary>
    public class MarginTests
    {
        const int Plots = 5;

        static int GroupFacility(GameSimulation sim)
        {
            var facilities = sim.World.Get<FacilityTable>();
            return Enumerable.Range(0, facilities.Count).First(f => facilities.Owner[f] == OwnerIds.Npc && facilities.Group[f] >= 0
                && facilities.Level[f] > 0 && !sim.Database.Facilities[facilities.DefIndex[f]].StateOwned
                && sim.World.Get<NodeBanks>().NationOf(facilities.Node[f]) >= 0);
        }

        // The branch of a nation's bank with the most land for sale, so a few plots move its price little.
        static int CityBranch(GameSimulation sim)
        {
            var nodes = sim.World.Get<NodeTable>();
            var banks = sim.World.Get<NodeBanks>();
            var land = sim.World.Get<LandState>();
            return Enumerable.Range(0, nodes.Count).Where(n => banks.BranchNation(n) >= 0).OrderByDescending(n => land.Free(n)).First();
        }

        static PlayerBalanceSheet Sheet(GameSimulation sim) => new PlayerBalanceSheet(sim.World, sim.Database);

        static int Continent(GameSimulation sim, int node) => sim.World.Get<NodeTable>().Continent[node];

        static void Borrow(GameSimulation sim, long amount)
        {
            var take = new TakeLoanCommand(amount);
            Assert.IsNull(take.Validate(sim.Context).Error);
            take.Apply(sim.Context);
        }

        // Plots at the node until their price is at most `share` of what it was.
        static void CheapenLand(GameSimulation sim, int n, int permille)
        {
            var land = sim.World.Get<LandState>();
            var config = sim.Database.Config;
            long target = land.Price(n, config) * permille / 1000;
            while (land.Price(n, config) > target) land.Plots[n]++;
        }

        [Test]
        public void SharesAndLand_ArePledged_AtTheirPrice()
        {
            using var sim = TestWorlds.BuildSample();
            int f = GroupFacility(sim);
            int n = CityBranch(sim);
            int c = Continent(sim, n);
            long before = Sheet(sim).CollateralValue(c);
            sim.World.Get<ShareTable>().Add(f, ShareTable.PlayerHolder, 10);
            sim.World.Get<LandState>().PlayerPlots[n] += 2;
            var sheet = Sheet(sim);
            int fc = Continent(sim, sim.World.Get<FacilityTable>().Node[f]);
            long shares = sheet.Convert(10 * ShareBook.Price(sim.World, sim.Database, f), fc, c);
            long land = 2 * sim.World.Get<LandState>().Price(n, sim.Database.Config);
            Assert.AreEqual(before + shares + land, sheet.CollateralValue(c));
        }

        [Test]
        public void FallingLand_CallsForMargin_ThenIsSold()
        {
            using var sim = TestWorlds.BuildSample();
            int n = CityBranch(sim);
            int c = Continent(sim, n);
            var player = sim.World.Get<PlayerState>();
            var land = sim.World.Get<LandState>();
            player.Node = n;
            land.PlayerPlots[n] = Plots;
            Borrow(sim, Sheet(sim).CreditLimit(c));
            CheapenLand(sim, n, 650);

            sim.AdvanceTurn();
            var call = sim.LastTurnEvents.OfType<MarginCalled>().Single();
            Assert.AreEqual(Plots, land.PlayerPlots[n], "a call first, no sale");
            int weeks = sim.Database.Config.MarginCallWeeks;
            for (int t = 1; t < weeks; t++)
            {
                sim.AdvanceTurn();
                Assert.AreEqual(Plots, land.PlayerPlots[n], $"week {t} of the call");
            }
            long debt = Sheet(sim).Debt(c);
            sim.AdvanceTurn();
            Assert.Less(land.PlayerPlots[n], Plots, "the bank sold land");
            Assert.Greater(land.PlayerPlots[n], 0, "only what brings the debt back");
            Assert.Less(Sheet(sim).Debt(c), debt);
            var sheet = Sheet(sim);
            Assert.LessOrEqual(sheet.Debt(c), sim.Database.Config.LoanToValue.MulInt(sheet.CollateralValue(c)));
            Assert.IsTrue(sim.LastTurnEvents.OfType<CollateralSeized>().Any(e => e.Kind == CollateralKind.Land));
        }

        [Test]
        public void PayingDown_LiftsTheCall()
        {
            using var sim = TestWorlds.BuildSample();
            int n = CityBranch(sim);
            int c = Continent(sim, n);
            var player = sim.World.Get<PlayerState>();
            var land = sim.World.Get<LandState>();
            player.Node = n;
            land.PlayerPlots[n] = Plots;
            Borrow(sim, Sheet(sim).CreditLimit(c));
            CheapenLand(sim, n, 650);
            sim.AdvanceTurn();
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<MarginCalled>().Count());

            var loans = sim.World.Get<LoanTable>();
            player.CoinsAt(n) += loans.Balance[0];
            var repay = new RepayLoanCommand(loans.Balance[0] / 2);
            Assert.IsNull(repay.Validate(sim.Context).Error);
            repay.Apply(sim.Context);
            for (int t = 0; t < sim.Database.Config.MarginCallWeeks + 2; t++) sim.AdvanceTurn();
            Assert.AreEqual(Plots, land.PlayerPlots[n]);
        }

        [Test]
        public void FallingShares_AreSold_ToTheGroup()
        {
            using var sim = TestWorlds.BuildSample();
            int f = GroupFacility(sim);
            var facilities = sim.World.Get<FacilityTable>();
            var banks = sim.World.Get<NodeBanks>();
            var nodes = sim.World.Get<NodeTable>();
            int nation = banks.NationOf(facilities.Node[f]);
            int branch = Enumerable.Range(0, nodes.Count).First(x => banks.BranchNation(x) == nation);
            int c = Continent(sim, branch);
            var player = sim.World.Get<PlayerState>();
            var shares = sim.World.Get<ShareTable>();
            player.Node = branch;
            shares.Add(f, ShareTable.PlayerHolder, 40);
            facilities.Level[f] += 4; // dearer for the loan, then back
            Borrow(sim, Sheet(sim).CreditLimit(c));
            facilities.Level[f] -= 4;
            sim.World.Get<GroupTable>().Cash[facilities.Group[f]] += 1_000_000_000;

            for (int t = 0; t <= sim.Database.Config.MarginCallWeeks; t++) sim.AdvanceTurn();
            Assert.Less(shares.Held(f, ShareTable.PlayerHolder), 40);
            Assert.IsTrue(sim.LastTurnEvents.OfType<CollateralSeized>().Any(e => e.Kind == CollateralKind.Shares && e.Facility.Value == f));
        }

        [Test]
        public void AnOverdueLoan_TakesSharesAndLand()
        {
            using var sim = TestWorlds.BuildSample();
            int n = CityBranch(sim);
            int c = Continent(sim, n);
            var player = sim.World.Get<PlayerState>();
            var land = sim.World.Get<LandState>();
            player.Node = n;
            land.PlayerPlots[n] = Plots;
            Borrow(sim, Sheet(sim).CreditLimit(c) / 2);
            player.CoinsAt(n) = 0;
            var seized = Enumerable.Range(0, sim.Database.Config.LoanGraceWeeks + 3)
                .SelectMany(_ => { sim.AdvanceTurn(); return sim.LastTurnEvents.OfType<CollateralSeized>().ToList(); }).ToList();
            Assert.IsTrue(seized.Any(e => e.Kind == CollateralKind.Land));
            Assert.Less(land.PlayerPlots[n], Plots);
        }

        [Test]
        public void MarginSales_KeepTheCoins()
        {
            using var sim = TestWorlds.BuildSample();
            int n = CityBranch(sim);
            var player = sim.World.Get<PlayerState>();
            var land = sim.World.Get<LandState>();
            var currency = sim.World.Get<CurrencyState>();
            player.Node = n;
            land.PlayerPlots[n] = Plots;
            Borrow(sim, Sheet(sim).CreditLimit(Continent(sim, n)));
            CheapenLand(sim, n, 650);
            for (int t = 0; t <= sim.Database.Config.MarginCallWeeks + 1; t++)
            {
                sim.AdvanceTurn();
                for (int k = 0; k < currency.ContinentCount; k++)
                    Assert.AreEqual(currency.CoinSupply[k], MoneyLedger.ContinentMoney(sim.World, k), $"turn {t}");
            }
        }
    }
}
