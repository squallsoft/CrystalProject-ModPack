using CrystalProjectModManager.Core;
using CrystalProjectModManager;
using CrystalProjectModInstaller;
using System.Text.Json;
using NAudio.Wave;

string repo = Path.GetFullPath(args[0]);
string root = Path.Combine(repo, "artifacts", "manager-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
var report = new List<string>();
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); report.Add("PASS " + name); Console.WriteLine(report[^1]); }
void Refused(Action action, string name) { try { action(); } catch { Check(true, name); return; } throw new Exception("FAIL did not refuse " + name); }
var library = new MusicLibrary(Path.Combine(root, "data")); var config = new Configuration();
Check(MusicLibrary.Cues.Count == 78 && MusicLibrary.Cues.Count(c => !c.IsAmbience) == 71, "Enum/config catalog coverage: 71 music + 7 ambience");
string a = Path.Combine(repo, "artifacts", "fixtures", "sine-a.ogg"), b = Path.Combine(repo, "artifacts", "fixtures", "sine-b.ogg");
string unicode = Path.Combine(root, "音楽 — " + new string('x', 120) + ".ogg"); File.Copy(a, unicode);
string sourceHash = MusicLibrary.Hash(a); var first = library.Import(unicode); var same = library.Import(a); var second = library.Import(b);
Check(first.Id == same.Id && Directory.GetFiles(Path.Combine(library.Store, "Library"), "*.ogg").Length == 2, "Content deduplication; Unicode and long source filenames");
Check(MusicLibrary.Hash(a) == sourceHash && first.Duration > 1.9 && first.Duration < 2.1, "Source untouched; decoded duration");
config.Library[first.Id] = first; config.Library[second.Id] = second;
string cue = MusicLibrary.Cues.Single(c => c.GameValue == 32).Id;
config.MusicPools[cue] = new() { Tracks = [first.Id, second.Id] }; config.UnlimitedHomePoints = true;
library.Save(config); var loaded = library.Load(); Check(loaded.MusicPools[cue].Tracks.SequenceEqual(config.MusicPools[cue].Tracks) && loaded.UnlimitedHomePoints, "Versioned configuration round trip");
string export = Path.Combine(root, "export.json"); MusicLibrary.Export(config, export);
Check(!File.ReadAllText(export).Contains(Path.GetDirectoryName(a)!) && MusicLibrary.Parse(File.ReadAllText(export)).Library.Values.All(t => t.ImportedFrom == null), "Portable export excludes source paths and audio bytes");
Refused(() => MusicLibrary.Parse("{\"schemaVersion\":42}"), "Unknown schema refused");
Refused(() => library.TrackPath("../escape"), "Library traversal refused");
var duplicate = MusicLibrary.Parse(File.ReadAllText(export)); duplicate.MusicPools[cue].Tracks.Add(first.Id); Refused(() => MusicLibrary.Validate(duplicate), "Duplicate membership refused");
string invalid = Path.Combine(root, "bad.ogg"); File.WriteAllText(invalid, "not Vorbis"); Refused(() => library.Import(invalid), "Invalid Vorbis refused");
string unsupported = Path.Combine(root, "bad.wav"); File.WriteAllText(unsupported, "not audio"); Refused(() => library.Import(unsupported), "Unsupported format refused");
string managed = library.TrackPath(first.Id); File.Delete(managed); Refused(() => library.PrepareRuntime(config, Path.Combine(root, "missing")), "Missing audio blocks deployment");
library.Locate(first, a); Check(MusicLibrary.Hash(managed) == first.Id, "Locate restores managed copy"); Refused(() => library.Locate(first, b), "Locate rejects wrong file");
string stage = Path.Combine(root, "stage"); var assets = library.PrepareRuntime(config, stage);
Check(assets.Count == 3 && assets.Keys.All(k => k.StartsWith("Mods/CrystalProjectModManager/")), "Only assigned deduplicated runtime assets deployed");
var disabled = MusicLibrary.Parse(File.ReadAllText(export)); disabled.MusicPools[cue].Enabled = false;
Check(library.PrepareRuntime(disabled, Path.Combine(root, "disabled")).Count == 1, "Disabled pool deploys vanilla mapping only");
string game = Path.Combine(root, "game"); Directory.CreateDirectory(game); File.Copy(Path.Combine(repo, "artifacts", "baseline", "Crystal Project.exe"), Path.Combine(game, "Crystal Project.exe"));
var engine = new Engine(game, Path.Combine(root, "store")); engine.Apply(new(true, true), assets);
Check(assets.All(pair => MusicLibrary.Hash(Path.Combine(game, pair.Key)) == MusicLibrary.Hash(pair.Value)), "Transactional executable, config, and music installation");
var last = engine.Record()!; string configName = "Mods/CrystalProjectModManager/config.json";
Check(last.RuntimeHashes!.Count == 3 && File.Exists(Path.Combine(engine.Store, "runtime", last.RuntimeHashes[configName] + ".json")), "External deployed-config recovery copy");
string runtimeConfig = Path.Combine(game, configName); File.WriteAllText(runtimeConfig, "changed");
Refused(() => engine.Apply(new(true, true), assets), "Changed managed config protected during ordinary apply");
engine.Apply(new(true, true), assets, repairManagedFiles: true); Check(MusicLibrary.Hash(runtimeConfig) == last.RuntimeHashes[configName], "Explicit repair restores verified deployed configuration");
string liveHash = Patches.Hash(engine.Exe); Refused(() => engine.Apply(new(true, true), new() { ["../bad.json"] = export }), "Deployment traversal refused"); Check(Patches.Hash(engine.Exe) == liveHash, "Refusal leaves executable intact");
// Recovery of nested assets and EXE after an interrupted commit.
string recovery = Path.Combine(engine.Store, "work", "nested-recovery"); Directory.CreateDirectory(Path.Combine(recovery, "before", "Mods", "CrystalProjectModManager")); File.Copy(runtimeConfig, Path.Combine(recovery, "before", configName));
string before = MusicLibrary.Hash(runtimeConfig); File.WriteAllText(runtimeConfig, "partial"); string after = MusicLibrary.Hash(runtimeConfig);
string pending = engine.Manifest.Replace(".json", ".pending.json"); File.WriteAllText(pending, JsonSerializer.Serialize(new Journal(game, recovery, [new(configName, true, before, after)])));
engine.Recover(); Check(MusicLibrary.Hash(runtimeConfig) == before && !File.Exists(pending), "Interrupted nested music config rolls back");
using (var player = new PreviewPlayer())
{
    player.Volume = 0; player.Open(managed); player.Play(); Thread.Sleep(120); Check(player.State == PlaybackState.Playing, "Preview play");
    player.Pause(); Check(player.State == PlaybackState.Paused, "Preview pause"); player.Play(); Check(player.State == PlaybackState.Playing, "Preview resume");
    player.Seek(TimeSpan.FromSeconds(1)); player.Pause(); Check(player.Position.TotalSeconds >= 0.9, "Preview seek"); player.Stop(); Check(player.State == PlaybackState.Stopped && player.Position.TotalSeconds == 0, "Preview stop resets position");
    player.Volume = 0.2f; Check(Math.Abs(player.Volume - 0.2f) < 0.001, "Preview volume"); player.Open(library.TrackPath(second.Id)); Check(player.Duration.TotalSeconds > 2.9, "Preview next track decoder"); player.Open(managed); Check(player.Duration.TotalSeconds < 2.1, "Preview previous track decoder");
    player.Volume = 0; player.Seek(player.Duration - TimeSpan.FromMilliseconds(10)); Check(player.Position > player.Duration - TimeSpan.FromMilliseconds(30), "Preview seek near final OGG page");
    player.Seek(player.Duration); player.Play(); player.Pause(); Check(player.Position.TotalSeconds < 0.5, "Preview replay restarts after end of file");
    Refused(() => player.Open(invalid), "Invalid preview file handled"); Refused(() => player.Open(Path.Combine(root, "absent.ogg")), "Missing preview file handled");
}
Check(Patches.Hash(engine.Exe) == liveHash && MusicLibrary.Hash(runtimeConfig) == before, "Preview leaves deployed game untouched");
engine.Apply(new(false, true)); Check(engine.Inspect().Mods == new Selection(false, true), "Music off retains Home Points");
engine.Apply(new(false, false)); Check(Patches.Hash(engine.Exe) == Patches.Original && File.Exists(managed), "Restore exact vanilla; retain imported library");
File.WriteAllLines(Path.Combine(repo, "artifacts", "manager-tests.txt"), report);
Console.WriteLine(report.Count + " manager checks passed.");
