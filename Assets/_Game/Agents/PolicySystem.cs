using System;
using System.Collections.Generic;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    public sealed class Lobbied : ISimEvent
    {
        public readonly NodeId Nation;
        public readonly PolicyKind Policy;
        public readonly int BaseGood;
        public readonly int Direction;
        public readonly long Amount;

        public Lobbied(NodeId nation, PolicyKind policy, int baseGood, int direction, long amount)
        {
            Nation = nation;
            Policy = policy;
            BaseGood = baseGood;
            Direction = direction;
            Amount = amount;
        }
    }

    /// <summary>One nation's yearly policy review (DEC-058): where each policy stands now and what moved.</summary>
    public sealed class PolicyReviewed : ISimEvent
    {
        public readonly NodeId Nation;
        public readonly int Year;
        public readonly int TollStep;
        public readonly int LicensePriceStep;
        /// <summary>The base good that now needs a license, or -1.</summary>
        public readonly int LicenseAdded;
        /// <summary>The base good that no longer needs one, or -1.</summary>
        public readonly int LicenseRemoved;
        public readonly long Influence;

        public PolicyReviewed(NodeId nation, int year, int tollStep, int licensePriceStep, int licenseAdded, int licenseRemoved, long influence)
        {
            Nation = nation;
            Year = year;
            TollStep = tollStep;
            LicensePriceStep = licensePriceStep;
            LicenseAdded = licenseAdded;
            LicenseRemoved = licenseRemoved;
            Influence = influence;
        }
    }

    public sealed class MintingPolicyChanged : ISimEvent
    {
        public readonly int Continent;
        public readonly int Step;

        public MintingPolicyChanged(int continent, int step)
        {
            Continent = continent;
            Step = step;
        }
    }

    /// <summary>
    /// Spends local coins lobbying a nation's policy in the company's name (DEC-058). The player must be in
    /// that nation; the coins go to its bank's reserve ([Draft]). Counts at the next yearly review.
    /// </summary>
    public sealed class LobbyCommand : ICommand
    {
        public readonly NodeId Nation;
        public readonly PolicyKind Policy;
        public readonly int Direction;
        public readonly long Amount;
        public readonly int BaseGood;

        public LobbyCommand(NodeId nation, PolicyKind policy, int direction, long amount, int baseGood = -1)
        {
            Nation = nation;
            Policy = policy;
            Direction = direction;
            Amount = amount;
            BaseGood = baseGood;
        }

        public CommandResult Validate(SimContext ctx)
        {
            if (ctx.World.Get<GroupTable>().PlayerCompany() < 0) return CommandResult.Fail("Lobbying is done in the company's name: found it first.");
            if (Amount <= 0) return CommandResult.Fail("Amount must be positive.");
            if (Direction != 1 && Direction != -1) return CommandResult.Fail("Direction must be +1 or -1.");
            if (Policy > PolicyKind.Minting) return CommandResult.Fail("Unknown policy.");
            var nodes = ctx.World.Get<NodeTable>();
            if (!Nation.IsValid || Nation.Value >= nodes.Count || nodes.LevelOf(Nation.Value) != WorldLevel.Nation)
                return CommandResult.Fail("Pick a nation.");
            int baseGoods = ctx.Services.Get<GameDatabase>().BaseGoodCount;
            if (Policy == PolicyKind.LicensedGoods ? BaseGood < 0 || BaseGood >= baseGoods : BaseGood != -1)
                return CommandResult.Fail("Name a good for licensing, and only for licensing.");
            var player = ctx.World.Get<PlayerState>();
            if (player.IsTravelling || nodes.AncestorAt(player.Node, WorldLevel.Nation) != Nation.Value)
                return CommandResult.Fail("Lobbying is done inside the nation.");
            return player.LocalCoins >= Amount ? CommandResult.Success : CommandResult.Fail("Not enough cash.");
        }

        public void Apply(SimContext ctx)
        {
            int nation = ctx.World.Get<NodeBanks>().NationIndex(Nation.Value);
            ctx.World.Get<PlayerState>().LocalCoins -= Amount;
            ctx.World.Get<BankState>().Reserve[nation] += Amount;
            ctx.World.Get<PolicyState>().Lobbying.Add(new LobbyEntry
            {
                Group = ctx.World.Get<GroupTable>().PlayerCompany(),
                Nation = nation,
                Policy = Policy,
                BaseGood = BaseGood,
                Direction = Direction,
                Amount = Amount,
            });
            ctx.Events.Publish(new Lobbied(Nation, Policy, BaseGood, Direction, Amount));
        }
    }

    /// <summary>
    /// The yearly policy review (DEC-058, DEC-059), after the year's statistics: every group's influence in each
    /// nation (assets, jobs, lobbying) weighs its stance on each policy; a policy whose weighted balance passes
    /// the config share of the nation's total influence moves one step. The share grows with every step the
    /// policy already stands from its start ([Draft] status-quo bias), so pressure settles at a level rather
    /// than pushing to the limit. Also reapplies toll rates after a load.
    /// </summary>
    public sealed class PolicySystem : ISimSystem, ILoadListener
    {
        SimulationConfig _config;
        GameDatabase _db;
        NodeTable _nodes;
        NodeBanks _banks;
        NodeTolls _tolls;
        PolicyState _policy;
        GroupTable _groups;
        GroupDirectory _directory;
        FacilityTable _facilities;
        AgentTable _agents;
        BankState _bank;
        CurrencyState _currency;
        FinanceState _finance;

        public string Id => "agents.policy";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.Policy;

        public void Initialize(SimContext ctx)
        {
            _db = ctx.Services.Get<GameDatabase>();
            _config = _db.Config;
            _nodes = ctx.World.Get<NodeTable>();
            _banks = ctx.World.Get<NodeBanks>();
            _tolls = ctx.World.Get<NodeTolls>();
            _policy = ctx.World.Get<PolicyState>();
            _groups = ctx.World.Get<GroupTable>();
            _directory = new GroupDirectory(_groups, _nodes);
            _facilities = ctx.World.Get<FacilityTable>();
            _agents = ctx.World.Get<AgentTable>();
            _bank = ctx.World.Get<BankState>();
            _currency = ctx.World.Get<CurrencyState>();
            _finance = ctx.World.Get<FinanceState>();
            ApplyTolls();
        }

        public void OnLoaded(SimContext ctx) => ApplyTolls();

        void ApplyTolls()
        {
            for (int n = 0; n < _policy.NationCount; n++)
                _tolls.ScaleNation(_nodes, _banks.NationNode(n), PolicyRules.Scale(_config, _policy.TollStep[n]));
        }

        public void Execute(SimContext ctx)
        {
            if (!ctx.Clock.IsNewYear || ctx.Turn == 0) return;
            new Review(this).Run(ctx);
        }

        /// <summary>What one group holds in one nation, and its stances there.</summary>
        sealed class Holding
        {
            public int Group;
            public int Nation;
            public long Assets;
            public long Jobs;
            public long OutputValue;
            public long InputValue;
            public readonly HashSet<int> Produces = new HashSet<int>();
            public readonly HashSet<int> Buys = new HashSet<int>();
            public int LicensedMerchants;
            public int UnlicensedMerchants;
        }

        /// <summary>One year's review; gathers holdings, then weighs every nation's policies.</summary>
        sealed class Review
        {
            readonly PolicySystem _s;
            readonly List<Holding> _holdings = new List<Holding>();
            readonly Dictionary<long, Holding> _byKey = new Dictionary<long, Holding>();
            long[] _tilt;
            long[] _lobbied;

            public Review(PolicySystem system) => _s = system;

            Holding Get(int group, int nation)
            {
                long key = ((long)group << 32) | (uint)nation;
                if (_byKey.TryGetValue(key, out var h)) return h;
                h = new Holding { Group = group, Nation = nation };
                _byKey.Add(key, h);
                _holdings.Add(h);
                return h;
            }

            int NationOf(int node) => _s._banks.NationOf(node);

            public void Run(SimContext ctx)
            {
                Gather(ctx);
                Weigh(ctx);
                _s._policy.Lobbying.Clear();
            }

            void Gather(SimContext ctx)
            {
                var groups = _s._groups;
                var worth = new long[groups.Count];
                for (int g = 0; g < groups.Count; g++) worth[g] = Math.Max(0, groups.Cash[g]);
                GatherFacilities(worth);
                GatherMerchants(ctx, worth);
                GatherGroups(worth);
            }

            void GatherFacilities(long[] worth)
            {
                var config = _s._config;
                var db = _s._db;
                var facilities = _s._facilities;
                for (int f = 0; f < facilities.Count; f++)
                {
                    int g = facilities.Group[f];
                    if (g < 0 || facilities.Owner[f] == OwnerIds.Bank) continue;
                    worth[g] += Math.Max(0, facilities.Cash[f]);
                    int node = facilities.Node[f];
                    int nation = NationOf(node);
                    if (nation < 0) continue;
                    var recipe = db.Facilities[facilities.DefIndex[f]].Recipe;
                    int level = facilities.Level[f];
                    var owner = Get(g, nation);
                    owner.Assets += NpcLending.FacilityValue(db, facilities, f) + Math.Max(0, facilities.Cash[f]);
                    owner.Jobs += (long)level * config.JobsPerFacilityLevel;
                    AddTrade(owner, recipe, level);
                    GuildSpeaks(node, nation, recipe, level);
                }
            }

            // Guilds speak for the trades their city's facilities work in (DEC-055).
            void GuildSpeaks(int node, int nation, RecipeDef recipe, int level)
            {
                int city = _s._directory.CityOf(node);
                if (city < 0) return;
                foreach (var line in recipe.Outputs)
                {
                    int guild = _s._directory.GuildOf(city, _s._db.Good(line.Good).CategoryIndex);
                    if (guild < 0) continue;
                    var members = Get(guild, nation);
                    members.Jobs += (long)level * _s._config.JobsPerFacilityLevel;
                    AddTrade(members, recipe, level);
                    break;
                }
            }

            void GatherMerchants(SimContext ctx, long[] worth)
            {
                var config = _s._config;
                var agents = _s._agents;
                for (int a = 0; a < agents.Count; a++)
                {
                    int g = agents.Group[a];
                    if (g < 0) continue;
                    worth[g] += Math.Max(0, agents.Cash[a]);
                    int nation = NationOf(agents.Home[a]);
                    if (nation < 0) continue;
                    var h = Get(g, nation);
                    long stake = MerchantCapital.Start(config, ctx.Seed, a);
                    h.Assets += stake + Math.Max(0, agents.Cash[a]);
                    h.Jobs += config.JobsPerMerchantGroup;
                    h.InputValue += stake; // merchants pay the tolls
                    if (LicenseRules.MerchantHoldsLicenses(ctx.Seed, new AgentId(a), config.LicensedMerchantPermille)) h.LicensedMerchants++;
                    else h.UnlicensedMerchants++;
                }
            }

            // Loss tilt: the share of last year's worth a group lost (DEC-059); lobbying from group cash.
            void GatherGroups(long[] worth)
            {
                var config = _s._config;
                var groups = _s._groups;
                var last = _s._policy.LastWorth;
                _tilt = new long[groups.Count];
                _lobbied = new long[groups.Count];
                for (int g = 0; g < groups.Count; g++)
                {
                    if (g < last.Count && last[g] > 0 && worth[g] < last[g])
                        _tilt[g] = Math.Min(Fx.OneRaw, (config.PolicyLossTilt * Fx.FromRatio(last[g] - worth[g], last[g])).Raw);
                    if (g < last.Count) last[g] = worth[g];
                    else last.Add(worth[g]);

                    int nation = NationOf(groups.Node[g]);
                    if (nation < 0) continue;
                    var h = Get(g, nation);
                    h.Assets += Math.Max(0, groups.Cash[g]);
                    if (groups.IsPlayer[g] != 0 || groups.Cash[g] <= 0) continue;
                    long spend = config.NpcLobbyShare.MulInt(groups.Cash[g]);
                    groups.Cash[g] -= spend;
                    _s._bank.Reserve[nation] += spend;
                    _lobbied[g] = spend;
                }
            }

            // What a facility sells and buys in a week at full output, valued at base prices ([Draft]).
            void AddTrade(Holding h, RecipeDef recipe, int level)
            {
                foreach (var line in recipe.Outputs) h.Produces.Add(line.Good.BaseIndex);
                foreach (var line in recipe.Inputs) h.Buys.Add(line.Good.BaseIndex);
                h.OutputValue += Value(recipe.Outputs, level);
                h.InputValue += Value(recipe.Inputs, level);
            }

            long Value(RecipeLine[] lines, int level)
            {
                long value = 0;
                foreach (var line in lines) value += (long)line.Quantity * level * _s._db.Good(line.Good).BasePrice;
                return value;
            }

            /// <summary>How the influence falls, per nation and per continent.</summary>
            sealed class Votes
            {
                public readonly int Goods;
                public readonly long[] Total, Tolls, Prices, AllGoods, PerGood, MintTotal, Mint;

                public Votes(int nations, int goods, int continents)
                {
                    Goods = goods;
                    Total = new long[nations];
                    Tolls = new long[nations];
                    Prices = new long[nations];
                    AllGoods = new long[nations];
                    PerGood = new long[nations * goods];
                    MintTotal = new long[continents];
                    Mint = new long[continents];
                }
            }

            void Weigh(SimContext ctx)
            {
                var policy = _s._policy;
                var votes = new Votes(policy.NationCount, policy.BaseGoodCount, policy.ContinentCount);
                foreach (var h in _holdings) CountHolding(h, votes);
                foreach (var e in policy.Lobbying) CountLobbying(e, votes);
                int year = ctx.Clock.Year - 1;
                for (int n = 0; n < policy.NationCount; n++) DecideNation(ctx, n, year, votes);
                for (int c = 0; c < policy.ContinentCount; c++) DecideMinting(ctx, c, votes);
            }

            void CountHolding(Holding h, Votes votes)
            {
                var s = _s;
                var config = s._config;
                var kind = s._groups.KindOf(h.Group);
                long influence = config.InfluenceAssetWeight.MulInt(h.Assets) + h.Jobs * config.InfluencePerJob;
                if (NationOf(s._groups.Node[h.Group]) == h.Nation) influence += config.InfluenceLobbyWeight.MulInt(_lobbied[h.Group]);
                int n = h.Nation;
                int continent = s._nodes.Continent[s._banks.NationNode(n)];
                votes.Total[n] += influence;
                votes.MintTotal[continent] += influence;
                if (kind == GroupKind.StateEnterprise)
                {
                    // Up on tolls, wider and dearer licenses, more minting (DEC-059).
                    votes.Tolls[n] += influence;
                    votes.Prices[n] += influence;
                    votes.AllGoods[n] += influence;
                    votes.Mint[continent] += influence;
                    return;
                }

                // Everyone else opposes minting once prices rose over the published year (DEC-060).
                votes.Mint[continent] -= Fx.FromRaw(MintingOpposition(continent)).MulInt(influence);

                long tilt = _tilt[h.Group];
                long trade = h.OutputValue + h.InputValue;
                long toll = (trade == 0 ? 0 : Fx.FromRatio(h.OutputValue - h.InputValue, trade).Raw) + tilt;
                votes.Tolls[n] += Fx.FromRaw(Clamp(toll)).MulInt(influence);
                CountGoods(h, tilt, influence, votes);
            }

            void CountGoods(Holding h, long tilt, long influence, Votes votes)
            {
                int n = h.Nation;
                long priceStance = 0;
                int licensedInvolved = 0;
                _involved.Clear();
                _involved.UnionWith(h.Produces);
                _involved.UnionWith(h.Buys);
                if (h.LicensedMerchants + h.UnlicensedMerchants > 0) _involved.UnionWith(LicensedGoods(n));
                foreach (int b in _involved)
                {
                    bool licensed = _s._policy.Requires(n, b);
                    long stance = GoodStance(h, b, licensed, tilt);
                    if (stance == long.MinValue) continue;
                    votes.PerGood[n * votes.Goods + b] += Fx.FromRaw(stance).MulInt(influence);
                    if (!licensed) continue;
                    priceStance += stance;
                    licensedInvolved++;
                }
                if (licensedInvolved > 0) votes.Prices[n] += Fx.FromRaw(priceStance / licensedInvolved).MulInt(influence);
            }

            void CountLobbying(LobbyEntry e, Votes votes)
            {
                var s = _s;
                long weight = s._config.InfluenceLobbyWeight.MulInt(e.Amount);
                long push = e.Direction * weight;
                int continent = s._nodes.Continent[s._banks.NationNode(e.Nation)];
                votes.Total[e.Nation] += weight;
                switch (e.Policy)
                {
                    case PolicyKind.Tolls: votes.Tolls[e.Nation] += push; break;
                    case PolicyKind.LicensePrice: votes.Prices[e.Nation] += push; break;
                    case PolicyKind.LicensedGoods: votes.PerGood[e.Nation * votes.Goods + e.BaseGood] += push; break;
                    case PolicyKind.Minting:
                        votes.Mint[continent] += push;
                        votes.MintTotal[continent] += weight;
                        break;
                }
            }

            void DecideNation(SimContext ctx, int n, int year, Votes votes)
            {
                var s = _s;
                var config = s._config;
                long threshold = config.PolicyMoveThreshold.MulInt(votes.Total[n]);
                int tollBefore = s._policy.TollStep[n];
                s._policy.TollStep[n] = Step(tollBefore, votes.Tolls[n], threshold);
                s._policy.LicensePriceStep[n] = Step(s._policy.LicensePriceStep[n], votes.Prices[n], threshold);
                ChangeLicensedGoods(n, votes.PerGood, votes.AllGoods[n], Resisted(threshold, LicensedChange(n)), out int added, out int removed);
                s._policy.LastInfluence[n] = votes.Total[n];
                int nationNode = s._banks.NationNode(n);
                if (s._policy.TollStep[n] != tollBefore)
                    s._tolls.ScaleNation(s._nodes, nationNode, PolicyRules.Scale(config, s._policy.TollStep[n]));
                ctx.Events.Publish(new PolicyReviewed(new NodeId(nationNode), year, s._policy.TollStep[n],
                    s._policy.LicensePriceStep[n], added, removed, votes.Total[n]));
            }

            void DecideMinting(SimContext ctx, int c, Votes votes)
            {
                var s = _s;
                int before = s._policy.MintStep[c];
                s._policy.MintStep[c] = Step(before, votes.Mint[c], s._config.PolicyMoveThreshold.MulInt(votes.MintTotal[c]));
                if (s._policy.MintStep[c] == before) return;
                s._currency.MintRateMultiplier[c] = PolicyRules.Scale(s._config, s._policy.MintStep[c]);
                ctx.Events.Publish(new MintingPolicyChanged(c, s._policy.MintStep[c]));
            }

            readonly HashSet<int> _involved = new HashSet<int>();
            readonly Dictionary<int, List<int>> _licensedGoods = new Dictionary<int, List<int>>();

            // The goods a nation licenses as the review began.
            List<int> LicensedGoods(int n)
            {
                if (_licensedGoods.TryGetValue(n, out var list)) return list;
                list = new List<int>();
                for (int b = 0; b < _s._policy.BaseGoodCount; b++)
                    if (_s._policy.Requires(n, b)) list.Add(b);
                _licensedGoods.Add(n, list);
                return list;
            }

            // The published inflation over the config level that turns a group fully against minting, at most 1.
            long MintingOpposition(int continent)
            {
                long inflation = _s._finance.PublishedInflation[continent];
                long full = _s._config.InflationOpposition.Raw;
                if (inflation <= 0 || full <= 0) return 0;
                return Math.Min(Fx.OneRaw, Fx.FromRatio(inflation, full).Raw);
            }

            // A group's stance on licensing a good, or long.MinValue when it has none (DEC-059): protect what it
            // produces, relax what it buys; its merchants protect licenses they hold and relax those they lack.
            static long GoodStance(Holding h, int b, bool licensed, long tilt)
            {
                long stance = 0;
                bool involved = false;
                if (h.Produces.Contains(b))
                {
                    stance += Fx.OneRaw;
                    involved = true;
                }
                if (h.Buys.Contains(b))
                {
                    stance -= Fx.OneRaw;
                    involved = true;
                }
                int merchants = h.LicensedMerchants + h.UnlicensedMerchants;
                if (licensed && merchants > 0)
                {
                    stance += Fx.FromRatio(h.LicensedMerchants - h.UnlicensedMerchants, 2L * merchants).Raw;
                    involved = true;
                }
                return involved ? Clamp(stance + tilt) : long.MinValue;
            }

            // One good per review: the strongest case to license an unlicensed good or to free a licensed one.
            void ChangeLicensedGoods(int n, long[] perGood, long allGoods, long threshold, out int added, out int removed)
            {
                added = removed = -1;
                var policy = _s._policy;
                int goods = policy.BaseGoodCount;
                long bestAdd = 0, bestRemove = 0;
                for (int b = 0; b < goods; b++)
                {
                    long net = perGood[n * goods + b] + allGoods;
                    if (!policy.Requires(n, b) && net >= threshold && net > bestAdd)
                    {
                        bestAdd = net;
                        added = b;
                    }
                    if (policy.Requires(n, b) && net <= -threshold && -net > bestRemove)
                    {
                        bestRemove = -net;
                        removed = b;
                    }
                }
                if (added >= 0 && removed >= 0)
                {
                    if (bestAdd >= bestRemove) removed = -1;
                    else added = -1;
                }
                if (added >= 0) policy.Licensed[n * goods + added] = 1;
                if (removed >= 0) policy.Licensed[n * goods + removed] = 0;
            }

            long Resisted(long threshold, int step) =>
                threshold + (_s._config.PolicyResistance * Fx.FromInt(Math.Abs(step))).MulInt(threshold);

            // How many goods the nation licenses beyond (or below) goods.csv's list.
            int LicensedChange(int n)
            {
                var policy = _s._policy;
                int change = 0;
                for (int b = 0; b < policy.BaseGoodCount; b++)
                    change += policy.Licensed[n * policy.BaseGoodCount + b] - policy.Licensed[policy.NationCount * policy.BaseGoodCount + b];
                return change;
            }

            int Step(int step, long net, long threshold)
            {
                threshold = Resisted(threshold, step);
                int max = _s._config.PolicyMaxSteps;
                if (net > 0 && net >= threshold) return Math.Min(max, step + 1);
                if (net < 0 && -net >= threshold) return Math.Max(-max, step - 1);
                return step;
            }

            static long Clamp(long raw) => Math.Max(-Fx.OneRaw, Math.Min(Fx.OneRaw, raw));
        }
    }
}
