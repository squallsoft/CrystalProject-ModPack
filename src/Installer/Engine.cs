using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace CrystalProjectModInstaller;

internal record Selection(bool Music, bool Home);
internal record InstallRecord(string GamePath, string GameVersion, string OriginalSha256, string BackupSha256, string InstallerVersion, Selection Mods, string OutputSha256, DateTime Timestamp);
internal record LegacyFile(string Path, string Hash);
internal record TransactionFile(string Name, bool Existed, string BeforeHash, string AfterHash);
internal record Journal(string GamePath, string Work, TransactionFile[] Files);
internal sealed class Engine
{
    public const string Version = "0.1.0";
    public string Game { get; }
    public string Exe => Path.Combine(Game, "Crystal Project.exe");
    public string Store { get; }
    public string Backup => Path.Combine(Store, "backups", Patches.Original, "Crystal Project.exe");
    public string Manifest => Path.Combine(Store, "manifests", Key + ".json");
    string Key => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Game.ToUpperInvariant())))[..24];
    string JournalPath => Path.Combine(Store, "manifests", Key + ".pending.json");
    public string LogPath => Path.Combine(Store, "logs", Key + ".log");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    static Dictionary<string, Selection>? supportedCache;
    public Engine(string game, string? store = null)
    {
        Game = Path.GetFullPath(game).TrimEnd(Path.DirectorySeparatorChar);
        Store = store ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrystalProjectModInstaller");
        foreach (var dir in new[] { "backups", "manifests", "logs", "work", "legacy" }) Directory.CreateDirectory(Path.Combine(Store, dir));
    }
    public void Log(string text) => File.AppendAllText(LogPath, DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine);
    public void Idle()
    {
        if (Process.GetProcessesByName("Crystal Project").Length != 0) throw new InvalidOperationException("Close Crystal Project completely before changing files.");
    }
    static void CopyVerified(string source, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(source, dest, true);
        File.SetAttributes(dest, File.GetAttributes(dest) & ~FileAttributes.ReadOnly);
        if (Patches.Hash(source) != Patches.Hash(dest)) throw new IOException("Copy verification failed: " + dest);
    }
    static void AtomicBytes(string path, byte[] bytes)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, bytes); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    static void AtomicCopy(string source, string dest)
    {
        string temp = Path.Combine(Path.GetDirectoryName(dest)!, ".cpmi-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            CopyVerified(source, temp);
            if (File.Exists(dest)) File.Replace(temp, dest, null); else File.Move(temp, dest);
            if (Patches.Hash(source) != Patches.Hash(dest)) throw new IOException("Replacement verification failed.");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public InstallRecord? Record()
    {
        try { return JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(Manifest)); } catch { return null; }
    }
    public string? FindOriginal()
    {
        if (File.Exists(Backup))
        {
            if (Patches.Hash(Backup) != Patches.Original) throw new IOException("The pristine backup is corrupt. It was not overwritten. Restore a verified backup or verify game files in Steam.");
            return Backup;
        }
        foreach (string candidate in new[] { Exe, Path.Combine(Game, "RandomMusic", "Backup", "Crystal Project.exe.1.6.9.original"), Path.Combine(Game, "RandomMusicPrototype", "Backup", "Crystal Project.exe.1.6.9.original") })
            if (File.Exists(candidate) && Patches.Hash(candidate) == Patches.Original) return candidate;
        return null;
    }
    public Dictionary<string, Selection> Supported(string original, string work)
    {
        if (supportedCache != null) return new(supportedCache);
        var result = new Dictionary<string, Selection> { [Patches.Original] = new(false, false) };
        foreach (var s in new[] { new Selection(true, false), new Selection(false, true), new Selection(true, true) })
        {
            string path = Path.Combine(work, $"candidate-{s.Music}-{s.Home}.exe");
            Patches.Build(original, path, s.Music, s.Home); result[Patches.Hash(path)] = s;
        }
        supportedCache = result;
        return new(result);
    }
    public (string Status, Selection? Mods) Inspect()
    {
        if (!File.Exists(Exe)) return ("Game executable not found", null);
        if (File.Exists(JournalPath)) return ("Interrupted operation — use Repair", null);
        string hash = Patches.Hash(Exe);
        if (hash == Patches.Original) return ("Vanilla · 1.6.9.0", new(false, false));
        if (hash == Patches.Legacy[2]) return ("Failed legacy Home Points build — repair recommended", new(true, true));
        if (Patches.Legacy.Contains(hash)) return ("Legacy Random Music", new(true, false));
        string? original = FindOriginal();
        if (original != null)
        {
            string work = NewWork();
            try { if (Supported(original, work).TryGetValue(hash, out var s)) return ("Modified · 1.6.9.0", s); }
            finally { Directory.Delete(work, true); }
        }
        return ("Unsupported / game update detected — no patching allowed", null);
    }
    string NewWork() { string path = Path.Combine(Store, "work", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    public void Recover()
    {
        Idle(); if (!File.Exists(JournalPath)) return;
        var j = JsonSerializer.Deserialize<Journal>(File.ReadAllText(JournalPath)) ?? throw new IOException("Invalid recovery journal.");
        if (j.GamePath != Game || !Path.GetFullPath(j.Work).StartsWith(Path.Combine(Store, "work") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid recovery location.");
        var allowed = new[] { "Crystal Project.exe", "CrystalProjectRandomMusic.dll", "CrystalProjectHomePoints.dll", "manifest.json" };
        foreach (var f in j.Files)
        {
            if (!allowed.Contains(f.Name)) throw new IOException("Invalid recovery file.");
            string live = f.Name == "manifest.json" ? Manifest : Path.Combine(Game, f.Name);
            string current = File.Exists(live) ? Patches.Hash(live) : "";
            if (current != f.BeforeHash && current != f.AfterHash) throw new IOException("Recovery found an externally changed file; leaving it untouched: " + live);
            if (f.Existed && Patches.Hash(Path.Combine(j.Work, "before", f.Name)) != f.BeforeHash) throw new IOException("Recovery backup is corrupt.");
        }
        foreach (var f in j.Files.Reverse())
        {
            string live = f.Name == "manifest.json" ? Manifest : Path.Combine(Game, f.Name);
            if ((File.Exists(live) ? Patches.Hash(live) : "") == f.BeforeHash) continue;
            if (f.Existed) AtomicCopy(Path.Combine(j.Work, "before", f.Name), live);
            else if (File.Exists(live)) File.Delete(live);
        }
        File.Delete(JournalPath); Log("Rolled back interrupted transaction " + j.Work);
    }
    public void Apply(Selection selection)
    {
        using var mutex = new Mutex(false, "Local\\CrystalProjectModInstaller-" + Key);
        bool locked; try { locked = mutex.WaitOne(0); } catch (AbandonedMutexException) { locked = true; }
        if (!locked) throw new IOException("Another installer operation is active.");
        try { ApplyCore(selection); } catch (Exception e) { Log(e.ToString()); throw; } finally { mutex.ReleaseMutex(); }
    }
    void ApplyCore(Selection selection)
    {
        Idle(); Recover();
        if ((File.GetAttributes(Exe) & FileAttributes.ReadOnly) != 0) throw new IOException("Game executable is read-only. No game files have been changed.");
        string current = Patches.Hash(Exe);
        string? original = FindOriginal();
        if (original == null) throw new IOException("No verified original exists. In Steam: Properties → Installed Files → Verify integrity of game files, then rerun this installer.");
        string work = NewWork();
        try
        {
            var supported = Supported(original, work);
            if (!supported.ContainsKey(current) && !Patches.Legacy.Contains(current)) throw new IOException("Your Crystal Project version is not yet supported. No game files have been changed.");
            if (!File.Exists(Backup)) CopyVerified(original, Backup);
            var after = Path.Combine(work, "after"); Directory.CreateDirectory(after);
            var output = Path.Combine(after, "Crystal Project.exe");
            Patches.Build(Backup, output, selection.Music, selection.Home);
            var files = new List<TransactionFile>();
            foreach (var rt in new[] { (selection.Music, "CrystalProjectRandomMusic.dll"), (selection.Home, "CrystalProjectHomePoints.dll") })
            {
                string target = Path.Combine(Game, rt.Item2);
                if (!rt.Item1) continue; // unused helpers are removed below only with verified provenance
                File.WriteAllBytes(Path.Combine(after, rt.Item2), Patches.Resource(rt.Item2));
                if (File.Exists(target) && Patches.Hash(target) != Patches.Hash(Path.Combine(after, rt.Item2)) && !(rt.Item2 == "CrystalProjectRandomMusic.dll" && Patches.Hash(target) == LegacyRuntimeHash))
                    throw new IOException("Unrecognized runtime DLL; it will not be overwritten: " + target);
                files.Add(Snapshot(rt.Item2, target, Path.Combine(after, rt.Item2), work));
            }
            files.Add(Snapshot("Crystal Project.exe", Exe, output, work));
            var record = new InstallRecord(Game, "1.6.9.0", Patches.Original, Patches.Hash(Backup), Version, selection, Patches.Hash(output), DateTime.UtcNow);
            File.WriteAllText(Path.Combine(after, "manifest.json"), JsonSerializer.Serialize(record, Json));
            files.Add(Snapshot("manifest.json", Manifest, Path.Combine(after, "manifest.json"), work));
            Idle(); if (Patches.Hash(Exe) != current) throw new IOException("Game changed during patching.");
            AtomicBytes(JournalPath, JsonSerializer.SerializeToUtf8Bytes(new Journal(Game, work, files.ToArray()), Json));
            try
            {
                foreach (var f in files) AtomicCopy(Path.Combine(after, f.Name), f.Name == "manifest.json" ? Manifest : Path.Combine(Game, f.Name));
                File.Delete(JournalPath);
                Log("Applied " + JsonSerializer.Serialize(record));
            }
            catch { Recover(); throw; }
            foreach (var rt in new[] { (selection.Music, "CrystalProjectRandomMusic.dll"), (selection.Home, "CrystalProjectHomePoints.dll") })
            {
                string target = Path.Combine(Game, rt.Item2);
                if (!rt.Item1 && File.Exists(target))
                {
                    string hash = Patches.Hash(target);
                    string own = Convert.ToHexString(SHA256.HashData(Patches.Resource(rt.Item2))).ToLowerInvariant();
                    if (hash == own || (rt.Item2 == "CrystalProjectRandomMusic.dll" && hash == LegacyRuntimeHash)) File.Delete(target);
                }
            }
        }
        finally { if (!File.Exists(JournalPath)) Directory.Delete(work, true); }
    }
    public static string LegacyRuntimeHash = "1eceede1152ef86f93d8e9e25047c24d11357b1e639df3c40c69d8e99733d64f";
    static TransactionFile Snapshot(string name, string live, string after, string work)
    {
        bool exists = File.Exists(live);
        if (exists) CopyVerified(live, Path.Combine(work, "before", name));
        return new(name, exists, exists ? Patches.Hash(live) : "", Patches.Hash(after));
    }
    public string CleanLegacy()
    {
        Idle();
        if (File.Exists(JournalPath)) throw new IOException("Repair the interrupted transaction before cleanup.");
        var state = Inspect(); if (state.Mods == null || state.Status.StartsWith("Failed")) throw new IOException("Apply or restore a supported configuration before cleanup.");
        var source = FindOriginal() ?? throw new IOException("Cleanup requires a verified pristine backup.");
        if (!File.Exists(Backup)) CopyVerified(source, Backup);
        var allow = JsonSerializer.Deserialize<LegacyFile[]>(Patches.Resource("cleanup.json"))!;
        string archive = Path.Combine(Store, "legacy", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(archive);
        var verified = new List<LegacyFile>(); var report = new List<string>();
        foreach (var f in allow)
        {
            string path = Path.GetFullPath(Path.Combine(Game, f.Path));
            if (!path.StartsWith(Game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid cleanup path.");
            if (!File.Exists(path)) continue;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || Patches.Hash(path) != f.Hash) { report.Add("PRESERVED unknown/changed: " + f.Path); continue; }
            CopyVerified(path, Path.Combine(archive, f.Path)); verified.Add(f); report.Add("ARCHIVED; eligible for removal: " + f.Path);
        }
        File.WriteAllLines(Path.Combine(archive, "cleanup-report.txt"), report);
        foreach (var f in verified)
        {
            string path = Path.Combine(Game, f.Path);
            if (Patches.Hash(path) != f.Hash) throw new IOException("Cleanup source changed: " + path);
            File.Delete(path);
            if (File.Exists(path)) throw new IOException("Cleanup failed: " + path);
        }
        foreach (string dir in verified.Select(f => Path.GetDirectoryName(Path.Combine(Game, f.Path))!).Where(d => d != Game).Distinct().OrderByDescending(d => d.Length))
        {
            string? d = dir;
            while (d != null && d.StartsWith(Game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) { Directory.Delete(d); d = Path.GetDirectoryName(d); }
        }
        Log("Archived and removed " + verified.Count + " legacy files to " + archive);
        return archive;
    }
}

