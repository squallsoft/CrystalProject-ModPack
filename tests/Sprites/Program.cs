using CrystalProjectModManager.Sprites;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

string repo = Path.GetFullPath(args[0]);
string root = Path.Combine(repo, "artifacts", "sprite-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
var report = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); report.Add("PASS " + name); Console.WriteLine(report[^1]); }
void Refused(Action action, string name) { try { action(); } catch { Check(true, name); return; } throw new Exception("FAIL did not refuse " + name); }
byte[] Png(int width, int height, byte color)
{
    var pixels = new byte[width * height * 4];
    for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = color; pixels[i + 1] = 70; pixels[i + 2] = 150; pixels[i + 3] = 128; }
    var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
}
byte[] Archive(params (string Name, byte[] Png)[] entries)
{
    using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
    writer.Write((byte)0); writer.Write((byte)0); writer.Write(entries.Length);
    writer.Write(Enumerable.Range(0, entries.Length * 7).Select(i => (byte)(i + 1)).ToArray());
    foreach (var entry in entries) { writer.Write(entry.Name.Length); writer.Write(entry.Name.ToCharArray()); writer.Write(entry.Png.Length); writer.Write(entry.Png); }
    return output.ToArray();
}
var png = Png(4, 3, 20); var changed = Png(4, 3, 220);
var source = Archive(("Bat", png), ("Slime", png), ("敵", png));
var entries = EnemySprites.Parse(source);
Check(entries.Count == 3 && entries[2].Name == "敵" && entries.All(e => e.Width == 4 && e.Height == 3), "Archive names, dimensions and Unicode round trip");
Check(EnemySprites.Rebuild(source, new Dictionary<string, byte[]>()).SequenceEqual(source), "No-op rebuild retains exact archive bytes");
var rebuilt = EnemySprites.Rebuild(source, new Dictionary<string, byte[]> { ["Slime"] = changed });
Check(rebuilt.AsSpan(0, 27).SequenceEqual(source.AsSpan(0, 27)) && EnemySprites.Parse(rebuilt)[0].Png.SequenceEqual(png) && EnemySprites.Parse(rebuilt)[1].Png.SequenceEqual(changed), "Changed image replaces only its entry and preserves timestamp table");
Refused(() => EnemySprites.Rebuild(source, new Dictionary<string, byte[]> { ["Slime"] = Png(8, 3, 2) }), "Wrong dimensions refused");
Refused(() => EnemySprites.Rebuild(source, new Dictionary<string, byte[]> { ["Unknown"] = changed }), "Unknown replacements refused");
Refused(() => EnemySprites.Parse(Archive(("../escape", png))), "Archive traversal refused");
Refused(() => EnemySprites.Parse(Archive(("CON", png))), "Reserved Windows names refused");
Refused(() => EnemySprites.Parse(Archive(("Bat", png), ("bat", png))), "Case-insensitive duplicate names refused");
Refused(() => EnemySprites.Parse(source[..^5]), "Truncated archive refused");
Refused(() => EnemySprites.Parse(source.Concat(new byte[] { 1 }).ToArray()), "Trailing archive bytes refused");
Refused(() => EnemySprites.Parse(new byte[] { 1, 0, 0, 0, 0, 0 }), "Unsupported archive version refused");
var corrupt = png.ToArray(); corrupt[30] ^= 1;
Refused(() => EnemySprites.ValidatePng(corrupt), "PNG checksum corruption refused");
Refused(() => EnemySprites.ValidatePng(png[..^12]), "Missing PNG end refused");
Refused(() => EnemySprites.ValidatePng(Encoding.UTF8.GetBytes("not PNG")), "Renamed image format refused");
string game = Path.Combine(root, "game"); string textureDir = Path.Combine(game, "Content", "Textures"); Directory.CreateDirectory(textureDir);
string live = Path.Combine(textureDir, "Monster.dat"); File.WriteAllBytes(live, source);
var service = new EnemySprites(game, Path.Combine(root, "data")); string workspace = Path.Combine(root, "editing");
var progress = new List<int>(); service.ExtractAll(workspace, (done, total) => progress.Add(done));
Check(progress.SequenceEqual(new[] { 1,2,3 }) && Directory.GetFiles(workspace, "*.png").Length == 3 && File.ReadAllBytes(live).SequenceEqual(source), "Extract all reports progress and leaves game untouched");
Refused(() => service.ExtractAll(workspace), "Extraction does not overwrite existing artwork");
Refused(() => service.ExtractAll(Path.Combine(game, "export")), "Extraction into game directory refused");
var unchanged = service.ValidateFolder(workspace); service.Apply(unchanged, () => { });
Check(unchanged.Changes.Count == 0 && !service.HasBackup, "Unedited workspace is a no-op");
File.WriteAllBytes(Path.Combine(workspace, "Bat.png"), changed);
var plan = service.ValidateFolder(workspace);
Check(plan.Changes.Count == 1 && plan.Changes[0].Name == "Bat", "Bulk validation identifies exact changed sprite");
Refused(() => service.Apply(plan, () => throw new IOException("Game running")), "Game-running guard prevents update");
Check(File.ReadAllBytes(live).SequenceEqual(source) && !service.HasBackup, "Refused update leaves game and backup untouched");
service.Apply(plan, () => { });
Check(service.HasBackup && EnemySprites.Parse(File.ReadAllBytes(live))[0].Png.SequenceEqual(changed) && EnemySprites.Parse(File.ReadAllBytes(live))[1].Png.SequenceEqual(png), "Apply updates selected sprite with verified backup");
Refused(() => service.Apply(plan, () => { }), "Stale validated archive snapshot refused");
Check(service.ValidateFolder(workspace).Changes.Count == 0, "Already installed workspace has no pending updates");
string secondExport = Path.Combine(root, "original-export"); service.ExtractAll(secondExport);
Check(File.ReadAllBytes(Path.Combine(secondExport, "Bat.png")).SequenceEqual(png), "Extraction after update exports backed-up originals");
service.Restore(() => { });
Check(File.ReadAllBytes(live).SequenceEqual(source) && File.ReadAllBytes(Path.Combine(workspace, "Bat.png")).SequenceEqual(changed), "Restore returns exact archive and preserves edited PNGs");
string unknown = Path.Combine(workspace, "Unknown.png"); File.WriteAllBytes(unknown, png);
Refused(() => service.ValidateFolder(workspace), "Unknown workspace PNG filenames refused"); File.Delete(unknown);
File.WriteAllBytes(Path.Combine(workspace, "Bat.png"), Png(8, 3, 30));
Refused(() => service.ValidateFolder(workspace), "Workspace dimensions validated before deployment"); File.WriteAllBytes(Path.Combine(workspace, "Bat.png"), changed);
var oversize = png.ToArray(); System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(oversize.AsSpan(16, 4), 8193);
File.WriteAllBytes(Path.Combine(workspace, "Bat.png"), oversize);
File.WriteAllBytes(Path.Combine(workspace, "Slime.png"), Png(8, 3, 30));
using (var large = File.Create(Path.Combine(workspace, "敵.png"))) large.SetLength(32 * 1024 * 1024 + 1);
var problems = service.ScanFolder(workspace);
Check(problems.Plan == null && problems.Issues.Count == 3 && problems.Issues.Any(i => i.Contains("Bat.png") && i.Contains("8193")) && problems.Issues.Any(i => i.Contains("32 MiB")), "Scan collects all file-specific size issues without returning an applicable plan");
Check(File.ReadAllBytes(live).SequenceEqual(source), "Reporting invalid PNGs leaves installed sprites unchanged");
using (var cancelled = new CancellationTokenSource()) { cancelled.Cancel(); Refused(() => service.ScanFolder(workspace, cancellation: cancelled.Token), "Sprite scan supports cancellation"); }
File.WriteAllBytes(Path.Combine(workspace, "Bat.png"), changed); File.WriteAllBytes(Path.Combine(workspace, "Slime.png"), png); File.WriteAllBytes(Path.Combine(workspace, "敵.png"), png);
Check(service.ScanFolder(workspace).Plan?.Changes.Count == 1, "Corrected PNGs can validate successfully after a failed scan");
string manifestPath = Path.Combine(workspace, "sprites.json"); string manifest = File.ReadAllText(manifestPath);
File.WriteAllText(manifestPath, manifest.Replace(EnemySprites.Hash(source), new string('0', 64)));
Refused(() => service.ValidateFolder(workspace), "Workspace from different source archive refused"); File.WriteAllText(manifestPath, manifest);
File.WriteAllBytes(live, rebuilt);
Refused(() => service.Restore(() => { }), "Externally modified sprite archive protected");
Check(File.ReadAllBytes(live).SequenceEqual(rebuilt), "External modification remains untouched"); File.WriteAllBytes(live, source);
string statePath = Path.Combine(service.Store, "state.json");
var state = JsonSerializer.Deserialize<SpriteState>(File.ReadAllText(statePath))!;
File.WriteAllText(statePath, JsonSerializer.Serialize(state with { PendingHash = EnemySprites.Hash(rebuilt) }));
Check(service.Catalog().Count == 3 && JsonSerializer.Deserialize<SpriteState>(File.ReadAllText(statePath))!.PendingHash == null, "Interrupted update before atomic replacement recovers previous state");
File.WriteAllText(statePath, JsonSerializer.Serialize(state with { PendingHash = EnemySprites.Hash(rebuilt) })); File.WriteAllBytes(live, rebuilt);
Check(service.InstalledSprites()[1].Png.SequenceEqual(changed), "Interrupted update after replacement recovers committed archive");
service.Restore(() => { }); Check(File.ReadAllBytes(live).SequenceEqual(source), "Recovered update restores exact backup");
byte[] backup = File.ReadAllBytes(Path.Combine(service.Store, "original.dat")); File.WriteAllBytes(Path.Combine(service.Store, "original.dat"), png);
Refused(() => service.Restore(() => { }), "Corrupt backup never replaces game"); File.WriteAllBytes(Path.Combine(service.Store, "original.dat"), backup);
if (args.Length > 1)
{
    // Optional acceptance uses a private local archive copy. Never changes live Steam files.
    byte[] installedArchive = File.ReadAllBytes(args[1]); var realEntries = EnemySprites.Parse(installedArchive);
    string realGame = Path.Combine(root, "real-archive-game"), realDir = Path.Combine(realGame, "Content", "Textures"); Directory.CreateDirectory(realDir);
    string realLive = Path.Combine(realDir, "Monster.dat"); File.WriteAllBytes(realLive, installedArchive);
    var realService = new EnemySprites(realGame, Path.Combine(root, "real-data")); string realWorkspace = Path.Combine(root, "real-editing"); realService.ExtractAll(realWorkspace);
    Check(Directory.GetFiles(realWorkspace, "*.png").Length == realEntries.Count && realEntries.Count == 273, "Installed 1.6.9 archive exports all 273 PNGs");
    var first = realEntries[0]; File.WriteAllBytes(Path.Combine(realWorkspace, first.Filename), Png(first.Width, first.Height, 45));
    var realPlan = realService.ValidateFolder(realWorkspace); realService.Apply(realPlan, () => { });
    var newEntries = EnemySprites.Parse(File.ReadAllBytes(realLive));
    Check(realPlan.Changes.Count == 1 && newEntries.Skip(1).Zip(realEntries.Skip(1)).All(p => p.First.Png.SequenceEqual(p.Second.Png)), "Real archive replacement preserves every other sprite");
    realService.Restore(() => { }); Check(File.ReadAllBytes(realLive).SequenceEqual(installedArchive), "Real archive restore is byte-identical");
    foreach (var entry in realEntries) File.WriteAllBytes(Path.Combine(realWorkspace, entry.Filename), Png(entry.Width, entry.Height, 67));
    var bulk = realService.ValidateFolder(realWorkspace); realService.Apply(bulk, () => { });
    var allUpdated = EnemySprites.Parse(File.ReadAllBytes(realLive));
    Check(bulk.Changes.Count == 273 && allUpdated.Count == 273 && allUpdated.Zip(realEntries).All(p => p.First.Name == p.Second.Name && p.First.Width == p.Second.Width && p.First.Height == p.Second.Height && !p.First.Png.SequenceEqual(p.Second.Png)), "Bulk update replaces all 273 sprites with names and dimensions intact");
    realService.Restore(() => { }); Check(File.ReadAllBytes(realLive).SequenceEqual(installedArchive), "Full-library update restores byte-identical originals");
    Check(File.ReadAllBytes(args[1]).SequenceEqual(installedArchive), "Live game sprite archive remains untouched");
}
File.WriteAllLines(Path.Combine(root, "checks.txt"), report); Console.WriteLine($"{report.Count} sprite checks passed.");
