using System;
using FES.Core;
using FES.Data;
using FES.Economy;
using FES.World;

namespace FES.Agents
{
    /// <summary>How merchant groups trade. Replace the service to plug in other merchant AI.</summary>
    public interface IMerchantBehavior
    {
        void Initialize(SimContext ctx);

        /// <summary>Every merchant group of a detailed node, every one based at one, and every one on a trip, decides, buys, travels and sells.</summary>
        void RunDetailed(SimContext ctx);

        /// <summary>The merchants of every aggregate node at once, summarized as node-to-node trade flows.</summary>
        void RunAggregate(SimContext ctx);
    }

    /// <summary>Merchants exist but do not trade (tests, or a world without trade).</summary>
    public sealed class IdleMerchantBehavior : IMerchantBehavior
    {
        public void Initialize(SimContext ctx) { }
        public void RunDetailed(SimContext ctx) { }
        public void RunAggregate(SimContext ctx) { }
    }

    public sealed class MerchantBankrupt : ISimEvent
    {
        public readonly AgentId Merchant;
        public readonly int Turn;

        public MerchantBankrupt(AgentId merchant, int turn)
        {
            Merchant = merchant;
            Turn = turn;
        }
    }

    /// <summary>
    /// Runs every merchant group of a detailed node, and every group on a trip (so it arrives), then the aggregate
    /// merchants of all other nodes in one pass.
    /// </summary>
    public sealed class MerchantSystem : ISimSystem, IDisposable
    {
        IMerchantBehavior _behavior;

        public string Id => "agents.merchants";
        public SimPhase Phase => SimPhase.Agents;
        public int Order => SimOrder.Default;

        public void Initialize(SimContext ctx)
        {
            _behavior = ctx.Services.Get<IMerchantBehavior>();
            _behavior.Initialize(ctx);
        }

        public void Execute(SimContext ctx)
        {
            _behavior.RunDetailed(ctx);
            _behavior.RunAggregate(ctx);
        }

        public void Dispose() => (_behavior as IDisposable)?.Dispose();
    }
}
