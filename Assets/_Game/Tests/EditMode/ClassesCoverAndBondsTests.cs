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
    /// <summary>Every node detailed, special goods no necessity, class nodes, price cover, and the player's bonds (DEC-099 to DEC-102).</summary>
    public class ClassesCoverAndBondsTests
    {
        static long Money(GameSimulation sim)
        {
            long money = 0;
            var currency = sim.World.Get<CurrencyState>();
            for (int c = 0; c < currency.ContinentCount; c++) money += MoneyLedger.ContinentMoney(sim.World, c);
            return money;
        }

        [Test]
        public void ThePlay_DetailsEveryNode()
        {
            using var sim = TestWorlds.BuildSample();
            var lod = sim.World.Get<LodState>();
            sim.AdvanceTurn();
            for (int n = 0; n < sim.World.Get<NodeTable>().Count; n++) Assert.IsTrue(lod.IsDetailed(n));
        }

        [Test]
        public void SpecialGoods_AreNoNecessity_AtAnyGrade()
        {
            using var sim = TestWorlds.BuildSample();
            var db = sim.Database;
            foreach (string key in new[] { "spice", "silk", "fur", "amber" })
                foreach (QualityGrade grade in System.Enum.GetValues(typeof(QualityGrade)))
                {
                    Assert.IsTrue(db.TryGetGood(key, grade, out var good));
                    Assert.AreEqual(Fx.One, db.Config.LuxuryShare(db.Good(good)), key);
                }
        }

        [Test]
        public void EveryClass_HasOneCore_InsideIt()
        {
            using var sim = TestWorlds.BuildSample();
            var nodes = sim.World.Get<NodeTable>();
            for (int n = 0; n < nodes.Count; n++)
            {
                int core = nodes.Core[n];
                Assert.IsTrue(nodes.IsAncestorOrSelf(n, core), nodes.KeyOf(n));
                Assert.GreaterOrEqual(nodes.Level[core], (byte)WorldLevel.City, nodes.KeyOf(n));
                if (nodes.Level[n] >= (byte)WorldLevel.City) Assert.AreEqual(n, core);
            }
            Assert.IsTrue(nodes.TryFind("aur_n1", out var nation));
            Assert.AreEqual("aur_n1_r1_c1", nodes.KeyOf(nodes.Core[nation.Value]));
            Assert.IsTrue(nodes.TryFind("aur_n1_r2_c1_v1", out var village));
            Assert.AreEqual("aur/aur_n1/aur_n1_r2/aur_n1_r2_c1/aur_n1_r2_c1_v1", nodes.ClassPath(village.Value));
        }

        [Test]
        public void ACoreOutsideItsClass_IsRefused()
        {
            var map = new MapDef();
            map.Nodes.Add(new MapNodeDef { Key = "a", Level = WorldLevel.Continent, CoreKey = "c2" });
            map.Nodes.Add(new MapNodeDef { Key = "b", Level = WorldLevel.Continent });
            map.Nodes.Add(new MapNodeDef { Key = "n", Level = WorldLevel.Nation, ParentKey = "b" });
            map.Nodes.Add(new MapNodeDef { Key = "r", Level = WorldLevel.Region, ParentKey = "n" });
            map.Nodes.Add(new MapNodeDef { Key = "c2", Level = WorldLevel.City, ParentKey = "r" });
            Assert.Throws<System.FormatException>(() => WorldBuilder.BuildNodes(map));
        }

        static (GameSimulation sim, int nation) AtBranch()
        {
            var sim = TestWorlds.BuildSample();
            var banks = sim.World.Get<NodeBanks>();
            var nodes = sim.World.Get<NodeTable>();
            var player = sim.World.Get<PlayerState>();
            int branch = Enumerable.Range(0, nodes.Count).First(n => banks.BranchNation(n) >= 0);
            player.Node = branch;
            player.LocalCoins = 1_000_000;
            return (sim, banks.BranchNation(branch));
        }

        [Test]
        public void ThePlayer_BuysAStateBond_AndEarnsInTheAccount()
        {
            var (sim, nation) = AtBranch();
            using (sim)
            {
                var bonds = sim.World.Get<BondTable>();
                var bank = sim.World.Get<BankState>();
                var player = sim.World.Get<PlayerState>();
                long money = Money(sim), coins = player.LocalCoins, deposit = bank.PlayerDeposit[nation];
                var buy = new BuyBondCommand(BondTable.OfState(nation), 1_000);
                Assert.IsTrue(buy.Validate(sim.Context).Ok);
                buy.Apply(sim.Context);
                Assert.AreEqual(coins - 1_000, player.LocalCoins);
                Assert.AreEqual(1_000, bonds.Held(BondTable.OfState(nation), BondTable.PlayerHolder));
                Assert.AreEqual(money, Money(sim));
                sim.AdvanceTurn();
                Assert.Greater(bank.PlayerDeposit[nation], deposit);
                Assert.Less(bonds.Held(BondTable.OfState(nation), BondTable.PlayerHolder), 1_000);
            }
        }

        [Test]
        public void ThePlayer_SellsABond_ToThoseWithIdleMoney()
        {
            var (sim, nation) = AtBranch();
            using (sim)
            {
                var bonds = sim.World.Get<BondTable>();
                var cash = sim.World.Get<HouseholdCash>();
                var player = sim.World.Get<PlayerState>();
                int issuer = BondTable.OfState(nation);
                bonds.Add(issuer, BondTable.PlayerHolder, 500);
                foreach (int n in sim.World.Get<PurseState>().NationNodes[nation]) cash.Cash[n] += 1_000_000;
                long money = Money(sim), coins = player.LocalCoins;
                var sell = new SellBondCommand(issuer, 500);
                Assert.IsTrue(sell.Validate(sim.Context).Ok);
                sell.Apply(sim.Context);
                long sold = player.LocalCoins - coins;
                Assert.Greater(sold, 0);
                Assert.AreEqual(500 - sold, bonds.Held(issuer, BondTable.PlayerHolder));
                Assert.AreEqual(money, Money(sim));
                Assert.IsFalse(new SellBondCommand(issuer, 501).Validate(sim.Context).Ok, "more than held");
            }
        }

        [Test]
        public void ABond_TradesOnlyAtTheIssuersBank_AndNeverThePlayersOwnGroup()
        {
            var (sim, nation) = AtBranch();
            using (sim)
            {
                var player = sim.World.Get<PlayerState>();
                Assert.IsFalse(new BuyBondCommand(BondTable.OfState(nation == 0 ? 1 : 0), 10).Validate(sim.Context).Ok);
                Assert.IsFalse(new BuyBondCommand(BondTable.OfState(nation), player.LocalCoins + 1).Validate(sim.Context).Ok);
                var groups = sim.World.Get<GroupTable>();
                for (int g = 0; g < groups.Count; g++)
                    if (groups.IsPlayer[g] != 0) Assert.IsFalse(new BuyBondCommand(g, 10).Validate(sim.Context).Ok);
            }
        }

        [Test]
        public void ThePlayersGroups_AreNeverPaidInBills()
        {
            using var sim = TestWorlds.BuildSample();
            var agents = sim.World.Get<AgentTable>();
            var facilities = sim.World.Get<FacilityTable>();
            for (int a = 0; a < agents.Count; a++) agents.Owner[a] = OwnerIds.Player;
            for (int f = 0; f < facilities.Count; f++) facilities.Owner[f] = OwnerIds.Player;
            var purses = sim.World.Get<PurseState>();
            for (int t = 0; t < 8; t++)
            {
                for (int p = 0; p < purses.Count; p++) purses.Float[p] = 0;
                sim.AdvanceTurn();
            }
            Assert.AreEqual(0, sim.World.Get<CreditStats>().BillsIssued);
        }

        [Test]
        public void PriceCover_IsBoughtAndKeepsTheCoinsWhole()
        {
            using var sim = TestWorlds.BuildSample();
            for (int t = 0; t < 6 * 52; t++) sim.AdvanceTurn();
            Assert.Greater(sim.World.Get<MerchantTripStats>().SpentHedge, 0);
            var currency = sim.World.Get<CurrencyState>();
            for (int c = 0; c < currency.ContinentCount; c++) Assert.AreEqual(currency.CoinSupply[c], MoneyLedger.ContinentMoney(sim.World, c));
        }
    }
}
