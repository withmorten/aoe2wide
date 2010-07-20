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
        public static void Patch(Stream oldDrs, Stream newDrs, int oldWidth, int oldHeight, int newWidth, int newHeight)
        {
            var reader = new BinaryReader(oldDrs);
            var writer = new BinaryWriter(newDrs);

            // Header consists of Text, then 0x1A EOF, then some 0x00's, then the version
            // The following code reads the whole header AND the first byte of the Version
            bool foundEof = false;
            while (true)
            {
                var byt =reader.ReadByte();
                writer.Write(byt);
                if (byt == 0x1A)
                    foundEof = true;
                else if (byt != '\0' && foundEof)
                    break;
            }
            //writer.Write(reader.ReadBytes(40)); // Copyright
            writer.Write(reader.ReadBytes(3)); // REST OF Version
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

        private static byte[] ConvertDrsItem(DrsItem item, uint type, int oldWidth, int oldHeight, int newWidth, int newHeight)
        {
            switch (type)
            {
                case 0x736c7020:
                    return SlpStretcher.Enlarge(item.Id, item.Data, oldWidth, oldHeight, newWidth, newHeight);

                case 0x62696E61:
                    //if (item.Id == 50006 || item.Id == 53290)
                        return GuiTablePatcher.UpdateGuiTable(item.Data, item.Id, oldWidth, oldHeight, newWidth, newHeight);
                    return item.Data;

                default:
                    return item.Data;
            }
        }
    }

    internal static class GuiTablePatcher
    {
        private enum Columns
        {
            Name = 0,
            X800,
            Y800,
            W800,
            H800,
            X1024,
            Y1024,
            W1024,
            H1024,
            X1280,
            Y1280,
            W1280,
            H1280,
            Notes,
        }

        public static byte[] UpdateGuiTable(byte[] data, uint id, int oldWidth, int oldHeight, int newWidth, int newHeight)
        {
            int Xcol, Ycol, Wcol, Hcol;
            switch (oldWidth)
            {
                case 800:
                    Xcol = (int)Columns.X800;
                    Ycol = (int)Columns.Y800;
                    Wcol = (int)Columns.W800;
                    Hcol = (int)Columns.H800;
                    break;

                case 1024:
                    Xcol = (int)Columns.X1024;
                    Ycol = (int)Columns.Y1024;
                    Wcol = (int)Columns.W1024;
                    Hcol = (int)Columns.H1024;
                    break;

                case 1280:
                    Xcol = (int)Columns.X1280;
                    Ycol = (int)Columns.Y1280;
                    Wcol = (int)Columns.W1280;
                    Hcol = (int)Columns.H1280;
                    break;

                default:
                    return data;
            }

            int higher = newHeight - oldHeight;
            int wider = newWidth - oldWidth;

            if (data.Length < 10)
                return data;

            try
            {
                var test = System.Text.Encoding.ASCII.GetString(data, 0, 10);
                if (test == null || !test.Equals("Item Name\t"))
                    return data;
            }
            catch
            {
                return data;
            }

            try
            {

                var file = System.Text.Encoding.ASCII.GetString(data);
                var lines = file.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var table = lines.Select(line => line.Split(new char[] { '\t' })).ToArray();
                foreach (var tableRow in table)
                {
                    if (tableRow.Length < 13)
                        continue;

                    if (string.IsNullOrEmpty(tableRow[(int)Columns.Name]))
                        continue;

                    if (string.IsNullOrEmpty(tableRow[(int)Columns.H1280]))
                        continue;

                    var x = int.Parse(tableRow[Xcol]);
                    var y = int.Parse(tableRow[Ycol]);
                    var w = int.Parse(tableRow[Wcol]);
                    var h = int.Parse(tableRow[Hcol]);

                    // Stuff that overlaps the centerline, stretches
                    if (y < oldHeight / 2 && (y + h) > oldHeight / 2)
                        h += higher;
                    // Stuff that is below the old centerline, moves down
                    else if (y > oldHeight / 2)
                        y += higher;
/*
                    // Only stuff that sticks to the right side:
                    if ((x + w) == oldWidth)
                    {
                        // Stuff on the right half, moves right
                        if (x > oldWidth / 2)
                        {
                            x += wider;
                        }
                        else if (x == oldWidth / 2)
                        {
                            // stuff in the center, stays in the center
                            x = newWidth / 2;
                            w = newWidth / 2;
                        }
                        else
                        {
                            // Stuff that stretches over the center, stretches (including full-width stuff)
                            w += wider;
                        }
                    }
*/
                    if (w == oldWidth)
                        w = newWidth;

                    tableRow[Xcol] = x.ToString();
                    tableRow[Ycol] = y.ToString();
                    tableRow[Wcol] = w.ToString();
                    tableRow[Hcol] = h.ToString();
                }
                var newLines = table.Select(tableRow => tableRow.Aggregate((output, col) => output + "\t" + col));
                var newText = newLines.Aggregate((output, line) => output + "\r\n" + line);
                var newBytes = System.Text.Encoding.ASCII.GetBytes(newText);
                UserFeedback.Info("Patched Gui Table #{0}", id);
                return newBytes;
            }
            catch (Exception e)
            {
                UserFeedback.Warning("Failed to patch Gui Table #{0}; leaving it unchanged.", id);
                UserFeedback.Error(e);
                return data;
            }
        }
    }
}
