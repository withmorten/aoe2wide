using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace AoE2Wide
{
    internal class Item
    {
        public int Pos;
        public int ReferenceValue;
        public string Type;
        public int Parameter;
        public string Comments;
        public string Asm;
        public int OriginalPos;
    }

    class Patch
    {
        public int FileSize;
        public string Md5;
        public string Version;
        public int InterfaceDrsPosition;
        public IEnumerable<Item> Items;
    }

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
            File.WriteAllText(@"..\Data\output.txt", sb.ToString());
        }

        public static Patch ReadPatch(string patchFile, bool activeOnly)
        {
            var items = new List<Item>(1024);
            var lines = File.ReadAllLines(patchFile);
            var usedLines = new List<string>(lines.Length);
            var patch = new Patch();

            foreach (var line in lines)
            {
                if (line.StartsWith("size="))
                {
                    patch.FileSize = int.Parse(line.Substring(5));
                    continue;
                }
                if (line.StartsWith("md5="))
                {
                    patch.Md5 = line.Substring(4);
                    continue;
                }
                if (line.StartsWith("version="))
                {
                    patch.Version = line.Substring(8);
                    continue;
                }
                if (line.StartsWith("drspos="))
                {
                    patch.InterfaceDrsPosition = int.Parse(line.Substring(7),
                                                           System.Globalization.NumberStyles.HexNumber);
                    continue;
                }

                if (line.StartsWith("["))
                    continue;
                if (line.StartsWith("#"))
                    continue;

                var splitAsm = line.Split(new[] {'|'}, 2);
                var words = splitAsm[0].Split(new[] {' ', '\t'}, 4, StringSplitOptions.RemoveEmptyEntries);
                if (activeOnly)
                {
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
                }
                else
                {
                    if (words.Length < 2)
                        continue;
                }

                var item = new Item
                               {
                                   Pos = int.Parse(words[0], System.Globalization.NumberStyles.HexNumber),
                                   ReferenceValue = int.Parse(words[1]),
                                   Type = @"",
                                   Comments = @""
                               };
                if (words.Length > 2)
                    item.Type = words[2];

                if (words.Length > 3)
                {
                    var splitComment = words[3].Split(new[] {' ', '\t'}, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (int.TryParse(splitComment[0], out item.Parameter))
                        item.Comments = splitComment.Length == 1 ? @"" : splitComment[1].TrimEnd();
                    else
                        item.Comments = words[3].TrimEnd();
                }

                if (!activeOnly && splitAsm.Length > 1)
                    item.Asm = splitAsm[1].Trim();

                item.OriginalPos = item.Pos;
                items.Add(item);
                usedLines.Add(line);
            }
            patch.Items = items;
            return patch;
        }

        static public void PatchDrsRefInExe(byte[] exe, string newInterfaceDrsName, Patch patch)
        {
            var drsPos = patch.InterfaceDrsPosition;
            if (exe[drsPos] != 'i')
                throw new FatalError(@"Didn't find interfac.drs reference at expected location. Wrong exe.");

            var newBytes = Encoding.ASCII.GetBytes(newInterfaceDrsName);
            foreach (var byt in newBytes)
            {
                exe[drsPos] = byt;
                drsPos++;
            }
        }

        private static string FindAsm(int pos, IDictionary<int,string> asmMap)
        {
            string retVal = null;
            var min = pos - 10;
            for (var iter = pos; iter > min; iter--)
            {
                if (asmMap.TryGetValue(iter, out retVal))
                    return retVal;
            }
            return retVal;
        }

        public static void AddAsm(Patch patch, IDictionary<int, string> asmMap)
        {
            foreach ( var item in patch.Items)
            {
                item.Asm = FindAsm(item.Pos, asmMap);
            }
        }

        public static IDictionary<int,string> ReadAsmMap( string lstPath)
        {
            var allLines = File.ReadAllLines(lstPath, Encoding.GetEncoding(437));
            var asmMap = new Dictionary<int, string>(allLines.Length);
            foreach (var line in allLines)
            {
                var noComment = line.Split(new[] {';'},2,StringSplitOptions.RemoveEmptyEntries);
                if (noComment.Length==0)
                    continue;

                var cleanLine = noComment[0].TrimEnd();
                if (cleanLine.Length <= 14)
                    continue;

                const int basis = 0x00400000;
                int offset;
                if (!int.TryParse(cleanLine.Substring(6, 8), System.Globalization.NumberStyles.HexNumber, null, out offset))
                    continue;

                if (offset < basis)
                    continue;
                asmMap[offset - basis] = line;
            }
            return asmMap;
        }

        private static IEnumerable<string> StringifyPatch(Patch patch)
        {
            foreach (var item in patch.Items)
            {
                var rootPos = item.OriginalPos != 0 && item.OriginalPos != item.Pos
                                  ? string.Format(@"[rootPos: {0:X8}, offset {1,-8}]", item.OriginalPos,
                                                  item.Pos - item.OriginalPos)
                                  : @"";
                var pos = item.Pos > 0
                              ? item.Pos.ToString(@"X8")
                              : string.Format(@"!!!{0:X8}", -item.Pos);

                var asm = string.IsNullOrEmpty(item.Asm)
                              ? @""
                              : " | " + item.Asm;

                var param = item.Parameter != 0 ? item.Parameter.ToString() : @"";

                yield return string.Format("{0}  {1,4}  {2,3}  {3,4}  {4,-60}{5}{6}",
                                           pos, item.ReferenceValue, item.Type, param, item.Comments, rootPos, asm);

            }
        }

        static public void WritePatch(Patch patch, string filePath)
        {
            var header = new List<string>
                             {
                                 @"[header]",
                                 string.Format(@"size={0}", patch.FileSize),
                                 string.Format(@"md5={0}", patch.Md5),
                                 string.Format(@"version={0}", patch.Version),
                                 string.Format(@"drspos={0:X8}", patch.InterfaceDrsPosition),
                                 @"[data]"
                             };

            var patchLines = StringifyPatch(patch);
            header.AddRange(patchLines);

            File.WriteAllLines(filePath, header.ToArray());
        }

        static public Patch ConvertPatch(byte[] rootExe, byte[] otherExe, Patch patch)
        {
            return new Patch
                       {
                           InterfaceDrsPosition =
                               FindComparablePos(rootExe, otherExe, patch.InterfaceDrsPosition),
                           Items = patch.Items.Select(item => new Item
                                                                  {
                                                                      Pos =
                                                                          FindComparablePos(rootExe, otherExe,
                                                                                            item.Pos),
                                                                      Comments = item.Comments,
                                                                      ReferenceValue = item.ReferenceValue,
                                                                      Parameter = item.Parameter,
                                                                      Type = item.Type,
                                                                      OriginalPos = item.Pos
                                                                  }).ToArray()
                       };
        }

        private static int FindComparablePos(byte[] correctExe, byte[] otherExe, int pos)
        {
            for (var windowSize = 4; windowSize < 25; windowSize += 4)
            {
                var window = new byte[windowSize];
                var startOffset = 0;
                if (startOffset + pos < 0)
                    startOffset = -pos;
                if ((startOffset + windowSize) >= correctExe.Length)
                    startOffset = correctExe.Length - (pos + windowSize);

                for (var i = 0; i < windowSize; i++)
                    window[i] = correctExe[i + pos + startOffset];

                var positions = FindWindow(otherExe, window).ToArray();

                if (positions.Length == 1)
                    return positions[0] - startOffset;

                if (positions.Length == 0)
                {
                    if (windowSize == 8 && startOffset == 0)
                    {
                        startOffset = -4;
                        for (var i = 0; i < windowSize; i++)
                            window[i] = correctExe[i + pos + startOffset];

                        positions = FindWindow(otherExe, window).ToArray();
                        if (positions.Length == 1)
                            return positions[0] - startOffset;
                    }
                    UserFeedback.Warning(string.Format(@"Found no matches for block {0:X8} {1}", pos, windowSize));
                    return -pos;
                }
            }

            UserFeedback.Warning("Found too many matches for block {0:X8}", pos);
            return -pos;
        }

        private static IEnumerable<int> FindWindow(byte[] otherExe, byte[] window)
        {
            for (var i = 0; i <= otherExe.Length - window.Length; i++)
            {
                if (!Equals(window, otherExe, i))
                    continue;
                yield return i;
            }
        }

        private static bool Equals(byte[] window, byte[] otherExe, int pos)
        {
            var errors = new List<int>();
            var maxErrors = window.Length / 3;

            for (var i = 0; i < window.Length; i++)
            {
                if (window[i] == otherExe[pos + i])
                    continue;

                // If the first 4 bytes (the key dword PLUS the first opcode) are wrong, cancel.
                if (i < 5)
                    return false;

                if (errors.Count >= maxErrors)
                    return false;

                errors.Add(i);
            }

            // No errors? OK
            if (errors.Count == 0)
                return true;

            foreach (var errByte in errors)
            {
                for (var back = 1; ; back++)
                {
                    var backPos = errByte - back;

                    // The opcode-to-test cannot exist in the key dword bytes
                    if (backPos < 4)
                        return false; // unaccepted error

                    var backByte = window[errByte - back];

                    if (backByte == 0xE8) // Call
                        break; // accepted error

                    if (backByte == 0xE9) // LongJmp
                        break; // accepted error

                    // Loop end condition
                    if (back == 4)
                        return false; // Unaccepted error
                }
            }
            return true;
        }

        static public void PatchResolutions(byte[] exe, int oldWidth, int oldHeight, int newWidth, int newHeight, Patch patch)
        {
            // Create the map so, that originally larger resolutions stay larger even after patching. They _may_ become invalid though.
            // This is necessary to keep the internal (in AoE) if > else if > else if > code working.
            var hmap = new Dictionary<int, int>();
            var existingWidths = new[] { 800, 1024, 1280, 1600 };
            var wshift = newWidth;
            foreach (var w in existingWidths)
            {
                if (w == oldWidth)
                    hmap[w] = newWidth;
                else if (w > oldWidth)
                    hmap[w] = ++wshift;
            }

            var vmap = new Dictionary<int, int>();
            var existingHeights = new[] { 600, 768, 1024, 1200 };
            var hshift = newHeight;
            foreach (var h in existingHeights)
            {
                if (h == oldHeight)
                    vmap[h] = newHeight;
                else if (h > oldHeight)
                    vmap[h] = ++hshift;
            }

            foreach (var pair in hmap)
                UserFeedback.Trace(string.Format(@"Horizontal {0} => {1}", pair.Key, pair.Value));

            foreach (var pair in vmap)
                UserFeedback.Trace(string.Format(@"Vertical {0} => {1}", pair.Key, pair.Value));

            foreach (var item in patch.Items)
            {
                if (item.Pos >= exe.Length)
                {
                    UserFeedback.Warning(@"Error in input: Invalid location {0:X8}. [NOT PATCHED]", item.Pos);
                    continue;
                }
                var oldValue = item.ReferenceValue;
                int newValue;
                var hor = item.Type.Contains("H");
                var ver = item.Type.Contains("V");

                // If a number is used for both horizontal and vertical
                //  prefer the one that we are patching.
                if (hor && ver)
                {
                    if (oldWidth == oldValue) // so if we have 1024 HV, and are patching 1024x768, we'd ignore the V, and use the H
                        ver = false;
                    else
                        hor = false;
                }
                Trace.Assert(hor || ver);
                var map = ver ? vmap : hmap;
                if (!map.TryGetValue(oldValue, out newValue))
                    newValue = oldValue;

                if (item.Type.Contains("H") && item.Type.Contains("V"))
                    UserFeedback.Trace(string.Format(@"{0} HV: Mapping to {1}", oldValue, newValue));

                var ob0 = (int)exe[item.Pos];
                var ob1 = (int)exe[item.Pos + 1];
                var ob2 = (int)exe[item.Pos + 2];
                var ob3 = (int)exe[item.Pos + 3];
                var orgValue = ob0 | ob1 << 8 | ob2 << 16 | ob3 << 24;
                if (item.Type.Equals("dV") || item.Type.Equals("dH"))
                {
                    var expectedOrgValue = item.Parameter;
                    if (expectedOrgValue == 0)
                    {
                        UserFeedback.Warning(
                            string.Format(
                                "{0} action is safer if you mention the expected orgValue. Encountered {1} @ {2:X8}",
                                item.Type, orgValue, item.Pos));
                    }
                    else
                    {
                        if (expectedOrgValue != orgValue)
                        {
                            UserFeedback.Warning(string.Format(
                                                     @"{0} action expected value mismatch: {1} expected, {2} encountered @ {3:X8} [NOT PATCHED]",
                                                     item.Type,
                                                     expectedOrgValue, orgValue, item.Pos));
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
                        UserFeedback.Warning(string.Format(
                                                 @"{0} action expected value mismatch: {1} expected, {2} encountered @ {3:X8} [NOT PATCHED]",
                                                 item.Type,
                                                 oldValue, orgValue, item.Pos));
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

    }
}