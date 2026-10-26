using System;

namespace FES.Core
{
    /// <summary>How often a world level's markets are stepped. A level is due when (turn + Offset) % Period == 0.</summary>
    public struct TierCadence
    {
        public int Period;
        public int Offset;

        public TierCadence(int period, int offset)
        {
            Period = Math.Max(1, period);
            Offset = offset;
        }
    }

    /// <summary>
    /// Per-level update cadence. Higher tiers update less often and consume the flows accumulated
    /// since their last update, so economic rates stay consistent regardless of period.
    /// </summary>
    public sealed class TierSchedule
    {
        readonly TierCadence[] _cadence = new TierCadence[WorldLevels.Count];

        public TierSchedule(TierCadence[] cadence)
        {
            if (cadence == null || cadence.Length != WorldLevels.Count)
                throw new ArgumentException($"Expected {WorldLevels.Count} cadences.", nameof(cadence));
            for (int i = 0; i < cadence.Length; i++)
                _cadence[i] = new TierCadence(cadence[i].Period, cadence[i].Offset);
        }

        public static TierSchedule EveryTurn()
        {
            var all = new TierCadence[WorldLevels.Count];
            for (int i = 0; i < all.Length; i++) all[i] = new TierCadence(1, 0);
            return new TierSchedule(all);
        }

        public TierCadence this[WorldLevel level] => _cadence[(int)level];

        public bool IsDue(WorldLevel level, int turn)
        {
            var c = _cadence[(int)level];
            return (turn + c.Offset) % c.Period == 0;
        }

        /// <summary>Turns covered by an update on a due turn (shorter for the first update).</summary>
        public int ElapsedTurns(WorldLevel level, int turn) => Math.Min(_cadence[(int)level].Period, turn + 1);
    }
}
