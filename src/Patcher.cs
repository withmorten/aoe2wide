using System;
using System.Collections.Generic;
using System.Text;

namespace AoE2Wide
{
    static class Patcher
    {
        static readonly uint[] Resolutions = new uint[] { 200, 320, 480, 640, 600, 768, 800, 1024, 1200, 1280, 1600 };

        static public void ListEm(byte[] exe)
        {
            var sb = new StringBuilder();
            for (var index = 0; index < exe.Length - 3; index++)
            {
                var b0 = (uint)exe[index];
                var b1 = (uint)exe[index + 1];
                var b2 = (uint)exe[index + 2];
                var b3 = (uint)exe[index + 3];
                var value = b0 | b1 << 8 | b2 << 16 | b3 << 24;
                foreach (var test in Resolutions)
                {
                    if (test == value)
                        sb.AppendLine(String.Format("{0:X8} {1:G4}", index, value));
                }
            }
            System.IO.File.WriteAllText(@"..\Data\output.txt", sb.ToString());
        }

        public static IEnumerable<Item> ReadPatch( string patchFile )
        {
            var items = new List<Item>(1024);
            var lines = System.IO.File.ReadAllLines(patchFile);
            var usedLines = new List<string>(lines.Length);
            foreach (var line in lines)
            {
                if (line.StartsWith("#"))
                    continue;
                var words = line.Split(new[] { ' ' }, 4);
                if (words.Length < 3)
                    continue;
                if (
                    !words[2].Equals("H") && 
                    !words[2].Equals("V") &&
                    !words[2].Equals("dH") &&
                    !words[2].Equals("dV") &&
                    !words[2].Equals("HV")
                    )
                    continue;

                var item = new Item
                               {
                                   Pos = int.Parse(words[0], System.Globalization.NumberStyles.HexNumber),
                                   OriginalValue = uint.Parse(words[1]),
                                   Type = words[2],
                                   Comments = words[3]
                               };
                if (words.Length == 4)
                    item.Desc = words[3];
                items.Add(item);
                usedLines.Add(line);
            }
            System.IO.File.WriteAllLines(@"AoE2Wide.dat.asused", usedLines.ToArray());
            return items;
        }

        static public void PatchDrsRefInExe(byte[] exe, string newInterfaceDrsName)
        {
                var drsPos = 0x0027BE90;
                if (exe[drsPos] != 'i')
                {
                    Console.WriteLine(@"Didn't find interfac.drs reference at expected location. Wrong exe. Stopping.");
                    throw new Exception();
                }
                var newBytes = Encoding.ASCII.GetBytes(newInterfaceDrsName);
                foreach (var byt in newBytes)
                {
                    exe[drsPos] = byt;
                    drsPos++;
                }
        }

        static public void PatchResolutions(byte[] exe, uint oldWidth, uint oldHeight, uint newWidth, uint newHeight, IEnumerable<Item> patch)
        {
            // Create the map so, that originally larger resolutions stay larger even after patching. They _may_ become invalid though.
            // This is necessary to keep the internal (in AoE) if > else if > else if > code working.
            var hmap = new Dictionary<uint, uint>();
            var vmap = new Dictionary<uint, uint>();
            var existingWidths = new uint[] { 640, 800, 1024, 1280, 1600 };
            var existingHeights = new uint[] { 480, 600, 768, 1024, 1200 };
            uint wshift = 1;
            uint hshift = 1;
            foreach (var w in existingWidths)
            {
                if (w > oldWidth && w <= newWidth)
                {
                    hmap[w] = newWidth + wshift;
                    wshift++;
                }
            }
            foreach (var h in existingHeights)
            {
                if (h > oldHeight && h <= newHeight)
                {
                    vmap[h] = newWidth + hshift;
                    hshift++;
                }
            }

            hmap[oldWidth] = newWidth;
            vmap[oldHeight] = newHeight;

            foreach (var item in patch)
            {
                var oldValue = item.OriginalValue;
                uint newValue;
                var map = item.Type.Contains("V") ? vmap : hmap;
                if (!map.TryGetValue(oldValue, out newValue))
                    newValue = oldValue;

                var ob0 = (uint)exe[item.Pos];
                var ob1 = (uint)exe[item.Pos + 1];
                var ob2 = (uint)exe[item.Pos + 2];
                var ob3 = (uint)exe[item.Pos + 3];
                var orgValue = ob0 | ob1 << 8 | ob2 << 16 | ob3 << 24;
                if (item.Type.Equals("dV") || item.Type.Equals("dH"))
                {
                    uint expectedOrgValue;
                    var subWords = item.Comments.Split(new[] {' '}, 2);
                    if (subWords.Length == 0 || !uint.TryParse(subWords[0], out expectedOrgValue))
                    {
                        Console.WriteLine("{0} action is safer if you mention the expected orgValue. Encountered {1} @ {2:X8}", item.Type, orgValue, item.Pos);
                    }
                    else
                    {
                        if (expectedOrgValue != orgValue)
                        {
                            Console.WriteLine(
                                @"{0} action expected value mismatch: {1} expected, {2} encountered @ {3:X8} [NOT PATCHED]", item.Type,
                                expectedOrgValue, orgValue, item.Pos);
                            continue;
                        }
                    }

                    var offset = newValue - oldValue;
                    newValue = orgValue + offset;
                }
                else
                {
                    if (orgValue != oldValue)
                    {
                        Console.WriteLine(
                            @"{0} action expected value mismatch: {1} expected, {2} encountered @ {3:X8} [NOT PATCHED]", item.Type,
                            oldValue, orgValue, item.Pos);
                        continue;
                    }
                }
                var b0 = (byte)(newValue & 0xFF);
                var b1 = (byte)((newValue >> 8) & 0xFF);
                var b2 = (byte)((newValue >> 16) & 0xFF);
                var b3 = (byte)((newValue >> 24) & 0xFF);
                exe[item.Pos] = b0;
                exe[item.Pos + 1] = b1;
                exe[item.Pos + 2] = b2;
                exe[item.Pos + 3] = b3;
            }
        }

        internal struct Item
        {
            public int Pos;
            public uint OriginalValue;
            public string Type;
            public string Desc;
            public string Comments;
        }
    }
}
