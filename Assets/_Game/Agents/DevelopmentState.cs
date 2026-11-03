using System;
using System.Collections.Generic;
using System.IO;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;
using Unity.Collections;

namespace FES.Agents
{
    public enum StateProjectKind : byte
    {
        Farmland = 0,
        Mine = 1,
        Harbor = 2,
        Highway = 3,
        Road = 4,
    }

    /// <summary>A project under way: on a node, or on a route when <see cref="Route"/> is not -1.</summary>
    public struct StateProject
    {
        public StateProjectKind Kind;
        public int Nation;
        public int Node;
        public int Route;
        /// <summary>For farmland and mines: the facility type to open.</summary>
        public int DefIndex;
        /// <summary>-1 while reserved, waiting for the nation's work before it.</summary>
        public int DoneTurn;
    }

    /// <summary>A nation pays producers of a good by what they make, until <see cref="EndYear"/>.</summary>
    public struct SubsidyPolicy
    {
        public int Nation;
        public int Good;
        public int EndYear;
    }

    public sealed class SubsidyEnacted : ISimEvent
    {
        public readonly NodeId Nation;
        public readonly GoodId Good;
        public readonly int Turn;

        public SubsidyEnacted(NodeId nation, GoodId good, int turn)
        {
            Nation = nation;
            Good = good;
            Turn = turn;
        }
    }

    public sealed class StateProjectFinished : ISimEvent
    {
        public readonly StateProjectKind Kind;
        public readonly NodeId Node;
        public readonly int Turn;

        public StateProjectFinished(StateProjectKind kind, NodeId node, int turn)
        {
            Kind = kind;
            Node = node;
            Turn = turn;
        }
    }

    /// <summary>What the state's choices read from a nation's markets.</summary>
    static class NationMarkets
    {
        /// <summary>Adds a good's price and anchor at each of the nodes carrying it.</summary>
        public static void AddPrices(MarketStore markets, int[] nodes, GoodId good, ref long price, ref long anchor)
        {
            foreach (int node in nodes)
                if (markets.TryFindCell(node, good, out int cell))
                {
                    price += markets.Price[cell];
                    anchor += markets.GoodBasePrice[good.Value];
                }
        }
    }

    /// <summary>
    /// What the states have built on the land around nodes and along routes (there is no tile map, so the land
    /// around a node stands for the tiles outside it), the year's defense budgets, what each area paid for imports
    /// this year, and the projects under way.
    /// </summary>
    public sealed class DevelopmentState : IPersistentComponent, IDisposable
    {
        /// <summary>Per node: farmland and mine development, each level raising primary output there.</summary>
        public readonly int[] LandLevel;
        /// <summary>Per node: harbor works, each level cheapening water routes from it.</summary>
        public readonly int[] HarborLevel;
        /// <summary>Per route: 1 when it is a highway.</summary>
        public readonly int[] RouteGrade;
        /// <summary>Per nation: the budget set at the year's start and what defense has cost this year.</summary>
        public readonly long[] YearBudget;
        public readonly long[] DefenseSpent;
        /// <summary>Raw Fx, per nation: how far its army cuts the danger of its routes this year.</summary>
        public readonly long[] Security;
        /// <summary>Per float: value paid this year to other floats for goods flowing in.</summary>
        public NativeArray<long> Imports;
        public readonly List<StateProject> Projects = new List<StateProject>();
        public readonly List<SubsidyPolicy> Subsidies = new List<SubsidyPolicy>();
        /// <summary>Raw Fx, indexed float * goods + good: the subsidy on each unit's value.</summary>
        public NativeArray<long> SubsidyRate;
        /// <summary>Per float: what this week's subsidies may still pay; set before production.</summary>
        public NativeArray<long> SubsidyLeft;
        readonly int _goods;

        /// <summary>DEC-135: farmland and mines planned at a node; each will open a facility there, so its place is taken.</summary>
        public int LandProjectsAt(int node)
        {
            int planned = 0;
            foreach (var p in Projects)
                if (p.Node == node && (p.Kind == StateProjectKind.Farmland || p.Kind == StateProjectKind.Mine)) planned++;
            return planned;
        }

        public string Key => "agents.development";
        public int Version => 1;

        public DevelopmentState(int nodes, int routes, int nations, int floats, int goods)
        {
            _goods = goods;
            Security = new long[nations];
            SubsidyRate = new NativeArray<long>(floats * goods, Allocator.Persistent);
            SubsidyLeft = new NativeArray<long>(floats, Allocator.Persistent);
            LandLevel = new int[nodes];
            HarborLevel = new int[nodes];
            RouteGrade = new int[routes];
            YearBudget = new long[nations];
            DefenseSpent = new long[nations];
            Imports = new NativeArray<long>(floats, Allocator.Persistent);
        }

        public void Write(BinaryWriter writer)
        {
            foreach (int v in LandLevel) writer.Write(v);
            foreach (int v in HarborLevel) writer.Write(v);
            foreach (int v in RouteGrade) writer.Write(v);
            foreach (long v in YearBudget) writer.Write(v);
            foreach (long v in DefenseSpent) writer.Write(v);
            foreach (long v in Security) writer.Write(v);
            writer.Write(Subsidies.Count);
            foreach (var p in Subsidies)
            {
                writer.Write(p.Nation);
                writer.Write(p.Good);
                writer.Write(p.EndYear);
            }
            NativeIO.WriteArray(writer, Imports);
            writer.Write(Projects.Count);
            foreach (var p in Projects)
            {
                writer.Write((byte)p.Kind);
                writer.Write(p.Nation);
                writer.Write(p.Node);
                writer.Write(p.Route);
                writer.Write(p.DefIndex);
                writer.Write(p.DoneTurn);
            }
        }

        public void Read(BinaryReader reader)
        {
            for (int i = 0; i < LandLevel.Length; i++) LandLevel[i] = reader.ReadInt32();
            for (int i = 0; i < HarborLevel.Length; i++) HarborLevel[i] = reader.ReadInt32();
            for (int i = 0; i < RouteGrade.Length; i++) RouteGrade[i] = reader.ReadInt32();
            for (int i = 0; i < YearBudget.Length; i++) YearBudget[i] = reader.ReadInt64();
            for (int i = 0; i < DefenseSpent.Length; i++) DefenseSpent[i] = reader.ReadInt64();
            for (int i = 0; i < Security.Length; i++) Security[i] = reader.ReadInt64();
            Subsidies.Clear();
            int subsidies = reader.ReadInt32();
            for (int i = 0; i < subsidies; i++)
                Subsidies.Add(new SubsidyPolicy { Nation = reader.ReadInt32(), Good = reader.ReadInt32(), EndYear = reader.ReadInt32() });
            NativeIO.ReadArray(reader, Imports);
            Projects.Clear();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
                Projects.Add(new StateProject
                {
                    Kind = (StateProjectKind)reader.ReadByte(),
                    Nation = reader.ReadInt32(),
                    Node = reader.ReadInt32(),
                    Route = reader.ReadInt32(),
                    DefIndex = reader.ReadInt32(),
                    DoneTurn = reader.ReadInt32(),
                });
        }

        /// <summary>The rates follow the policies in force; call after they change or a load.</summary>
        public void RefreshSubsidyRates(Fx rate)
        {
            for (int i = 0; i < SubsidyRate.Length; i++) SubsidyRate[i] = 0;
            foreach (var p in Subsidies) SubsidyRate[p.Nation * _goods + p.Good] = rate.Raw;
        }

        public void Dispose()
        {
            Imports.DisposeIfCreated();
            SubsidyRate.DisposeIfCreated();
            SubsidyLeft.DisposeIfCreated();
        }
    }
}
