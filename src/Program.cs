using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;

namespace AoE2Wide
{
    class FatalError : Exception
    {
        public FatalError(string msg) : base(msg)
        {
        }
    }

    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// 
        private static string _orgDrsPath;
        private static string _orgExePath;
        private static string _patchFilePath;
        private static string _gameDirectory;

        private static string FindPatchFile()
        {
            const string fileName = @"AoE2Wide.dat";
            const string whatFile = @"Patch data";
            return FindFile(whatFile, fileName, null, null);
        }

        private static string FindExeFile()
        {
            const string fileName = @"age2_x1*.exe";
            const string whatFile = @"original exe";
            return FindFile(whatFile, fileName, 2695213, "0D-9D-3B-61-BC-11-BF-DA-72-D7-D1-75-04-50-E0-25");
        }

        private static string FindFile(string whatFile, string fileName, Int64? expectedSize, string expectedHash)
        {
            UserFeedback.Trace(string.Format(@"Locating {0} file '{1}'", whatFile, fileName));
            var files = Directory.GetFiles(_gameDirectory, fileName, SearchOption.AllDirectories);

            if (expectedSize.HasValue)
                files = files.Where(fn => IsFileSizeCorrect(fn, expectedSize.Value)).ToArray();

            if (expectedHash != null)
                files = files.Where(fn => IsFileHashCorrect(fn, expectedHash)).ToArray();

            if (files.Length == 0)
                throw new FatalError(string.Format(@"No correct {0} found in current directory or subdirectories",
                                                  whatFile));

            if (files.Length > 1)
            {
                UserFeedback.Warning(
                    @"Multiple correct {0} instances found in current directory and subdirectories:",
                    whatFile);
                foreach (var file in files)
                    UserFeedback.Trace(file);
            }
            UserFeedback.Info(@"Using '{0}'", files[0]);
            return files[0];
        }

        private static bool IsFileSizeCorrect(string fn, long expectedSize)
        {
            if (new FileInfo(fn).Length == expectedSize)
                return true;
            UserFeedback.Trace("'{0}' is not the expected size", fn);
            return false;
        }

        private static string GetChecksum(string file)
        {
            using (var stream = File.OpenRead(file))
                using (var md5 = MD5.Create())
                    return BitConverter.ToString(md5.ComputeHash(stream));
        }

        private static bool IsFileHashCorrect(string fn, string expectedHash)
        {
            var actualHash = GetChecksum(fn);
            if (actualHash.Equals(expectedHash,StringComparison.InvariantCultureIgnoreCase))
                return true;
            UserFeedback.Trace(string.Format(@"'{0}' doesn't meet the expected hashcode '{1}' instead of '{2}'",
                                             fn,
                                             actualHash,
                                             expectedHash));
            return false;
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
            _gameDirectory = FindGameDirectory();
            _patchFilePath = FindPatchFile();
            _orgExePath = FindExeFile();
            _orgDrsPath = Path.Combine(Path.Combine(_gameDirectory, @"Data"), @"interfac.drs");

            if (!File.Exists(_orgDrsPath))
                throw new FatalError(string.Format(@"Cannot find drs file '{0}' in current folder", _orgDrsPath));

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
                                var key = newWidth + (newHeight * 65536);
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

        private static string FindGameDirectory()
        {
            UserFeedback.Trace(@"Locating game main folder...");
            var iter = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (iter.GetFiles(@"language_x1.dll").Length == 0)
            {
                iter = iter.Parent;
                if (iter == null)
                    throw new FatalError(@"Cannot locate the game directory (where I expect language_x1.dll)");
            }
            UserFeedback.Trace(@"Located @'{0}'", iter.FullName);
            return iter.FullName;
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
                var exe = File.ReadAllBytes(_orgExePath);

                var newDrsName = Path.Combine(Path.Combine(_gameDirectory, @"Data"), string.Format(@"{0:D4}{1:D4}.drs", newWidth, newHeight));
                var newExeName = Path.Combine(Path.GetDirectoryName(_orgExePath),
                                              string.Format(@"age2_x1_{0}x{1}.exe", newWidth, newHeight));

                //Trace(@"Writing file with all occurrences of resolutions");
                //Patcher.ListEm(bytes);

                UserFeedback.Trace("Reading the patch file");
                var patch = Patcher.ReadPatch(_patchFilePath);

                UserFeedback.Trace("Patching the executable: DRS reference");
                Patcher.PatchDrsRefInExe(exe, Path.GetFileName(newDrsName));

                UserFeedback.Trace("Patching the executable: resolutions");
                Patcher.PatchResolutions(exe, oldWidth, oldHeight, newWidth, newHeight, patch);

                UserFeedback.Trace(string.Format(@"Writing the patched executable '{0}'", newExeName));
                File.WriteAllBytes(newExeName, exe);

                UserFeedback.Trace(@"Opening original interfac.drs");
                using (
                    var interfaceDrs = new FileStream(_orgDrsPath, FileMode.Open,
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

                try
                {
                    var pubDir = Path.Combine(_gameDirectory, @"donePatches");
                    if (Directory.Exists(pubDir))
                    {
                        var specificPubDir = Path.Combine(pubDir, string.Format(@"{0}x{1}", newWidth, newHeight));
                        if (!Directory.Exists(specificPubDir))
                            Directory.CreateDirectory(specificPubDir);

                        var specificPubDataDir = Path.Combine(specificPubDir,@"Data");
                        if (!Directory.Exists(specificPubDataDir))
                            Directory.CreateDirectory(specificPubDataDir);

                        var pubExePath = Path.Combine(specificPubDir, Path.GetFileName(newExeName));
                        var pubDrsPath = Path.Combine(specificPubDataDir, Path.GetFileName(newDrsName));

                        File.Copy(newExeName, pubExePath, true);
                        File.Copy(newDrsName, pubDrsPath, true);
                    }
                }
                catch (Exception e)
                {
                    UserFeedback.Warning("Couldn't publish: {0}", e.ToString());
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
            // Arbitrary threshold: (allows for a bit of vertical shrinkage, but no horizontal)
            if (newWidth >= 1280 && newHeight >= 960)
            {
                oldWidth = 1280;
                oldHeight = 1024;
                return;
            }

            // Can't vertically scale yet in 800x600, so only PREFER 800x600 if Ht == 600 or Wd too small for 1024 (hor shr not possible)
            if (newWidth < 1024 || newHeight == 600)
            {
                oldWidth = 800;
                oldHeight = 600;
                return;
            }

            oldWidth = 1024;
            oldHeight = 768;
            return;

            // The only-up code: 1024x768 or 800x660 doesn't work well atm.
            var widths = new[] { 800, 1024, 1280 };
            var heights = new[] { 600, 768, 1024 };

            for (var q = widths.Length - 1; q >= 0; q--)
            {
                if (newWidth >= widths[q] && newHeight >= heights[q])
                {
                    oldHeight = heights[q];
                    oldWidth = widths[q];
                    if (oldHeight == newHeight && oldWidth == newWidth)
                        throw new FatalError("It doesn't make sense to patch original resolutions");
                    return;
                }
            }
            throw new FatalError("Resolutions smaller than 800 wide or 600 high are not possible");
        }
    }
}