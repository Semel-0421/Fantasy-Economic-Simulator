using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Simulation;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>Contracts as signed by the year's round: the term paid for is the term delivered (C39), and licenses hold (C38).</summary>
    public class ContractRuleTests
    {
        static GameSimulation SignedYear(params string[] config)
        {
            var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig(config), 1).Build();
            for (int t = 0; t < SimClock.WeeksPerYear + 1; t++) sim.AdvanceTurn();
            Assert.Greater(sim.World.Get<ContractTable>().Count, 0, "the year's round signed contracts");
            return sim;
        }

        // Delivery runs before signing, so a term of n weeks needs n turns after the turn of signing.
        [Test]
        public void AContract_RunsForTheWeeksItsDepositPaysFor()
        {
            using var sim = SignedYear();
            var contracts = sim.World.Get<ContractTable>();
            var share = sim.Database.Config.ContractDepositShare;
            int signed = sim.Turn - 1;
            for (int r = 0; r < contracts.Count; r++)
            {
                // The deposit is rounded once over the whole term, so a term is found by trying it.
                long weekly = contracts.Quantity[r] * contracts.UnitPrice[r];
                int weeks = contracts.EndTurn[r] - signed - 1;
                Assert.AreEqual(contracts.Prepaid[r], share.MulInt(weekly * weeks));
            }
        }

        // Every good needs a license by signing time and no merchant holds one, so no merchant may sign.
        [Test]
        public void AnUnlicensedMerchant_SignsForNoLicensedGood()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("license.merchant_share_permille,0"), 1).Build();
            for (int t = 0; t < SimClock.WeeksPerYear; t++) sim.AdvanceTurn();
            var policy = sim.World.Get<PolicyState>();
            for (int i = 0; i < policy.Licensed.Length; i++) policy.Licensed[i] = 1;
            sim.AdvanceTurn();
            var contracts = sim.World.Get<ContractTable>();
            for (int r = 0; r < contracts.Count; r++)
                Assert.IsFalse(ContractParty.IsMerchant(contracts.Seller[r]) || ContractParty.IsMerchant(contracts.Buyer[r]), "row " + r);
        }
    }
}
