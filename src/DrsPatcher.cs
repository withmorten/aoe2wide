using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AoE2Wide
{
    class DrsItem
    {
        public UInt32 Id;
        public UInt32 Start;
        public UInt32 Size;
        public byte[] Data;
    }

    class DrsTable
    {
        public UInt32 Type;
        public UInt32 Start;
        public IEnumerable<DrsItem> Items;
    }

    internal static class DrsPatcher
    {
        public static void Patch(Stream oldDrs, Stream newDrs, uint oldWidth, uint oldHeight, uint newWidth, uint newHeight)
        {
            var reader = new BinaryReader(oldDrs);
            var writer = new BinaryWriter(newDrs);
            writer.Write(reader.ReadBytes(40)); // Copyright
            writer.Write(reader.ReadBytes(4)); // Version
            writer.Write(reader.ReadBytes(12)); // type
            var tableCount = reader.ReadUInt32(); writer.Write(tableCount);
            var firstFilePos = reader.ReadUInt32(); writer.Write(firstFilePos);

            var inTables = new DrsTable[tableCount];
            for (var q = 0; q < tableCount; q++)
                inTables[q] = new DrsTable();

            foreach (var table in inTables)
            {
                table.Type = reader.ReadUInt32();
                table.Start = reader.ReadUInt32();
                var itemCount = reader.ReadUInt32();
                var itemsArray = new DrsItem[itemCount];
                for (var q = 0; q < itemCount; q++)
                    itemsArray[q] = new DrsItem();
                table.Items = itemsArray;
            }

            foreach (var table in inTables)
            {
                Trace.Assert(oldDrs.Position == table.Start);
                foreach (var item in table.Items)
                {
                    item.Id = reader.ReadUInt32();
                    item.Start = reader.ReadUInt32();
                    item.Size = reader.ReadUInt32();
                }
            }

            foreach (var item in inTables.SelectMany(table => table.Items))
            {
                Trace.Assert(oldDrs.Position == item.Start);
                item.Data = reader.ReadBytes((int)item.Size);
            }

            reader.Close();

            var dataPos = firstFilePos;

            var outTables = new List<DrsTable>(inTables.Length);
            foreach (var inTable in inTables)
            {
                var outItemList = new List<DrsItem>();
                var outTable = new DrsTable
                {
                    Start = inTable.Start,
                    Type = inTable.Type,
                    Items = outItemList
                };

                foreach (var inItem in inTable.Items)
                {
                    var outItem = new DrsItem
                    {
                        Id = inItem.Id,
                        Start = dataPos,
                        Data = ConvertDrsItem(inItem, inTable.Type, oldWidth, oldHeight, newWidth, newHeight)
                    };
                    outItem.Size = (uint)outItem.Data.Length;
                    dataPos += outItem.Size;
                    outItemList.Add(outItem);
                }

                outTables.Add(outTable);
            }

            foreach (var outTable in outTables)
            {
                writer.Write(outTable.Type);
                writer.Write(outTable.Start);
                writer.Write(outTable.Items.Count());
            }

            foreach (var outTable in outTables)
            {
                var ndp = newDrs.Position;
                Trace.Assert(ndp == outTable.Start);
                foreach (var outItem in outTable.Items)
                {
                    writer.Write(outItem.Id);
                    writer.Write(outItem.Start);
                    writer.Write(outItem.Size);
                }
            }

            foreach (var outItem in outTables.SelectMany(outTable => outTable.Items))
            {
                var ndp = newDrs.Position;
                Trace.Assert(ndp == outItem.Start);
                writer.Write(outItem.Data);
            }
            writer.Close();
        }

        private static byte[] ConvertDrsItem(DrsItem item, uint type, uint oldWidth, uint oldHeight, uint newWidth, uint newHeight)
        {
            return type == 0x736c7020 ? SlpStretcher.Enlarge(item.Data, oldWidth, oldHeight, newWidth, newHeight) : item.Data;
        }
    }
}
