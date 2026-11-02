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
    /// <summary>Bills, government and corporate bonds (DEC-097).</summary>
    public class CreditTests
    {
        static readonly string[] AllOff = { "credit.bill_cap_weeks,0", "credit.state_debt_permille,0", "credit.company_debt_permille,0", "credit.working_weeks,0" };

        // Only the credit a test names is on.
        static GameSimulation Build(params string[] extras) =>
            SimulationBuilder.FromData(TestWorlds.SampleConfig(AllOff.Where(off => !extras.Any(e => e.Split(',')[0] == off.Split(',')[0])).Concat(extras).ToArray()), 42).Build();

        static void Run(GameSimulation sim) => sim.Systems.OfType<CreditSystem>().Single().Execute(sim.Context);

        static long Money(GameSimulation sim)
        {
            long money = 0;
            var currency = sim.World.Get<CurrencyState>();
            for (int c = 0; c < currency.ContinentCount; c++) money += MoneyLedger.ContinentMoney(sim.World, c);
            return money;
        }

        static int Town(GameSimulation sim, int nation = 0)
        {
            var nodes = sim.World.Get<NodeTable>();
            return sim.World.Get<PurseState>().NationNodes[nation].First(n => nodes.LevelOf(n) == WorldLevel.City);
        }

        static void AddBill(GameSimulation sim, int holder, int purse, long face, int due)
        {
            var bills = sim.World.Get<BillTable>();
            bills.Holder.Add(holder);
            bills.Purse.Add(purse);
            bills.Face.Add(face);
            bills.DueTurn.Add(due);
        }

        [Test]
        public void Bills_FallDue_AndArePaidWithinAShareOfTheFloat()
        {
            using var sim = Build("credit.bill_cap_weeks,8");
            var config = sim.Database.Config;
            var purses = sim.World.Get<PurseState>();
            var bank = sim.World.Get<BankState>();
            var bills = sim.World.Get<BillTable>();
            config.BillRepayShare = Fx.FromPermille(500);
            purses.Float[0] = 1_000;
            AddBill(sim, BillHolders.Bank, 0, 300, sim.Turn);
            AddBill(sim, BillHolders.Bank, 0, 100_000, sim.Turn + 1);
            long reserve = bank.Reserve[0], money = Money(sim);
            Run(sim);
            Assert.AreEqual(1, bills.Count, "the one due is paid; the other waits");
            Assert.AreEqual(100_000, bills.Face[0]);
            Assert.AreEqual(700, purses.Float[0]);
            Assert.AreEqual(reserve + 300, bank.Reserve[0]);
            Assert.AreEqual(money, Money(sim));

            bills.DueTurn[0] = sim.Turn;
            purses.Float[0] = 1_000;
            Run(sim);
            Assert.AreEqual(99_500, bills.Face[0], "half the float is all it may take");
            Assert.AreEqual(500, purses.Float[0]);
        }

        [Test]
        public void Bills_LongOverdue_AreLostToTheirHolder()
        {
            using var sim = Build("credit.bill_cap_weeks,8");
            var purses = sim.World.Get<PurseState>();
            var bills = sim.World.Get<BillTable>();
            purses.Float[0] = 0;
            AddBill(sim, BillHolders.Households, 0, 500, sim.Turn - sim.Database.Config.BillDefaultWeeks);
            long money = Money(sim);
            Run(sim);
            Assert.AreEqual(0, bills.Count);
            Assert.AreEqual(500, sim.World.Get<CreditStats>().BillsLost);
            Assert.AreEqual(money, Money(sim), "a claim lost, no coins");
        }

        [Test]
        public void BillRoom_FollowsWishes_LessWhatIsOwed_AndTheBankKeepsAFloor()
        {
            using var sim = Build("credit.bill_cap_weeks,8");
            var config = sim.Database.Config;
            var cash = sim.World.Get<HouseholdCash>();
            var purses = sim.World.Get<PurseState>();
            var bank = sim.World.Get<BankState>();
            var room = sim.World.Get<BillRoom>();
            foreach (int node in purses.NationNodes[0]) cash.Desired[node] = 0;
            cash.Desired[Town(sim)] = 1_000;
            AddBill(sim, BillHolders.Bank, 0, 3_000, sim.Turn + 5);
            bank.Reserve[0] = 0;
            Run(sim);
            Assert.AreEqual(8 * 1_000 - 3_000, room.Room[0]);
            Assert.AreEqual(0, room.BankRoom[0], "a bank below its floor discounts nothing");
            bank.Reserve[0] = long.MaxValue / 4;
            Run(sim);
            Assert.Greater(room.BankRoom[0], 0);
        }

        [Test]
        public void StateBonds_MoveIdleMoneyToShortTowns()
        {
            using var sim = Build("credit.state_debt_permille,300");
            var config = sim.Database.Config;
            var cash = sim.World.Get<HouseholdCash>();
            var purses = sim.World.Get<PurseState>();
            var bonds = sim.World.Get<BondTable>();
            var nodes = sim.World.Get<NodeTable>();
            int town = Town(sim);
            int rich = purses.NationNodes[0].First(n => nodes.LevelOf(n) == WorldLevel.Village);
            foreach (int node in purses.NationNodes[0])
            {
                cash.Cash[node] = 0;
                cash.Desired[node] = 0;
                cash.DesiredNecessity[node] = 0;
            }
            cash.Cash[rich] = 1_000_000;
            cash.DesiredNecessity[town] = 1_000;
            long money = Money(sim);
            Run(sim);
            long debt = bonds.DebtOf(BondTable.OfState(0));
            Assert.AreEqual(1_000, debt, "as much as the town falls short");
            Assert.AreEqual(1_000, cash.Cash[town]);
            Assert.AreEqual(1_000_000 - 1_000, cash.Cash[rich]);
            Assert.AreEqual(money, Money(sim));
        }

        [Test]
        public void StateBonds_PayInterestAndAPart_AndStopWhileTheStateIsBehind()
        {
            using var sim = Build("credit.state_debt_permille,300", "credit.state_bond_interest_permille,520", "credit.state_bond_weeks,10");
            var cash = sim.World.Get<HouseholdCash>();
            var bonds = sim.World.Get<BondTable>();
            var bank = sim.World.Get<BankState>();
            var purses = sim.World.Get<PurseState>();
            int town = Town(sim);
            bonds.Add(BondTable.OfState(0), town, 10_000);
            foreach (int node in purses.NationNodes[0])
            {
                cash.DesiredNecessity[node] = 0;
                cash.Cash[node] = 0;
                cash.Desired[node] = 0;
            }
            long reserve = bank.Reserve[0];
            Run(sim);
            Assert.AreEqual(9_000, bonds.Face[0], "a tenth of the face");
            Assert.That(cash.Cash[town], Is.InRange(1_095, 1_100), "and 1% a week of interest");
            Assert.AreEqual(reserve - cash.Cash[town], bank.Reserve[0]);

            bank.Reserve[0] = bank.PlayerDeposit[0];
            cash.Cash[town] = 0;
            cash.DesiredNecessity[town] = 500;
            int rich = purses.NationNodes[0].First(n => n != town);
            cash.Cash[rich] = 1_000_000;
            Run(sim);
            Assert.AreEqual(1, bonds.Count, "nothing new while it cannot pay");
            Assert.AreEqual(9_000, bonds.Face[0]);
            Assert.Greater(bonds.Interest[0], 0);
        }

        [Test]
        public void Groups_FeedTheirFacilities_FromTheirOwnMoneyThenFromBonds()
        {
            using var sim = Build("credit.working_weeks,4", "credit.company_debt_permille,500");
            var config = sim.Database.Config;
            var facilities = sim.World.Get<FacilityTable>();
            var groups = sim.World.Get<GroupTable>();
            var cash = sim.World.Get<HouseholdCash>();
            var purses = sim.World.Get<PurseState>();
            var bonds = sim.World.Get<BondTable>();
            int f = Enumerable.Range(0, facilities.Count).First(i => facilities.Group[i] >= 0 && facilities.Owner[i] == OwnerIds.Npc
                && sim.Database.Facilities[facilities.DefIndex[i]].Recipe.Inputs.Length == 0 && purses.NodeFloat[facilities.Node[i]] < purses.NationCount);
            int g = facilities.Group[f];
            long start = config.GroupStartCash[groups.Kind[g]];
            long upkeep = NpcLending.FacilityUpkeep(sim.Database, facilities, f);
            for (int other = 0; other < facilities.Count; other++) facilities.Cash[other] = 1_000_000_000;
            facilities.Cash[f] = 0;
            groups.Cash[g] = start + upkeep;
            long money = Money(sim);
            Run(sim);
            Assert.AreEqual(upkeep, facilities.Cash[f] - bonds.DebtOf(g), "first its own money");
            Assert.AreEqual(start, groups.Cash[g], "down to its reserve");
            Assert.AreEqual(money, Money(sim));

            int nation = purses.NodeFloat[groups.Node[g]];
            foreach (int node in purses.NationNodes[nation]) cash.Cash[node] = 0;
            cash.Cash[purses.NationNodes[nation][0]] = 1_000_000_000;
            cash.Desired[purses.NationNodes[nation][0]] = 0;
            long before = bonds.DebtOf(g);
            facilities.Cash[f] = 0;
            money = Money(sim);
            Run(sim);
            Assert.Greater(bonds.DebtOf(g), before, "then bonds");
            Assert.GreaterOrEqual(facilities.Cash[f], bonds.DebtOf(g) - before, "what it raised went to the facility");
            Assert.LessOrEqual(bonds.DebtOf(g), BondDesk.CompanyRoom(sim.World, sim.Database, g) + bonds.DebtOf(g));
            Assert.AreEqual(money, Money(sim));
        }

        [Test]
        public void GroupTooLongBehind_Defaults_AndItsHoldersLoseTheFace()
        {
            using var sim = Build();
            var config = sim.Database.Config;
            var groups = sim.World.Get<GroupTable>();
            var bonds = sim.World.Get<BondTable>();
            int g = Enumerable.Range(0, groups.Count).First(i => groups.IsPlayer[i] == 0);
            groups.Cash[g] = 0;
            bonds.Add(g, Town(sim), 10_000);
            bonds.BehindWeeks[0] = config.CompanyDefaultWeeks - 1;
            long money = Money(sim);
            Run(sim);
            Assert.AreEqual(0, bonds.Count);
            Assert.AreEqual(1, sim.World.Get<CreditStats>().Defaults);
            Assert.AreEqual(money, Money(sim), "a claim lost, no coins");
        }

        [Test]
        public void Bonds_TakeOnlyWhatHoldersCanSpare()
        {
            using var sim = Build();
            var config = sim.Database.Config;
            var cash = sim.World.Get<HouseholdCash>();
            var purses = sim.World.Get<PurseState>();
            var groups = sim.World.Get<GroupTable>();
            foreach (int node in purses.NationNodes[0])
            {
                cash.Cash[node] = 0;
                cash.Desired[node] = 0;
            }
            for (int g = 0; g < groups.Count; g++) groups.Cash[g] = 0;
            int town = Town(sim);
            cash.Desired[town] = 100;
            cash.Cash[town] = config.BondKeepWeeks * 100 + 10_000;
            sim.AdvanceTurn();
            foreach (int node in purses.NationNodes[0]) cash.Cash[node] = 0;
            cash.Desired[town] = 100;
            cash.Cash[town] = config.BondKeepWeeks * 100 + 10_000;
            for (int g = 0; g < groups.Count; g++) groups.Cash[g] = 0;
            long raised = BondDesk.Raise(sim.World, config, sim.Turn + 1000, BondTable.OfState(0), 0, long.MaxValue / 4);
            Assert.AreEqual(config.BondBuyShare.MulInt(10_000), raised);
            Assert.AreEqual(0, BondDesk.Raise(sim.World, config, sim.Turn + 1000, BondTable.OfState(0), 0, 1), "a week's offers are spent");
        }

        [Test]
        public void ReloadingInPlace_ReplaysTheSameTurns_WithCreditActive()
        {
            using var sim = Build("credit.bill_cap_weeks,8", "credit.state_debt_permille,300", "credit.working_weeks,4",
                "markets.trader_float_permille,100", "lod.mode,1");
            for (int t = 0; t < SimClock.WeeksPerYear; t++) sim.AdvanceTurn();
            using var buffer = new System.IO.MemoryStream();
            sim.Save(buffer);
            for (int t = 0; t < 3; t++) sim.AdvanceTurn();
            ulong after = sim.ComputeHash();

            buffer.Position = 0;
            sim.Load(buffer);
            for (int t = 0; t < 3; t++) sim.AdvanceTurn();

            Assert.AreEqual(after, sim.ComputeHash());
        }

        [Test]
        public void ALoadedGame_GoesOnAsTheOriginal_WithCreditActive()
        {
            string[] on = { "credit.bill_cap_weeks,8", "credit.state_debt_permille,300", "credit.working_weeks,4", "credit.company_debt_permille,500",
                "markets.trader_float_permille,100", "lod.mode,1" };
            using var original = Build(on);
            for (int t = 0; t < 2 * SimClock.WeeksPerYear; t++) original.AdvanceTurn();
            Assert.Greater(original.World.Get<BillTable>().Count, 0, "bills are out when the game is saved");
            using var buffer = new System.IO.MemoryStream();
            original.Save(buffer);

            using var restored = Build(on);
            buffer.Position = 0;
            restored.Load(buffer);
            Assert.AreEqual(original.ComputeHash(), restored.ComputeHash());
            for (int t = 0; t < 20; t++)
            {
                original.AdvanceTurn();
                restored.AdvanceTurn();
            }
            Assert.AreEqual(original.ComputeHash(), restored.ComputeHash());
        }

        [Test]
        public void Credit_ConservesCoins_AndMatchesBurst()
        {
            string[] on = { "credit.bill_cap_weeks,8", "credit.bill_producers,1", "credit.state_debt_permille,300", "credit.working_weeks,4", "credit.company_debt_permille,500",
                "merchants.return_home,1", "markets.trader_float_permille,100", "lod.mode,1" };
            using var burst = Build(on);
            using var managed = Build(on);
            ((ArbitrageMerchantBehavior)managed.Context.Services.Get<IMerchantBehavior>()).ForceManaged = true;
            managed.Systems.OfType<ProductionSystem>().Single().ForceManaged = true;
            var currency = burst.World.Get<CurrencyState>();
            for (int t = 0; t < 3 * SimClock.WeeksPerYear; t++)
            {
                burst.AdvanceTurn();
                managed.AdvanceTurn();
                for (int c = 0; c < currency.ContinentCount; c++)
                    Assert.AreEqual(currency.CoinSupply[c], MoneyLedger.ContinentMoney(burst.World, c), $"turn {t}");
            }
            Assert.AreEqual(managed.ComputeHash(), burst.ComputeHash());
            Assert.Greater(burst.World.Get<CreditStats>().BillsIssued, 0);
        }
    }
}
