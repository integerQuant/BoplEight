using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BoplEight.Installer
{
    internal static class InstallerCore
    {
        internal const string InstallerVersion = "1.0.5";
        internal const string SupportedAssemblySha256 = "06A154AF64AD962E534587058219FB94216C5CE53605BB9AF5F77CB433A4AE07";
        internal const string InstallMarkerRelativePath = "BepInEx\\BoplEight\\installed.txt";
        internal const string PluginRelativePath = "BepInEx\\plugins\\BoplEight.dll";
        private const string BepInExCoreRelativePath = "BepInEx\\core\\BepInEx.dll";

        internal static IList<string> ParseSteamLibraryFolders(string contents)
        {
            var paths = new List<string>();
            if (string.IsNullOrEmpty(contents))
            {
                return paths;
            }

            MatchCollection matches = Regex.Matches(
                contents,
                "\\\"path\\\"\\s*\\\"([^\\\"]+)\\\"",
                RegexOptions.IgnoreCase);
            for (var index = 0; index < matches.Count; index++)
            {
                AddUniquePath(paths, DecodeVdfPath(matches[index].Groups[1].Value));
            }

            // Steam's older VDF format stored library paths directly under numeric keys.
            matches = Regex.Matches(contents, "\\\"\\d+\\\"\\s*\\\"([^\\\"]+)\\\"");
            for (var index = 0; index < matches.Count; index++)
            {
                AddUniquePath(paths, DecodeVdfPath(matches[index].Groups[1].Value));
            }

            return paths;
        }

        internal static IList<string> DiscoverGameDirectories()
        {
            var steamRoots = new List<string>();
            AddRegistryPath(steamRoots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
            AddRegistryPath(steamRoots, Registry.LocalMachine, @"Software\Valve\Steam", "InstallPath");
            AddRegistryPath(steamRoots, Registry.LocalMachine, @"Software\WOW6432Node\Valve\Steam", "InstallPath");
            AddUniquePath(steamRoots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));

            var allLibraries = new List<string>(steamRoots);
            for (var index = 0; index < steamRoots.Count; index++)
            {
                string libraryFile = Path.Combine(steamRoots[index], "steamapps", "libraryfolders.vdf");
                if (!File.Exists(libraryFile))
                {
                    continue;
                }

                try
                {
                    IList<string> parsed = ParseSteamLibraryFolders(File.ReadAllText(libraryFile));
                    for (var libraryIndex = 0; libraryIndex < parsed.Count; libraryIndex++)
                    {
                        AddUniquePath(allLibraries, parsed[libraryIndex]);
                    }
                }
                catch
                {
                    // A manual folder picker remains available when Steam metadata is unavailable.
                }
            }

            var gameDirectories = new List<string>();
            for (var index = 0; index < allLibraries.Count; index++)
            {
                string candidate = Path.Combine(allLibraries[index], "steamapps", "common", "Bopl Battle");
                if (File.Exists(Path.Combine(candidate, "BoplBattle.exe")))
                {
                    AddUniquePath(gameDirectories, candidate);
                }
            }

            return gameDirectories;
        }

        internal static bool ValidateGameDirectory(string gameDirectory, out string reason)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                reason = "Select the Bopl Battle installation folder.";
                return false;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(gameDirectory.Trim());
            }
            catch (Exception exception)
            {
                reason = "The selected folder path is invalid: " + exception.Message;
                return false;
            }

            if (!File.Exists(Path.Combine(fullPath, "BoplBattle.exe")))
            {
                reason = "BoplBattle.exe was not found in the selected folder.";
                return false;
            }

            string assemblyPath = Path.Combine(fullPath, "BoplBattle_Data", "Managed", "Assembly-CSharp.dll");
            if (!File.Exists(assemblyPath))
            {
                reason = "BoplBattle_Data\\Managed\\Assembly-CSharp.dll was not found.";
                return false;
            }

            string actualHash;
            try
            {
                actualHash = ComputeSha256(assemblyPath);
            }
            catch (Exception exception)
            {
                reason = "The game version could not be verified: " + exception.Message;
                return false;
            }

            if (!string.Equals(actualHash, SupportedAssemblySha256, StringComparison.OrdinalIgnoreCase))
            {
                reason = "This Bopl Battle version is unsupported. Verify the game files in Steam, then use the matching BoplEight installer.";
                return false;
            }

            reason = null;
            return true;
        }

        internal static bool IsInstalled(string gameDirectory)
        {
            return !string.IsNullOrEmpty(gameDirectory)
                && File.Exists(Path.Combine(gameDirectory, PluginRelativePath));
        }

        internal static void Install(string gameDirectory, Stream payloadStream)
        {
            string reason;
            if (!ValidateGameDirectory(gameDirectory, out reason))
            {
                throw new InvalidOperationException(reason);
            }

            if (payloadStream == null)
            {
                throw new ArgumentNullException("payloadStream");
            }

            string root = Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            bool existingBepInEx = IsCompleteBepInExInstallation(root);
            if (!existingBepInEx
                && File.Exists(Path.Combine(root, "winhttp.dll"))
                && !File.Exists(Path.Combine(root, ".doorstop_version")))
            {
                throw new InvalidOperationException("Another winhttp.dll mod loader is already installed. Remove it or install BepInEx manually before BoplEight.");
            }

            if (payloadStream.CanSeek)
            {
                payloadStream.Position = 0;
            }

            using (var archive = new ZipArchive(payloadStream, ZipArchiveMode.Read, true))
            {
                for (var index = 0; index < archive.Entries.Count; index++)
                {
                    ZipArchiveEntry entry = archive.Entries[index];
                    string relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    string destination = GetSafeDestination(root, relativePath);
                    EnsureNoReparsePointBelowRoot(root, destination);
                    bool isDirectory = string.IsNullOrEmpty(entry.Name);
                    if (isDirectory)
                    {
                        if (!existingBepInEx)
                        {
                            Directory.CreateDirectory(destination);
                        }
                        continue;
                    }

                    if (existingBepInEx && !ShouldInstallAlongsideExistingBepInEx(relativePath))
                    {
                        continue;
                    }

                    string parent = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    string temporaryPath = destination + ".bopleight.tmp";
                    try
                    {
                        using (Stream source = entry.Open())
                        using (var target = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            source.CopyTo(target);
                        }

                        if (File.Exists(destination))
                        {
                            File.Delete(destination);
                        }
                        File.Move(temporaryPath, destination);
                    }
                    finally
                    {
                        if (File.Exists(temporaryPath))
                        {
                            File.Delete(temporaryPath);
                        }
                    }
                }
            }

            string markerPath = Path.Combine(root, InstallMarkerRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath));
            File.WriteAllText(
                markerPath,
                "BoplEight installer version=" + InstallerVersion + Environment.NewLine
                + "BepInEx was already installed=" + existingBepInEx + Environment.NewLine);
        }

        internal static void Uninstall(string gameDirectory)
        {
            if (string.IsNullOrWhiteSpace(gameDirectory))
            {
                throw new InvalidOperationException("Select the Bopl Battle installation folder.");
            }

            DeleteIfPresent(Path.Combine(gameDirectory, PluginRelativePath));
            DeleteIfPresent(Path.Combine(gameDirectory, "BepInEx", "config", "io.opencode.bopleight.cfg"));
            DeleteIfPresent(Path.Combine(gameDirectory, InstallMarkerRelativePath));

            string informationDirectory = Path.Combine(gameDirectory, "BepInEx", "BoplEight");
            if (Directory.Exists(informationDirectory)
                && Directory.GetFiles(informationDirectory).Length == 0
                && Directory.GetDirectories(informationDirectory).Length == 0)
            {
                Directory.Delete(informationDirectory);
            }
        }

        private static bool ShouldInstallAlongsideExistingBepInEx(string relativePath)
        {
            return string.Equals(relativePath, PluginRelativePath, StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith("BepInEx" + Path.DirectorySeparatorChar + "BoplEight" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCompleteBepInExInstallation(string root)
        {
            return File.Exists(Path.Combine(root, BepInExCoreRelativePath))
                && File.Exists(Path.Combine(root, "BepInEx", "core", "BepInEx.Preloader.dll"))
                && File.Exists(Path.Combine(root, ".doorstop_version"))
                && File.Exists(Path.Combine(root, "doorstop_config.ini"))
                && File.Exists(Path.Combine(root, "winhttp.dll"));
        }

        private static string GetSafeDestination(string root, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath) || Path.IsPathRooted(relativePath))
            {
                throw new InvalidDataException("The installer payload contains an invalid path.");
            }

            string destination = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The installer payload attempted to write outside the game directory.");
            }

            return destination;
        }

        private static void EnsureNoReparsePointBelowRoot(string root, string destination)
        {
            string relativePath = destination.Substring(root.Length);
            string current = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string[] parts = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < parts.Length; index++)
            {
                current = Path.Combine(current, parts[index]);
                if ((Directory.Exists(current) || File.Exists(current))
                    && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("The selected game folder contains a redirected path that the installer will not modify: " + current);
                }
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (SHA256 algorithm = SHA256.Create())
            {
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }

        private static string DecodeVdfPath(string path)
        {
            return path.Replace("\\\\", "\\").Replace("\\/", "/");
        }

        private static void AddRegistryPath(ICollection<string> paths, RegistryKey root, string keyPath, string valueName)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(keyPath))
                {
                    if (key != null)
                    {
                        AddUniquePath(paths, key.GetValue(valueName) as string);
                    }
                }
            }
            catch
            {
                // Steam may be portable or unavailable; library metadata and browsing remain available.
            }
        }

        private static void AddUniquePath(ICollection<string> paths, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string normalized;
            try
            {
                normalized = Path.GetFullPath(path.Trim().Replace('/', Path.DirectorySeparatorChar))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return;
            }

            foreach (string existing in paths)
            {
                if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            paths.Add(normalized);
        }

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
