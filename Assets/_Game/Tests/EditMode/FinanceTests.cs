using System.IO;
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
    public class FinanceTests
    {
        const string City = "aur_n1_r1_c1";

        static GameSimulation Build(string config = "", ulong seed = 42) => SimulationBuilder.FromData(
            TestWorlds.SampleWith(GameDatabase.ConfigFile, "key,value\nroad.upkeep,0\n" + config), seed).Build();

        /// <summary>No financial events, so tests of lending and records see no random shocks.</summary>
        static GameSimulation BuildCalm(string config = "") => Build("finance.event_chance_permille,0\n" + config);

        static NodeId Node(GameSimulation sim, string key)
        {
            Assert.IsTrue(sim.View.TryFindNode(key, out var node), key);
            return node;
        }

        static NodeId NationNode(GameSimulation sim) =>
            new NodeId(sim.World.Get<NodeTable>().AncestorAt(Node(sim, City).Value, WorldLevel.Nation));

        static int Nation(GameSimulation sim) => sim.World.Get<NodeBanks>().NationIndex(NationNode(sim).Value);

        static PlayerState PlaceAt(GameSimulation sim, string node, long coins)
        {
            var player = sim.World.Get<PlayerState>();
            System.Array.Clear(player.Coins, 0, player.Coins.Length);
            player.Node = Node(sim, node).Value;
            player.LocalCoins = coins;
            return player;
        }

        static int CityFacility(GameSimulation sim)
        {
            var facilities = sim.World.Get<FacilityTable>();
            return Enumerable.Range(0, facilities.Count).First(f => facilities.Node[f] == Node(sim, City).Value);
        }

        static int Rejected(GameSimulation sim) => sim.LastTurnEvents.OfType<CommandRejected>().Count();

        static void AdvanceTo(GameSimulation sim, int turn)
        {
            while (sim.Turn < turn) sim.AdvanceTurn();
        }

        [Test]
        public void MoneyTotals_CountCashWhereItIs_AndDepositsOnlyOnce()
        {
            using var sim = BuildCalm();
            PlaceAt(sim, City, 1_000);
            var totals = sim.World.Get<MoneyTotals>();
            totals.Recompute(sim.World);
            int n = Nation(sim);
            var nodes = sim.World.Get<NodeTable>();
            var facilities = sim.World.Get<FacilityTable>();
            var agents = sim.World.Get<AgentTable>();
            var groups = sim.World.Get<GroupTable>();
            int nationNode = NationNode(sim).Value;
            bool InNation(int node) => nodes.AncestorAt(node, WorldLevel.Nation) == nationNode;
            long cash = 1_000
                + Enumerable.Range(0, facilities.Count).Where(f => InNation(facilities.Node[f])).Sum(f => System.Math.Max(0, facilities.Cash[f]))
                + Enumerable.Range(0, agents.Count).Where(a => InNation(agents.Node[a])).Sum(a => System.Math.Max(0, agents.Cash[a]))
                + Enumerable.Range(0, groups.Count).Where(g => InNation(groups.Node[g])).Sum(g => System.Math.Max(0, groups.Cash[g]))
                + System.Math.Max(0, sim.World.Get<PurseState>().Float[n]) // traders (DEC-061)
                + Enumerable.Range(0, nodes.Count).Where(InNation).Sum(node => System.Math.Max(0, sim.World.Get<HouseholdCash>().Cash[node]));
            Assert.AreEqual(cash, totals.NationCash[n]);
            Assert.AreEqual(cash + sim.World.Get<BankState>().Reserve[n], sim.View.NationMoneyTotal(NationNode(sim)));

            // A deposit moves coins from cash into the reserve: the total stays.
            long total = sim.View.NationMoneyTotal(NationNode(sim));
            var bank = sim.World.Get<BankState>();
            new BankTransferCommand(400).Apply(sim.Context);
            totals.Recompute(sim.World);
            Assert.AreEqual(400, totals.NationDeposits[n]);
            Assert.AreEqual(bank.Reserve[n], totals.NationReserve[n]);
            Assert.AreEqual(total, sim.View.NationMoneyTotal(NationNode(sim)));

            int continent = nodes.Continent[nationNode];
            long expected = bank.InternationalReserve[continent] + totals.ContinentLoose[continent];
            for (int k = 0; k < bank.Reserve.Length; k++)
                if (nodes.Continent[sim.World.Get<NodeBanks>().NationNode(k)] == continent) expected += totals.NationTotal(k);
            Assert.AreEqual(expected, sim.View.ContinentMoneyTotal(continent));
        }

        [Test]
        public void Records_AreTakenEachQuarter_AndStatisticsPublishedEachYear()
        {
            using var sim = BuildCalm();
            var records = new System.Collections.Generic.List<FinanceRecordInfo>();
            AdvanceTo(sim, SimClock.WeeksPerYear);
            sim.View.GetFinanceRecords(NationNode(sim), records);
            CollectionAssert.AreEqual(new[] { 12, 25, 38, 51 }, records.Select(r => r.Turn));
            Assert.AreEqual(-1, sim.View.PublishedYear);

            sim.AdvanceTurn(); // turn 52, the new year
            Assert.AreEqual(0, sim.View.PublishedYear);
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<StatisticsPublished>().Count());
            var finance = sim.World.Get<FinanceState>();
            var last = finance.Latest;
            Assert.AreEqual(last.NationTotal[Nation(sim)], sim.View.PublishedNation(NationNode(sim)).Total);
            Assert.AreEqual(Fx.Zero, sim.View.PublishedContinent(0).Inflation); // nothing minted in the first year

            // Prices follow the published total (DEC-062), so the first year's coins show from year 2's record.
            AdvanceTo(sim, 2 * SimClock.WeeksPerYear + 1);
            Assert.AreEqual(1, sim.View.PublishedYear);
            var before = sim.View.PublishedContinent(0).PriceIndex;
            AdvanceTo(sim, 3 * SimClock.WeeksPerYear + 1);
            var published = sim.View.PublishedContinent(0);
            Assert.AreEqual(Fx.FromRaw(finance.Latest.PriceIndex[0]), published.PriceIndex);
            Assert.AreEqual(published.PriceIndex / before - Fx.One, published.Inflation);
            Assert.Greater(published.Inflation.Raw, 0);

            int kept = sim.Database.Config.FinanceHistoryQuarters;
            AdvanceTo(sim, (kept / 4 + 1) * SimClock.WeeksPerYear);
            Assert.AreEqual(kept, finance.Records.Count);
        }

        [Test]
        public void LoanRate_RisesAsTheReserveIsLentOut()
        {
            var config = SimulationConfig.Defaults();
            var target = config.TargetReserveRatio;
            Assert.AreEqual(config.LoanInterestPerYear, FinanceRules.LoanRate(config, Fx.One, FinancialEventKind.None));
            Assert.AreEqual(config.LoanInterestPerYear, FinanceRules.LoanRate(config, target, FinancialEventKind.None));
            Assert.AreEqual(config.LoanInterestPerYear * Fx.FromInt(2),
                FinanceRules.LoanRate(config, Fx.FromRaw(target.Raw / 2), FinancialEventKind.None));
            Assert.AreEqual(config.LoanInterestPerYear * config.MaxLoanRateMultiplier, FinanceRules.LoanRate(config, Fx.Zero, FinancialEventKind.None));
            Assert.AreEqual(config.LoanInterestPerYear * config.RateSpikeMultiplier, FinanceRules.LoanRate(config, Fx.One, FinancialEventKind.RateSpike));
            Assert.AreEqual(Fx.One, FinanceRules.ReserveRatio(0, 0));
            Assert.AreEqual(Fx.FromPermille(250), FinanceRules.ReserveRatio(100, 300));
        }

        [Test]
        public void ABankThatLentMost_ChargesMoreFromTheNextQuarter()
        {
            using var sim = BuildCalm("bank.npc_max_reserve_share_permille,0\n");
            int n = Nation(sim);
            var bank = sim.World.Get<BankState>();
            var finance = sim.World.Get<FinanceState>();
            // A loan worth three times the reserve leaves a ratio of a quarter: twice the base rate.
            sim.World.Get<NpcLoanTable>().Add(NpcBorrowers.Facility(CityFacility(sim)), n, 3 * bank.Reserve[n], 1);
            Assert.AreEqual(sim.Database.Config.LoanInterestPerYear.Raw, finance.LoanRate[n]);
            AdvanceTo(sim, SimClock.WeeksPerQuarter);
            var ratio = finance.Latest.ReserveRatio(n);
            Assert.Less(ratio.Raw, sim.Database.Config.TargetReserveRatio.Raw);
            Assert.AreEqual(FinanceRules.LoanRate(sim.Database.Config, ratio, FinancialEventKind.None), sim.View.LoanRate(NationNode(sim)));
            Assert.Greater(sim.View.LoanRate(NationNode(sim)).Raw, sim.Database.Config.LoanInterestPerYear.Raw);

            // The player's new loan is charged at the bank's current rate.
            PlaceAt(sim, City, 0);
            var facilities = sim.World.Get<FacilityTable>();
            facilities.Owner[CityFacility(sim)] = OwnerIds.Player;
            sim.Actions.TakeLoan(100);
            sim.AdvanceTurn();
            var taken = sim.LastTurnEvents.OfType<LoanTaken>().Single();
            var config = sim.Database.Config;
            Assert.AreEqual(100 + FinanceRules.Interest(sim.View.LoanRate(NationNode(sim)), 100, config.LoanTermWeeks), taken.Balance);
        }

        [Test]
        public void NpcFacility_ShortOfCash_BorrowsAndRepays()
        {
            using var sim = BuildCalm("facility.dividend_permille,0\n");
            var config = sim.Database.Config;
            int f = CityFacility(sim);
            int n = Nation(sim);
            var facilities = sim.World.Get<FacilityTable>();
            var loans = sim.World.Get<NpcLoanTable>();
            var bank = sim.World.Get<BankState>();
            facilities.Cash[f] = -100_000; // more than the collateral covers
            long reserve = bank.Reserve[n];
            sim.AdvanceTurn();

            int row = loans.Find(NpcBorrowers.Facility(f));
            Assert.GreaterOrEqual(row, 0);
            long limit = config.LoanToValue.MulInt(NpcLending.FacilityValue(sim.Database, facilities, f));
            long expectedBalance = limit + FinanceRules.Interest(config.LoanInterestPerYear, limit, config.NpcLoanTermWeeks);
            Assert.AreEqual(expectedBalance, loans.Balance[row]);
            Assert.AreEqual(n, loans.Nation[row]);
            Assert.Less(bank.Reserve[n], reserve);

            // A healthy facility borrows nothing; a paid-down loan comes back to the reserve.
            int other = Enumerable.Range(0, facilities.Count).First(i => i != f && facilities.Cash[i] > 0 && facilities.Owner[i] == OwnerIds.Npc);
            Assert.AreEqual(-1, loans.Find(NpcBorrowers.Facility(other)));
            facilities.Cash[f] = 1_000_000;
            sim.AdvanceTurn();
            row = loans.Find(NpcBorrowers.Facility(f));
            Assert.AreEqual(expectedBalance - loans.WeeklyPayment[row], loans.Balance[row]);
            Assert.AreEqual(1_000_000 - loans.WeeklyPayment[row], facilities.Cash[f], 1_000); // a week of trade and upkeep besides
        }

        [Test]
        public void NpcFacility_InDefault_IsAuctioned_WithoutTouchingThePlayersLoan()
        {
            using var sim = BuildCalm();
            var config = sim.Database.Config;
            int f = CityFacility(sim);
            int n = Nation(sim);
            var facilities = sim.World.Get<FacilityTable>();
            var loans = sim.World.Get<NpcLoanTable>();
            PlaceAt(sim, City, 1_000);
            var player = sim.World.Get<LoanTable>();
            player.Add(n, 5_000, 1); // the player owes this bank too
            loans.Add(NpcBorrowers.Facility(f), n, 1_000_000_000, 1_000_000_000);

            NpcLoanDefaulted defaulted = null;
            for (int t = 0; t < config.LoanGraceWeeks + 2 && defaulted == null; t++)
            {
                sim.AdvanceTurn();
                defaulted = sim.LastTurnEvents.OfType<NpcLoanDefaulted>().SingleOrDefault();
            }
            Assert.IsNotNull(defaulted);
            Assert.AreEqual(f, defaulted.Facility.Value);
            Assert.AreEqual(OwnerIds.Bank, facilities.Owner[f]);
            Assert.AreEqual(-1, loans.Find(NpcBorrowers.Facility(f)));
            var auctions = sim.World.Get<AuctionTable>();
            int a = auctions.Find(new FacilityId(f));
            Assert.AreEqual(0, auctions.ForPlayerLoan[a]);

            long playerBalance = player.Balance[0];
            int from = sim.Turn;
            long price = auctions.ReservePrice[a];
            AdvanceTo(sim, auctions.ClosesTurn[a] + 1);
            var closed = sim.LastTurnEvents.OfType<AuctionClosed>().Single();
            Assert.IsFalse(closed.WonByPlayer);
            Assert.AreEqual(price, closed.Price);
            Assert.AreEqual(OwnerIds.Npc, facilities.Owner[f]);
            Assert.AreEqual(playerBalance - (sim.Turn - from), player.Balance[0]); // only the weekly instalments
        }

        [Test]
        public void NpcMerchant_BorrowsAgainstTheCargoItCarries()
        {
            using var sim = BuildCalm();
            var agents = sim.World.Get<AgentTable>();
            var nodes = sim.World.Get<NodeTable>();
            var cargo = sim.World.Get<CargoTable>();
            var loans = sim.World.Get<NpcLoanTable>();
            var inNation = Enumerable.Range(0, agents.Count)
                .Where(a => nodes.AncestorAt(agents.Node[a], WorldLevel.Nation) >= 0 && agents.Owner[a] == OwnerIds.Npc && !agents.IsTravelling(a))
                .ToList();
            int laden = inNation[0], empty = inNation[1];
            Assert.IsTrue(sim.Database.TryGetGood("wheat", QualityGrade.Mid, out var wheat));
            cargo.Change(CargoHolders.Merchant(new AgentId(laden)), wheat, 200);
            agents.Cash[laden] = -1_000;
            agents.Cash[empty] = -1_000;
            sim.AdvanceTurn();
            Assert.GreaterOrEqual(loans.Find(NpcBorrowers.Merchant(laden)), 0);
            Assert.AreEqual(-1, loans.Find(NpcBorrowers.Merchant(empty)));
        }

        [Test]
        public void CreditCrunch_StopsNewLoans_AndBankRun_LimitsWithdrawals()
        {
            using var sim = BuildCalm();
            int n = Nation(sim);
            var finance = sim.World.Get<FinanceState>();
            var facilities = sim.World.Get<FacilityTable>();
            PlaceAt(sim, City, 1_000);
            facilities.Owner[CityFacility(sim)] = OwnerIds.Player;
            int npc = Enumerable.Range(0, facilities.Count).First(f => facilities.Owner[f] == OwnerIds.Npc
                && sim.World.Get<NodeTable>().AncestorAt(facilities.Node[f], WorldLevel.Nation) == NationNode(sim).Value);
            finance.Event[n] = (byte)FinancialEventKind.CreditCrunch;
            finance.EventStart[n] = sim.Turn;
            facilities.Cash[npc] = -500;
            sim.Actions.TakeLoan(100);
            sim.AdvanceTurn();
            Assert.AreEqual(1, Rejected(sim));
            Assert.AreEqual(-1, sim.World.Get<NpcLoanTable>().Find(NpcBorrowers.Facility(npc)));

            finance.Event[n] = (byte)FinancialEventKind.BankRun;
            sim.Actions.Deposit(1_000);
            sim.AdvanceTurn();
            long cap = sim.Database.Config.BankRunWithdrawShare.MulInt(sim.View.PlayerDeposit(NationNode(sim)));
            sim.Actions.Withdraw(cap + 1);
            sim.Actions.Withdraw(cap);
            sim.Actions.Withdraw(1); // once a week
            sim.AdvanceTurn();
            Assert.AreEqual(2, Rejected(sim));
            Assert.AreEqual(cap, sim.World.Get<PlayerState>().LocalCoins);
        }

        [Test]
        public void FinancialEvents_StartAtRandom_AndEndOnceReservesRecover()
        {
            using var sim = Build("finance.event_chance_permille,1000\nbank.npc_max_reserve_share_permille,0\n");
            int nations = sim.World.Get<NodeBanks>().NationCount;
            var bank = sim.World.Get<BankState>();
            var finance = sim.World.Get<FinanceState>();
            var config = sim.Database.Config;
            var reserves = (long[])bank.Reserve.Clone();
            AdvanceTo(sim, SimClock.WeeksPerQuarter);
            var started = sim.LastTurnEvents.OfType<FinancialEventStarted>().ToList();
            Assert.AreEqual(nations, started.Count);
            var banks = sim.World.Get<NodeBanks>();
            foreach (var e in started)
            {
                int k = banks.NationIndex(e.Nation.Value);
                Assert.AreEqual(e.Kind, finance.EventOf(k));
                if (e.Kind == FinancialEventKind.BankRun) Assert.Less(bank.Reserve[k], reserves[k]);
                var expectedRate = e.Kind == FinancialEventKind.RateSpike ? config.LoanInterestPerYear * config.RateSpikeMultiplier : config.LoanInterestPerYear;
                Assert.AreEqual(expectedRate.Raw, finance.LoanRate[k]);
            }

            // One nation's bank has lent nearly everything, so its event outlasts the quarter.
            int n = Nation(sim);
            sim.World.Get<NpcLoanTable>().Add(NpcBorrowers.Facility(CityFacility(sim)), n, 100 * bank.Reserve[n], 1);
            sim.World.Get<FacilityTable>().Cash[CityFacility(sim)] = 1_000_000; // keeps paying, so no default
            AdvanceTo(sim, 2 * SimClock.WeeksPerQuarter);
            var ended = sim.LastTurnEvents.OfType<FinancialEventEnded>().ToList();
            Assert.AreEqual(nations - 1, ended.Count);
            Assert.IsFalse(ended.Any(e => e.Nation == NationNode(sim)));
            Assert.AreNotEqual(FinancialEventKind.None, sim.View.FinancialEvent(NationNode(sim)));
            Assert.AreEqual(config.LoanInterestPerYear.Raw, finance.LoanRate[banks.NationIndex(ended[0].Nation.Value)]);
        }

        [Test]
        public void FinanceState_AndNpcLoans_SurviveSaveAndLoad()
        {
            using var sim = Build("finance.event_chance_permille,1000\n");
            int f = CityFacility(sim);
            sim.World.Get<NpcLoanTable>().Add(NpcBorrowers.Facility(f), Nation(sim), 1_000_000, 100);
            sim.World.Get<FacilityTable>().Cash[f] = 1_000_000; // pays on time all year
            AdvanceTo(sim, SimClock.WeeksPerYear + 1);
            using var buffer = new MemoryStream();
            sim.Save(buffer);

            using var copy = Build("finance.event_chance_permille,1000\n");
            buffer.Position = 0;
            copy.Load(buffer);
            var loans = copy.World.Get<NpcLoanTable>();
            int row = loans.Find(NpcBorrowers.Facility(f));
            Assert.GreaterOrEqual(row, 0);
            var original = sim.World.Get<NpcLoanTable>();
            Assert.AreEqual(original.Balance[original.Find(NpcBorrowers.Facility(f))], loans.Balance[row]);
            Assert.AreEqual(sim.World.Get<FinanceState>().Records.Count, copy.World.Get<FinanceState>().Records.Count);
            Assert.AreEqual(sim.View.PublishedContinent(0).PriceIndex, copy.View.PublishedContinent(0).PriceIndex);
            for (int t = 0; t < 14; t++)
            {
                sim.AdvanceTurn();
                copy.AdvanceTurn();
            }
            Assert.AreEqual(sim.ComputeHash(), copy.ComputeHash());
        }

        [Test]
        public void AuctionTableV1Save_IsMigrated()
        {
            using var sim = BuildCalm();
            using var v1 = new MemoryStream();
            using (var writer = new BinaryWriter(v1, System.Text.Encoding.UTF8, true))
            {
                void Column<T>(System.Action<T> write, T value)
                {
                    writer.Write(1);
                    write(value);
                }
                Column<int>(writer.Write, 7); // Facility
                Column<int>(writer.Write, 0); // Nation
                Column<int>(writer.Write, 9); // ClosesTurn
                Column<long>(writer.Write, 100); // ReservePrice
                Column<long>(writer.Write, 0); // PlayerBid
            }
            var migrated = sim.Migrations.Upgrade(AuctionTable.SectionKey, 1, 2, v1.ToArray());
            var auctions = new AuctionTable();
            auctions.Read(new BinaryReader(new MemoryStream(migrated)));
            Assert.AreEqual(1, auctions.Count);
            Assert.AreEqual(7, auctions.Facility[0]);
            Assert.AreEqual(1, auctions.ForPlayerLoan[0]);
            auctions.Dispose();
        }
    }
}
