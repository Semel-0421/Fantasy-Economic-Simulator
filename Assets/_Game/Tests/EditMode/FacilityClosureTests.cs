using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Simulation;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>A facility that loses money two years in a row closes (DEC-065).</summary>
    public class FacilityClosureTests
    {
        const long Fortune = 1_000_000_000, Working = 1_000_000;

        static int Pick(GameSimulation sim, bool stateOwned)
        {
            var facilities = sim.World.Get<FacilityTable>();
            var loans = sim.World.Get<NpcLoanTable>();
            return Enumerable.Range(0, facilities.Count).First(f =>
                facilities.Owner[f] == OwnerIds.Npc && loans.Find(NpcBorrowers.Facility(f)) < 0
                && sim.Database.Facilities[facilities.DefIndex[f]].StateOwned == stateOwned);
        }

        // Runs a year with the facility's book written so that the year shows this result. Its cash is kept up, so that
        // it never goes bankrupt instead.
        static void Year(GameSimulation sim, int f, long result)
        {
            var facilities = sim.World.Get<FacilityTable>();
            var books = sim.World.Get<IncomeTaxState>();
            int year = sim.Context.Clock.Year;
            sim.AdvanceTurn(); // the first turn opens the books
            while (sim.Context.Clock.Year == year)
            {
                if (facilities.Level[f] > 0 && facilities.Cash[f] < Working) facilities.Cash[f] = Working;
                if (books.FacilityStart.Length > f) books.FacilityStart[f] = facilities.Cash[f] - result;
                sim.AdvanceTurn();
            }
            sim.AdvanceTurn(); // the new year's turn settles the books
        }

        [Test]
        public void TwoLosingYears_CloseIt()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("facility.close_loss_years,2"), 1).Build();
            int f = Pick(sim, false);
            var facilities = sim.World.Get<FacilityTable>();
            Year(sim, f, -Fortune);
            Assert.Greater(facilities.Level[f], 0, "one bad year is not enough");
            Year(sim, f, -Fortune);
            Assert.AreEqual(0, facilities.Level[f]);
            Assert.AreEqual(0, facilities.Cash[f]);
        }

        [Test]
        public void AGoodYear_BetweenLosses_KeepsItOpen()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("facility.close_loss_years,2"), 1).Build();
            int f = Pick(sim, false);
            Year(sim, f, -Fortune);
            Year(sim, f, Fortune);
            Year(sim, f, -Fortune);
            Assert.Greater(sim.World.Get<FacilityTable>().Level[f], 0);
        }

        // The state founds no new mines, so closing one would end its output for good.
        [Test]
        public void StateFacilities_StayOpen()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("facility.close_loss_years,2"), 1).Build();
            int f = Pick(sim, true);
            Year(sim, f, -Fortune);
            Year(sim, f, -Fortune);
            Assert.Greater(sim.World.Get<FacilityTable>().Level[f], 0);
        }

        [Test]
        public void AClosedFacility_CostsNothing_AndCannotBeBought()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("facility.close_loss_years,2"), 1).Build();
            int f = Pick(sim, false);
            var facilities = sim.World.Get<FacilityTable>();
            Year(sim, f, -Fortune);
            Year(sim, f, -Fortune);
            Assert.AreEqual(0, facilities.Level[f]);
            sim.AdvanceTurn();
            Assert.AreEqual(0, facilities.Cash[f], "no upkeep, wages or sales");
            sim.World.Get<PlayerState>().Node = facilities.Node[f];
            var buy = new BuyFacilityCommand(new FacilityId(f), long.MaxValue / 4, default);
            Assert.AreEqual("The facility is closed.", buy.Validate(sim.Context).Error);
        }
    }
}
