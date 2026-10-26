using System.Collections.Generic;

namespace FES.Core
{
    public readonly struct CommandResult
    {
        public readonly bool Ok;
        public readonly string Error;

        CommandResult(bool ok, string error)
        {
            Ok = ok;
            Error = error;
        }

        public static CommandResult Success => new CommandResult(true, null);
        public static CommandResult Fail(string error) => new CommandResult(false, error);
    }

    /// <summary>
    /// A player action. The only way presentation code mutates simulation state.
    /// Commands are validated and applied at the start of a turn, in enqueue order.
    /// Validate must not mutate anything; command objects must not store simulation-derived state.
    /// </summary>
    public interface ICommand
    {
        CommandResult Validate(SimContext ctx);
        void Apply(SimContext ctx);
    }

    public sealed class CommandApplied : ISimEvent
    {
        public readonly ICommand Command;
        public readonly int Turn;

        public CommandApplied(ICommand command, int turn)
        {
            Command = command;
            Turn = turn;
        }
    }

    public sealed class CommandRejected : ISimEvent
    {
        public readonly ICommand Command;
        public readonly string Reason;
        public readonly int Turn;

        public CommandRejected(ICommand command, string reason, int turn)
        {
            Command = command;
            Reason = reason;
            Turn = turn;
        }
    }

    public sealed class CommandQueue
    {
        readonly List<ICommand> _queue = new List<ICommand>();
        readonly List<ICommand> _applied = new List<ICommand>();

        public int Count => _queue.Count;

        /// <summary>
        /// Commands applied in this turn's Commands phase, in order. Later systems read this
        /// (instead of subscribing to events) to react to player actions.
        /// </summary>
        public IReadOnlyList<ICommand> AppliedThisTurn => _applied;

        public void Enqueue(ICommand command) => _queue.Add(command);

        internal ICommand[] Drain()
        {
            var drained = _queue.ToArray();
            _queue.Clear();
            _applied.Clear();
            return drained;
        }

        internal void MarkApplied(ICommand command) => _applied.Add(command);

        internal void Clear()
        {
            _queue.Clear();
            _applied.Clear();
        }
    }

    /// <summary>Built-in Commands-phase system that validates and applies queued player commands.</summary>
    public sealed class CommandSystem : ISimSystem
    {
        public string Id => "core.commands";
        public SimPhase Phase => SimPhase.Commands;
        public int Order => SimOrder.Default;

        public void Initialize(SimContext ctx) { }

        public void Execute(SimContext ctx)
        {
            foreach (var command in ctx.Commands.Drain())
            {
                var result = command.Validate(ctx);
                if (!result.Ok)
                {
                    ctx.Events.Publish(new CommandRejected(command, result.Error, ctx.Turn));
                    continue;
                }
                command.Apply(ctx);
                ctx.Commands.MarkApplied(command);
                ctx.Events.Publish(new CommandApplied(command, ctx.Turn));
            }
        }
    }
}
