using System;
using System.IO;
using FES.Core;
using FES.Data;
using FES.World;

namespace FES.Agents
{
    /// <summary>
    /// Merchants in associations, a safety net for supply (DEC-086). Each year a group not yet in one may join: the
    /// richest guild of its home's city, else the richest company of its nation. Members pay a yearly fee on cash above
    /// their starting cash, and the association tops up a member running short, from a share of its own cash, so no
    /// coins appear. Members using carts and wagons pay part of the insurance premium (<see cref="DetailedMerchantJob"/>).
    /// Off unless merchants.join_permille is set.
    /// </summary>
    public sealed class MerchantMembershipSystem : ISimSystem
    {
        static readonly uint JoinStream = DetHash.Of("agents.merchant_join");

        SimulationConfig _config;
        AgentTable _agents;
        GroupTable _groups;
        NodeTable _nodes;
        GroupDirectory _directory;

        public string Id => "agents.merchant_membership";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.MerchantMembership;

        public void Initialize(SimContext ctx)
        {
            _config = ctx.Services.Get<GameDatabase>().Config;
            _agents = ctx.World.Get<AgentTable>();
            _groups = ctx.World.Get<GroupTable>();
            _nodes = ctx.World.Get<NodeTable>();
            _directory = new GroupDirectory(_groups, _nodes);
        }

        public void Execute(SimContext ctx)
        {
            if (_config.MerchantJoinShare.Raw <= 0) return;
            bool year = ctx.Clock.IsNewYear;
            for (int a = 0; a < _agents.Count; a++)
            {
                if (_agents.Owner[a] != OwnerIds.Npc) continue;
                int association = _agents.Association[a];
                if (association < 0)
                {
                    if (year && DetRandom.For(ctx.Seed, ctx.Turn, JoinStream, a).NextFx01().Raw < _config.MerchantJoinShare.Raw)
                        _agents.Association[a] = Find(_agents.Home[a]);
                    continue;
                }
                long start = _config.MerchantStartCash[_agents.Scale[a]];
                if (year && _agents.Cash[a] > start)
                {
                    long fee = _config.MerchantMemberFee.MulInt(_agents.Cash[a] - start);
                    _agents.Cash[a] -= fee;
                    ctx.World.Get<MerchantTripStats>().SpentFeesToAssociations += fee;
                    _groups.Cash[association] += fee;
                }
                long floor = _config.MerchantSupportFloor.MulInt(start);
                if (_agents.Cash[a] >= floor) continue;
                long grant = Math.Min(floor - _agents.Cash[a], _config.MerchantSupportShare.MulInt(Math.Max(0, _groups.Cash[association])));
                if (grant <= 0) continue;
                _groups.Cash[association] -= grant;
                _agents.Cash[a] += grant;
                ctx.World.Get<IncomeTaxState>().MoveMerchantCapital(a, grant);
            }
        }

        int Find(int home)
        {
            int city = _directory.CityOf(home), nation = _nodes.AncestorAt(home, WorldLevel.Nation), best = -1;
            for (int g = 0; g < _groups.Count; g++)
            {
                if (_groups.IsPlayer[g] != 0) continue;
                bool may = city >= 0
                    ? _groups.KindOf(g) == GroupKind.Guild && _groups.Node[g] == city
                    : _groups.KindOf(g) == GroupKind.Company && _groups.Node[g] == nation;
                if (may && (best < 0 || _groups.Cash[g] > _groups.Cash[best])) best = g;
            }
            return best;
        }
    }

    /// <summary>Version 5 had no associations: every group starts outside one.</summary>
    public sealed class AgentTableV5Migration : ISaveMigration
    {
        public string SectionKey => AgentTable.SectionKey;
        public int FromVersion => 5;

        public byte[] Migrate(byte[] section)
        {
            int rows = new BinaryReader(new MemoryStream(section)).ReadInt32();
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(rows);
                for (int r = 0; r < rows; r++) writer.Write(-1);
            }
            return output.ToArray();
        }
    }
}
