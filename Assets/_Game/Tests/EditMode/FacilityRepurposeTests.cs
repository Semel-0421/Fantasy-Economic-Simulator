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
    /// <summary>A facility taken over after a bankruptcy may change its use once (DEC-125).</summary>
    public class FacilityRepurposeTests
    {
        const long Plenty = 1_000_000_000;

        static int NpcFacility(GameSimulation sim)
        {
            var facilities = sim.World.Get<FacilityTable>();
            var loans = sim.World.Get<NpcLoanTable>();
            return Enumerable.Range(0, facilities.Count).First(f => facilities.Owner[f] == OwnerIds.Npc && facilities.Level[f] > 0
                && loans.Find(NpcBorrowers.Facility(f)) < 0 && !sim.Database.Facilities[facilities.DefIndex[f]].StateOwned);
        }

        // Another type that may stand where the facility does.
        static FacilityDef OtherUse(GameSimulation sim, int f, bool withoutInputs = false)
        {
            var facilities = sim.World.Get<FacilityTable>();
            var nodes = sim.World.Get<NodeTable>();
            return sim.Database.Facilities.First(d => d.Index != facilities.DefIndex[f]
                && FacilityRepurpose.MayStand(d, nodes, facilities.Node[f]) && d.Recipe.Outputs.Length > 0
                && (!withoutInputs || d.Recipe.Inputs.Length == 0));
        }

        [Test]
        public void APlayersBankruptFacility_PassesToAFamily_ThatMayChangeItsUse()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            int f = NpcFacility(sim);
            Assert.AreEqual(0, facilities.Repurposable[f], "a facility built or kept by its owner may not");
            facilities.Owner[f] = OwnerIds.Player;
            facilities.Group[f] = sim.World.Get<GroupTable>().PlayerCompany();
            facilities.Cash[f] = -Plenty;
            facilities.NegativeCashWeeks[f] = sim.Database.Config.FacilityBankruptWeeks;
            sim.AdvanceTurn();
            Assert.AreEqual(OwnerIds.Npc, facilities.Owner[f]);
            Assert.AreEqual(1, facilities.Repurposable[f]);
        }

        [Test]
        public void TheNewOwner_ChangesItsUse_AtLevelOne_AndPaysTheBuilders()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            int f = NpcFacility(sim);
            var target = OtherUse(sim, f);
            facilities.Owner[f] = OwnerIds.Player;
            facilities.Repurposable[f] = 1;
            facilities.Level[f] = 3;
            var player = sim.World.Get<PlayerState>();
            player.Node = facilities.Node[f];
            player.CoinsAt(player.Node) = Plenty;
            var command = new RepurposeFacilityCommand(new FacilityId(f), target.Key);
            Assert.IsNull(command.Validate(sim.Context).Error);
            command.Apply(sim.Context);
            Assert.AreEqual(target.Index, facilities.DefIndex[f]);
            Assert.AreEqual(1, facilities.Level[f], "new equipment starts small");
            Assert.AreEqual(0, facilities.Repurposable[f], "once");
            Assert.AreEqual(Plenty - FacilityRepurpose.Cost(target, sim.Database.Config), player.CoinsAt(player.Node));
            Assert.IsNotNull(new RepurposeFacilityCommand(new FacilityId(f), sim.Database.Facilities[0].Key).Validate(sim.Context).Error);
        }

        [Test]
        public void AFacilityNotTakenOver_CannotChangeItsUse()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            int f = NpcFacility(sim);
            facilities.Owner[f] = OwnerIds.Player;
            var player = sim.World.Get<PlayerState>();
            player.Node = facilities.Node[f];
            player.CoinsAt(player.Node) = Plenty;
            var error = new RepurposeFacilityCommand(new FacilityId(f), OtherUse(sim, f).Key).Validate(sim.Context).Error;
            Assert.AreEqual("The facility may not change its use.", error);
        }

        [Test]
        public void AChangedFacility_MakesItsNewGoods()
        {
            using var sim = TestWorlds.BuildSample();
            var facilities = sim.World.Get<FacilityTable>();
            var flows = sim.World.Get<MarketFlows>();
            var nodes = sim.World.Get<NodeTable>();
            // A village facility, so that a type needing nothing but land may stand there.
            int f = Enumerable.Range(0, facilities.Count).First(i => facilities.Owner[i] == OwnerIds.Npc && facilities.Level[i] > 0
                && nodes.LevelOf(facilities.Node[i]) == WorldLevel.Village && !sim.Database.Facilities[facilities.DefIndex[i]].StateOwned);
            var target = OtherUse(sim, f, withoutInputs: true);
            var good = target.Recipe.Outputs[0].Good;
            Assert.IsTrue(flows.TryFindNearestMarket(facilities.Node[f], good, out _, out int cell));
            sim.AdvanceTurn(); // production lines are built
            facilities.ChangeUse(f, target.Index);
            facilities.Cash[f] = Plenty;
            var produced = sim.World.Get<DemandForecast>();
            long made = produced.Produced[cell];
            sim.AdvanceTurn();
            Assert.Greater(produced.Produced[cell], made, "the facility makes what its new recipe makes");
        }
    }
}
