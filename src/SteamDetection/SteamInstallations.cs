using Microsoft.Win32;
using System.IO;
using System.Text.RegularExpressions;

namespace CrystalProjectModManager;
internal static class SteamInstallations
{
    public static IEnumerable<string> Detect()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in new[] { Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null), Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam") })
            if (value is string path && Directory.Exists(path)) roots.Add(path);
        foreach (var root in roots.ToArray())
        {
            string file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(file)) continue;
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "\"path\"\\s+\"([^\"]+)\"")) roots.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
        }
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            foreach (string name in new[] { "SteamLibrary", "Steam", @"Games\Steam" }) roots.Add(Path.Combine(drive.RootDirectory.FullName, name));
        foreach (string root in roots)
        {
            string game = Path.Combine(root, "steamapps", "common", "Crystal Project");
            string manifest = Path.Combine(root, "steamapps", "appmanifest_1637730.acf");
            if (File.Exists(manifest))
            {
                var match = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                if (match.Success) game = Path.Combine(root, "steamapps", "common", match.Groups[1].Value);
            }
            if (File.Exists(Path.Combine(game, "Crystal Project.exe"))) yield return game;
        }
    }
}
