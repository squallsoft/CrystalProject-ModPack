using CrystalProjectModInstaller;
using System.Text.Json;

if (args.Length > 0 && args[0] == "--hold") { Thread.Sleep(30000); return; }
string root = Path.GetFullPath(args[0]);
string original = Path.Combine(root, "artifacts", "baseline", "Crystal Project.exe");
string test = Path.Combine(root, "artifacts", "test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(test);
var results = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); results.Add("PASS: " + name); Console.WriteLine(results[^1]); }
void Refused(Action action, string name) { try { action(); } catch (Exception e) { results.Add("PASS: " + name + " (" + e.Message + ")"); Console.WriteLine(results[^1]); return; } throw new Exception("FAIL: did not refuse " + name); }
string game = Path.Combine(test, "game"); Directory.CreateDirectory(game);
File.Copy(original, Path.Combine(game, "Crystal Project.exe"));
var engine = new Engine(game, Path.Combine(test, "store"));
if (args.Length > 1 && args[1] == "deploy")
{
    var live = new Engine(args[2]); live.Apply(new(true, true)); Console.WriteLine(live.CleanLegacy()); return;
}
if (args.Length > 1 && args[1] == "failure-extra")
{
    string host = Path.Combine(AppContext.BaseDirectory, "Crystal Project.exe");
    File.Copy(Path.Combine(AppContext.BaseDirectory, "Tests.exe"), host, true);
    using (var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(host, "--hold") { UseShellExecute = false, CreateNoWindow = true })!)
    {
        try { Refused(() => engine.Apply(new(true, true)), "Running game process"); Check(Patches.Hash(engine.Exe) == Patches.Original, "Running-process refusal leaves EXE intact"); }
        finally { child.Kill(); child.WaitForExit(); }
    }
    File.Delete(host);
    File.SetAttributes(engine.Exe, FileAttributes.ReadOnly);
    try { Refused(() => engine.Apply(new(true, true)), "Read-only executable"); Check(Patches.Hash(engine.Exe) == Patches.Original, "Read-only failure leaves EXE intact"); }
    finally { File.SetAttributes(engine.Exe, FileAttributes.Normal); engine.Recover(); }
    File.WriteAllLines(Path.Combine(root, "artifacts", "failure-extra-tests.txt"), results); return;
}
var configs = new[] { new Selection(false, false), new Selection(true, false), new Selection(false, true), new Selection(true, true) };
var hashes = new Dictionary<Selection, string>();
foreach (var c in configs)
{
    string output = Path.Combine(test, $"build-{c.Music}-{c.Home}.exe");
    Patches.Build(original, output, c.Music, c.Home); hashes[c] = Patches.Hash(output);
    Patches.Verify(original, output, c.Music, c.Home);
    Check(true, "Build/reload/unchanged methods and ALL constants: " + c);
}
foreach (var start in configs) foreach (var end in configs)
{
    engine.Apply(start); engine.Apply(end);
    Check(Patches.Hash(engine.Exe) == hashes[end] && engine.Record()?.Mods == end, "Transition " + start + " -> " + end);
}
Check(Patches.Hash(engine.Backup) == Patches.Original, "External backup hash");
File.AppendAllText(engine.Exe, "unknown"); string unknown = Patches.Hash(engine.Exe);
Refused(() => engine.Apply(new(true, true)), "Unsupported hash"); Check(Patches.Hash(engine.Exe) == unknown, "Unsupported EXE untouched");
File.Copy(original, engine.Exe, true);
File.AppendAllText(engine.Backup, "corrupt"); Refused(() => engine.Apply(new(true, true)), "Corrupt canonical backup"); Check(Patches.Hash(engine.Exe) == Patches.Original, "Corrupt-backup failure preserved live EXE");
File.Copy(original, engine.Backup, true);
using (var held = new FileStream(engine.Exe, FileMode.Open, FileAccess.Read, FileShare.None)) Refused(() => engine.Apply(new(true, true)), "Exclusive-lock/file access failure");
Check(Patches.Hash(engine.Exe) == Patches.Original, "File-access failure preserved live EXE");
string orphan = Path.Combine(engine.Store, "work", "interrupted-temp"); Directory.CreateDirectory(orphan); File.WriteAllText(Path.Combine(orphan, "partial.exe"), "partial");
engine.Apply(new(true, true)); Check(Patches.Hash(engine.Exe) == hashes[new(true, true)], "Interrupted pre-commit temp is ignored");
// Construct a durable transaction interrupted immediately after replacing the EXE.
string recovery = Path.Combine(engine.Store, "work", "recovery-fixture"); Directory.CreateDirectory(Path.Combine(recovery, "before"));
File.Copy(engine.Exe, Path.Combine(recovery, "before", "Crystal Project.exe")); string beforeHash = Patches.Hash(engine.Exe);
File.Copy(original, engine.Exe, true);
string pending = Directory.GetFiles(Path.Combine(engine.Store, "manifests"), "*.json").Single().Replace(".json", ".pending.json");
File.WriteAllText(pending, JsonSerializer.Serialize(new Journal(game, recovery, [new("Crystal Project.exe", true, beforeHash, Patches.Original)])));
engine.Recover(); Check(Patches.Hash(engine.Exe) == beforeHash && !File.Exists(pending), "Interrupted commit rollback");
string missingGame = Path.Combine(test, "missing"); Directory.CreateDirectory(missingGame); File.Copy(engine.Exe, Path.Combine(missingGame, "Crystal Project.exe"));
Refused(() => new Engine(missingGame, Path.Combine(test, "missing-store")).Apply(new(false, false)), "Missing original backup");
// Cleanup removes only an exact path+hash match; changed allowlisted files stay.
var entry = JsonSerializer.Deserialize<LegacyFile[]>(Patches.Resource("cleanup.json"))!.First(f => f.Path == "src\\Patcher.cs");
Directory.CreateDirectory(Path.Combine(game, "src")); File.Copy(Path.Combine(root, "src", "Mods", "RandomMusic", "LegacyPatcher.cs"), Path.Combine(game, entry.Path));
File.WriteAllText(Path.Combine(game, "unknown.bak"), "keep"); File.WriteAllText(Path.Combine(game, "README.txt"), "user file");
string archive = engine.CleanLegacy();
Check(!File.Exists(Path.Combine(game, entry.Path)) && File.Exists(Path.Combine(archive, entry.Path)), "Known cleanup artifact archived then removed");
Check(File.Exists(Path.Combine(game, "unknown.bak")) && File.Exists(Path.Combine(game, "README.txt")), "Unknown and changed artifacts preserved");
File.WriteAllLines(Path.Combine(root, "artifacts", "installer-tests.txt"), results);
File.WriteAllText(Path.Combine(root, "artifacts", "test-location.txt"), test);
File.WriteAllText(Path.Combine(root, "docs", "output-hashes.json"), JsonSerializer.Serialize(hashes.Select(p => new { p.Key.Music, p.Key.Home, Sha256 = p.Value }), new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Tests finished: " + test);
