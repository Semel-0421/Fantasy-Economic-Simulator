using System.Collections.Generic;
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
    /// <summary>Traders pay a seller the price less their margin (DEC-098); what producers decide and earn follows that.</summary>
    public class RecipeMarginTests
    {
        static long Anchor(GameDatabase db, RecipeLine[] lines) => lines.Sum(l => (long)l.Quantity * db.Good(l.Good).BasePrice);

        // A week of a level one facility with every price at its anchor.
        static long MarginAtAnchor(GameDatabase db, FacilityDef def)
        {
            var config = db.Config;
            long sold = Anchor(db, def.Recipe.Outputs);
            return config.TraderOffer(sold) - config.FacilityOutputWage.MulInt(sold)
                - Anchor(db, def.Recipe.Inputs) - Anchor(db, def.Recipe.Aids) - config.FacilityUpkeep[(int)def.Tier];
        }

        [Test]
        public void EveryRecipe_PaysAtAnchorPrices()
        {
            using var sim = TestWorlds.BuildSample();
            var db = sim.Database;
            var losing = db.Facilities.Where(d => MarginAtAnchor(db, d) < 0).Select(d => $"{d.Key} {MarginAtAnchor(db, d)}").ToList();
            UnityEngine.Debug.Log("[PERF] margins " + string.Join(", ", db.Facilities.Select(d => $"{d.Key}={MarginAtAnchor(db, d)}/{Anchor(db, d.Recipe.Outputs)}/{Anchor(db, d.Recipe.Inputs)}/{db.Config.FacilityUpkeep[(int)d.Tier]}")));
            CollectionAssert.IsEmpty(losing);
        }

        static (GameSimulation sim, int mill, int wheat, int flour) MillWithMarkets()
        {
            var sim = TestWorlds.BuildSample();
            var db = sim.Database;
            var facilities = sim.World.Get<FacilityTable>();
            var flows = sim.World.Get<MarketFlows>();
            int defIndex = db.Facilities.First(d => d.Key == "mill").Index;
            int mill = Enumerable.Range(0, facilities.Count).First(f => facilities.DefIndex[f] == defIndex);
            var def = db.Facilities[defIndex];
            Assert.IsTrue(flows.TryFindNearestMarket(facilities.Node[mill], def.Recipe.Inputs[0].Good, out _, out int wheat));
            Assert.IsTrue(flows.TryFindNearestMarket(facilities.Node[mill], def.Recipe.Outputs[0].Good, out _, out int flour));
            return (sim, mill, wheat, flour);
        }

        // Output at list price 100 pays 75 at the offer: the mill's yield covers inputs at 60, not at 95, which list price would.
        [TestCase(95, false)]
        [TestCase(60, true)]
        public void AFacilityBidsForInputs_OnlyWithinWhatItsOutputIsPaid(int inputPrice, bool bids)
        {
            var (sim, mill, wheat, flour) = MillWithMarkets();
            using (sim)
            {
                var markets = sim.World.Get<MarketStore>();
                var facilities = sim.World.Get<FacilityTable>();
                markets.Price[flour] = 100;
                markets.Price[wheat] = inputPrice;
                markets.Stock[wheat] = 0;
                markets.InputBid[wheat] = 0;
                for (int f = 0; f < facilities.Count; f++) facilities.Cash[f] = 0; // others bid for wheat too
                facilities.Cash[mill] = 10_000_000;

                sim.Systems.OfType<ProductionSystem>().Single().Execute(sim.Context);

                Assert.AreEqual(bids, markets.InputBid[wheat] > 0);
            }
        }

        [Test]
        public void AnInvestor_WeighsOutputAtTheOffer()
        {
            var (sim, mill, wheat, flour) = MillWithMarkets();
            using (sim)
            {
                var markets = sim.World.Get<MarketStore>();
                var facilities = sim.World.Get<FacilityTable>();
                var def = sim.Database.Facilities[facilities.DefIndex[mill]];
                markets.Price[flour] = 100;
                markets.Price[wheat] = 80;
                long margin = sim.Systems.OfType<InvestmentSystem>().Single().UnitMargin(facilities.Node[mill], def);
                long wage = sim.Database.Config.FacilityOutputWage.MulInt(def.Recipe.Outputs[0].Quantity * markets.GoodBasePrice[def.Recipe.Outputs[0].Good.Value]);
                Assert.AreEqual(def.Recipe.Outputs[0].Quantity * 75L - wage - def.Recipe.Inputs[0].Quantity * 80L, margin);
            }
        }

        [Test]
        public void WorkingCash_IsNotRaisedForARunThatLosesAtTheOffer()
        {
            var (sim, mill, wheat, flour) = MillWithMarkets();
            using (sim)
            {
                var markets = sim.World.Get<MarketStore>();
                markets.Price[flour] = 100;
                markets.Price[wheat] = 95;
                sim.World.Get<FacilityTable>().Cash[mill] = 0;
                Assert.AreEqual(0, sim.Systems.OfType<CreditSystem>().Single().WorkingCashGap(mill));
            }
        }

        [Test]
        public void TheAverageNecessityElasticity_IsTheStatedOne()
        {
            using var sim = TestWorlds.BuildSample();
            var config = sim.Database.Config;
            var demand = DemandParams.From(config);
            long mean = (demand.Quintile1Raw + demand.Quintile2Raw + demand.Quintile3Raw + demand.Quintile4Raw + demand.Quintile5Raw) / 5;
            Assert.AreEqual(config.NecessityElasticity.Raw, mean, 16);
            Assert.Greater(demand.Quintile1Raw, demand.Quintile5Raw);
        }
    }
}
