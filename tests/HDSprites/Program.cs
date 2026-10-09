using CrystalProjectModManager.Sprites;
using CrystalProjectModInstaller;
using Mono.Cecil;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

string repo = Path.GetFullPath(args[0]);
string root = Path.Combine(repo, "artifacts", "hd-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
var checks = new List<string>();
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); checks.Add("PASS " + name); Console.WriteLine(checks[^1]); }
void Refused(Action action, string name) { try { action(); } catch { Check(true, name); return; } throw new Exception("FAIL did not refuse " + name); }
string originalExe = Path.Combine(repo, "artifacts", "baseline", "Crystal Project.exe");
foreach (bool hd in new[] { false, true }) foreach (bool music in new[] { false, true }) foreach (bool home in new[] { false, true })
{
    string output = Path.Combine(root, $"build-{music}-{home}-{hd}.exe"); Patches.Build(originalExe, output, music, home, hd); Patches.Verify(originalExe, output, music, home, hd);
    Check(true, $"Verified executable composition: music={music}, home={home}, HD={hd}");
}
using (var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(root, "build-True-True-True.exe")))
{
    var methods = assembly.MainModule.Types.SelectMany(t => t.Methods).Where(m => m.HasBody).ToArray();
    int Calls(string type, string method, string hook) => methods.Single(m => m.DeclaringType.FullName == type && m.Name == method).Body.Instructions.Count(i => i.Operand is MethodReference r && r.DeclaringType.FullName == "CrystalProjectHDSprites.Runtime" && r.Name == hook);
    Check(Calls("Sang.Battle.CBattle", "GetMonsterTexture", "Register") == 3, "Original and alternate texture paths register the actual selected image");
    Check(Calls("Sang.Battle.BattlerMonster", "SetMonsterTexture", "LogicalWidth") == 1 && Calls("Sang.Battle.BattlerMonster", "RefreshScreenPos", "LogicalHeight") == 1, "Battle sizing and indicators use logical dimensions");
    Check(Calls("Sang.Window.Field.Atlas.WindowMonsterDetails", "RefreshContent", "RenderScale") == 1, "Atlas applies texture-density compensation");
    var portrait = methods.Single(m => m.DeclaringType.FullName == "Sang.Window.WindowHelper" && m.Name == "DrawMonsterPortrait" && m.Parameters.Count == 6);
    Check(portrait.Body.Instructions.Count(i => i.Operand is MethodReference r && r.Name == "ScaleRegion") == 2, "Portrait and shadow source rectangles scale to HD pixel coordinates");
}
byte[] Png(int width, int height)
{
    var pixels = new byte[width * height * 4]; for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 30; pixels[i + 1] = 140; pixels[i + 2] = 200; pixels[i + 3] = 180; }
    var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
}
string game = Path.Combine(root, "game"); string textures = Path.Combine(game, "Content", "Textures"); Directory.CreateDirectory(textures);
File.Copy(originalExe, Path.Combine(game, "Crystal Project.exe"));
byte[] original = File.ReadAllBytes(args[1]); string live = Path.Combine(textures, "Monster.dat"); File.WriteAllBytes(live, original);
var service = new EnemySprites(game, Path.Combine(root, "data")); var engine = new Engine(game, Path.Combine(root, "data"));
var entries = service.Catalog(); string workspace = service.ExtractAll(Path.Combine(root, "editing"));
File.WriteAllBytes(Path.Combine(workspace, entries[0].Filename), Png(entries[0].Width * 2, entries[0].Height * 2));
File.WriteAllBytes(Path.Combine(workspace, entries[1].Filename), Png(entries[1].Width * 4, entries[1].Height * 4));
var plan = service.ValidateFolder(workspace);
Check(plan.Changes.Count == 2 && plan.HdSprites == 2 && plan.Changes.Select(c => c.Scale).Order().SequenceEqual(new[] { 2d,4d }), "Mixed original, 2× and 4× workspace validates");
Check(EnemySprites.Scale(30, 60, 10, 20) == 3 && EnemySprites.Scale(35, 70, 10, 20) == 3.5, "Integer and fractional enlargement factors accepted");
Check(Math.Abs(EnemySprites.Scale(368, 501, 105, 143) - 3.5) < 0.01, "Fractional Sentry resize tolerates whole-pixel rounding");
Refused(() => EnemySprites.Scale(101, 200, 10, 20), "Dimensions exceeding 10× are refused");
Refused(() => EnemySprites.Scale(5, 10, 10, 20), "Images smaller than the original are refused");
Refused(() => EnemySprites.Scale(20, 80, 10, 20), "Nonuniform enlargement refused");
Refused(() => service.Apply(plan, engine.Idle), "HD images cannot deploy without a rendering-patch callback");
Check(File.ReadAllBytes(live).SequenceEqual(original), "Missing renderer refusal leaves original archive untouched");
Refused(() => service.Apply(plan, engine.Idle, _ => throw new IOException("Unsupported game")), "Failed renderer installation blocks HD image deployment");
Check(!service.HasBackup && File.ReadAllBytes(live).SequenceEqual(original), "Failed renderer preflight leaves sprites and backup untouched");
Refused(() => engine.Apply(new(false, false, true)), "HD executable cannot install without original-size metadata");
engine.Apply(new(true, true));
if (args.Length > 2)
{
    foreach (string helper in new[] { "CrystalProjectRandomMusic.dll", "CrystalProjectHomePoints.dll" })
        File.Copy(Path.Combine(args[2], helper), Path.Combine(game, helper), true);
    string previousHd = Path.Combine(args[2], "CrystalProjectHDSprites.dll");
    if (File.Exists(previousHd)) File.Copy(previousHd, Path.Combine(game, "CrystalProjectHDSprites.dll"), true);
    // Older releases had no helper ownership hashes in their manifests.
    File.WriteAllText(engine.Manifest, System.Text.Json.JsonSerializer.Serialize(engine.Record()! with { HelperHashes = null }));
}
void Install(byte[] sizes)
{
    string path = Path.Combine(root, "sprite-sizes.txt"); File.WriteAllBytes(path, sizes);
    var mods = engine.Inspect().Mods!; engine.Apply(mods with { HdSprites = true }, new() { [Engine.SpriteSizesName] = path });
}
service.Apply(plan, engine.Idle, Install);
Check(engine.Inspect().Mods == new Selection(true, true, true) && File.Exists(Path.Combine(game, "CrystalProjectHDSprites.dll")), "HD apply composes renderer with installed music and Home Points");
Check(engine.Record()!.HelperHashes!.Count == 3 && engine.Record()!.HelperHashes!.All(h => h.Value == Patches.Hash(Path.Combine(game, h.Key))), "Migration records exact helper ownership hashes for subsequent app upgrades");
foreach (string helper in new[] { "CrystalProjectRandomMusic.dll", "CrystalProjectHomePoints.dll", "CrystalProjectHDSprites.dll" })
{
    string path = Path.Combine(game, helper); byte[] saved = File.ReadAllBytes(path);
    File.AppendAllText(path, "outside edit"); string changed = Patches.Hash(path), exeHash = Patches.Hash(engine.Exe), spriteHash = Patches.Hash(live), manifestHash = Patches.Hash(engine.Manifest);
    try
    {
        Refused(() => Install(plan.SizeCatalog), "Unrecognized or modified helper still refuses overwrite: " + helper);
        Check(Patches.Hash(path) == changed && Patches.Hash(engine.Exe) == exeHash && Patches.Hash(live) == spriteHash && Patches.Hash(engine.Manifest) == manifestHash, "Rejected helper upgrade leaves all deployed files unchanged: " + helper);
    }
    finally { File.WriteAllBytes(path, saved); }
}
// Model a helper deployed by another build and recorded by the manager.
string musicHelper = Path.Combine(game, "CrystalProjectRandomMusic.dll");
File.AppendAllText(musicHelper, "previous build fixture");
var previousRecord = engine.Record()!; previousRecord.HelperHashes!["CrystalProjectRandomMusic.dll"] = Patches.Hash(musicHelper);
File.WriteAllText(engine.Manifest, System.Text.Json.JsonSerializer.Serialize(previousRecord));
Install(plan.SizeCatalog);
Check(File.ReadAllBytes(musicHelper).SequenceEqual(Patches.Resource("CrystalProjectRandomMusic.dll")), "Manager-owned previous helper build upgrades using its recorded hash");
Check(service.InstalledHdCount() == 2 && service.InstalledSprites().Skip(2).Zip(entries.Skip(2)).All(p => p.First.Png.SequenceEqual(p.Second.Png)), "HD deployment preserves all original-size images");
Check(File.ReadAllBytes(Path.Combine(game, Engine.SpriteSizesName)).SequenceEqual(plan.SizeCatalog), "Runtime receives original canvas dimensions for the full library");
string savedRecord = Path.Combine(root, "preserved-record.json"), savedSizes = Path.Combine(root, "preserved-sizes.txt");
File.Move(engine.Manifest, savedRecord); File.Move(Path.Combine(game, Engine.SpriteSizesName), savedSizes);
try { Refused(() => engine.Apply(new(false, false)), "Renderer removal detects HD executable even when manifest and catalog are missing"); }
finally { File.Move(savedRecord, engine.Manifest); File.Move(savedSizes, Path.Combine(game, Engine.SpriteSizesName)); }
string mutexKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(game).ToUpperInvariant())))[..24];
using (var held = new Mutex(false, "Local\\CrystalProjectModInstaller-" + mutexKey))
{
    held.WaitOne();
    try { Refused(() => Task.Run(() => service.ValidateFolder(workspace)).GetAwaiter().GetResult(), "Sprite operations respect the shared game-patch mutex"); }
    finally { held.ReleaseMutex(); }
}
string hash = Patches.Hash(engine.Exe);
Refused(() => engine.Apply(new(false, false)), "Vanilla restore blocked while HD textures remain");
Check(Patches.Hash(engine.Exe) == hash && service.InstalledHdCount() == 2, "Blocked renderer removal leaves executable and HD archive intact");
engine.Apply(new(false, true, true)); Check(engine.Inspect().Mods == new Selection(false, true, true), "Music changes can preserve HD renderer and Home Points");
File.WriteAllBytes(Path.Combine(workspace, entries[0].Filename), Png(entries[0].Width * 4, entries[0].Height * 4));
var upgrade = service.ValidateFolder(workspace); service.Apply(upgrade, engine.Idle, Install);
Check(upgrade.Changes.Count == 1 && service.InstalledSprites()[0].Width == entries[0].Width * 4, "Installed 2× sprite upgrades to 4× against original baseline");
File.WriteAllBytes(Path.Combine(workspace, entries[0].Filename), Png(entries[0].Width * 10, entries[0].Height * 10));
var tenfold = service.ValidateFolder(workspace); service.Apply(tenfold, engine.Idle, Install);
Check(tenfold.Changes.Count == 1 && tenfold.Changes[0].Scale == 10 && service.InstalledSprites()[0].Height == entries[0].Height * 10, "10× sprites validate and deploy alongside original and 4× images");
Refused(() => EnemySprites.Scale(100, 80, 10, 20), "Nonuniform 10× enlargement refused");
int fractionalWidth = (int)Math.Round(entries[0].Width * 3.5, MidpointRounding.AwayFromZero), fractionalHeight = (int)Math.Round(entries[0].Height * 3.5, MidpointRounding.AwayFromZero);
File.WriteAllBytes(Path.Combine(workspace, entries[0].Filename), Png(fractionalWidth, fractionalHeight));
var fractional = service.ValidateFolder(workspace); service.Apply(fractional, engine.Idle, Install);
Check(fractional.Changes.Count == 1 && Math.Abs(fractional.Changes[0].Scale - 3.5) < 0.01 && service.InstalledSprites()[0].Width == fractionalWidth, "Fractional PNG validates and deploys with other HD sprites");
File.WriteAllBytes(Path.Combine(workspace, entries[0].Filename), entries[0].Png); File.WriteAllBytes(Path.Combine(workspace, entries[1].Filename), entries[1].Png);
var normal = service.ValidateFolder(workspace); service.Apply(normal, engine.Idle);
Check(normal.HdSprites == 0 && File.ReadAllBytes(live).SequenceEqual(original), "HD sprites can return to original resolution without changing filenames");
foreach (var entry in entries) File.WriteAllBytes(Path.Combine(workspace, entry.Filename), Png(entry.Width * 4, entry.Height * 4));
var fullHd = service.ValidateFolder(workspace); service.Apply(fullHd, engine.Idle, Install);
Check(fullHd.HdSprites == 273 && service.InstalledHdCount() == 273, "All 273 enemy sprites support 4× resolution together");
service.Restore(engine.Idle); Check(File.ReadAllBytes(live).SequenceEqual(original), "Full HD library restores byte-identical original archive");
engine.Apply(new(false, true)); Check(engine.Inspect().Mods == new Selection(false, true) && !File.Exists(Path.Combine(game, "CrystalProjectHDSprites.dll")) && !File.Exists(Path.Combine(game, Engine.SpriteSizesName)), "Renderer removal clears owned helper and metadata while retaining Home Points");
engine.Apply(new(false, false)); Check(Patches.Hash(engine.Exe) == Patches.Original, "Restoring vanilla returns exact executable after HD removal");
Check(engine.Record()!.HelperHashes!.Count == 0, "Removed helpers clear their ownership records");
Check(File.ReadAllBytes(args[1]).SequenceEqual(original), "Source sprite archive remains unchanged");
// A separate isolated fixture is used to execute patched methods in .NET Framework.
string runtimeGame = Path.Combine(root, "runtime-game"); Directory.CreateDirectory(runtimeGame);
File.Copy(Path.Combine(root, "build-False-False-True.exe"), Path.Combine(runtimeGame, "Crystal Project.exe")); File.WriteAllBytes(Path.Combine(runtimeGame, "CrystalProjectHDSprites.dll"), Patches.Resource("CrystalProjectHDSprites.dll"));
Directory.CreateDirectory(Path.Combine(runtimeGame, "Mods", "CrystalProjectModManager")); File.WriteAllText(Path.Combine(runtimeGame, Engine.SpriteSizesName), "CrystalProjectHDSprites:1\nMonster/Test|10|20\nMonster/Alt|12|6\nMonster/Odd|11|7\n");
File.WriteAllText(Path.Combine(repo, "artifacts", "hd-runtime-location.txt"), runtimeGame); File.WriteAllLines(Path.Combine(root, "checks.txt"), checks); Console.WriteLine($"{checks.Count} HD integration checks passed.");
