using System.Collections.Generic;
using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Economy;
using FES.Simulation;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>Land rent, crunches that lift rates, and groups trading their shares as prices move (DEC-134).</summary>
    public class RentTests
    {
        static int PopulatedNode(GameSimulation sim)
        {
            var banks = sim.World.Get<NodeBanks>();
            var h = sim.World.Get<HouseholdState>();
            return Enumerable.Range(0, h.Population.Length).First(n => banks.NationOf(n) >= 0 && h.Population[n] > 0 && sim.World.Get<AssetMarkets>().Households(n) == n);
        }

        // Advances until a quarter has closed with the nation's exchange held at `premium`.
        static void HoldUntilQuarterEnd(GameSimulation sim, int nation, Fx premium)
        {
            var markets = sim.World.Get<AssetMarkets>();
            long published = markets.PublishedPremium[nation];
            for (int t = 0; t <= SimClock.WeeksPerQuarter; t++)
            {
                markets.Premium[nation] = premium.Raw;
                sim.AdvanceTurn();
                if (markets.PublishedPremium[nation] != published) return; // the quarter closed
            }
        }

        [Test]
        public void APlot_IsWorthYearsOfItsRent_AboveItsScarcity()
        {
            using var sim = TestWorlds.BuildSample();
            var land = sim.World.Get<LandState>();
            var config = sim.Database.Config;
            int n = PopulatedNode(sim);
            long rent = land.YearRent(n, config);
            Assert.Greater(rent, 0);
            long scarcity = config.LandValue[sim.World.Get<NodeTable>().Level[n]] * land.Plots[n] / System.Math.Max(1, land.Free(n));
            Assert.AreEqual(scarcity + config.LandRentYears * rent, land.Fundamental(n, config));
        }

        [Test]
        public void ThePlayersPlots_EarnRent_FromTheHouseholds()
        {
            using var sim = TestWorlds.BuildSample();
            var land = sim.World.Get<LandState>();
            var bank = sim.World.Get<BankState>();
            int n = PopulatedNode(sim);
            int nation = sim.World.Get<NodeBanks>().NationOf(n);
            land.PlayerPlots[n] = 3;
            long deposit = bank.PlayerDeposit[nation];
            long week = 3 * land.YearRent(n, sim.Database.Config) / SimClock.WeeksPerYear;
            Assert.Greater(week, 0);
            sim.AdvanceTurn();
            long interest = sim.Database.Config.DepositInterestPerYear.MulInt(deposit) / SimClock.WeeksPerYear;
            Assert.AreEqual(deposit + interest + week, bank.PlayerDeposit[nation], 2);
        }

        [Test]
        public void ACrunch_LiftsLoanRates()
        {
            using var sim = TestWorlds.BuildSample();
            var config = sim.Database.Config;
            Assert.AreEqual(config.LoanInterestPerYear * config.RateSpikeMultiplier, FinanceRules.LoanRate(config, Fx.One, FinancialEventKind.CreditCrunch));
        }

        [Test]
        public void OnADearExchange_GroupsListTheirShares()
        {
            using var sim = TestWorlds.BuildSample();
            var shares = sim.World.Get<ShareTable>();
            var facilities = sim.World.Get<FacilityTable>();
            int before = Enumerable.Range(0, facilities.Count).Sum(f => shares.Outside(f));
            HoldUntilQuarterEnd(sim, 0, Fx.FromPermille(1600));
            int after = Enumerable.Range(0, facilities.Count).Sum(f => shares.Outside(f));
            Assert.Greater(after, before);
        }

        [Test]
        public void OnACheapExchange_GroupsBuyBack()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("credit.working_weeks,4", "credit.company_debt_permille,0"), 1).Build();
            for (int t = 0; t < 30; t++) sim.AdvanceTurn();
            var shares = sim.World.Get<ShareTable>();
            var groups = sim.World.Get<GroupTable>();
            int held = Enumerable.Range(0, shares.Count.Length).Where(r => ShareTable.IsHouseholds(shares.Holder[r], out int nation) && nation == 0).Sum(r => shares.Count[r]);
            Assert.Greater(held, 0);
            for (int g = 0; g < groups.Count; g++) groups.Cash[g] += 1_000_000;
            HoldUntilQuarterEnd(sim, 0, Fx.FromPermille(600));
            int after = Enumerable.Range(0, shares.Count.Length).Where(r => ShareTable.IsHouseholds(shares.Holder[r], out int nation) && nation == 0).Sum(r => shares.Count[r]);
            Assert.Less(after, held);
        }
    }
}
