using System;
using System.IO;
using System.Security.Cryptography;
using BoplEight.Installer;

internal static class PayloadIntegrationTests
{
    private static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: PayloadIntegrationTests <payload.zip> <Assembly-CSharp.dll> <BoplEight.dll>");
            return 2;
        }

        string temporaryRoot = Path.Combine(Path.GetTempPath(), "BoplEightPayloadTest", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temporaryRoot);
            File.WriteAllText(Path.Combine(temporaryRoot, "BoplBattle.exe"), "test executable");
            string managed = Path.Combine(temporaryRoot, "BoplBattle_Data", "Managed");
            Directory.CreateDirectory(managed);
            File.Copy(args[1], Path.Combine(managed, "Assembly-CSharp.dll"));

            using (var payload = File.OpenRead(args[0]))
            {
                InstallerCore.Install(temporaryRoot, payload);
            }

            Require(File.Exists(Path.Combine(temporaryRoot, ".doorstop_version")), "Unity Doorstop version file was not installed.");
            Require(File.Exists(Path.Combine(temporaryRoot, "doorstop_config.ini")), "Unity Doorstop configuration was not installed.");
            Require(File.Exists(Path.Combine(temporaryRoot, "winhttp.dll")), "Unity Doorstop loader was not installed.");
            Require(File.Exists(Path.Combine(temporaryRoot, "BepInEx", "core", "BepInEx.dll")), "BepInEx core was not installed.");

            string installedPlugin = Path.Combine(temporaryRoot, InstallerCore.PluginRelativePath);
            Require(File.Exists(installedPlugin), "BoplEight.dll was not installed.");
            Require(
                string.Equals(Hash(installedPlugin), Hash(args[2]), StringComparison.OrdinalIgnoreCase),
                "The installed BoplEight.dll did not match the release build.");

            InstallerCore.Uninstall(temporaryRoot);
            Require(!File.Exists(installedPlugin), "BoplEight.dll remained after uninstall.");
            Require(File.Exists(Path.Combine(temporaryRoot, "BepInEx", "core", "BepInEx.dll")), "Uninstall removed shared BepInEx files.");

            Console.WriteLine("PASS Packaged payload installs, matches, and uninstalls in isolation");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL Packaged payload integration: " + exception);
            return 1;
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, true);
            }
        }
    }

    private static string Hash(string path)
    {
        using (var stream = File.OpenRead(path))
        using (SHA256 algorithm = SHA256.Create())
        {
            return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
