using System.Collections.Generic;
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
    /// <summary>Short selling under an exchange's licence, its margin and calls, and the markets' screens (DEC-133).</summary>
    public class ShortTests
    {
        const long Plenty = 1_000_000_000;

        // Households hold shares and idle money once groups have listed for working cash.
        static GameSimulation Listed()
        {
            var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("credit.working_weeks,4", "credit.company_debt_permille,0"), 1).Build();
            for (int t = 0; t < 30; t++) sim.AdvanceTurn();
            return sim;
        }

        static int ListedFacility(GameSimulation sim)
        {
            var facilities = sim.World.Get<FacilityTable>();
            var shares = sim.World.Get<ShareTable>();
            var banks = sim.World.Get<NodeBanks>();
            return Enumerable.Range(0, facilities.Count).First(f => facilities.Owner[f] == OwnerIds.Npc && facilities.Group[f] >= 0 && facilities.Level[f] > 0
                && !sim.Database.Facilities[facilities.DefIndex[f]].StateOwned && banks.NationOf(facilities.Node[f]) >= 0
                && shares.Outside(f) > 0 && shares.Outside(f) < 60);
        }

        static int Nation(GameSimulation sim, int f) => sim.World.Get<NodeBanks>().NationOf(sim.World.Get<FacilityTable>().Node[f]);

        // At a branch of the facility's nation's bank, with coins.
        static void AtTheExchange(GameSimulation sim, int f)
        {
            var nodes = sim.World.Get<NodeTable>();
            var banks = sim.World.Get<NodeBanks>();
            int nation = Nation(sim, f);
            var player = sim.World.Get<PlayerState>();
            player.Node = Enumerable.Range(0, nodes.Count).First(n => banks.BranchNation(n) == nation);
            player.CoinsAt(player.Node) = Plenty;
        }

        // Coins moved from the player's purse into the deposit, so no money is made.
        static void Deposit(GameSimulation sim, int nation, long amount)
        {
            var player = sim.World.Get<PlayerState>();
            var bank = sim.World.Get<BankState>();
            player.CoinsAt(player.Node) -= amount;
            bank.PlayerDeposit[nation] += amount;
            bank.Reserve[nation] += amount;
        }

        static void Licence(GameSimulation sim, int nation) => sim.World.Get<ShortLicences>().Licensed[AssetMarkets.Stock(nation)] = 1;

        static void Do(GameSimulation sim, ICommand command)
        {
            Assert.IsNull(command.Validate(sim.Context).Error);
            command.Apply(sim.Context);
        }

        [Test]
        public void TheLicence_NeedsAYearOfTrading_AndADeposit()
        {
            using var sim = Listed();
            int f = ListedFacility(sim);
            int nation = Nation(sim, f);
            AtTheExchange(sim, f);
            var licence = new BuyShortLicenceCommand(AssetMarketKind.Stock);
            Assert.AreEqual("The licence needs a year of trading on this exchange.", licence.Validate(sim.Context).Error);
            Do(sim, new BuyShareCommand(new FacilityId(f), 1));
            for (int t = 0; t < sim.Database.Config.ShortLicenceWeeks; t++) sim.AdvanceTurn();
            Assert.AreEqual("The licence needs a deposit at this exchange's bank.", licence.Validate(sim.Context).Error);
            Deposit(sim, nation, sim.Database.Config.ShortLicenceDeposit);
            long reserve = sim.World.Get<BankState>().Reserve[nation];
            var player = sim.World.Get<PlayerState>();
            long coins = player.CoinsAt(player.Node);
            Do(sim, licence);
            Assert.IsTrue(sim.View.HasShortLicence(AssetMarketKind.Stock, nation));
            Assert.AreEqual(coins - sim.Database.Config.ShortLicenceFee, player.CoinsAt(player.Node));
            Assert.AreEqual(reserve + sim.Database.Config.ShortLicenceFee, sim.World.Get<BankState>().Reserve[nation]);
        }

        [Test]
        public void SellingShort_NeedsTheLicence_AndTheMargin()
        {
            using var sim = Listed();
            int f = ListedFacility(sim);
            int nation = Nation(sim, f);
            AtTheExchange(sim, f);
            Assert.AreEqual("Short selling needs this exchange's licence.", new SellShortCommand(false, f, 5).Validate(sim.Context).Error);
            Licence(sim, nation);
            sim.World.Get<BankState>().PlayerDeposit[nation] = 0;
            Assert.AreEqual("The deposit at the exchange's bank does not cover the margin.", new SellShortCommand(false, f, 5).Validate(sim.Context).Error);
            Assert.AreEqual("No one lends so many shares.", new SellShortCommand(false, f, sim.Database.Config.ShortMaxLentShares + 1).Validate(sim.Context).Error);
        }

        [Test]
        public void AShortSale_SellsBorrowedShares_IntoTheDeposit_AndIsBoughtBack()
        {
            using var sim = Listed();
            int f = ListedFacility(sim);
            int nation = Nation(sim, f);
            AtTheExchange(sim, f);
            Licence(sim, nation);
            Deposit(sim, nation, 100_000);
            var shares = sim.World.Get<ShareTable>();
            var bank = sim.World.Get<BankState>();
            int households = shares.Held(f, ShareTable.Households(nation));
            long price = ShareBook.Price(sim.World, sim.Database, f), deposit = bank.PlayerDeposit[nation];
            Do(sim, new SellShortCommand(false, f, 5));
            int sold = sim.World.Get<ShortTable>().Held(false, f); // as far as the households' idle money goes
            Assert.Greater(sold, 0);
            Assert.AreEqual(households + sold, shares.Held(f, ShareTable.Households(nation)), "the borrowed shares pass to the households");
            Assert.AreEqual(deposit + sold * price, bank.PlayerDeposit[nation]);
            Assert.AreEqual("The deposit holds the margin on short sales.", new BankTransferCommand(-bank.PlayerDeposit[nation]).Validate(sim.Context).Error);

            Do(sim, new CoverShortCommand(false, f, sold));
            Assert.AreEqual(0, sim.World.Get<ShortTable>().Held(false, f));
            Assert.AreEqual(households, shares.Held(f, ShareTable.Households(nation)), "and back to the group");
            Assert.AreEqual(deposit, bank.PlayerDeposit[nation]);
        }

        [Test]
        public void TheLender_IsPaidAFee_EachWeek()
        {
            using var sim = Listed();
            int f = ListedFacility(sim);
            int nation = Nation(sim, f);
            AtTheExchange(sim, f);
            Licence(sim, nation);
            Deposit(sim, nation, 100_000);
            Do(sim, new SellShortCommand(false, f, 10));
            var bank = sim.World.Get<BankState>();
            long deposit = bank.PlayerDeposit[nation];
            long fee = sim.Database.Config.ShortFee.MulInt(10 * ShareBook.Price(sim.World, sim.Database, f)) / SimClock.WeeksPerYear;
            long interest = sim.Database.Config.DepositInterestPerYear.MulInt(deposit) / SimClock.WeeksPerYear;
            Assert.Greater(fee, 0);
            sim.AdvanceTurn();
            long paid = deposit + interest - bank.PlayerDeposit[nation];
            Assert.Greater(paid, fee / 2, "a week's fee left the deposit");
            Assert.Less(paid, fee * 2, "no more than about a week's fee");
        }

        [Test]
        public void ARisingPrice_CallsForMargin_ThenTheBankBuysBack()
        {
            using var sim = Listed();
            int f = ListedFacility(sim);
            int nation = Nation(sim, f);
            AtTheExchange(sim, f);
            Licence(sim, nation);
            var bank = sim.World.Get<BankState>();
            long price = ShareBook.Price(sim.World, sim.Database, f);
            Deposit(sim, nation, sim.Database.Config.ShortOpenShare.MulInt(10 * price) - 10 * price + 1);
            Do(sim, new SellShortCommand(false, f, 10));
            var player = sim.World.Get<PlayerState>();
            for (int c = 0; c < player.Coins.Length; c++) player.Coins[c] = 0;
            var markets = sim.World.Get<AssetMarkets>();
            var events = new List<ISimEvent>();
            for (int t = 0; t <= sim.Database.Config.MarginCallWeeks + 1; t++)
            {
                markets.Premium[AssetMarkets.Stock(nation)] = Fx.FromInt(4).Raw;
                sim.AdvanceTurn();
                events.AddRange(sim.LastTurnEvents);
            }
            Assert.AreEqual(1, events.OfType<ShortCalled>().Count());
            var covered = events.OfType<ShortsCovered>().Single();
            Assert.Greater(covered.Lent, 0, "the deposit fell short, and the bank lent the rest");
            Assert.AreEqual(0, sim.World.Get<ShortTable>().Count);
            Assert.AreEqual(1, sim.World.Get<LoanTable>().Count);
        }

        [Test]
        public void Shorts_KeepTheCoins_AndAreSaved()
        {
            using var sim = Listed();
            int f = ListedFacility(sim);
            int nation = Nation(sim, f);
            AtTheExchange(sim, f);
            Licence(sim, nation);
            Deposit(sim, nation, 100_000);
            var currency = sim.World.Get<CurrencyState>();
            var supply = Enumerable.Range(0, currency.ContinentCount).Select(c => MoneyLedger.ContinentMoney(sim.World, c)).ToArray();
            Do(sim, new SellShortCommand(false, f, 10));
            for (int c = 0; c < currency.ContinentCount; c++) Assert.AreEqual(supply[c], MoneyLedger.ContinentMoney(sim.World, c));
            var markets = sim.World.Get<AssetMarkets>();
            for (int t = 0; t < 10; t++)
            {
                markets.Premium[AssetMarkets.Stock(nation)] = Fx.FromInt(5).Raw;
                sim.AdvanceTurn();
                for (int c = 0; c < currency.ContinentCount; c++)
                    Assert.AreEqual(supply[c], MoneyLedger.ContinentMoney(sim.World, c), $"turn {t}"); // the test's coins aside
            }
            ulong hash = sim.ComputeHash();
            using var buffer = new MemoryStream();
            sim.Save(buffer);
            buffer.Position = 0;
            sim.Load(buffer);
            Assert.AreEqual(hash, sim.ComputeHash());
        }

        [Test]
        public void TheScreens_ShowEveryExchange_AndTheLines()
        {
            using var sim = Listed();
            var markets = new List<AssetMarketInfo>();
            sim.View.GetAssetMarkets(markets);
            var banks = sim.World.Get<NodeBanks>();
            Assert.AreEqual(banks.NationCount, markets.Count(m => m.Kind == AssetMarketKind.Stock));
            Assert.AreEqual(sim.World.Get<NodeTable>().ContinentCount, markets.Count(m => m.Kind == AssetMarketKind.Venture));
            Assert.IsTrue(markets.Any(m => m.Kind == AssetMarketKind.Property));
            Assert.AreEqual(sim.Database.Config.MarketBurstPremium, sim.View.MarketLines.Burst);
            var ventures = new List<VentureInfo>();
            sim.View.GetVentures(ventures);
            Assert.AreEqual(sim.World.Get<VentureTable>().Count, ventures.Count);
            int f = ListedFacility(sim);
            Assert.AreEqual(ShareBook.Price(sim.World, sim.Database, f), sim.View.Shares(new FacilityId(f)).Price);
        }
    }
}
