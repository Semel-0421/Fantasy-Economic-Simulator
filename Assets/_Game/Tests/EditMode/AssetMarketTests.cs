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
    /// <summary>Stock exchanges and property markets: a premium over the fundamental, margin credit, published indicators, bursts (DEC-131).</summary>
    public class AssetMarketTests
    {
        // Groups list shares to raise working cash, so households hold some.
        static GameSimulation Listed(params string[] lines) =>
            SimulationBuilder.FromData(TestWorlds.SampleConfig(new[] { "credit.working_weeks,4", "credit.company_debt_permille,0" }.Concat(lines).ToArray()), 1).Build();

        static int GroupFacility(GameSimulation sim)
        {
            var facilities = sim.World.Get<FacilityTable>();
            return Enumerable.Range(0, facilities.Count).First(f => facilities.Owner[f] == OwnerIds.Npc && facilities.Group[f] >= 0
                && sim.World.Get<NodeBanks>().NationOf(facilities.Node[f]) >= 0);
        }

        // Advances until a quarter has closed with the market's premium held at `premium` the turn before.
        static List<ISimEvent> HoldUntilQuarterEnd(GameSimulation sim, int market, Fx premium)
        {
            var markets = sim.World.Get<AssetMarkets>();
            var events = new List<ISimEvent>();
            for (int t = 0; t <= SimClock.WeeksPerQuarter; t++)
            {
                markets.Premium[market] = premium.Raw;
                sim.AdvanceTurn();
                events.AddRange(sim.LastTurnEvents);
                if (markets.PublishedPremium[market] != Fx.OneRaw) break; // the quarter closed
            }
            return events;
        }

        [Test]
        public void ASharePrice_IsItsFundamental_TimesTheExchangePremium()
        {
            using var sim = TestWorlds.BuildSample();
            int f = GroupFacility(sim);
            int nation = sim.World.Get<NodeBanks>().NationOf(sim.World.Get<FacilityTable>().Node[f]);
            long fundamental = ShareBook.Fundamental(sim.World, sim.Database, f);
            Assert.AreEqual(fundamental, ShareBook.Price(sim.World, sim.Database, f));
            sim.World.Get<AssetMarkets>().Premium[AssetMarkets.Stock(nation)] = Fx.FromInt(2).Raw;
            Assert.AreEqual(2 * fundamental, ShareBook.Price(sim.World, sim.Database, f));
        }

        [Test]
        public void ALandPrice_IsItsFundamental_TimesThePropertyPremium()
        {
            using var sim = TestWorlds.BuildSample();
            var land = sim.World.Get<LandState>();
            var markets = sim.World.Get<AssetMarkets>();
            var config = sim.Database.Config;
            int n = Enumerable.Range(0, land.Plots.Length).First(x => sim.World.Get<NodeBanks>().NationOf(x) >= 0);
            long fundamental = land.Fundamental(n, config);
            markets.Premium[markets.Property(n)] = Fx.FromPermille(1500).Raw;
            Assert.AreEqual(Fx.FromPermille(1500).MulInt(fundamental), land.Price(n, config));
        }

        [Test]
        public void Households_BorrowOnTheirShares_AndCreditLiftsThePrice()
        {
            using var sim = Listed();
            var markets = sim.World.Get<AssetMarkets>();
            var stock = Enumerable.Range(0, markets.NationCount).ToList();
            bool borrowed = false, rose = false;
            for (int t = 0; t < 3 * SimClock.WeeksPerYear && !rose; t++)
            {
                sim.AdvanceTurn();
                borrowed |= stock.Any(m => markets.Debt[m] > 0);
                rose |= stock.Any(m => markets.Debt[m] > 0 && markets.Premium[m] > Fx.FromPermille(1100).Raw);
            }
            Assert.IsTrue(borrowed, "households borrowed on their shares");
            Assert.IsTrue(rose, "and the price rose above the fundamental");
        }

        [Test]
        public void TheIndicators_ArePublishedEachQuarter()
        {
            using var sim = Listed();
            var markets = sim.World.Get<AssetMarkets>();
            for (int t = 0; t < 5 * SimClock.WeeksPerQuarter; t++) sim.AdvanceTurn(); // the last turn closes a quarter
            int m = Enumerable.Range(0, markets.NationCount).First(x => markets.Debt[x] > 0);
            Assert.AreEqual(markets.Premium[m], markets.PublishedPremium[m]);
            Assert.Greater(markets.PublishedLeverage[m], 0);
            Assert.LessOrEqual(markets.PublishedLeverage[m], sim.Database.Config.MarginCallShare.Raw, "calls keep it under the call line");
            Assert.AreNotEqual(0, markets.PublishedCreditGrowth[m]);
        }

        [Test]
        public void AMarket_HeatsAboveTheFirstLine()
        {
            using var sim = TestWorlds.BuildSample();
            var events = HoldUntilQuarterEnd(sim, AssetMarkets.Stock(0), Fx.FromPermille(1800));
            var heated = events.OfType<MarketHeated>().Single();
            Assert.AreEqual(AssetMarketKind.Stock, heated.Kind);
            Assert.IsEmpty(events.OfType<MarketBurst>());
            Assert.AreEqual((byte)AssetMarketPhase.Heated, sim.World.Get<AssetMarkets>().Phase[0]);
        }

        [Test]
        public void AMarket_BurstsAboveTheSecondLine_AndItsNationsCreditStops()
        {
            using var sim = TestWorlds.BuildSample();
            var markets = sim.World.Get<AssetMarkets>();
            var events = HoldUntilQuarterEnd(sim, AssetMarkets.Stock(0), Fx.FromInt(3));
            var burst = events.OfType<MarketBurst>().Single();
            Assert.AreEqual(sim.World.Get<NodeBanks>().NationNode(0), burst.Node.Value);
            Assert.AreEqual((byte)AssetMarketPhase.Bust, markets.Phase[0]);
            Assert.Less(markets.Premium[0], Fx.FromInt(3).Raw, "the price falls at once");
            Assert.Less(markets.Expectation[0], 0, "and buyers expect it to fall");
            Assert.AreEqual(FinancialEventKind.CreditCrunch, events.OfType<FinancialEventStarted>().Single(e => e.Nation.Value == burst.Node.Value).Kind);
            Assert.AreEqual(FinancialEventKind.CreditCrunch, sim.World.Get<FinanceState>().EventOf(0));
        }

        [Test]
        public void ABurst_OnAPropertyMarket_StopsItsNationsCredit()
        {
            using var sim = TestWorlds.BuildSample();
            var markets = sim.World.Get<AssetMarkets>();
            var banks = sim.World.Get<NodeBanks>();
            int n = Enumerable.Range(0, sim.World.Get<LandState>().Plots.Length).First(x => banks.NationOf(x) >= 0 && markets.Households(x) == x);
            var events = HoldUntilQuarterEnd(sim, markets.Property(n), Fx.FromInt(3));
            Assert.AreEqual(AssetMarketKind.Property, events.OfType<MarketBurst>().Single().Kind);
            Assert.AreEqual(FinancialEventKind.CreditCrunch, sim.World.Get<FinanceState>().EventOf(banks.NationOf(n)));
        }

        [Test]
        public void AFall_CallsTheHouseholdsMargin()
        {
            using var sim = Listed();
            var markets = sim.World.Get<AssetMarkets>();
            for (int t = 0; t < 60; t++) sim.AdvanceTurn();
            int m = Enumerable.Range(0, markets.NationCount).First(x => markets.Debt[x] > 0);
            long debt = markets.Debt[m];
            markets.Premium[m] = 1; // all but worthless
            sim.AdvanceTurn();
            Assert.Less(markets.Debt[m], debt / 2, "paid back or written off down to the loan-to-value");
        }

        [Test]
        public void ABankUnderItsCover_HasARun()
        {
            using var sim = SimulationBuilder.FromData(TestWorlds.SampleConfig("finance.run_cover_permille,1000000000"), 1).Build();
            var events = new List<ISimEvent>();
            for (int t = 0; t < 2 * SimClock.WeeksPerQuarter; t++)
            {
                sim.AdvanceTurn();
                events.AddRange(sim.LastTurnEvents);
            }
            Assert.IsTrue(events.OfType<FinancialEventStarted>().Any(e => e.Kind == FinancialEventKind.BankRun));
        }

        [Test]
        public void TheSampleData_DrawsNoRandomEvents()
        {
            using var sim = TestWorlds.BuildSample();
            Assert.AreEqual(0, sim.Database.Config.FinancialEventChancePermille);
        }

        [Test]
        public void Markets_KeepTheCoins_AndAreSaved()
        {
            using var sim = Listed();
            var currency = sim.World.Get<CurrencyState>();
            for (int t = 0; t < 80; t++)
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
