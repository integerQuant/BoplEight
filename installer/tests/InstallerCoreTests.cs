using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BoplEight.Installer;

internal static class InstallerCoreTests
{
    private static int failures;

    private static int Main(string[] args)
    {
        if (args.Length != 1 || !File.Exists(args[0]))
        {
            Console.Error.WriteLine("Pass the supported Assembly-CSharp.dll path to the installer tests.");
            return 2;
        }

        Run("Steam library paths parse", SteamLibraryPathsParse);
        Run("Missing game files reject", MissingGameFilesReject);
        Run("Supported game validates", delegate { SupportedGameValidates(args[0]); });
        Run("Payload installs safely", delegate { PayloadInstallsSafely(args[0]); });
        Run("Existing BepInEx is preserved", delegate { ExistingBepInExIsPreserved(args[0]); });
        Run("Partial BepInEx is repaired", delegate { PartialBepInExIsRepaired(args[0]); });
        Run("Traversal payload rejects", delegate { TraversalPayloadRejects(args[0]); });
        Run("Uninstall keeps shared BepInEx", delegate { UninstallKeepsSharedBepInEx(args[0]); });

        Console.WriteLine("Installer tests: 8, Failures: " + failures);
        return failures == 0 ? 0 : 1;
    }

    private static void SteamLibraryPathsParse()
    {
        const string vdf = "\"libraryfolders\"\n{\n  \"0\" { \"path\" \"C:\\\\Program Files (x86)\\\\Steam\" }\n  \"1\" { \"path\" \"D:\\\\Games\\\\Steam\" }\n}";
        IList<string> paths = InstallerCore.ParseSteamLibraryFolders(vdf);

        AssertEqual(2, paths.Count, "Both Steam library paths should parse.");
        AssertEqual(@"C:\Program Files (x86)\Steam", paths[0], "Escaped backslashes should decode.");
        AssertEqual(@"D:\Games\Steam", paths[1], "The secondary library should parse.");
    }

    private static void MissingGameFilesReject()
    {
        string directory = NewTemporaryDirectory();
        try
        {
            string reason;
            AssertFalse(InstallerCore.ValidateGameDirectory(directory, out reason), "An empty directory is not Bopl Battle.");
            AssertContains("BoplBattle.exe", reason, "The validation reason should identify the missing executable.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void SupportedGameValidates(string supportedAssembly)
    {
        string directory = CreateSupportedGameDirectory(supportedAssembly);
        try
        {
            string reason;
            AssertTrue(InstallerCore.ValidateGameDirectory(directory, out reason), "The supported assembly should validate: " + reason);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void PayloadInstallsSafely(string supportedAssembly)
    {
        string directory = CreateSupportedGameDirectory(supportedAssembly);
        try
        {
            using (MemoryStream payload = CreatePayload("new-core", "plugin"))
            {
                InstallerCore.Install(directory, payload);
            }

            AssertEqual("new-core", File.ReadAllText(Path.Combine(directory, "BepInEx", "core", "BepInEx.dll")), "BepInEx should install when absent.");
            AssertEqual("plugin", File.ReadAllText(Path.Combine(directory, "BepInEx", "plugins", "BoplEight.dll")), "BoplEight should install.");
            AssertTrue(File.Exists(Path.Combine(directory, InstallerCore.InstallMarkerRelativePath)), "The install marker should be written.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void ExistingBepInExIsPreserved(string supportedAssembly)
    {
        string directory = CreateSupportedGameDirectory(supportedAssembly);
        string coreDirectory = Path.Combine(directory, "BepInEx", "core");
        Directory.CreateDirectory(coreDirectory);
        File.WriteAllText(Path.Combine(coreDirectory, "BepInEx.dll"), "existing-core");
        File.WriteAllText(Path.Combine(coreDirectory, "BepInEx.Preloader.dll"), "existing-preloader");
        File.WriteAllText(Path.Combine(directory, ".doorstop_version"), "existing-version");
        File.WriteAllText(Path.Combine(directory, "doorstop_config.ini"), "existing-config");
        File.WriteAllText(Path.Combine(directory, "winhttp.dll"), "existing-loader");
        try
        {
            using (MemoryStream payload = CreatePayload("packaged-core", "plugin"))
            {
                InstallerCore.Install(directory, payload);
            }

            AssertEqual("existing-core", File.ReadAllText(Path.Combine(coreDirectory, "BepInEx.dll")), "An existing BepInEx installation must not be overwritten.");
            AssertEqual("plugin", File.ReadAllText(Path.Combine(directory, "BepInEx", "plugins", "BoplEight.dll")), "The plugin should still update.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void PartialBepInExIsRepaired(string supportedAssembly)
    {
        string directory = CreateSupportedGameDirectory(supportedAssembly);
        string coreDirectory = Path.Combine(directory, "BepInEx", "core");
        Directory.CreateDirectory(coreDirectory);
        File.WriteAllText(Path.Combine(coreDirectory, "BepInEx.dll"), "incomplete-core");
        try
        {
            using (MemoryStream payload = CreatePayload("repaired-core", "plugin"))
            {
                InstallerCore.Install(directory, payload);
            }

            AssertEqual("repaired-core", File.ReadAllText(Path.Combine(coreDirectory, "BepInEx.dll")), "A partial BepInEx installation should be repaired.");
            AssertTrue(File.Exists(Path.Combine(coreDirectory, "BepInEx.Preloader.dll")), "The missing BepInEx preloader should be restored.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void TraversalPayloadRejects(string supportedAssembly)
    {
        string directory = CreateSupportedGameDirectory(supportedAssembly);
        string escapedPath = Path.Combine(Path.GetDirectoryName(directory), "escape.txt");
        try
        {
            using (var payload = new MemoryStream())
            {
                using (var archive = new ZipArchive(payload, ZipArchiveMode.Create, true))
                using (var writer = new StreamWriter(archive.CreateEntry("../escape.txt").Open()))
                {
                    writer.Write("unsafe");
                }

                payload.Position = 0;
                AssertThrows<InvalidDataException>(delegate { InstallerCore.Install(directory, payload); }, "Traversal entries must be rejected.");
            }

            AssertFalse(File.Exists(escapedPath), "A traversal payload must never write outside the game directory.");
        }
        finally
        {
            if (File.Exists(escapedPath))
            {
                File.Delete(escapedPath);
            }
            Directory.Delete(directory, true);
        }
    }

    private static void UninstallKeepsSharedBepInEx(string supportedAssembly)
    {
        string directory = CreateSupportedGameDirectory(supportedAssembly);
        try
        {
            using (MemoryStream payload = CreatePayload("core", "plugin"))
            {
                InstallerCore.Install(directory, payload);
            }

            InstallerCore.Uninstall(directory);

            AssertFalse(File.Exists(Path.Combine(directory, "BepInEx", "plugins", "BoplEight.dll")), "Uninstall should remove BoplEight.");
            AssertFalse(File.Exists(Path.Combine(directory, InstallerCore.InstallMarkerRelativePath)), "Uninstall should remove its marker.");
            AssertTrue(File.Exists(Path.Combine(directory, "BepInEx", "core", "BepInEx.dll")), "Shared BepInEx files should remain installed.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static MemoryStream CreatePayload(string coreContents, string pluginContents)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            WriteEntry(archive, ".doorstop_version", "4.3.0");
            WriteEntry(archive, "doorstop_config.ini", "target_assembly=BepInEx\\core\\BepInEx.Preloader.dll");
            WriteEntry(archive, "winhttp.dll", "loader");
            WriteEntry(archive, "BepInEx/core/BepInEx.dll", coreContents);
            WriteEntry(archive, "BepInEx/core/BepInEx.Preloader.dll", "preloader");
            WriteEntry(archive, "BepInEx/plugins/BoplEight.dll", pluginContents);
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string path, string contents)
    {
        using (var writer = new StreamWriter(archive.CreateEntry(path).Open()))
        {
            writer.Write(contents);
        }
    }

    private static string CreateSupportedGameDirectory(string supportedAssembly)
    {
        string directory = NewTemporaryDirectory();
        File.WriteAllText(Path.Combine(directory, "BoplBattle.exe"), "test executable");
        string managed = Path.Combine(directory, "BoplBattle_Data", "Managed");
        Directory.CreateDirectory(managed);
        File.Copy(supportedAssembly, Path.Combine(managed, "Assembly-CSharp.dll"));
        return directory;
    }

    private static string NewTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "BoplEightInstallerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception exception)
        {
            failures++;
            Console.Error.WriteLine("FAIL " + name + ": " + exception.Message);
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition) throw new Exception(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(message + " Expected: " + expected + ", actual: " + actual + ".");
        }
    }

    private static void AssertContains(string expected, string actual, string message)
    {
        if (actual == null || actual.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new Exception(message + " Actual: " + actual);
        }
    }

    private static void AssertThrows<T>(Action action, string message) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new Exception(message);
    }
}
