using System.IO;
using FES.Core;

namespace FES.Economy
{
    /// <summary>Version 1 had no input bids: they start empty.</summary>
    public sealed class MarketStoreV1Migration : ISaveMigration
    {
        public string SectionKey => "economy.markets";
        public int FromVersion => 1;

        public byte[] Migrate(byte[] section)
        {
            int cells = new BinaryReader(new MemoryStream(section)).ReadInt32();
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(section);
                writer.Write(cells);
                for (int c = 0; c < cells; c++) writer.Write(0);
            }
            return output.ToArray();
        }
    }
}
