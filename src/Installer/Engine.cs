using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace CrystalProjectModInstaller;

internal record Selection(bool Music, bool Home, bool HdSprites = false);
internal record InstallRecord(string GamePath, string GameVersion, string OriginalSha256, string BackupSha256, string InstallerVersion, Selection Mods, string OutputSha256, DateTime Timestamp, Dictionary<string,string>? RuntimeHashes = null);
internal record LegacyFile(string Path, string Hash);
internal record TransactionFile(string Name, bool Existed, string BeforeHash, string AfterHash);
internal record Journal(string GamePath, string Work, TransactionFile[] Files);
internal sealed class Engine
{
    public const string Version = "0.1.0-rc1";
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
    readonly string? migrationBackup;
    public Engine(string game, string? store = null, string? migrationBackup = null)
    {
        this.migrationBackup = migrationBackup;
        Game = Path.GetFullPath(game).TrimEnd(Path.DirectorySeparatorChar);
        Store = store ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrystalProjectModInstaller");
        foreach (var dir in new[] { "backups", "manifests", "logs", "work", "legacy" }) Directory.CreateDirectory(Path.Combine(Store, dir));
    }
    public void Log(string text) => File.AppendAllText(LogPath, DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine);
    public void Idle()
    {
        foreach (var process in Process.GetProcessesByName("Crystal Project"))
        {
            using (process)
            {
                string? running;
                try { running = process.MainModule?.FileName; }
                catch { throw new InvalidOperationException("Close Crystal Project completely before changing files."); }
                if (running == null || string.Equals(Path.GetFullPath(running), Exe, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Crystal Project is currently running. Close the game and try again.");
            }
        }
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
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        string temp = Path.Combine(Path.GetDirectoryName(dest)!, ".cpmi-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            CopyVerified(source, temp);
            if (File.Exists(dest))
            {
                string before = Patches.Hash(dest);
                for (int attempt = 0; ; attempt++)
                {
                    try { File.Replace(temp, dest, null); break; }
                    catch (IOException e) when (attempt < 4 && (e.HResult & 0xffff) is 32 or 33 or 1175 && File.Exists(temp) && File.Exists(dest) && Patches.Hash(dest) == before)
                    { Thread.Sleep(150); }
                }
            }
            else File.Move(temp, dest);
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
        foreach (string candidate in new[] { Exe, migrationBackup ?? "", Path.Combine(Game, "RandomMusic", "Backup", "Crystal Project.exe.1.6.9.original"), Path.Combine(Game, "RandomMusicPrototype", "Backup", "Crystal Project.exe.1.6.9.original") })
            if (File.Exists(candidate) && Patches.Hash(candidate) == Patches.Original) return candidate;
        return null;
    }
    public Dictionary<string, Selection> Supported(string original, string work)
    {
        if (supportedCache != null) return new(supportedCache);
        var result = new Dictionary<string, Selection> { [Patches.Original] = new(false, false) };
        foreach (var s in from hd in new[] { false, true } from music in new[] { false, true } from home in new[] { false, true } where hd || music || home select new Selection(music, home, hd))
        {
            string path = Path.Combine(work, $"candidate-{s.Music}-{s.Home}-{s.HdSprites}.exe");
            Patches.Build(original, path, s.Music, s.Home, s.HdSprites); result[Patches.Hash(path)] = s;
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
        if (Patches.PreviousRelease.TryGetValue(hash, out var previous)) return ("Previous installer release · 1.6.9.0", previous);
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
        using var mutex = new Mutex(false, "Local\\CrystalProjectModInstaller-" + Key);
        bool locked; try { locked = mutex.WaitOne(0); } catch (AbandonedMutexException) { locked = true; }
        if (!locked) throw new IOException("Another installer operation is active.");
        try { RecoverCore(); } finally { mutex.ReleaseMutex(); }
    }
    void RecoverCore()
    {
        Idle(); if (!File.Exists(JournalPath)) return;
        var j = JsonSerializer.Deserialize<Journal>(File.ReadAllText(JournalPath)) ?? throw new IOException("Invalid recovery journal.");
        if (j.GamePath != Game || !Path.GetFullPath(j.Work).StartsWith(Path.Combine(Store, "work") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid recovery location.");
        var allowed = new[] { "Crystal Project.exe", "CrystalProjectRandomMusic.dll", "CrystalProjectHomePoints.dll", "CrystalProjectHDSprites.dll", "manifest.json" };
        foreach (var f in j.Files)
        {
            if (!allowed.Contains(f.Name) && !RuntimeName(f.Name)) throw new IOException("Invalid recovery file.");
            string live = f.Name == "manifest.json" ? Manifest : Path.Combine(Game, f.Name);
            if (f.Name != "manifest.json") SafeTarget(f.Name);
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
    public void Apply(Selection selection, Dictionary<string,string>? runtimeAssets = null, IProgress<string>? progress = null, bool repairManagedFiles = false)
    {
        using var mutex = new Mutex(false, "Local\\CrystalProjectModInstaller-" + Key);
        bool locked; try { locked = mutex.WaitOne(0); } catch (AbandonedMutexException) { locked = true; }
        if (!locked) throw new IOException("Another installer operation is active.");
        try { ApplyCore(selection, runtimeAssets, progress, repairManagedFiles); } catch (Exception e) { Log(e.ToString()); throw; } finally { mutex.ReleaseMutex(); }
    }
    public const string SpriteSizesName = "Mods/CrystalProjectModManager/sprite-sizes.txt";
    static bool RuntimeName(string name) => name is "Mods/CrystalProjectModManager/config.json" or SpriteSizesName || System.Text.RegularExpressions.Regex.IsMatch(name, "^Mods/CrystalProjectModManager/Music/[a-f0-9]{64}\\.ogg$");
    void SafeTarget(string name)
    {
        string target = Path.GetFullPath(Path.Combine(Game, name));
        if (!target.StartsWith(Game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid deployment path.");
        for (string? p = target; p != null && p.Length > Game.Length; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new IOException("A managed deployment path is a link. No linked files will be changed.");
    }
    static bool KnownHelper(string name, string hash) => hash == Convert.ToHexString(SHA256.HashData(Patches.Resource(name))).ToLowerInvariant() || (name == "CrystalProjectRandomMusic.dll" && (hash == LegacyRuntimeHash || hash == "e54d32438d0e266d8559a07816f68989096196c0cafea248a74b0458d7b412d8")) || (name == "CrystalProjectHomePoints.dll" && hash == "997d7be54518622a8047feb6a8c66ff77bbae4da348cab7d88dc7b9909bedbdd");
    void ValidateHdSelection(Selection selection, Dictionary<string, string>? assets, Dictionary<string, string> hashes, bool currentHd)
    {
        SafeTarget(SpriteSizesName); string installed = Path.Combine(Game, SpriteSizesName);
        bool supplied = assets?.TryGetValue(SpriteSizesName, out _) == true;
        if (!selection.HdSprites && supplied) throw new IOException("Enable HD rendering before deploying the sprite size catalog.");
        string? source = supplied ? assets![SpriteSizesName] : File.Exists(installed) ? installed : null;
        if (selection.HdSprites && source == null) throw new IOException("HD rendering needs the original sprite size catalog. Install it from Enemy Sprites.");
        if (!selection.HdSprites && source == null && (currentHd || Record()?.Mods.HdSprites == true)) throw new IOException("Repair the missing HD sprite catalog before removing HD rendering.");
        if (source == null) return;
        if (!supplied && (!hashes.TryGetValue(SpriteSizesName, out var hash) || Patches.Hash(source) != hash)) throw new IOException("The installed HD sprite catalog is unrecognized or changed. Repair it before continuing.");
        if (new FileInfo(source).Length > 1024 * 1024) throw new IOException("Invalid HD sprite catalog size.");
        string[] lines = File.ReadAllLines(source);
        if (lines.Length < 2 || lines[0] != "CrystalProjectHDSprites:1") throw new IOException("Unsupported HD sprite catalog.");
        var sizes = new Dictionary<string, (int Width, int Height)>(StringComparer.Ordinal);
        foreach (string line in lines.Skip(1))
        {
            string[] parts = line.Split('|');
            if (parts.Length != 3 || !parts[0].StartsWith("Monster/", StringComparison.Ordinal) || !int.TryParse(parts[1], out int width) || !int.TryParse(parts[2], out int height) || width <= 0 || height <= 0 || width > 8192 || height > 8192 || !sizes.TryAdd(parts[0], (width, height))) throw new IOException("Invalid HD sprite catalog entry.");
        }
        if (selection.HdSprites) return;
        // Block removal while HD PNGs remain, including through the legacy installer.
        SafeTarget("Content/Textures/Monster.dat");
        using var reader = new BinaryReader(File.OpenRead(Path.Combine(Game, "Content", "Textures", "Monster.dat")), new UTF8Encoding(false, true));
        var stream = reader.BaseStream;
        if (reader.ReadByte() != 0 || reader.ReadByte() != 0) throw new IOException("Cannot verify sprite sizes before removing HD rendering.");
        int count = reader.ReadInt32();
        if (count <= 0 || count > 10000 || 7L * count > stream.Length - stream.Position) throw new IOException("Invalid sprite archive.");
        stream.Position += 7L * count;
        for (int i = 0; i < count; i++)
        {
            int length = reader.ReadInt32(); if (length <= 0 || length > 180) throw new IOException("Invalid sprite name.");
            string name = new(reader.ReadChars(length)); int bytes = reader.ReadInt32(); long start = stream.Position;
            if (bytes < 24 || bytes > stream.Length - start) throw new IOException("Invalid sprite bounds.");
            byte[] header = reader.ReadBytes(24);
            if (!sizes.TryGetValue("Monster/" + name, out var size) || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4)) != size.Width || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4)) != size.Height)
                throw new IOException("Restore Backed-Up Sprites on Enemy Sprites before removing HD rendering or restoring vanilla. HD images still need the rendering patch.");
            stream.Position = start + bytes;
        }
        if (stream.Position != stream.Length) throw new IOException("Invalid sprite archive.");
    }
    void ApplyCore(Selection selection, Dictionary<string,string>? runtimeAssets, IProgress<string>? progress, bool repairManagedFiles)
    {
        progress?.Report("Preparing");
        Idle(); Recover();
        SafeTarget("Crystal Project.exe");
        progress?.Report("Validating Crystal Project");
        if ((File.GetAttributes(Exe) & FileAttributes.ReadOnly) != 0) throw new IOException("Game executable is read-only. No game files have been changed.");
        string current = Patches.Hash(Exe);
        string? original = FindOriginal();
        if (original == null) throw new IOException("No verified original exists. In Steam: Properties → Installed Files → Verify integrity of game files, then rerun this installer.");
        string work = NewWork();
        try
        {
            var supported = Supported(original, work);
            if (!supported.ContainsKey(current) && !Patches.Legacy.Contains(current) && !Patches.PreviousRelease.ContainsKey(current)) throw new IOException("Your Crystal Project version is not yet supported. No game files have been changed.");
            if (!File.Exists(Backup)) CopyVerified(original, Backup);
            var after = Path.Combine(work, "after"); Directory.CreateDirectory(after);
            var output = Path.Combine(after, "Crystal Project.exe");
            Patches.Build(Backup, output, selection.Music, selection.Home, selection.HdSprites);
            var files = new List<TransactionFile>();
            var runtimeHashes = new Dictionary<string,string>(Record()?.RuntimeHashes ?? []);
            ValidateHdSelection(selection, runtimeAssets, runtimeHashes, supported.TryGetValue(current, out var currentMods) && currentMods.HdSprites);
            progress?.Report("Preparing music");
            foreach (var asset in runtimeAssets ?? [])
            {
                if (!RuntimeName(asset.Key)) throw new IOException("Invalid managed music destination.");
                SafeTarget(asset.Key); string target = Path.Combine(Game, asset.Key);
                string hash = Patches.Hash(asset.Value);
                if (asset.Key.EndsWith(".ogg") && Path.GetFileNameWithoutExtension(asset.Key) != hash) throw new IOException("Music asset hash mismatch.");
                if (File.Exists(target))
                {
                    string liveHash = Patches.Hash(target);
                    if (liveHash != hash && (!runtimeHashes.TryGetValue(asset.Key, out var recorded) || (liveHash != recorded && !repairManagedFiles))) throw new IOException("An unrecognized managed file will not be overwritten: " + Path.GetFileName(target));
                }
                string staged = Path.Combine(after, asset.Key); CopyVerified(asset.Value, staged);
                files.Add(Snapshot(asset.Key, target, staged, work)); runtimeHashes[asset.Key] = hash;
                if (asset.Key.EndsWith("config.json")) CopyVerified(staged, Path.Combine(Store, "runtime", hash + ".json"));
                if (asset.Key == SpriteSizesName) CopyVerified(staged, Path.Combine(Store, "runtime", hash + ".txt"));
            }
            if (!selection.HdSprites && runtimeHashes.Remove(SpriteSizesName, out var sizesHash))
            {
                SafeTarget(SpriteSizesName); string sizes = Path.Combine(Game, SpriteSizesName);
                if (File.Exists(sizes))
                {
                    if (Patches.Hash(sizes) != sizesHash) throw new IOException("The HD sprite catalog changed outside the manager.");
                    files.Add(Snapshot(SpriteSizesName, sizes, null, work));
                }
            }
            progress?.Report("Building configuration");
            foreach (var rt in new[] { (selection.Music, "CrystalProjectRandomMusic.dll"), (selection.Home, "CrystalProjectHomePoints.dll"), (selection.HdSprites, "CrystalProjectHDSprites.dll") })
            {
                string target = Path.Combine(Game, rt.Item2);
                SafeTarget(rt.Item2);
                if (!rt.Item1)
                {
                    if (File.Exists(target) && KnownHelper(rt.Item2, Patches.Hash(target))) files.Add(Snapshot(rt.Item2, target, null, work));
                    continue;
                }
                File.WriteAllBytes(Path.Combine(after, rt.Item2), Patches.Resource(rt.Item2));
                if (File.Exists(target) && !KnownHelper(rt.Item2, Patches.Hash(target)))
                    throw new IOException("Unrecognized runtime DLL; it will not be overwritten: " + target);
                files.Add(Snapshot(rt.Item2, target, Path.Combine(after, rt.Item2), work));
            }
            files.Add(Snapshot("Crystal Project.exe", Exe, output, work));
            var record = new InstallRecord(Game, "1.6.9.0", Patches.Original, Patches.Hash(Backup), Version, selection, Patches.Hash(output), DateTime.UtcNow, runtimeHashes);
            File.WriteAllText(Path.Combine(after, "manifest.json"), JsonSerializer.Serialize(record, Json));
            files.Add(Snapshot("manifest.json", Manifest, Path.Combine(after, "manifest.json"), work));
            Idle(); if (Patches.Hash(Exe) != current) throw new IOException("Game changed during patching.");
            AtomicBytes(JournalPath, JsonSerializer.SerializeToUtf8Bytes(new Journal(Game, work, files.ToArray()), Json));
            try
            {
                progress?.Report("Applying game patch");
                foreach (var f in files)
                {
                    string target = f.Name == "manifest.json" ? Manifest : Path.Combine(Game, f.Name);
                    if (f.Name != "manifest.json") SafeTarget(f.Name);
                    if ((File.Exists(target) ? Patches.Hash(target) : "") != f.BeforeHash) throw new IOException("A deployment file changed during installation.");
                    if (f.BeforeHash == f.AfterHash) continue;
                    if (f.AfterHash == "") File.Delete(target); else AtomicCopy(Path.Combine(after, f.Name), target);
                }
                progress?.Report("Verifying installation");
                foreach (var f in files)
                {
                    string target = f.Name == "manifest.json" ? Manifest : Path.Combine(Game, f.Name);
                    if ((File.Exists(target) ? Patches.Hash(target) : "") != f.AfterHash) throw new IOException("Installation verification failed.");
                }
                File.Delete(JournalPath);
                Log("Applied " + JsonSerializer.Serialize(record));
                progress?.Report("Complete");
            }
            catch { Recover(); throw; }
        }
        finally { if (!File.Exists(JournalPath)) Directory.Delete(work, true); }
    }
    public static string LegacyRuntimeHash = "1eceede1152ef86f93d8e9e25047c24d11357b1e639df3c40c69d8e99733d64f";
    static TransactionFile Snapshot(string name, string live, string? after, string work)
    {
        bool exists = File.Exists(live);
        if (exists) CopyVerified(live, Path.Combine(work, "before", name));
        return new(name, exists, exists ? Patches.Hash(live) : "", after == null ? "" : Patches.Hash(after));
    }
    public string CleanLegacy()
    {
        using var mutex = new Mutex(false, "Local\\CrystalProjectModInstaller-" + Key);
        bool locked; try { locked = mutex.WaitOne(0); } catch (AbandonedMutexException) { locked = true; }
        if (!locked) throw new IOException("Another installer operation is active.");
        try { return CleanLegacyCore(); } finally { mutex.ReleaseMutex(); }
    }
    string CleanLegacyCore()
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
            try { SafeTarget(f.Path); } catch (IOException) { report.Add("PRESERVED linked path: " + f.Path); continue; }
            if (!File.Exists(path)) continue;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || Patches.Hash(path) != f.Hash) { report.Add("PRESERVED unknown/changed: " + f.Path); continue; }
            CopyVerified(path, Path.Combine(archive, f.Path)); verified.Add(f); report.Add("ARCHIVED; eligible for removal: " + f.Path);
        }
        File.WriteAllLines(Path.Combine(archive, "cleanup-report.txt"), report);
        foreach (var f in verified)
        {
            Idle(); SafeTarget(f.Path);
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

