using System;
using System.Collections.Generic;

namespace FES.Core
{
    /// <summary>Marker for notifications published by systems (never used to carry bulk data).</summary>
    public interface ISimEvent
    {
    }

    /// <summary>
    /// Buffered, deterministic event bus. Events are delivered at phase boundaries in publish order,
    /// so handlers never re-enter a running system. Events are outbound notifications only (UI, logs,
    /// achievements): simulation state must never be changed from a handler. Systems share data
    /// through components instead.
    /// </summary>
    public sealed class EventBus
    {
        /// <summary>Limit on events published by handlers during one dispatch (guards infinite cascades).</summary>
        const int MaxCascade = 10_000;

        readonly Dictionary<Type, List<Action<ISimEvent>>> _handlers = new Dictionary<Type, List<Action<ISimEvent>>>();
        readonly List<ISimEvent> _pending = new List<ISimEvent>();
        readonly List<ISimEvent> _turnLog = new List<ISimEvent>();

        /// <summary>Every event delivered during the last completed turn, for presentation.</summary>
        public IReadOnlyList<ISimEvent> TurnLog => _turnLog;

        public void Subscribe<T>(Action<T> handler) where T : ISimEvent
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
                _handlers.Add(typeof(T), list = new List<Action<ISimEvent>>());
            list.Add(e => handler((T)e));
        }

        public void Publish(ISimEvent e) => _pending.Add(e);

        /// <summary>Delivers pending events, including ones published by handlers during delivery.</summary>
        public void Dispatch()
        {
            int published = _pending.Count;
            try
            {
                for (int i = 0; i < _pending.Count; i++)
                {
                    if (_pending.Count - published > MaxCascade)
                        throw new InvalidOperationException("Event cascade limit exceeded.");
                    var e = _pending[i];
                    _turnLog.Add(e);
                    if (!_handlers.TryGetValue(e.GetType(), out var list)) continue;
                    // Index loop: a handler may subscribe new handlers without breaking enumeration.
                    for (int h = 0; h < list.Count; h++) list[h](e);
                }
            }
            finally
            {
                _pending.Clear();
            }
        }

        internal void BeginTurn() => _turnLog.Clear();

        /// <summary>Drops undelivered events and the turn log (used after loading a save).</summary>
        internal void Reset()
        {
            _pending.Clear();
            _turnLog.Clear();
        }
    }
}
