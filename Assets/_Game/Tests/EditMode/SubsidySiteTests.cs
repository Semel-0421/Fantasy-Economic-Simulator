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
    /// <summary>The state opens a subsidised producer or a land project's where necessities are shortest, under its cap a node (DEC-135).</summary>
    public class SubsidySiteTests
    {
        // A new year, a rich nation 0, and one subsidy on the good of the first type that `fits`.
        static GameSimulation Ready(System.Func<FacilityDef, bool> fits, out FacilityDef def)
        {
            var sim = TestWorlds.BuildSample();
            sim.AdvanceTurn();
            while (!sim.Context.Clock.IsNewYear) sim.AdvanceTurn();
            def = sim.Database.Facilities.First(d => !d.StateOwned && d.Recipe.Outputs.Length > 0 && fits(d));
            var development = sim.World.Get<DevelopmentState>();
            sim.Database.Config.SubsidyMaxPerNation = 1;
            development.Subsidies.Clear();
            development.Subsidies.Add(new SubsidyPolicy { Nation = 0, Good = def.Recipe.Outputs[0].Good.Value, EndYear = 9_999 });
            sim.World.Get<BankState>().Reserve[0] += 10_000_000;
            return sim;
        }

        // Necessities met in permille at each of the nation's nodes; the rest at 1000.
        static void Shortage(GameSimulation sim, params (int node, int met)[] short_)
        {
            var households = sim.World.Get<HouseholdState>();
            foreach (int node in sim.World.Get<PurseState>().NationNodes[0])
            {
                households.NecessityWanted[node] = 1000;
                households.NecessityMet[node] = 1000;
            }
            foreach (var (node, met) in short_) households.NecessityMet[node] = met;
        }

        static int Count(GameSimulation sim, int node)
        {
            var facilities = sim.World.Get<FacilityTable>();
            return Enumerable.Range(0, facilities.Count).Count(f => facilities.Node[f] == node && facilities.Level[f] > 0);
        }

        static int Open(GameSimulation sim)
        {
            var facilities = sim.World.Get<FacilityTable>();
            int before = facilities.Count;
            sim.Systems.OfType<SubsidySystem>().Single().Execute(sim.Context);
            return facilities.Count > before ? facilities.Node[facilities.Count - 1] : -1;
        }

        static int[] Cities(GameSimulation sim, FacilityDef def)
        {
            var nodes = sim.World.Get<NodeTable>();
            return sim.World.Get<PurseState>().NationNodes[0].Where(n => nodes.LevelOf(n) == WorldLevel.City && FacilityPlaces.Allowed(def, nodes, n)).ToArray();
        }

        [Test]
        public void ANodeAtTheCap_IsPassedOver_ForTheNextShortest()
        {
            using var sim = Ready(d => !d.CoreOnly && d.Tier != IndustryTier.Primary, out var def);
            var cities = Cities(sim, def);
            Assert.GreaterOrEqual(cities.Length, 2);
            Shortage(sim, (cities[0], 0), (cities[1], 500));
            sim.Database.Config.StateMaxPerNode = Count(sim, cities[0]);
            Assert.AreEqual(cities[1], Open(sim));
        }

        [Test]
        public void WithNoRoomAnywhere_NoProducerOpens()
        {
            using var sim = Ready(d => !d.CoreOnly && d.Tier != IndustryTier.Primary, out _);
            sim.Database.Config.StateMaxPerNode = 0;
            Assert.AreEqual(-1, Open(sim));
        }

        [Test]
        public void AWorkshopForACore_GoesToTheCoreShortestOfNecessities()
        {
            using var sim = Ready(d => d.CoreOnly, out var def);
            var nodes = sim.World.Get<NodeTable>();
            int capital = nodes.Core[sim.World.Get<NodeBanks>().NationNode(0)];
            var seats = Cities(sim, def);
            Assert.Contains(capital, seats);
            int other = seats.FirstOrDefault(n => n != capital);
            Assume.That(other, Is.GreaterThan(0), "the nation has a second core town");
            int village = sim.World.Get<PurseState>().NationNodes[0].First(n => nodes.LevelOf(n) == WorldLevel.Village);
            Shortage(sim, (village, 0), (other, 300), (capital, 600));
            Assert.AreEqual(other, Open(sim), "not the capital, and not the village where it cannot stand");
        }

        [Test]
        public void ALandProject_PassesOverANodeAtTheCap()
        {
            using var sim = TestWorlds.BuildSample();
            sim.AdvanceTurn();
            while (!sim.Context.Clock.IsNewYear) sim.AdvanceTurn();
            var nodes = sim.World.Get<NodeTable>();
            var development = sim.World.Get<DevelopmentState>();
            int city = sim.World.Get<PurseState>().NationNodes[0].First(n => nodes.LevelOf(n) == WorldLevel.City);
            Shortage(sim, sim.World.Get<PurseState>().NationNodes[0].Select(n => (n, n == city ? 0 : 200)).ToArray()); // farmland is what the nation needs most
            sim.World.Get<BankState>().Reserve[0] += 10_000_000;
            var system = sim.Systems.OfType<StateProjectSystem>().Single();

            sim.Database.Config.StateMaxPerNode = 999;
            development.Projects.Clear();
            system.Execute(sim.Context);
            Assume.That(development.Projects.Any(p => p.Nation == 0 && p.Node == city && (p.Kind == StateProjectKind.Farmland || p.Kind == StateProjectKind.Mine)),
                "without a cap the neediest town gets the land project");

            sim.Database.Config.StateMaxPerNode = Count(sim, city);
            development.Projects.Clear();
            system.Execute(sim.Context);
            Assert.IsFalse(development.Projects.Any(p => p.Node == city && (p.Kind == StateProjectKind.Farmland || p.Kind == StateProjectKind.Mine)));
        }
    }
}
