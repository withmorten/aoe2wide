using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;

namespace AoE2Wide
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// 
        private const string OrgDrsName = @"Data\interfac.drs";
        private const string OrgExeName = @"age2_x1.exe";
        private static string _patchFileName;

        private static string FindPatchFile()
        {
            const string fileName = @"AoE2Wide.dat";
            DumpTrace(@"Locating Patch data file '{0}'", fileName);
            var files = Directory.GetFiles(Directory.GetCurrentDirectory(), fileName, SearchOption.AllDirectories);
            if (files.Length==0)
                throw new Exception(@"No AoE2Wide.dat found in current directory or subdirectories");

            if (files.Length > 1)
            {
                DumpWarning(@"Multiple AoE2Wide.dat instances found in current directory and subdirectories:");
                foreach( var file in files)
                    DumpTrace(file);
            }
            DumpInfo(@"Using '{0}'", files[0]);
            return files[0];
        }

        [STAThread]
        static void Main( string[] args )
        {
            try
            {
                _patchFileName = FindPatchFile();

                if (!File.Exists(OrgExeName))
                    throw new Exception(string.Format(@"Cannot find original exe '{0}' in current folder", OrgExeName));
                if (!File.Exists(OrgDrsName))
                    throw new Exception(string.Format(@"Cannot find drs file '{0}' in current folder", OrgDrsName));

                if (args.Length == 0)
                {
                    DumpInfo("Auto patching for all current screen sizes. Note that the game will always use the primary screen!");
                    var doneList = new HashSet<uint>();
                    foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                    {
                        try
                        {
                            var newWidth = (uint)screen.Bounds.Width;
                            var newHeight = (uint)screen.Bounds.Height;
                            var key = newWidth + (newHeight << 16);
                            if (doneList.Add(key))
                            {
                                MakePatch(newWidth, newHeight);
                            }
                        }
                        catch (Exception e)
                        {
                            DumpException(e);
                        }
                    }
                }
                else if (args.Length == 2)
                {
                    uint newWidth, newHeight;
                    if (!uint.TryParse(args[0], out newWidth) || ! uint.TryParse(args[1], out newHeight))
                    {
                        ShowUsage();
                        return;
                    }

                    MakePatch(newWidth, newHeight);
                }
                else
                {
                    ShowUsage();
                }
            }
            catch (Exception e)
            {
                DumpException(e);
            }
        }

        private static void ShowUsage()
        {
            DumpWarning("Usage: aoe2wide.exe [width height]");
            DumpInfo("If the new width and height are omitted, the current screen resolution(s) will be used.");
        }

        private static void MakePatch(uint newWidth, uint newHeight)
        {
            try
            {
                uint oldWidth, oldHeight;
                GetOldWidthHeight(newWidth, newHeight, out oldWidth, out oldHeight);

                DumpInfo(string.Format(@"Changing {0}x{1} to {2}x{3}", oldWidth, oldHeight, newWidth, newHeight));

                DumpTrace(@"Reading original executable");
                var exe = File.ReadAllBytes(OrgExeName);

                var newDrsName = string.Format(@"Data\{0:D4}{1:D4}.drs", newWidth, newHeight);
                var newExeName = string.Format(@"age2_x1_{0}x{1}.exe", newWidth, newHeight);

                //DumpTrace(@"Writing file with all occurrences of resolutions");
                //Patcher.ListEm(bytes);

                DumpTrace("Reading the patch file");
                var patch = Patcher.ReadPatch(_patchFileName);

                DumpTrace("Patching the executable: DRS reference");
                Patcher.PatchDrsRefInExe(exe, Path.GetFileName(newDrsName));

                DumpTrace("Patching the executable: resolutions");
                Patcher.PatchResolutions(exe, oldWidth, oldHeight, newWidth, newHeight, patch);

                DumpTrace(string.Format(@"Writing the patched executable '{0}'", newExeName));
                File.WriteAllBytes(newExeName, exe);

                DumpTrace(@"Opening original interfac.drs");
                using (
                    var interfaceDrs = new FileStream(OrgDrsName, FileMode.Open,
                                                      FileSystemRights.ReadData,
                                                      FileShare.Read, 1024*1024, FileOptions.SequentialScan))
                {
                    DumpTrace(string.Format(@"Creating patched drs file '{0}'", newDrsName));
                    using (
                        var newDrs = new FileStream(newDrsName, FileMode.Create, FileSystemRights.Write, FileShare.None,
                                                    1024*1024, FileOptions.SequentialScan))
                    {
                        DumpTrace(@"Patching DRS");
                        DrsPatcher.Patch(interfaceDrs, newDrs, oldWidth, oldHeight, newWidth, newHeight);
                    }
                }

                DumpTrace("Done");
            }
            catch (Exception e)
            {
                DumpException(e);
            }
        }

        private static void DumpException(Exception e)
        {
            var normalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("An error occurred:");
            Console.WriteLine(e.Message);
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine("Exception Details:");
            Console.WriteLine(e.ToString());
            Console.ForegroundColor = normalColor;
        }

        private static void DumpInfo(string msg, string param1)
        {
            DumpInfo(string.Format(msg, param1));
        }

        private static void DumpInfo(string msg)
        {
            Console.WriteLine(msg);
        }

        private static void DumpTrace(string msg, string param1)
        {
            DumpTrace(string.Format(msg, param1));
        }

        private static void DumpTrace(string msg)
        {
            var normalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(msg);
            Console.ForegroundColor = normalColor;
        }

        private static void DumpWarning(string msg, string param1)
        {
            DumpWarning(string.Format(msg, param1));
        }

        private static void DumpWarning(string msg)
        {
            var normalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write("Warning: ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(msg);
            Console.ForegroundColor = normalColor;
        }

        private static void GetOldWidthHeight(uint newWidth, uint newHeight, out uint oldWidth, out uint oldHeight)
        {
            var widths = new uint[] {800, 1024, 1280};
            var heights = new uint[] {600, 768, 1024};
            
            for (var q = widths.Length-1; q>=0; q--)
            {
                if (newWidth >= widths[q] && newHeight>= heights[q])
                {
                    oldHeight = heights[q];
                    oldWidth = widths[q];
                    return;
                }
            }
            throw new Exception("Resolutions smaller than 800 wide or 600 high are not possible");
        }
    }

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
            for (var q = 0; q < tableCount; q++ )
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
                foreach( var item in table.Items)
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
            return type == 0x736c7020 ? SlpEnlarger.Enlarge(item.Data, oldWidth, oldHeight, newWidth, newHeight) : item.Data;
        }
    }

    internal static class SlpEnlarger
    {
        public static byte[] Enlarge(byte[] data, uint oldWidth, uint oldHeight, uint newWidth, uint newHeight)
        {
            var reader = new BinaryReader(new MemoryStream(data, false));

            var version = reader.ReadUInt32();
            if (version != 0x4e302e32)
                return data;

            var framecount = reader.ReadUInt32();
            if (framecount != 1)
                return data;

            var comment = reader.ReadBytes(24);
            var linesOffset = reader.ReadUInt32();
            var maskOffset = reader.ReadUInt32();
            var paletteOffset = reader.ReadUInt32();
            var properties = reader.ReadUInt32();

            var width = reader.ReadUInt32();
            if (width != oldWidth)
                return data;

            var height = reader.ReadUInt32();
            if (height != oldHeight)
                return data;

            var centerX = reader.ReadInt32();
            var centerY = reader.ReadInt32();

            var newMaskSize = newHeight*4;
            var newLinesOffset = maskOffset + newMaskSize;
            var newLinesSize = newHeight*4;
            var newLineDataStart = newLinesOffset + newLinesSize;

            var outStream = new MemoryStream();
            var writer = new BinaryWriter(outStream);

            writer.Write(version);
            writer.Write(framecount);
            writer.Write(comment);
            writer.Write(newLinesOffset);
            writer.Write(maskOffset);
            writer.Write(paletteOffset);
            writer.Write(properties);
            writer.Write(newWidth);
            writer.Write(newHeight);
            writer.Write(centerX);
            writer.Write(centerY);

            var extraLines = newHeight - oldHeight;

            for (var inLine = 0; inLine < oldHeight; inLine++)
            {
                var lineMaskData = reader.ReadUInt32();
                writer.Write(lineMaskData);
                if (inLine != oldHeight/2)
                    continue;

                for (var el = 0; el < extraLines; el++)
                    writer.Write(lineMaskData);
            }

            var lineStarts = new UInt32[oldHeight + 1];
            for (var inLine = 0; inLine < oldHeight; inLine++)
                lineStarts[inLine] = reader.ReadUInt32();
            lineStarts[oldHeight] = (uint) data.Length;

            var lineSizes = new UInt32[oldHeight];
            for (var inLine = 0; inLine < oldHeight; inLine++)
                lineSizes[inLine] = lineStarts[inLine + 1] - lineStarts[inLine];

            var nextLineStart = newLineDataStart;

            for (var inLine = 0; inLine < oldHeight; inLine++)
            {
                writer.Write(nextLineStart);
                nextLineStart += lineSizes[inLine];

                if (inLine != oldHeight/2)
                    continue;

                for (var el = 0; el < extraLines; el++)
                {
                    writer.Write(nextLineStart);
                    nextLineStart += lineSizes[inLine];
                }
            }

            var osp = outStream.Position;
            Trace.Assert(newLineDataStart == osp);

            for (var inLine = 0; inLine < oldHeight; inLine++)
            {
                var lineData = reader.ReadBytes((int) lineSizes[inLine]);
                writer.Write(lineData);

                if (inLine != oldHeight/2)
                    continue;

                for (var el = 0; el < extraLines; el++)
                    writer.Write(lineData);
            }

            writer.Close();

            return outStream.GetBuffer();
        }
    }
}
