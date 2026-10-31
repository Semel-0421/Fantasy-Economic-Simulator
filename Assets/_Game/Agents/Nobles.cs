using System;
using System.IO;
using FES.Core;
using FES.Data;
using FES.World;

namespace FES.Agents
{
    public sealed class NobleContractBroken : ISimEvent
    {
        public readonly NodeId Vassal;
        public readonly NodeId Lord;
        public readonly int Turn;

        public NobleContractBroken(NodeId vassal, NodeId lord, int turn)
        {
            Vassal = vassal;
            Lord = lord;
            Turn = turn;
        }
    }

    /// <summary>
    /// Who holds each fief (DEC-076): a noble house in every village and city, a royal house at every nation node,
    /// and the nation's first city as the royal domain. A vassal owes its lord aid; the lord owes the vassal
    /// protection. The map decides the holders, so only money and contracts are saved.
    /// </summary>
    public sealed class NobleState : IPersistentComponent
    {
        /// <summary>Per node: the purse of the house seated there.</summary>
        public readonly long[] Cash;
        /// <summary>Per vassal: the protection its lord owes for last year's aid.</summary>
        public readonly long[] ProtectionOwed;
        /// <summary>Per vassal: false for a year after its lord failed to protect it, so it withholds aid.</summary>
        public readonly bool[] ContractHolds;
        /// <summary>Per city: the house that holds it, its own or the royal house for the domain. -1 elsewhere.</summary>
        public readonly int[] CityHolder;
        /// <summary>Per node: its lord's node, or -1 when it is no vassal.</summary>
        public readonly int[] Lord;

        public string Key => "agents.nobles";
        public int Version => 1;

        public NobleState(NodeTable nodes)
        {
            Cash = new long[nodes.Count];
            ProtectionOwed = new long[nodes.Count];
            ContractHolds = new bool[nodes.Count];
            CityHolder = new int[nodes.Count];
            Lord = new int[nodes.Count];
            var domainTaken = new bool[nodes.Count];
            for (int n = 0; n < nodes.Count; n++)
            {
                ContractHolds[n] = true;
                CityHolder[n] = -1;
                Lord[n] = -1;
            }
            for (int n = 0; n < nodes.Count; n++)
            {
                if (nodes.LevelOf(n) != WorldLevel.City) continue;
                int nation = nodes.AncestorAt(n, WorldLevel.Nation);
                if (nation < 0)
                {
                    CityHolder[n] = n;
                    continue;
                }
                CityHolder[n] = domainTaken[nation] ? n : nation;
                if (domainTaken[nation]) Lord[n] = nation;
                domainTaken[nation] = true;
            }
            for (int n = 0; n < nodes.Count; n++)
            {
                if (nodes.LevelOf(n) != WorldLevel.Village) continue;
                int city = nodes.AncestorAt(n, WorldLevel.City);
                Lord[n] = city >= 0 ? CityHolder[city] : nodes.AncestorAt(n, WorldLevel.Nation);
            }
        }

        public bool IsRoyalDomain(int city) => CityHolder[city] >= 0 && CityHolder[city] != city;

        public void Write(BinaryWriter writer)
        {
            writer.Write(Cash.Length);
            for (int n = 0; n < Cash.Length; n++)
            {
                writer.Write(Cash[n]);
                writer.Write(ProtectionOwed[n]);
                writer.Write(ContractHolds[n]);
            }
        }

        public void Read(BinaryReader reader)
        {
            if (reader.ReadInt32() != Cash.Length) throw new InvalidDataException("Saved node count differs.");
            for (int n = 0; n < Cash.Length; n++)
            {
                Cash[n] = reader.ReadInt64();
                ProtectionOwed[n] = reader.ReadInt64();
                ContractHolds[n] = reader.ReadBoolean();
            }
        }
    }

    public static class Nobles
    {
        /// <summary>
        /// Income tax raised at a node: the royal house takes its share first, then the city's holder and the
        /// village's noble; the state's reserve gets the rest. Outside a nation it all goes to the state.
        /// </summary>
        public static void CollectTax(WorldState world, SimulationConfig config, int node, long amount)
        {
            var nodes = world.Get<NodeTable>();
            int nation = nodes.AncestorAt(node, WorldLevel.Nation);
            if (nation < 0)
            {
                MoneyLedger.ToState(world, node, amount);
                return;
            }
            var state = world.Get<NobleState>();
            long rest = amount;
            rest -= Take(state, nation, config.NobleRoyalShare.MulInt(amount));
            int city = nodes.AncestorAt(node, WorldLevel.City);
            if (city >= 0) rest -= Take(state, state.CityHolder[city], config.NobleCityShare.MulInt(amount));
            if (nodes.LevelOf(node) == WorldLevel.Village) rest -= Take(state, node, config.NobleVillageShare.MulInt(amount));
            MoneyLedger.ToState(world, node, rest);
        }

        static long Take(NobleState state, int holder, long share)
        {
            state.Cash[holder] += share;
            return share;
        }
    }

    /// <summary>
    /// The houses' year and week. Each week a house spends a share of its purse on its household and retainers, paid
    /// to the people of its fief. Each year a lord first pays the protection owed for last year's aid; one who cannot
    /// pay it in full releases that vassal from aid for the year. Then vassals pay aid, and what the lord
    /// will owe for it is set. Villages pay before cities, so aid climbs to the royal house (DEC-076).
    /// </summary>
    public sealed class NobleSystem : ISimSystem
    {
        SimulationConfig _config;
        NodeTable _nodes;
        NobleState _state;
        PurseState _purses;
        WorldState _world;

        public string Id => "agents.nobles";
        public SimPhase Phase => SimPhase.Finance;
        public int Order => FinanceOrder.Nobles;

        public void Initialize(SimContext ctx)
        {
            _config = ctx.Services.Get<GameDatabase>().Config;
            _nodes = ctx.World.Get<NodeTable>();
            _state = ctx.World.Get<NobleState>();
            _purses = ctx.World.Get<PurseState>();
            _world = ctx.World;
        }

        public void Execute(SimContext ctx)
        {
            if (ctx.Clock.IsNewYear && ctx.Turn > 0)
            {
                for (int n = 0; n < _nodes.Count; n++) Protect(ctx, n);
                // Nodes are sorted by level, so this walks villages first.
                for (int n = _nodes.Count - 1; n >= 0; n--) PayAid(n);
            }
            for (int n = 0; n < _nodes.Count; n++)
            {
                long spent = _config.NobleSpendShare.MulInt(Math.Max(0, _state.Cash[n]));
                if (spent <= 0) continue;
                _state.Cash[n] -= spent;
                int nation = _nodes.LevelOf(n) == WorldLevel.Nation ? _purses.NodeFloat[n] : -1;
                if (nation >= 0 && nation < _purses.NationCount) MoneyLedger.PayNationWages(_world, _config, nation, spent);
                else _purses.PayHouseholds(n, spent);
            }
        }

        void Protect(SimContext ctx, int vassal)
        {
            int lord = _state.Lord[vassal];
            if (lord < 0) return;
            long owed = _state.ProtectionOwed[vassal];
            long paid = Math.Min(owed, Math.Max(0, _state.Cash[lord]));
            _state.Cash[lord] -= paid;
            _purses.PayHouseholds(vassal, paid);
            _state.ProtectionOwed[vassal] = 0;
            _state.ContractHolds[vassal] = paid >= owed;
            if (paid < owed) ctx.Events.Publish(new NobleContractBroken(new NodeId(vassal), new NodeId(lord), ctx.Turn));
        }

        void PayAid(int vassal)
        {
            int lord = _state.Lord[vassal];
            if (lord < 0 || !_state.ContractHolds[vassal]) return;
            long aid = _config.NobleAidShare.MulInt(Math.Max(0, _state.Cash[vassal]));
            _state.Cash[vassal] -= aid;
            _state.Cash[lord] += aid;
            _state.ProtectionOwed[vassal] = _config.NobleProtectionShare.MulInt(aid);
        }
    }
}
