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
    public class BankTests
    {
        const string City = "aur_n1_r1_c1"; // a branch of aur_n1 and Aurelia's international bank

        // Nations pay their paved roads' upkeep from bank reserves; these tests follow reserves exactly.
        // Without road upkeep, NPC loans, financial events, lobbying, state spending, dividends or merchants
        // paying tolls into them, reserves move only by what a test does.
        static GameSimulation Build(ulong seed = 42) => SimulationBuilder.FromData(TestWorlds.SampleWith(GameDatabase.ConfigFile,
            "key,value\nroad.upkeep,0\nbank.npc_max_reserve_share_permille,0\nfinance.event_chance_permille,0\npolicy.npc_lobby_share_permille,0\n"
            + "bank.state_spending_permille,0\nfacility.dividend_permille,0\nstate.defense_permille,0\nstate.project_share_permille,0\n"
            + "noble.royal_share_permille,0\nnoble.city_share_permille,0\nnoble.village_share_permille,0\n"), seed)
            .ConfigureServices(s => s.Set<IMerchantBehavior>(new IdleMerchantBehavior())).Build();

        static NodeId Node(GameSimulation sim, string key)
        {
            Assert.IsTrue(sim.View.TryFindNode(key, out var node), key);
            return node;
        }

        /// <summary>Puts the player at a node with exactly <paramref name="coins"/> of its continent's coin and nothing else.</summary>
        static PlayerState PlaceAt(GameSimulation sim, string node, long coins)
        {
            var player = sim.World.Get<PlayerState>();
            System.Array.Clear(player.Coins, 0, player.Coins.Length);
            player.Node = Node(sim, node).Value;
            player.LocalCoins = coins;
            return player;
        }

        static NodeId NationOf(GameSimulation sim, string node) =>
            new NodeId(sim.World.Get<NodeTable>().AncestorAt(Node(sim, node).Value, WorldLevel.Nation));

        static int Rejected(GameSimulation sim) => sim.LastTurnEvents.OfType<CommandRejected>().Count();

        /// <summary>A city facility the player owns, for collateral.</summary>
        static int OwnCityFacility(GameSimulation sim)
        {
            var facilities = sim.World.Get<FacilityTable>();
            int f = Enumerable.Range(0, facilities.Count).First(i => facilities.Node[i] == Node(sim, City).Value);
            facilities.Owner[f] = OwnerIds.Player;
            return f;
        }

        [Test]
        public void Branches_AreCitiesAndAbove_InternationalBanksAreMarked()
        {
            using var sim = Build();
            var view = sim.View;
            Assert.IsTrue(view.IsBankBranch(Node(sim, City)));
            Assert.IsFalse(view.IsBankBranch(Node(sim, "aur_n1_r1_c2"))); // banks stand at core towns (DEC-104)
            Assert.IsFalse(view.IsBankBranch(Node(sim, "aur_n1")));
            Assert.IsFalse(view.IsBankBranch(Node(sim, "aur_n1_r1_c1_v1")));
            Assert.IsFalse(view.IsBankBranch(Node(sim, "aur")));
            Assert.AreEqual(Node(sim, City), view.InternationalBankOf(view.ContinentOf(Node(sim, City))));
            Assert.IsTrue(view.IsInternationalBank(Node(sim, "bor_n1_r1_c2")));

            var nodes = TestWorlds.Sample().ReadText(CsvMapSource.NodesFile);
            var twice = nodes.Replace("aur_n1_r1_c2,Aurelia City 1-2,City,aur_n1_r1,,50,100,", "aur_n1_r1_c2,Aurelia City 1-2,City,aur_n1_r1,,50,100,1");
            Assert.AreNotEqual(nodes, twice);
            Assert.Throws<System.FormatException>(() => SimulationBuilder.FromData(TestWorlds.SampleWith(CsvMapSource.NodesFile, twice), 1).Build());
        }

        [Test]
        public void Reserves_StartAsAShareOfCoinSupply_AndReceiveMintedCoins()
        {
            using var sim = Build();
            var currency = sim.World.Get<CurrencyState>();
            var nation = NationOf(sim, City);
            int continent = sim.View.ContinentOf(nation);
            long start = sim.Database.Config.BankInitialReserveShare.MulInt(currency.CoinSupply[continent]);
            Assert.AreEqual(start, sim.View.BankReserve(nation)); // one nation per continent in the sample

            long supply = currency.CoinSupply[continent];
            for (int t = 0; t <= SimClock.WeeksPerYear; t++) sim.AdvanceTurn();
            Assert.Greater(currency.CoinSupply[continent], supply);
            var tax = sim.World.Get<IncomeTaxState>();
            Assert.AreEqual(start + currency.CoinSupply[continent] - supply + tax.LastHouseholds[continent] + tax.LastCompanies[continent], sim.View.BankReserve(nation));
        }

        [Test]
        public void Deposits_AreMadeAtBranches_AndEarnInterest()
        {
            using var sim = Build();
            var player = PlaceAt(sim, City, 200_000);
            var nation = NationOf(sim, City);
            long reserve = sim.View.BankReserve(nation);

            sim.Actions.Deposit(100_000);
            sim.Actions.Withdraw(200_000); // more than the account holds
            sim.AdvanceTurn();
            Assert.AreEqual(1, Rejected(sim));
            Assert.AreEqual(100_000, player.LocalCoins);
            long interest = sim.Database.Config.DepositInterestPerYear.MulInt(100_000) / SimClock.WeeksPerYear;
            Assert.AreEqual(100_000 + interest, sim.View.PlayerDeposit(nation));
            Assert.AreEqual(reserve + 100_000, sim.View.BankReserve(nation));

            sim.Actions.Withdraw(40_000);
            sim.AdvanceTurn();
            Assert.AreEqual(140_000, player.LocalCoins);

            PlaceAt(sim, "aur_n1_r1_c1_v1", 100);
            sim.Actions.Deposit(50);
            sim.AdvanceTurn();
            Assert.AreEqual(1, Rejected(sim)); // villages have no branch
        }

        [Test]
        public void Purchases_AtABranch_CanBePaidFromTheDeposit()
        {
            using var sim = Build();
            for (int t = 0; t < 3; t++) sim.AdvanceTurn();
            var player = PlaceAt(sim, City, 5_000);
            var nation = NationOf(sim, City);
            sim.Actions.Deposit(5_000);
            sim.AdvanceTurn();

            var quotes = new List<MarketQuote>();
            sim.View.GetQuotes(Node(sim, City), quotes);
            var quote = quotes[0];
            sim.World.Get<MarketStore>().TryFindCell(Node(sim, City).Value, quote.Good, out int cell);
            sim.World.Get<MarketStore>().Stock[cell] = 100;
            long deposit = sim.View.PlayerDeposit(nation);
            sim.Actions.BuyGoods(quote.Good, 2, payFromDeposit: true);
            sim.AdvanceTurn();
            var trade = sim.LastTurnEvents.OfType<GoodsTraded>().Single();
            Assert.AreEqual(2, trade.Filled);
            Assert.AreEqual(0, player.LocalCoins);
            long interest = sim.Database.Config.DepositInterestPerYear.MulInt(deposit - 2L * trade.Price) / SimClock.WeeksPerYear;
            Assert.AreEqual(deposit - 2L * trade.Price + interest, sim.View.PlayerDeposit(nation));
        }

        [Test]
        public void Exchange_HappensAtTheInternationalBank_WithAFee()
        {
            using var sim = Build();
            var player = PlaceAt(sim, City, 10_000);
            int home = player.LocalContinent, away = 1 - home;
            var currency = sim.World.Get<CurrencyState>();
            long gross = currency.Convert(1_000, home, away);
            long fee = sim.Database.Config.ExchangeFee.MulInt(gross);

            sim.Actions.Exchange(home, away, 1_000);
            sim.AdvanceTurn();
            var exchanged = sim.LastTurnEvents.OfType<CoinsExchanged>().Single();
            Assert.AreEqual(gross - fee, exchanged.Received);
            Assert.AreEqual(9_000, player.Coins[home]);
            Assert.AreEqual(gross - fee, player.Coins[away]);

            PlaceAt(sim, "aur_n1_r1_c2", 1_000); // a branch, but not the international bank
            sim.Actions.Exchange(home, away, 100);
            sim.AdvanceTurn();
            Assert.AreEqual(1, Rejected(sim));
        }

        [Test]
        public void Loans_AreLimitedByCollateral_AndTheReserve()
        {
            using var sim = Build();
            var player = PlaceAt(sim, City, 0);
            var nation = NationOf(sim, City);
            Assert.AreEqual(0, sim.View.CreditLimit);
            sim.Actions.TakeLoan(100);
            sim.AdvanceTurn();
            Assert.AreEqual(1, Rejected(sim)); // nothing to pledge

            int f = OwnCityFacility(sim);
            var sheet = new PlayerBalanceSheet(sim.World, sim.Database);
            long value = sheet.FacilityValue(f);
            Assert.AreEqual(value, sim.View.CollateralValue);
            Assert.AreEqual(sim.Database.Config.LoanToValue.MulInt(value), sim.View.CreditLimit);

            long reserve = sim.View.BankReserve(nation);
            sim.Actions.TakeLoan(sim.View.CreditLimit + 1);
            sim.Actions.TakeLoan(2_000);
            sim.Actions.TakeLoan(100); // one loan per bank
            sim.AdvanceTurn();
            Assert.AreEqual(2, Rejected(sim));
            var taken = sim.LastTurnEvents.OfType<LoanTaken>().Single();
            Assert.AreEqual(2_000 + 200, taken.Balance); // 10% a year over a 52-week term
            Assert.AreEqual((2_200 + 51) / 52, taken.WeeklyPayment);

            // The first instalment is collected in the same turn, from the coins just borrowed.
            Assert.AreEqual(2_000 - taken.WeeklyPayment, player.LocalCoins);
            Assert.AreEqual(reserve - 2_000 + taken.WeeklyPayment, sim.View.BankReserve(nation));
            var loans = new List<LoanInfo>();
            sim.View.GetLoans(loans);
            Assert.AreEqual(2_200 - taken.WeeklyPayment, loans.Single().Balance);
            Assert.AreEqual(0, loans.Single().OverdueWeeks);

            player.LocalCoins = 10_000;
            sim.Actions.RepayLoan(loans.Single().Balance);
            sim.AdvanceTurn();
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<LoanRepaid>().Count());
            sim.World.Get<BankState>().Reserve[sim.World.Get<NodeBanks>().NationIndex(nation.Value)] = 10;
            sim.Actions.TakeLoan(100);
            sim.AdvanceTurn();
            Assert.AreEqual(1, Rejected(sim)); // the bank's reserve is too small
        }

        [Test]
        public void FacilityHeldForAuction_CannotGoBankrupt()
        {
            using var sim = Build();
            PlaceAt(sim, City, 0);
            var facilities = sim.World.Get<FacilityTable>();
            int f = OwnCityFacility(sim);
            facilities.Owner[f] = OwnerIds.Bank;
            facilities.Cash[f] = -100_000;
            facilities.NegativeCashWeeks[f] = sim.Database.Config.FacilityBankruptWeeks;
            sim.World.Get<AuctionTable>().Add(f, 0, sim.Turn + 10, 1);

            sim.AdvanceTurn();

            Assert.IsEmpty(sim.LastTurnEvents.OfType<FacilityBankrupt>());
            Assert.AreEqual(OwnerIds.Bank, facilities.Owner[f]);
            Assert.AreEqual(0, facilities.NegativeCashWeeks[f]);
            Assert.AreEqual(1, sim.World.Get<AuctionTable>().Count);
        }

        [Test]
        public void UnpaidLoan_LeadsToSeizure_AndAuctionToAnNpc()
        {
            using var sim = Build();
            var player = PlaceAt(sim, City, 0);
            var nation = NationOf(sim, City);
            int f = OwnCityFacility(sim);
            sim.Actions.TakeLoan(2_000);
            sim.AdvanceTurn();
            player.LocalCoins = 0;
            PlaceAt(sim, "aur_n1_r1_c1_v1", 0); // away from branches, nothing to pay with

            var seized = new List<CollateralSeized>();
            for (int t = 0; t <= sim.Database.Config.LoanGraceWeeks + 1 && seized.Count == 0; t++)
            {
                sim.AdvanceTurn();
                seized.AddRange(sim.LastTurnEvents.OfType<CollateralSeized>());
            }
            Assert.AreEqual(CollateralKind.Facility, seized.Single().Kind);
            var facilities = sim.World.Get<FacilityTable>();
            Assert.AreEqual(OwnerIds.Bank, facilities.Owner[f]);
            var auctions = new List<AuctionInfo>();
            sim.View.GetAuctions(auctions);
            long reservePrice = auctions.Single().ReservePrice;
            Assert.AreEqual(sim.Database.Config.AuctionReserveShare.MulInt(new PlayerBalanceSheet(sim.World, sim.Database).FacilityValue(f)), reservePrice);

            var loans = new List<LoanInfo>();
            sim.View.GetLoans(loans);
            long owed = loans.Single().Balance;
            long bankReserve = sim.View.BankReserve(nation);
            AuctionClosed closed = null;
            for (int t = 0; t <= sim.Database.Config.AuctionWeeks && closed == null; t++)
            {
                sim.AdvanceTurn();
                closed = sim.LastTurnEvents.OfType<AuctionClosed>().SingleOrDefault();
            }
            Assert.IsNotNull(closed);
            Assert.IsFalse(closed.WonByPlayer);
            Assert.AreEqual(reservePrice, closed.Price);
            Assert.AreEqual(OwnerIds.Npc, facilities.Owner[f]);
            // The sale repays the debt; what is left goes to the bank, not the former owner (DEC-039).
            Assert.Greater(reservePrice, owed);
            Assert.AreEqual(1, sim.LastTurnEvents.OfType<LoanRepaid>().Count());
            Assert.AreEqual(bankReserve + reservePrice, sim.View.BankReserve(nation));
            Assert.AreEqual(0, player.LocalCoins);
        }

        [Test]
        public void Seizure_TakesDepositsFirst_AndThePlayerCanBuyBackAtAuction()
        {
            using var sim = Build();
            var player = PlaceAt(sim, City, 0);
            int f = OwnCityFacility(sim);
            sim.Actions.TakeLoan(3_000);
            sim.AdvanceTurn();
            PlaceAt(sim, "aur_n1_r1_c1_v1", 0);
            // A deposit abroad: instalments only draw on the lender's account, but seizure takes any deposit.
            var banks = sim.World.Get<NodeBanks>();
            int abroad = banks.NationIndex(NationOf(sim, "bor_n1_r1_c2").Value);
            var bank = sim.World.Get<BankState>();
            bank.PlayerDeposit[abroad] = 1_000;
            bank.Reserve[abroad] += 1_000;

            var seized = new List<CollateralSeized>();
            for (int t = 0; t <= sim.Database.Config.LoanGraceWeeks + 20 && !seized.Any(s => s.Kind == CollateralKind.Facility); t++)
            {
                sim.AdvanceTurn();
                seized.AddRange(sim.LastTurnEvents.OfType<CollateralSeized>());
            }
            Assert.AreEqual(CollateralKind.Deposit, seized.First().Kind);
            Assert.AreEqual(0, sim.View.PlayerDeposit(NationOf(sim, "bor_n1_r1_c2")));
            var auction = sim.World.Get<AuctionTable>();
            int a = auction.Find(new FacilityId(f));
            Assert.GreaterOrEqual(a, 0);

            // Instalments keep coming out of the coins carried, so bring more than the bid.
            long coins = auction.ReservePrice[a] + 10_000;
            player.LocalCoins = coins;
            sim.Actions.Bid(new FacilityId(f), auction.ReservePrice[a] - 1); // below the reserve price
            sim.Actions.Bid(new FacilityId(f), auction.ReservePrice[a]);
            AuctionClosed closed = null;
            for (int t = 0; t <= sim.Database.Config.AuctionWeeks && closed == null; t++)
            {
                sim.AdvanceTurn();
                closed = sim.LastTurnEvents.OfType<AuctionClosed>().SingleOrDefault();
            }
            Assert.IsTrue(closed.WonByPlayer);
            Assert.AreEqual(OwnerIds.Player, sim.World.Get<FacilityTable>().Owner[f]);
            Assert.LessOrEqual(player.LocalCoins, coins - closed.Price);
        }

        [Test]
        public void PlayersLoan_IsNotCoveredByAnotherBorrowersAuction()
        {
            using var sim = Build();
            PlaceAt(sim, City, 0);
            int f = OwnCityFacility(sim);
            sim.Actions.TakeLoan(2_000);
            sim.AdvanceTurn();
            PlaceAt(sim, "aur_n1_r1_c1_v1", 0);
            var facilities = sim.World.Get<FacilityTable>();
            int other = Enumerable.Range(0, facilities.Count).First(i => facilities.Owner[i] == OwnerIds.Npc);
            facilities.Owner[other] = OwnerIds.Bank;
            int nation = sim.World.Get<NodeBanks>().NationIndex(NationOf(sim, City).Value);
            sim.World.Get<AuctionTable>().Add(other, nation, sim.Turn + 10_000, 1_000_000, forPlayerLoan: false);

            for (int t = 0; t <= sim.Database.Config.LoanGraceWeeks + 1 && facilities.Owner[f] == OwnerIds.Player; t++) sim.AdvanceTurn();

            Assert.AreEqual(OwnerIds.Bank, facilities.Owner[f]);
        }

        [Test]
        public void Seizure_TakesOnlyTheDepositCoinsTheBankHolds()
        {
            using var sim = Build();
            PlaceAt(sim, City, 0);
            OwnCityFacility(sim);
            sim.Actions.TakeLoan(3_000);
            sim.AdvanceTurn();
            PlaceAt(sim, "aur_n1_r1_c1_v1", 0);
            int abroad = sim.World.Get<NodeBanks>().NationIndex(NationOf(sim, "bor_n1_r1_c2").Value);
            var bank = sim.World.Get<BankState>();
            bank.PlayerDeposit[abroad] = 1_000;
            bank.Reserve[abroad] = 300; // it lent the rest

            var seized = new List<CollateralSeized>();
            for (int t = 0; t <= sim.Database.Config.LoanGraceWeeks + 1 && seized.Count == 0; t++)
            {
                sim.AdvanceTurn();
                seized.AddRange(sim.LastTurnEvents.OfType<CollateralSeized>());
            }

            Assert.AreEqual(CollateralKind.Deposit, seized.First().Kind);
            Assert.AreEqual(0, bank.Reserve[abroad]);
            Assert.GreaterOrEqual(bank.PlayerDeposit[abroad], 700);
        }

        [Test]
        public void WinningBid_IsNotPaidFromDepositCoinsTheBankLacks()
        {
            using var sim = Build();
            PlaceAt(sim, City, 0);
            var facilities = sim.World.Get<FacilityTable>();
            int f = OwnCityFacility(sim);
            facilities.Owner[f] = OwnerIds.Bank;
            var banks = sim.World.Get<NodeBanks>();
            int home = banks.NationIndex(NationOf(sim, City).Value);
            int lender = banks.NationIndex(NationOf(sim, "bor_n1_r1_c2").Value);
            var bank = sim.World.Get<BankState>();
            sim.AdvanceTurn();
            bank.PlayerDeposit[home] = 1_000;
            bank.Reserve[home] = 300;
            var auctions = sim.World.Get<AuctionTable>();
            auctions.Add(f, lender, sim.Turn, 800, forPlayerLoan: false); // the sale's coins go abroad
            auctions.PlayerBid[auctions.Find(new FacilityId(f))] = 800;

            sim.AdvanceTurn();

            Assert.IsFalse(sim.LastTurnEvents.OfType<AuctionClosed>().Single().WonByPlayer);
            Assert.GreaterOrEqual(bank.Reserve[home], 0);
        }

        [Test]
        public void BankState_SurvivesSaveAndLoad()
        {
            using var sim = Build();
            PlaceAt(sim, City, 5_000);
            OwnCityFacility(sim);
            sim.Actions.Deposit(1_000);
            sim.Actions.TakeLoan(1_000);
            sim.AdvanceTurn();
            using var buffer = new System.IO.MemoryStream();
            sim.Save(buffer);
            using var copy = Build(seed: 5);
            buffer.Position = 0;
            copy.Load(buffer);
            Assert.AreEqual(sim.ComputeHash(), copy.ComputeHash());
            var loans = new List<LoanInfo>();
            copy.View.GetLoans(loans);
            Assert.AreEqual(1, loans.Count);
        }
    }
}
