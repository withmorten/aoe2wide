﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;

namespace AoE2Wide
{
    class FatalError : Exception
    {
        public FatalError(string msg)
            : base(msg)
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
        private static string _orgX1DrsPath;
        private static string _orgExePath;
        private static string _gameDirectory;
        private const bool skipExistingFiles = false;

        private static string FindPatchFile()
        {
            const string fileName = @"AoE2Wide*.patch";
            const string whatFile = @"Patch data";
            return FindFile(whatFile, fileName, null, null);
        }

        private static string[] FindPatchFiles()
        {
            const string fileName = @"AoE2Wide*.patch";
            const string whatFile = @"Patch data";
            return FindFiles(whatFile, fileName, null, null);
        }

        private static string FindRootPatchFile()
        {
            const string fileName = @"AoE2Wide.RootPatch";
            const string whatFile = @"Root Patch data";
            return FindFile(whatFile, fileName, null, null);
        }

        private static Patch FindPatchForExe(string exeFilename)
        {
            var fileSize = (int)new FileInfo(exeFilename).Length;
            var exeMd5 = GetChecksum(exeFilename);
            return FindPatchForExe(fileSize, exeMd5, exeFilename);
        }

        static string[] patchFiles;
        static Patch[] allPatches;
        private static Patch FindPatchForExe(int exeFileSize, string exeMd5, string exeFilenameForFeedback)
        {
            if (patchFiles == null)
                patchFiles = FindPatchFiles();

            if (allPatches == null)
                allPatches = patchFiles.Select(patchFile => Patcher.TryReadPatch(patchFile, true)).ToArray();

            var matchingPatches = allPatches.Where(patch => patch.FileSize == exeFileSize && patch.Md5.Equals(exeMd5)).ToArray();

            if (matchingPatches.Length == 0)
                return null;

            if (matchingPatches.Length == 1)
                return matchingPatches[0];

            UserFeedback.Warning("Multiple matching patches found for executable '{0}', using first:", exeFilenameForFeedback);
            foreach (var mp in matchingPatches)
                UserFeedback.Trace("* {0}", mp.PatchFilepath);

            return matchingPatches[0];
        }

        private static string FindExeFile(int fileSize, string md5)
        {
            const string fileName = @"*.exe";
            const string whatFile = @"unpatched exe";
            return FindFile(whatFile, fileName, fileSize, md5);
        }

        private static string FindProcessExe()
        {
            try
            {
                const string fileName = @"process.exe";
                const string whatFile = @"process suspender/resumer";
                return FindFile(whatFile, fileName, null, null);
            }
            catch (FatalError fe)
            {
                return null;
            }
        }

        private static string[] FindFiles(string whatFile, string fileName, Int64? expectedSize, string expectedHash)
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
            return files;
        }

        private static string FindFile(string whatFile, string fileName, Int64? expectedSize, string expectedHash)
        {
            var files = FindFiles(whatFile, fileName, expectedSize, expectedHash);
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

        private static string GetChecksum(byte[] data)
        {
            using (var md5 = MD5.Create())
                return BitConverter.ToString(md5.ComputeHash(data));
        }

        private static bool IsFileHashCorrect(string fn, string expectedHash)
        {
            var actualHash = GetChecksum(fn);
            if (actualHash.Equals(expectedHash, StringComparison.InvariantCultureIgnoreCase))
                return true;
            UserFeedback.Trace(string.Format(@"'{0}' doesn't meet the expected hashcode '{1}' instead of '{2}'",
                                             fn,
                                             actualHash,
                                             expectedHash));
            return false;
        }

        private static readonly bool IsVistaOrHigher = Environment.OSVersion.Version.Major >= 6;

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

        static void PatchAllDrs()
        {
            var doneList = new HashSet<int>();
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                try
                {
                    var newWidth = screen.Bounds.Width;
                    var newHeight = screen.Bounds.Height;
                    var key = newWidth + (newHeight * 65536);
                    if (doneList.Add(key))
                        PatchAllDrs(newWidth, newHeight);
                }
                catch (Exception e)
                {
                    UserFeedback.Error(e);
                }
            }
        }

        static void PatchAllDrs(int newWidth, int newHeight)
        {
            int oldWidth, oldHeight;
            GetOldWidthHeight(newWidth, newHeight, out oldWidth, out oldHeight);
            foreach ( var drsPath in System.IO.Directory.GetFiles( @"data\", "*.drs"))
            {
                var directory = string.Format("data{0}x{1}", newWidth, newHeight);
                if (!System.IO.Directory.Exists( directory))
                    System.IO.Directory.CreateDirectory( directory);

                var newPath = System.IO.Path.Combine( directory, System.IO.Path.GetFileName( drsPath ));
                PatchADrs(drsPath, newPath, oldWidth, oldHeight, newWidth, newHeight);
            }
        }

        static void PatchADrs(string _orgDrsPath, string newDrsName, int oldWidth, int oldHeight, int newWidth, int newHeight)
        {
            UserFeedback.Trace(@"Opening original {0}", System.IO.Path.GetFileName(_orgDrsPath));
            using (
                var interfaceDrs = new FileStream(_orgDrsPath, FileMode.Open,
                                                  FileSystemRights.ReadData,
                                                  FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
            {
                UserFeedback.Trace(@"Creating patched drs file '{0}'", newDrsName);
                using (
                    var newDrs = new FileStream(newDrsName, FileMode.Create, FileSystemRights.Write,
                                                FileShare.None,
                                                1024 * 1024, FileOptions.SequentialScan))
                {
                    UserFeedback.Trace(@"Patching DRS '{0}'", System.IO.Path.GetFileName(_orgDrsPath));
                    DrsPatcher.Patch(interfaceDrs, newDrs, oldWidth, oldHeight, newWidth, newHeight);
                }
            }
        }

        static void Go(string[] args)
        {
            _gameDirectory = FindGameDirectory();
            if (args.Length == 1)
            {
                if (args[0].Equals(@"addasm"))
                {
                    AddAsmToRootPatchFile();
                    return;
                }
                /*                if (args[0].Equals(@"test"))
                                {
                                    PatchAllDrs();
                                    return;
                                }*/
                if (args[0].Equals("createpatches"))
                {
                    var allExes = FindFiles(@"AoK-TC executables", @"age2_x1*.exe", null, null);
                    var allPatchedExes = new List<string>();
                    try
                    {
                        allPatchedExes = new List<string>(FindFiles(@"Patched executables", @"age2_x1*_???*x???*.exe", null, null));
                    }
                    catch (FatalError)
                    {
                        // Happens if no patched exes are found. No problem!
                    }

                    foreach (var exe in allExes)
                    {
                        if (allPatchedExes.Contains(exe))
                        {
                            UserFeedback.Trace(@"Skipping patched exe '{0}'", exe);
                            continue;
                        }

                        ConvertPatchFile(exe);
                    }
                }
                else
                {
                    ShowUsage();
                }
                return;
            }

            int newWidth = 0, newHeight = 0;
            switch (args.Length)
            {
                case 0:
                    break;
                case 2:
                    {
                        if (!int.TryParse(args[0], out newWidth) || !int.TryParse(args[1], out newHeight))
                        {
                            ShowUsage();
                            return;
                        }

                    }
                    break;
                default:
                    ShowUsage();
                    break;
            }

            _orgDrsPath = Path.Combine(Path.Combine(_gameDirectory, @"Data"), @"interfac.drs");
            _orgX1DrsPath = Path.Combine(Path.Combine(_gameDirectory, @"Data"), @"interfac_x1.drs");

            if (!File.Exists(_orgDrsPath))
                throw new FatalError(string.Format(@"Cannot find drs file '{0}' in the game folder", _orgDrsPath));

            UserFeedback.Info("Trying to find a patch file for all executables > 2MiB in size");
            var allExecutables = FindFiles("executables", "*.exe", null, null);
            var allLargeExes = allExecutables.Where(exe => new FileInfo(exe).Length > 2000000);

            foreach (var exe in allLargeExes)
                FindResAndPatchExecutable(newWidth, newHeight, exe);
        }

        private static void FindResAndPatchExecutable(int newWidth, int newHeight, string exe)
        {
            try
            {
                UserFeedback.Trace(@"");
                _orgExePath = exe;
                var patch = FindPatchForExe(_orgExePath);
                if (patch == null)
                {
                    UserFeedback.Trace("No patches found for executable '{0}'", _orgExePath);
                    return;
                }

                UserFeedback.Info(string.Format("Patching Exe '{0}' (version {1}) with patch file '{2}'", _orgExePath, patch.Version, patch.PatchFilepath));

                if (patch.InterfaceX1DrsPosition > 0 && !File.Exists(_orgX1DrsPath))
                    throw new FatalError(string.Format(@"Cannot find X1 drs file '{0}' in the game folder", _orgX1DrsPath));

                if (patch.InterfaceX1DrsPosition == 0 && File.Exists(_orgX1DrsPath))
                    UserFeedback.Warning(string.Format(@"Found X1 drs file '{0}' in the game folder, but no x1drspos=??? in patch file. X1 drs won't be patched!", _orgX1DrsPath));

                if (newWidth == 0 && newHeight == 0)
                {
                    UserFeedback.Info(
                        @"Auto patching for all current screen sizes. Note that the game will always use the primary screen!");
                    var doneList = new HashSet<int>();
                    foreach (var screen in System.Windows.Forms.Screen.AllScreens)
                    {
                        try
                        {
                            newWidth = screen.Bounds.Width;
                            newHeight = screen.Bounds.Height;
                            var key = newWidth + (newHeight * 65536);
                            if (doneList.Add(key))
                                PatchExecutable(newWidth, newHeight, patch);
                        }
                        catch (Exception e)
                        {
                            UserFeedback.Error(e);
                        }
                    }
                }
                else
                {
                    PatchExecutable(newWidth, newHeight, patch);
                }
            }
            catch (Exception e)
            {
                UserFeedback.Error(e);
            }
            return;
        }

        private static void AddAsmToRootPatchFile()
        {
            UserFeedback.Info(@"Locating listing file");
            var lstFile = FindFile("listing file", "age2_x1*.lst", null, null);

            UserFeedback.Info(@"Locating RootPatch file");
            var rootPatchFilePath = FindRootPatchFile();

            UserFeedback.Trace(@"Reading the root patch file");
            var patch = Patcher.ReadPatch(rootPatchFilePath, false);

            UserFeedback.Trace(@"Reading the listing file");
            var asmMap = Patcher.ReadAsmMap(lstFile);

            UserFeedback.Trace(@"Adding Asm to the patch data");
            Patcher.AddAsm(patch, asmMap);

            var outputRootPatchFilePath = rootPatchFilePath + "2";

            UserFeedback.Trace("Writing the patch file '{0}'", outputRootPatchFilePath);
            Patcher.WritePatch(patch, outputRootPatchFilePath);
        }

        private static void ConvertPatchFile(string otherExe)
        {
            UserFeedback.Info(@"Converting RootPatch file to a patch file for '{0}'", otherExe);
            var rootPatchFilePath = FindRootPatchFile();

            UserFeedback.Trace(@"Reading the root patch file");
            var patch = Patcher.ReadPatch(rootPatchFilePath, true);

            UserFeedback.Trace(@"Locating the root executable file");
            var rootExePath = FindExeFile(patch.FileSize, patch.Md5);

            UserFeedback.Trace(@"Reading the target executable");
            var exe2 = File.ReadAllBytes(otherExe);

            UserFeedback.Trace(@"Reading root executable");
            var exe1 = File.ReadAllBytes(rootExePath);

            var md5 = GetChecksum(exe2);

            UserFeedback.Trace(@"Detecting version from filename and/or md5");
            string version = GetVersion(otherExe, md5);
            UserFeedback.Trace(@"Version: '{0}'", version);

            var rootMd5 = GetChecksum(exe1);
            Patch newPatch;
            if (rootMd5.Equals(md5))
            {
                UserFeedback.Trace(@"Executable is equal; leaving patch file as-is");
                newPatch = new Patch
                               {
                                   FileSize = patch.FileSize,
                                   InterfaceDrsPosition = patch.InterfaceDrsPosition,
                                   Items = patch.Items,
                                   Md5 = patch.Md5
                               };
            }
            else
            {
                UserFeedback.Trace(@"Locating comparable locations, this may take a while");
                newPatch = Patcher.ConvertPatch(exe1, exe2, patch);
                newPatch.FileSize = exe2.Length;
                newPatch.Md5 = md5;
            }
            newPatch.Version = version;

            var patchFileName = @"AoE2Wide_" + version + @".patch";

            var patchOutput = Path.Combine(Path.GetDirectoryName(otherExe), patchFileName);
            UserFeedback.Trace("Writing the patch file '{0}'", patchOutput);
            Patcher.WritePatch(newPatch, patchOutput);
        }

        private static string GetVersion(string otherExe, string md5)
        {
            var version = Path.GetFileNameWithoutExtension(otherExe);
            version = version.Replace("age2_x1", "");

            while (version.Length > 0 && version[0] == '_')
                version = version.Substring(1);

            if (version.Length > 0)
                return version;

            switch (md5)
            {
                case "CB-15-BC-FA-FF-FB-C3-BA-80-F3-07-D4-1F-50-E5-46":
                    return "1.0e";

                case "F2-72-38-DF-A1-C3-EC-44-41-52-C8-5F-01-AF-30-A0":
                    return "1.0c";

                case "0D-9D-3B-61-BC-11-BF-DA-72-D7-D1-75-04-50-E0-25":
                    return "1.0";
                default:
                    return md5.Replace("-", "");
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
                    throw new FatalError(@"Cannot locate the game directory (where I expect language_x1.dll). Please run AoE2Wide from the game folder or a subfolder of it.");
            }
            UserFeedback.Trace(@"Located @'{0}'", iter.FullName);
            return iter.FullName;
        }

        private static void ShowUsage()
        {
            UserFeedback.Warning("Usage: aoe2wide.exe [width height]");
            UserFeedback.Info("If the new width and height are omitted, the current screen resolution(s) will be used.");
            UserFeedback.Trace("Alternatively, use: aoe2wide.exe createpatches");
            UserFeedback.Trace("  to try to create fresh patch files for all your age2_x1 executables, from the RootPatch file");
        }

        private static void PatchExecutable(int newWidth, int newHeight, Patch patch)
        {
            try
            {
                int oldWidth, oldHeight;
                GetOldWidthHeight(newWidth, newHeight, out oldWidth, out oldHeight);

                UserFeedback.Info(string.Format(@"Changing {0}x{1} to {2}x{3}", oldWidth, oldHeight, newWidth, newHeight));

                UserFeedback.Trace(@"Reading original executable");
                var exe = File.ReadAllBytes(_orgExePath);

                var versionString = patch.Version.Length == 0 ? "" : patch.Version + "_";

                var newDrsName = Path.Combine(Path.Combine(_gameDirectory, @"Data"), string.Format(@"{0:D4}{1:D4}.drs", newWidth, newHeight));
                var newX1DrsName = Path.Combine(Path.Combine(_gameDirectory, @"Data"), string.Format(@"{0:D4}{1:D4}_x1.drs", newWidth, newHeight));
                var newExeName = Path.Combine(_gameDirectory,
                                              string.Format(@"age2_x1_{2}{0}x{1}.exe", newWidth, newHeight,
                                                            versionString));

                var batchName = Path.Combine(_gameDirectory,
                                              string.Format(@"AoC{2} {0}x{1}.bat", newWidth, newHeight,
                                                            patch.Version));

                var desktopBatchName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                                              string.Format(@"AoC{2} {0}x{1}.bat", newWidth, newHeight,
                                                            patch.Version));
                //Trace(@"Writing file with all occurrences of resolutions");
                //Patcher.ListEm(bytes);

                UserFeedback.Trace("Patching the executable: DRS reference");
                Patcher.PatchDrsRefInExe(exe, Path.GetFileName(newDrsName), patch);

                if (patch.InterfaceX1DrsPosition > 0)
                {
                    UserFeedback.Trace("Patching the executable: X1 DRS reference");
                    Patcher.PatchX1DrsRefInExe(exe, Path.GetFileName(newX1DrsName), patch);
                }

                UserFeedback.Trace("Patching the executable: resolutions");
                Patcher.PatchResolutions(exe, oldWidth, oldHeight, newWidth, newHeight, patch);

                UserFeedback.Trace(string.Format(@"Writing the patched executable '{0}'", newExeName));
                File.WriteAllBytes(newExeName, exe);

                if (skipExistingFiles && File.Exists(batchName))
                {
                    UserFeedback.Trace(@"Skipping existing convenience batch file '{0}'", batchName);
                }
                else
                {
                    UserFeedback.Trace(@"Writing convenience batch file '{0}'", batchName);
                    var batContent = new List<string> {@"@echo off"};

                    var processExe = IsVistaOrHigher ? FindProcessExe() : null;

                    if (processExe != null)
                    {
                        batContent.Add(
                            @"ECHO Using process.exe to suspend explorer.exe (win7, vista palette fix)");
                        batContent.Add(string.Format("\"{0}\" -s explorer.exe", processExe));
                    }

                    batContent.Add(@"ECHO Starting Age of Empires II - The Conquerers in the correct screen mode");
                    batContent.Add(string.Format("\"{0}\" {1}", Path.GetFileName(newExeName), oldWidth));

                    if (processExe != null)
                    {
                        batContent.Add(@"ECHO Resuming explorer (was suspended before)");
                        batContent.Add(string.Format("\"{0}\" -r explorer.exe", processExe));
                    }

                    File.WriteAllLines(batchName, batContent.ToArray());
                }

                if (skipExistingFiles && File.Exists(desktopBatchName))
                {
                    UserFeedback.Trace(@"Skipping existing convenience desktop batch file '{0}'", desktopBatchName);
                }
                else
                {
                    var driveLetter = Path.GetPathRoot(_gameDirectory);
                    driveLetter = driveLetter.Replace(Path.DirectorySeparatorChar, ' ');
                    UserFeedback.Trace(@"Writing convenience desktop batch file '{0}'", desktopBatchName);
                    var batContent = new List<string>
                                         {
                                             @"@echo off",
                                             driveLetter,
                                             string.Format("cd \"{0}\"", _gameDirectory)
                                         };

                    var processExe = IsVistaOrHigher ? FindProcessExe() : null;

                    if (processExe != null)
                    {
                        batContent.Add(
                            @"ECHO Using process.exe to suspend explorer.exe (win7, vista palette fix)");
                        batContent.Add(string.Format("\"{0}\" -s explorer.exe", processExe));
                    }

                    batContent.Add(@"ECHO Starting Age of Empires II - The Conquerers in the correct screen mode");
                    batContent.Add(string.Format("\"{0}\" {1}", Path.GetFileName(newExeName), oldWidth));

                    if (processExe != null)
                    {
                        batContent.Add(@"ECHO Resuming explorer (was suspended before)");
                        batContent.Add(string.Format("\"{0}\" -r explorer.exe", processExe));
                    }

                    File.WriteAllLines(desktopBatchName, batContent.ToArray());
                }

                if (skipExistingFiles && File.Exists(newDrsName))
                {
                    UserFeedback.Info(@"Patched drs file '{0}' exists already, skipping.", newDrsName);
                }
                else
                {
                    PatchADrs(_orgDrsPath, newDrsName, oldWidth, oldHeight, newWidth, newHeight);
                }

                if (patch.InterfaceX1DrsPosition > 0)
                {
                    if (skipExistingFiles && File.Exists(newX1DrsName))
                    {
                        UserFeedback.Info(@"Patched X1 drs file '{0}' exists already, skipping.", newX1DrsName);
                    }
                    else
                    {
                        PatchADrs(_orgX1DrsPath, newX1DrsName, oldWidth, oldHeight, newWidth, newHeight);
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

                        var specificPubDataDir = Path.Combine(specificPubDir, @"Data");
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
            if (newWidth >= 1280 && newHeight >= 1024)
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