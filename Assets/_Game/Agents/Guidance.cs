using System.IO;
using FES.Core;
using FES.Data;

namespace FES.Agents
{
    /// <summary>One guiding signal: push the business toward <see cref="Axis"/>, centered on <see cref="FocusNode"/>.</summary>
    public readonly struct GuidanceSignal
    {
        public readonly GuidanceAxis Axis;
        public readonly Fx Weight;
        public readonly NodeId FocusNode;

        public GuidanceSignal(GuidanceAxis axis, Fx weight, NodeId focusNode)
        {
            Axis = axis;
            Weight = weight;
            FocusNode = focusNode;
        }
    }

    /// <summary>Implemented by commands that signal an expansion direction (founding a company, buying a guild hall...).</summary>
    public interface IGuidanceSource
    {
        /// <summary>Called after the command was applied this turn; reads the world, never changes it.</summary>
        GuidanceSignal GetGuidance(SimContext ctx);
    }

    /// <summary>The implicit expansion direction derived from the player's actions.</summary>
    public sealed class GuidanceState : IPersistentComponent
    {
        public readonly Fx[] Weights = new Fx[GuidanceAxes.Count];
        /// <summary>Node of the most recent guiding action, -1 if none yet.</summary>
        public int FocusNode = -1;

        public string Key => "agents.guidance";
        public int Version => 1;

        public Fx this[GuidanceAxis axis] => Weights[(int)axis];

        public GuidanceAxis Dominant
        {
            get
            {
                int best = 0;
                for (int i = 1; i < GuidanceAxes.Count; i++)
                    if (Weights[i] > Weights[best]) best = i;
                return (GuidanceAxis)best;
            }
        }

        public void Write(BinaryWriter writer)
        {
            foreach (var w in Weights) writer.Write(w.Raw);
            writer.Write(FocusNode);
        }

        public void Read(BinaryReader reader)
        {
            for (int i = 0; i < GuidanceAxes.Count; i++) Weights[i] = Fx.FromRaw(reader.ReadInt64());
            FocusNode = reader.ReadInt32();
        }
    }

    /// <summary>Decays guidance, then adds the signals of the commands applied this turn.</summary>
    public sealed class GuidanceSystem : ISimSystem
    {
        GuidanceState _state;
        Fx _keep;

        public string Id => "agents.guidance";
        public SimPhase Phase => SimPhase.Guidance;
        public int Order => SimOrder.Default;

        public void Initialize(SimContext ctx)
        {
            _state = ctx.World.Get<GuidanceState>();
            _keep = Fx.One - ctx.Services.Get<GameDatabase>().Config.GuidanceDecayPerTurn;
        }

        public void Execute(SimContext ctx)
        {
            for (int i = 0; i < GuidanceAxes.Count; i++) _state.Weights[i] *= _keep;
            foreach (var command in ctx.Commands.AppliedThisTurn)
            {
                if (!(command is IGuidanceSource source)) continue;
                var signal = source.GetGuidance(ctx);
                _state.Weights[(int)signal.Axis] += signal.Weight;
                if (signal.FocusNode.IsValid) _state.FocusNode = signal.FocusNode.Value;
            }
        }
    }
}
