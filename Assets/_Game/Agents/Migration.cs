using System;
using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    public sealed class EmancipationEnacted : ISimEvent
    {
        public readonly NodeId Nation;
        public readonly int Year;

        public EmancipationEnacted(NodeId nation, int year)
        {
            Nation = nation;
            Year = year;
        }
    }

    public sealed class PeopleMigrated : ISimEvent
    {
        public readonly NodeId From;
        public readonly NodeId To;
        public readonly long People;

        public PeopleMigrated(NodeId from, NodeId to, long people)
        {
            From = from;
            To = to;
            People = people;
        }
    }

    /// <summary>
    /// Who may leave (DEC-079): per node the share of free people, the rest bound to their lord's land. Town air makes
    /// free, so towns are all free. Per nation: the year emancipation was enacted, or -1.
    /// </summary>
    public sealed class MigrationState : IPersistentComponent
    {
        /// <summary>Raw Fx, per node.</summary>
        public readonly long[] FreeShare;
        public readonly int[] EnactedYear;

        public string Key => "agents.migration";
        public int Version => 1;

        public MigrationState(NodeTable nodes, int nations, SimulationConfig config)
        {
            FreeShare = new long[nodes.Count];
            EnactedYear = new int[nations];
            for (int n = 0; n < nodes.Count; n++)
                FreeShare[n] = nodes.LevelOf(n) == WorldLevel.Village ? config.MigrationStartFreeShare.Raw : Fx.OneRaw;
            for (int n = 0; n < nations; n++) EnactedYear[n] = -1;
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(FreeShare.Length);
            foreach (long v in FreeShare) writer.Write(v);
            foreach (int v in EnactedYear) writer.Write(v);
        }

        public void Read(BinaryReader reader)
        {
            if (reader.ReadInt32() != FreeShare.Length) throw new InvalidDataException("Saved node count differs.");
            for (int n = 0; n < FreeShare.Length; n++) FreeShare[n] = reader.ReadInt64();
            for (int n = 0; n < EnactedYear.Length; n++) EnactedYear[n] = reader.ReadInt32();
        }
    }

    /// <summary>
    /// Rural-urban migration, once a year (DEC-079). People weigh a village's income per head against a town's wage times
    /// the chance of a job there (Harris-Todaro), and free people move on the gap, up to a limit. Bound people run
    /// away only at a small rate, faster where the lord broke the contract (year and a day in a town frees them).
    /// When a nation's towns stay short of workers it enacts emancipation: after a lead time villages free a share a
    /// year, and freed households pay their lord redemption for decades (Prussia 1807, Russia 1861). Migrants carry
    /// their share of the node's cash, so no coins appear. Off unless migration.max_share_permille is set.
    /// </summary>
    public sealed class MigrationSystem : ISimSystem
    {
        SimulationConfig _config;
        NodeTable _nodes;
        NodeBanks _banks;
        PurseState _purses;
        HouseholdState _population;
        HouseholdCash _cash;
        FacilityTable _facilities;
        LaborState _labor;
        NobleState _houses;
        MigrationState _state;
        long[] _jobs;

        public string Id => "agents.migration";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.Migration;

        public void Initialize(SimContext ctx)
        {
            _config = ctx.Services.Get<GameDatabase>().Config;
            _nodes = ctx.World.Get<NodeTable>();
            _banks = ctx.World.Get<NodeBanks>();
            _purses = ctx.World.Get<PurseState>();
            _population = ctx.World.Get<HouseholdState>();
            _cash = ctx.World.Get<HouseholdCash>();
            _facilities = ctx.World.Get<FacilityTable>();
            _labor = ctx.World.Get<LaborState>();
            _houses = ctx.World.Get<NobleState>();
            _state = ctx.World.Get<MigrationState>();
            _jobs = new long[_nodes.Count];
        }

        public void Execute(SimContext ctx)
        {
            if (!ctx.Clock.IsNewYear || ctx.Turn == 0 || _config.MigrationMaxShare.Raw <= 0) return;
            Array.Clear(_jobs, 0, _jobs.Length);
            for (int f = 0; f < _facilities.Count; f++)
                _jobs[_facilities.Node[f]] += _facilities.Level[f] * _config.MigrationJobsPerFacilityLevel;
            for (int n = 0; n < _banks.NationCount; n++)
            {
                Emancipate(ctx, n);
                Move(ctx, n);
            }
        }

        void Emancipate(SimContext ctx, int nation)
        {
            long jobs = 0, people = 0, bound = 0;
            foreach (int node in _purses.NationNodes[nation])
            {
                if (IsTown(node))
                {
                    jobs += _jobs[node];
                    people += _population.Population[node];
                }
                else bound += _population.Population[node] - Free(node, _population.Population[node]);
            }
            int enacted = _state.EnactedYear[nation];
            if (enacted < 0)
            {
                if (ctx.Turn < _config.MigrationEmancipationEarliestYears * SimClock.WeeksPerYear) return;
                if (bound <= 0 || jobs <= 0 || jobs - people < _config.MigrationVacancyShare.MulInt(jobs)) return;
                _state.EnactedYear[nation] = ctx.Clock.Year;
                ctx.Events.Publish(new EmancipationEnacted(new NodeId(_banks.NationNode(nation)), ctx.Clock.Year));
                return;
            }
            int freedFrom = enacted + _config.MigrationEmancipationLeadYears;
            if (ctx.Clock.Year < freedFrom) return;
            foreach (int node in _purses.NationNodes[nation])
            {
                if (IsTown(node)) continue;
                _state.FreeShare[node] = Math.Min(Fx.OneRaw, _state.FreeShare[node] + _config.MigrationFreedPerYear.Raw);
                if (ctx.Clock.Year >= freedFrom + _config.MigrationRedemptionYears) continue;
                long paid = Math.Min(_config.MigrationRedemptionShare.MulInt(Math.Max(0, _cash.YearIncome(node))), Math.Max(0, _cash.Cash[node]));
                _cash.Cash[node] -= paid;
                _houses.Cash[node] += paid;
            }
        }

        void Move(SimContext ctx, int nation)
        {
            var nodes = _purses.NationNodes[nation];
            var rate = _labor.Rate(nation);
            foreach (int village in nodes)
            {
                if (IsTown(village)) continue;
                long people = _population.Population[village];
                if (people <= 0) continue;
                long here = PerHead(village);
                // Towns by expected gain; the best sets how many want to go, each takes its part.
                long best = 0, weights = 0;
                foreach (int town in nodes)
                {
                    if (!IsTown(town)) continue;
                    long gain = Expected(town, rate) - here;
                    if (gain <= 0) continue;
                    if (gain > best) best = gain;
                    weights += Weight(town, gain);
                }
                if (best <= 0 || weights <= 0) continue;

                long free = Free(village, people);
                long bound = people - free;
                long wish = Fx.FromRatio(best, Math.Max(best, here) + best).MulInt(_config.MigrationMaxShare.MulInt(people) * 2);
                long freeMoving = Math.Min(free, wish);
                var flee = _houses.ContractHolds[village] ? _config.MigrationFleeShare : _config.MigrationFleeShare * Fx.FromInt(_config.MigrationBrokenContractFactor);
                long boundMoving = flee.MulInt(bound);
                long moving = freeMoving + boundMoving;
                if (moving <= 0) continue;

                long cashMoving = people > 0 ? Math.Max(0, _cash.Cash[village]) / people * moving
                    + Math.Max(0, _cash.Cash[village]) % people * moving / people : 0;
                long leftPeople = moving, leftCash = cashMoving, leftWeights = weights;
                foreach (int town in nodes)
                {
                    if (!IsTown(town)) continue;
                    long gain = Expected(town, rate) - here;
                    if (gain <= 0) continue;
                    long weight = Weight(town, gain);
                    long toPeople = leftPeople * weight / leftWeights;
                    long toCash = leftCash * weight / leftWeights;
                    leftWeights -= weight;
                    leftPeople -= toPeople;
                    leftCash -= toCash;
                    _population.Population[town] += toPeople;
                    _cash.Cash[town] += toCash;
                    if (toPeople > 0) ctx.Events.Publish(new PeopleMigrated(new NodeId(village), new NodeId(town), toPeople));
                }
                _cash.Cash[village] -= cashMoving;
                _population.Population[village] = people - moving;
                long leftFree = free - freeMoving;
                _state.FreeShare[village] = people - moving > 0 ? Fx.FromRatio(leftFree, people - moving).Raw : Fx.OneRaw;
            }
        }

        bool IsTown(int node) => _nodes.LevelOf(node) != WorldLevel.Village;

        long Free(int node, long people) => Fx.FromRaw(_state.FreeShare[node]).MulInt(people);

        long PerHead(int node)
        {
            long people = _population.Population[node];
            return people > 0 ? Fx.FromRatio(Math.Max(0, _cash.YearIncome(node)), people).Raw : 0;
        }

        // The wage here times the chance of a job: the jobs per head, at most one, times the nation's employment rate.
        long Expected(int town, Fx rate)
        {
            long people = Math.Max(1, _population.Population[town]);
            var chance = Fx.Min(Fx.One, Fx.FromRatio(_jobs[town], people));
            return (Fx.FromRaw(PerHead(town)) * chance * rate).Raw;
        }

        // Open jobs draw people; a full town still draws a little by its gain.
        long Weight(int town, long gain) => (gain >> 6) * (1 + Math.Max(0, _jobs[town] - _population.Population[town]) / 1000) + 1;
    }
}
