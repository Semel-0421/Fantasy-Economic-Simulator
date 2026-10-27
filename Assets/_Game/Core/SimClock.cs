using System.IO;

namespace FES.Core
{
    /// <summary>Turn counter and master seed. One turn = one week.</summary>
    public sealed class SimClock : IPersistentComponent
    {
        public const int WeeksPerYear = 52;
        public const int WeeksPerQuarter = 13;

        public int Turn { get; private set; }
        public ulong Seed { get; private set; }

        public int Year => Turn / WeeksPerYear;
        public int Week => Turn % WeeksPerYear;
        public bool IsNewYear => Week == 0;
        /// <summary>The last week of a quarter, when the quarter's financial record is taken (DEC-043).</summary>
        public bool IsQuarterEnd => Turn % WeeksPerQuarter == WeeksPerQuarter - 1;

        public SimClock(ulong seed) => Seed = seed;

        public string Key => "core.clock";
        public int Version => 1;

        internal void Advance() => Turn++;

        public void Write(BinaryWriter writer)
        {
            writer.Write(Turn);
            writer.Write(Seed);
        }

        public void Read(BinaryReader reader)
        {
            Turn = reader.ReadInt32();
            Seed = reader.ReadUInt64();
        }
    }
}
