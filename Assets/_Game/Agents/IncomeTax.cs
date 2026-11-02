using System;
using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;
using Unity.Collections;

namespace FES.Agents
{
    public sealed class IncomeTaxCollected : ISimEvent
    {
        /// <summary>The year taxed.</summary>
        public readonly int Year;
        public readonly long Households;
        public readonly long Companies;

        public IncomeTaxCollected(int year, long households, long companies)
        {
            Year = year;
            Households = households;
            Companies = companies;
        }
    }

    /// <summary>
    /// What the income tax needs to know of each company: its cash when the tax year began, and the dividends a
    /// facility has paid since. A new operator starts a new book, so its starting cash is not profit.
    /// </summary>
    public sealed class IncomeTaxState : IPersistentComponent, IDisposable
    {
        public NativeList<long> FacilityStart;
        public NativeList<long> FacilityDividends;
        public NativeList<long> MerchantStart;
        /// <summary>False until the books are opened: a new game, or a save from before the tax.</summary>
        public bool Opened;
        /// <summary>Per continent: last year's collections.</summary>
        public readonly long[] LastHouseholds;
        public readonly long[] LastCompanies;

        public string Key => "agents.income_tax";
        public int Version => 1;

        public IncomeTaxState(int continents)
        {
            FacilityStart = new NativeList<long>(Allocator.Persistent);
            FacilityDividends = new NativeList<long>(Allocator.Persistent);
            MerchantStart = new NativeList<long>(Allocator.Persistent);
            LastHouseholds = new long[continents];
            LastCompanies = new long[continents];
        }

        /// <summary>Companies founded since the last call start their books with their cash.</summary>
        public void EnsureRows(FacilityTable facilities, AgentTable agents)
        {
            for (int f = FacilityStart.Length; f < facilities.Count; f++)
            {
                FacilityStart.Add(facilities.Cash[f]);
                FacilityDividends.Add(0);
            }
            for (int a = MerchantStart.Length; a < agents.Count; a++) MerchantStart.Add(agents.Cash[a]);
        }

        public void RestartFacility(int f, long cash)
        {
            if (f >= FacilityStart.Length) return;
            FacilityStart[f] = cash;
            FacilityDividends[f] = 0;
        }

        public void RestartMerchant(int a, long cash)
        {
            if (a < MerchantStart.Length) MerchantStart[a] = cash;
        }

        /// <summary>Cash that came or went without being earned or spent moves the start of the book with it, so it is no profit or loss (DEC-107).</summary>
        public void MoveFacilityCapital(int f, long amount)
        {
            if (f < FacilityStart.Length) FacilityStart[f] += amount;
        }

        public void MoveMerchantCapital(int a, long amount)
        {
            if (a < MerchantStart.Length) MerchantStart[a] += amount;
        }

        /// <summary>A facility's profit this tax year, holding this cash: the change in its cash plus the dividends it paid.</summary>
        public long FacilityProfit(int f, long cash) => f < FacilityStart.Length ? cash - FacilityStart[f] + FacilityDividends[f] : 0;

        public void Write(BinaryWriter writer)
        {
            writer.Write(Opened);
            NativeIO.WriteList(writer, FacilityStart);
            NativeIO.WriteList(writer, FacilityDividends);
            NativeIO.WriteList(writer, MerchantStart);
            for (int c = 0; c < LastHouseholds.Length; c++)
            {
                writer.Write(LastHouseholds[c]);
                writer.Write(LastCompanies[c]);
            }
        }

        public void Read(BinaryReader reader)
        {
            Opened = reader.ReadBoolean();
            NativeIO.ReadList(reader, FacilityStart);
            NativeIO.ReadList(reader, FacilityDividends);
            NativeIO.ReadList(reader, MerchantStart);
            for (int c = 0; c < LastHouseholds.Length; c++)
            {
                LastHouseholds[c] = reader.ReadInt64();
                LastCompanies[c] = reader.ReadInt64();
            }
        }

        public void Dispose()
        {
            FacilityStart.DisposeIfCreated();
            FacilityDividends.DisposeIfCreated();
            MerchantStart.DisposeIfCreated();
        }
    }

    /// <summary>
    /// Once a year every node's households pay income tax on what they measurably received, and every facility
    /// and merchant group on its profit: the change in its cash plus the dividends it paid. Loans, their repayment
    /// (interest included, so it is not deducted) and what owners or associations put in or take out are no profit or
    /// loss. A loss carries no relief, and nobody pays beyond the cash they hold. The money goes to the nation's bank reserve (or the
    /// international bank outside a nation), and the state spends it through its reserve rule.
    /// A bank-held facility pays nothing. Interest groups are not taxed: their income is taxed facilities'
    /// profit, and it is taxed again in households when passed on. Runs before the year's books close.
    /// </summary>
    public sealed class IncomeTaxSystem : ISimSystem
    {
        SimulationConfig _config;
        NodeTable _nodes;
        HouseholdCash _households;
        FacilityTable _facilities;
        AgentTable _agents;
        IncomeTaxState _books;
        WorldState _world;

        public string Id => "agents.income_tax";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.IncomeTax;

        public void Initialize(SimContext ctx)
        {
            _config = ctx.Services.Get<GameDatabase>().Config;
            _nodes = ctx.World.Get<NodeTable>();
            _households = ctx.World.Get<HouseholdCash>();
            _facilities = ctx.World.Get<FacilityTable>();
            _agents = ctx.World.Get<AgentTable>();
            _books = ctx.World.Get<IncomeTaxState>();
            _world = ctx.World;
        }

        public void Execute(SimContext ctx)
        {
            _books.EnsureRows(_facilities, _agents);
            if (!_books.Opened)
            {
                OpenBooks();
                return;
            }
            if (!ctx.Clock.IsNewYear || ctx.Turn == 0) return;

            Array.Clear(_books.LastHouseholds, 0, _books.LastHouseholds.Length);
            Array.Clear(_books.LastCompanies, 0, _books.LastCompanies.Length);
            long households = 0, companies = 0;
            for (int node = 0; node < _nodes.Count; node++)
            {
                long paid = Math.Min(_config.TaxHousehold.MulInt(Math.Max(0, _households.YearIncome(node))), Math.Max(0, _households.Cash[node]));
                if (paid <= 0) continue;
                _households.Cash[node] -= paid;
                Nobles.CollectTax(_world, _config, node, paid);
                _books.LastHouseholds[_nodes.Continent[node]] += paid;
                households += paid;
            }
            for (int f = 0; f < _facilities.Count; f++)
            {
                if (_facilities.Owner[f] == OwnerIds.Bank) continue;
                long profit = _books.FacilityProfit(f, _facilities.Cash[f]);
                long paid = Pay(_facilities.Node[f], profit, _facilities.Cash[f]);
                _facilities.Cash[f] -= paid;
                companies += paid;
            }
            for (int a = 0; a < _agents.Count; a++)
            {
                long paid = Pay(MoneyLedger.MerchantSettlesAt(_nodes, _agents, a), _agents.Cash[a] - _books.MerchantStart[a], _agents.Cash[a]);
                _agents.Cash[a] -= paid;
                _world.Get<MerchantTripStats>().SpentTax += paid;
                companies += paid;
            }
            OpenBooks();
            ctx.Events.Publish(new IncomeTaxCollected(ctx.Clock.Year - 1, households, companies));
        }

        long Pay(int node, long profit, long cash)
        {
            long paid = Math.Min(_config.TaxCompany.MulInt(Math.Max(0, profit)), Math.Max(0, cash));
            if (paid <= 0) return 0;
            Nobles.CollectTax(_world, _config, node, paid);
            _books.LastCompanies[_nodes.Continent[node]] += paid;
            return paid;
        }

        void OpenBooks()
        {
            for (int f = 0; f < _facilities.Count; f++)
            {
                _books.FacilityStart[f] = _facilities.Cash[f];
                _books.FacilityDividends[f] = 0;
            }
            for (int a = 0; a < _agents.Count; a++) _books.MerchantStart[a] = _agents.Cash[a];
            _books.Opened = true;
        }
    }
}
