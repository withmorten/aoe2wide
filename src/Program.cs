using System;
using System.Collections.Generic;
using System.IO;
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
            UserFeedback.Trace(@"Locating Patch data file '{0}'", fileName);
            var files = Directory.GetFiles(Directory.GetCurrentDirectory(), fileName, SearchOption.AllDirectories);
            if (files.Length == 0)
                throw new Exception(@"No AoE2Wide.dat found in current directory or subdirectories");

            if (files.Length > 1)
            {
                UserFeedback.Warning(@"Multiple AoE2Wide.dat instances found in current directory and subdirectories:");
                foreach (var file in files)
                    UserFeedback.Trace(file);
            }
            UserFeedback.Info(@"Using '{0}'", files[0]);
            return files[0];
        }

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Go(args);
            }
            catch (Exception e)
            {
                UserFeedback.Error(e);
            }

            UserFeedback.Close();
        }

        static void Go(string[] args)
        {
            _patchFileName = FindPatchFile();

            if (!File.Exists(OrgExeName))
                throw new Exception(string.Format(@"Cannot find original exe '{0}' in current folder", OrgExeName));
            if (!File.Exists(OrgDrsName))
                throw new Exception(string.Format(@"Cannot find drs file '{0}' in current folder", OrgDrsName));

            switch (args.Length)
            {
                case 0:
                    {
                        UserFeedback.Info(
                            "Auto patching for all current screen sizes. Note that the game will always use the primary screen!");
                        var doneList = new HashSet<int>();
                        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                        {
                            try
                            {
                                var newWidth = screen.Bounds.Width;
                                var newHeight = screen.Bounds.Height;
                                var key = newWidth + (newHeight*65536);
                                if (doneList.Add(key))
                                    MakePatch(newWidth, newHeight);
                            }
                            catch (Exception e)
                            {
                                UserFeedback.Error(e);
                            }
                        }
                    }
                    break;
                case 2:
                    {
                        int newWidth, newHeight;
                        if (!int.TryParse(args[0], out newWidth) || !int.TryParse(args[1], out newHeight))
                        {
                            ShowUsage();
                            return;
                        }

                        MakePatch(newWidth, newHeight);
                    }
                    break;
                default:
                    ShowUsage();
                    break;
            }
        }

        private static void ShowUsage()
        {
            UserFeedback.Warning("Usage: aoe2wide.exe [width height]");
            UserFeedback.Info("If the new width and height are omitted, the current screen resolution(s) will be used.");
        }

        private static void MakePatch(int newWidth, int newHeight)
        {
            try
            {
                int oldWidth, oldHeight;
                GetOldWidthHeight(newWidth, newHeight, out oldWidth, out oldHeight);

                UserFeedback.Info(string.Format(@"Changing {0}x{1} to {2}x{3}", oldWidth, oldHeight, newWidth, newHeight));

                UserFeedback.Trace(@"Reading original executable");
                var exe = File.ReadAllBytes(OrgExeName);

                var newDrsName = string.Format(@"Data\{0:D4}{1:D4}.drs", newWidth, newHeight);
                var newExeName = string.Format(@"age2_x1_{0}x{1}.exe", newWidth, newHeight);

                //Trace(@"Writing file with all occurrences of resolutions");
                //Patcher.ListEm(bytes);

                UserFeedback.Trace("Reading the patch file");
                var patch = Patcher.ReadPatch(_patchFileName);

                UserFeedback.Trace("Patching the executable: DRS reference");
                Patcher.PatchDrsRefInExe(exe, Path.GetFileName(newDrsName));

                UserFeedback.Trace("Patching the executable: resolutions");
                Patcher.PatchResolutions(exe, oldWidth, oldHeight, newWidth, newHeight, patch);

                UserFeedback.Trace(string.Format(@"Writing the patched executable '{0}'", newExeName));
                File.WriteAllBytes(newExeName, exe);

                UserFeedback.Trace(@"Opening original interfac.drs");
                using (
                    var interfaceDrs = new FileStream(OrgDrsName, FileMode.Open,
                                                      FileSystemRights.ReadData,
                                                      FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
                {
                    UserFeedback.Trace(string.Format(@"Creating patched drs file '{0}'", newDrsName));
                    using (
                        var newDrs = new FileStream(newDrsName, FileMode.Create, FileSystemRights.Write, FileShare.None,
                                                    1024 * 1024, FileOptions.SequentialScan))
                    {
                        UserFeedback.Trace(@"Patching DRS");
                        DrsPatcher.Patch(interfaceDrs, newDrs, oldWidth, oldHeight, newWidth, newHeight);
                    }
                }

                UserFeedback.Trace("Done");
            }
            catch (Exception e)
            {
                UserFeedback.Error(e);
            }
        }

        private static void GetOldWidthHeight(int newWidth, int newHeight, out int oldWidth, out int oldHeight)
        {
            // Temp ?? if width >= 1280, use 1280x1024 as source even if newHeight is smaller (scale down then)
            if (newWidth >= 1280)
            {
                oldWidth = 1280;
                oldHeight = 1024;
                return;
            }

            // The only-up code: 1024x768 or 800x660 doesn't work well atm.
            var widths = new [] { 800, 1024, 1280 };
            var heights = new [] { 600, 768, 1024 };

            for (var q = widths.Length - 1; q >= 0; q--)
            {
                if (newWidth >= widths[q] && newHeight >= heights[q])
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
