using System.IO;
using System.Linq;
using FES.Agents;
using FES.Core;
using FES.Economy;
using FES.Simulation;
using FES.World;
using NUnit.Framework;

namespace FES.Tests
{
    /// <summary>Facilities are split into shares that groups list, households and the player buy, and dividends follow (DEC-129).</summary>
    public class ShareTests
    {
        const long Plenty = 1_000_000_000;

        static int GroupFacility(GameSimulation sim)
        {
            var facilities = sim.World.Get<FacilityTable>();
            return Enumerable.Range(0, facilities.Count).First(f => facilities.Owner[f] == OwnerIds.Npc && facilities.Group[f] >= 0
                && facilities.Level[f] > 0 && !sim.Database.Facilities[facilities.DefIndex[f]].StateOwned
                && sim.World.Get<NodeBanks>().NationOf(facilities.Node[f]) >= 0);
        }

        // The player at a branch of the facility's nation's bank, with coins.
        static int AtTheExchange(GameSimulation sim, int f)
        {
            var nodes = sim.World.Get<NodeTable>();
            var banks = sim.World.Get<NodeBanks>();
            int nation = banks.NationOf(sim.World.Get<FacilityTable>().Node[f]);
            int branch = Enumerable.Range(0, nodes.Count).First(n => banks.BranchNation(n) == nation);
            var player = sim.World.Get<PlayerState>();
            player.Node = branch;
            player.CoinsAt(branch) = Plenty;
            return branch;
        }

        [Test]
        public void AShare_IsTheFacilitysAskingPrice_OverItsShares()
        {
            using var sim = TestWorlds.BuildSample();
            int f = GroupFacility(sim);
            long asking = BuyFacilityCommand.AskingPrice(sim.World, sim.Database, f);
            Assert.AreEqual(asking / sim.Database.Config.FacilityShares, ShareBook.Price(sim.World, sim.Database, f));
        }

        [Test]
        public void ThePlayer_BuysShares_FromTheGroup_AndSellsThemBack()
        {
            using var sim = TestWorlds.BuildSample();
            int f = GroupFacility(sim);
            int branch = AtTheExchange(sim, f);
            var shares = sim.World.Get<ShareTable>();
            var groups = sim.World.Get<GroupTable>();
            int group = sim.World.Get<FacilityTable>().Group[f];
            long price = ShareBook.Price(sim.World, sim.Database, f), cash = groups.Cash[group];
            var buy = new BuyShareCommand(new FacilityId(f), 10);
            Assert.IsNull(buy.Validate(sim.Context).Error);
            buy.Apply(sim.Context);
            Assert.AreEqual(10, shares.Held(f, ShareTable.PlayerHolder));
            Assert.AreEqual(Plenty - 10 * price, sim.World.Get<PlayerState>().CoinsAt(branch));
            Assert.AreEqual(cash + 10 * price, groups.Cash[group]);

            groups.Cash[group] += Plenty;
            var sell = new SellShareCommand(new FacilityId(f), 4);
            Assert.IsNull(sell.Validate(sim.Context).Error);
            sell.Apply(sim.Context);
            Assert.AreEqual(6, shares.Held(f, ShareTable.PlayerHolder));
            Assert.IsNotNull(new SellShareCommand(new FacilityId(f), 7).Validate(sim.Context).Error, "holds only six");
        }

        [Test]
        public void TheGroup_KeepsControl()
        {
            using var sim = TestWorlds.BuildSample();
            int f = GroupFacility(sim);
            AtTheExchange(sim, f);
            int most = sim.Database.Config.FacilityMaxListedShares;
            Assert.IsNull(new BuyShareCommand(new FacilityId(f), most).Validate(sim.Context).Error);
            Assert.AreEqual("The group keeps control of it.", new BuyShareCommand(new FacilityId(f), most + 1).Validate(sim.Context).Error);
        }

        [Test]
        public void SharesAreTraded_OnlyAtTheNationsBank()
        {
            using var sim = TestWorlds.BuildSample();
            int f = GroupFacility(sim);
            var nodes = sim.World.Get<NodeTable>();
            var player = sim.World.Get<PlayerState>();
            player.Node = Enumerable.Range(0, nodes.Count).First(n => sim.World.Get<NodeBanks>().BranchNation(n) < 0);
            player.CoinsAt(player.Node) = Plenty;
            Assert.AreEqual("Shares of this facility trade at its nation's bank.", new BuyShareCommand(new FacilityId(f), 1).Validate(sim.Context).Error);
        }

        [Test]
        public void Holders_GetTheirPartOfTheDividends()
        {
            using var sim = TestWorlds.BuildSample();
            sim.AdvanceTurn(); // the books open on the first turn
            int f = GroupFacility(sim);
            var facilities = sim.World.Get<FacilityTable>();
            var shares = sim.World.Get<ShareTable>();
            var bank = sim.World.Get<BankState>();
            var books = sim.World.Get<IncomeTaxState>();
            int nation = sim.World.Get<NodeBanks>().NationOf(facilities.Node[f]);
            shares.Add(f, ShareTable.PlayerHolder, 40);
            long deposit = bank.PlayerDeposit[nation], paid = 0;
            for (int t = 0; t < 20; t++)
            {
                facilities.Cash[f] += 100_000; // well above the buffer, so a dividend goes out each week
                long before = books.FacilityDividends[f];
                sim.AdvanceTurn();
                long week = books.FacilityDividends[f] - before;
                if (week > 0) paid += week;
            }
            Assert.Greater(paid, 0);
            long received = bank.PlayerDeposit[nation] - deposit;
            long expected = paid * 40 / 100;
            Assert.GreaterOrEqual(received, expected - 20, "forty of a hundred shares, less rounding");
            Assert.Less(received, expected + expected / 100, "and the deposit's interest on them, no more");
        }

        [Test]
        public void AGroupShortOfCash_ListsShares_ToHouseholds()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("credit.working_weeks,4", "credit.company_debt_permille,0"), 1).Build();
            var shares = sim.World.Get<ShareTable>();
            for (int t = 0; t < 30; t++) sim.AdvanceTurn();
            var facilities = sim.World.Get<FacilityTable>();
            int listed = Enumerable.Range(0, facilities.Count).Sum(f => shares.Outside(f));
            Assert.Greater(listed, 0, "groups raised working cash by listing shares");
            for (int f = 0; f < facilities.Count; f++) Assert.LessOrEqual(shares.Outside(f), sim.Database.Config.FacilityMaxListedShares);
        }

        [Test]
        public void SharesKeepTheCoins_AndAreSaved()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("credit.working_weeks,4", "credit.company_debt_permille,0"), 1).Build();
            var currency = sim.World.Get<CurrencyState>();
            for (int t = 0; t < 30; t++)
            {
                sim.AdvanceTurn();
                for (int c = 0; c < currency.ContinentCount; c++)
                    Assert.AreEqual(currency.CoinSupply[c], MoneyLedger.ContinentMoney(sim.World, c), $"turn {t}");
            }
            ulong hash = sim.ComputeHash();
            using var buffer = new MemoryStream();
            sim.Save(buffer);
            buffer.Position = 0;
            sim.Load(buffer);
            Assert.AreEqual(hash, sim.ComputeHash());
        }
    }
}
