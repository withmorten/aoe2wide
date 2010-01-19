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
                    if (oldHeight == newHeight && oldWidth == newWidth)
                        throw new Exception("It doesn't make sense to patch original resolutions");
                    return;
                }
            }
            throw new Exception("Resolutions smaller than 800 wide or 600 high are not possible");
        }
    }
}
