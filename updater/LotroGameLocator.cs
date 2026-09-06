using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LotroTurkceYama.Setup;

/// <summary>Finds verified Steam and standalone LOTRO installations.</summary>
public static class LotroGameLocator
{
    private const string SteamAppId = "212500";
    private static readonly string[] MarkerFiles = { "LotroLauncher.exe", "lotroclient.exe", "lotroclient64.exe", "lotroinvoker.exe" };

    public static string FindFirst()
    {
        foreach (string candidate in Discover()) if (IsValid(candidate)) return Path.GetFullPath(candidate);
        return null;
    }

    public static IEnumerable<string> Discover()
    {
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string candidate in RegistryInstallLocations())
            if (Add(seen, candidate)) yield return candidate;

        foreach (string steamRoot in SteamRoots())
            foreach (string candidate in FindSteamGameDirectories(steamRoot))
                if (Add(seen, candidate)) yield return candidate;

        string[] roots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        string[] publishers = { "StandingStoneGames", "Standing Stone Games" };
        foreach (string root in roots)
            foreach (string publisher in publishers)
            {
                string candidate = Path.Combine(root ?? string.Empty, publisher, "The Lord of the Rings Online");
                if (Add(seen, candidate)) yield return candidate;
            }
    }

    public static IEnumerable<string> FindSteamGameDirectories(string steamRoot)
    {
        if (string.IsNullOrWhiteSpace(steamRoot)) yield break;
        HashSet<string> libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(libraries, steamRoot);
        string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (string line in File.ReadLines(vdf))
            {
                Match match = Regex.Match(line, "^\\s*\"path\"\\s*\"(?<path>.*)\"\\s*$", RegexOptions.IgnoreCase);
                if (match.Success) Add(libraries, match.Groups["path"].Value.Replace("\\\\", "\\"));
            }
        }
        foreach (string library in libraries)
        {
            string steamApps = Path.Combine(library, "steamapps");
            string installName = ReadVdfValue(Path.Combine(steamApps, "appmanifest_" + SteamAppId + ".acf"), "installdir");
            if (!string.IsNullOrWhiteSpace(installName)) yield return Path.Combine(steamApps, "common", installName);
            yield return Path.Combine(steamApps, "common", "Lord of the Rings Online");
        }
    }

    public static bool IsValid(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)
                || !File.Exists(Path.Combine(directory, "client_local_English.dat"))) return false;
            foreach (string marker in MarkerFiles) if (File.Exists(Path.Combine(directory, marker))) return true;
        }
        catch { }
        return false;
    }

    private static IEnumerable<string> SteamRoots()
    {
        string[] keys = { @"Software\Valve\Steam", @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" };
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                foreach (string keyName in keys)
                {
                    string value = ReadRegistry(hive, view, keyName, "SteamPath") ?? ReadRegistry(hive, view, keyName, "InstallPath");
                    if (!string.IsNullOrWhiteSpace(value)) yield return value;
                }
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
    }

    private static IEnumerable<string> RegistryInstallLocations()
    {
        string[] keys =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 212500",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 212500",
            @"SOFTWARE\StandingStoneGames\The Lord of the Rings Online",
            @"SOFTWARE\WOW6432Node\StandingStoneGames\The Lord of the Rings Online"
        };
        foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                foreach (string keyName in keys)
                {
                    string value = ReadRegistry(hive, view, keyName, "InstallLocation")
                        ?? ReadRegistry(hive, view, keyName, "InstallPath");
                    if (!string.IsNullOrWhiteSpace(value)) yield return value;
                }
    }

    private static string ReadRegistry(RegistryHive hive, RegistryView view, string keyName, string valueName)
    {
        try
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
            using (RegistryKey key = baseKey.OpenSubKey(keyName, false))
                return key == null ? null : key.GetValue(valueName) as string;
        }
        catch { return null; }
    }

    private static string ReadVdfValue(string path, string key)
    {
        if (!File.Exists(path)) return null;
        Regex pattern = new Regex("^\\s*\"" + Regex.Escape(key) + "\"\\s*\"(?<value>.*)\"\\s*$", RegexOptions.IgnoreCase);
        foreach (string line in File.ReadLines(path))
        {
            Match match = pattern.Match(line);
            if (match.Success) return match.Groups["value"].Value.Replace("\\\\", "\\");
        }
        return null;
    }

    private static bool Add(HashSet<string> values, string path)
    {
        try { return !string.IsNullOrWhiteSpace(path) && values.Add(Path.GetFullPath(path.Trim().Trim('"'))); }
        catch { return false; }
    }
}
