using Mono.Cecil;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Reads metadata only. Never loads or executes the game and never exports audio.
if (args.Length != 3) throw new ArgumentException("Usage: Audit <pristine executable> <game folder> <repository>");
const string supported = "36f7d413160a4deee36b47fc6ac534e87cadb6f23f57337d4630ec99cedb14e6";
string Hash(string p) { using var f = File.OpenRead(p); return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant(); }
if (Hash(args[0]) != supported) throw new InvalidDataException("Audit requires the verified pristine 1.6.9 executable.");
using var assembly = AssemblyDefinition.ReadAssembly(args[0]);
var enumType = assembly.MainModule.Types.Single(t => t.FullName == "Sang.Audio.TrackCue");
var values = enumType.Fields.Where(f => f.HasConstant).ToDictionary(f => f.Name, f => Convert.ToInt32(f.Constant));
var audioPath = Path.Combine(args[1], "Content", "Audio", "bgm.config");
var biomePath = Path.Combine(args[1], "Content", "Database", "biome.dat");
var bytes = File.ReadAllBytes(biomePath);
if (bytes[0] != 10 || bytes[1] != 0) throw new InvalidDataException("Unexpected biome database header.");
using var biomes = JsonDocument.Parse(bytes.Skip(2).Select(b => (byte)(255 - b)).ToArray());
var entries = new List<Dictionary<string,string>>();
Dictionary<string,string>? current = null;
foreach (var line in File.ReadLines(audioPath))
{
    var pos = line.IndexOf('='); if (pos < 0) continue;
    var key = line[..pos]; var val = line[(pos + 1)..];
    if (key is "MusicCue" or "AmbienceCue") { current = new() { ["Kind"] = key, ["Cue"] = val }; entries.Add(current); }
    else if (current != null) current[key] = val;
}
if (entries.Count != values.Count || entries.Select(e => e["Cue"]).Distinct().Count() != values.Count || entries.Any(e => !values.ContainsKey(e["Cue"]))) throw new InvalidDataException("Cue/config coverage mismatch.");
string Words(string name) => Regex.Replace(Regex.Replace(name, @"^Z\d+_", ""), @"(?<=[a-z])(?=[A-Z])", " ");
var catalog = entries.Select(e => {
    string name = e["Cue"]; int number = values[name];
    var usage = new List<string>();
    foreach (var b in biomes.RootElement.EnumerateArray())
    {
        if (b.ValueKind != JsonValueKind.Object) continue;
        foreach (var prop in new[] { "MusicCue", "BattleMusicCue", "BossMusicCue" })
            if (b.TryGetProperty(prop, out var cue) && cue.ValueKind == JsonValueKind.Number && cue.GetInt32() == number)
                usage.Add(b.GetProperty("Name").GetString() + " (" + prop + ")");
    }
    bool ambience = e["Kind"] == "AmbienceCue";
    string category = ambience ? "Ambience" : name.StartsWith("Fanfare") ? "Victory" : name.StartsWith("Boss") || name is "BattleFinal" or "BattleAngelo" ? "Boss" : name.StartsWith("Battle") ? "Battle" : name.StartsWith("Narration") || name is "Credits" or "RivalsTheme" or "QuintarTheme" ? "Events" : name.StartsWith("Z") ? "Areas" : "Other";
    string display = name == "Z1_SpawningMeadows" && usage.Any(x => x.StartsWith("Proving Grounds")) ? "Proving Grounds / Spawning Meadows" : Words(name);
    return new { id = Regex.Replace(name, @"(?<=[a-z0-9])(?=[A-Z])", "_").ToLowerInvariant(), displayName = display, gameCue = name, gameValue = number, category, originalTitle = e.GetValueOrDefault("Title", ""), usage, isAmbience = ambience, notes = "Shared cue: assignments apply everywhere this cue is used. Native validation pending." };
}).OrderBy(e => e.gameValue).ToArray();
Directory.CreateDirectory(Path.Combine(args[2], "src", "Music"));
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
File.WriteAllText(Path.Combine(args[2], "src", "Music", "cues.json"), JsonSerializer.Serialize(catalog, jsonOptions) + "\n");
var report = new StringBuilder("# Music cue audit — Crystal Project 1.6.9.0\n\nGenerated from the hash-verified clean executable's TrackCue enum, installed bgm.config, and decoded biome.dat. No audio extracted.\n\n");
report.AppendLine($"Executable SHA-256: `{supported}`\n\nbgm.config SHA-256: `{Hash(audioPath)}`\n\nbiome.dat SHA-256: `{Hash(biomePath)}`\n\nCatalog: **{catalog.Count(c => !c.isAmbience)} music cues**, **{catalog.Count(c => c.isAmbience)} separate ambience cues**. Every defined TrackCue has exactly one config entry. Categories are UI groupings, not claims about area type. A biome may share its cue with other regions. Event/world-script occurrences are not exhaustively mapped by the biome database; these remain a native audit item.\n");
report.AppendLine("| Logical ID | Friendly name | Game cue / value | Usage from biome database | Replacement / pool validation |\n|---|---|---|---|---|");
foreach (var c in catalog) report.AppendLine($"| {c.id} | {c.displayName} | {c.gameCue} / {c.gameValue} | {(c.usage.Count == 0 ? (c.isAmbience ? "Weather / ambience" : "Title, event, battle or scripted playback; see identifier") : string.Join("; ", c.usage))} | Native pending | ");
report.AppendLine("\n## Playback architecture\n\nNAudioMusicManager.Play suppresses active, fading, and already queued duplicates. Update constructs NAudioTrack only when the queue starts. PlayOnce continuation and PlayBookmark share that queue. A hook immediately before construction can cover every configured cue while preserving the game's duplicate suppression. Bookmark restoration must keep the same selected file because bookmarks are PCM byte offsets.\n\nMCache accepts .mp3 and .ogg only. OGG is decoded by the installed NVorbis adapter; WAV/FLAC are not accepted. First manager build imports Ogg Vorbis only. Packed vanilla MP3 tracks remain in bgm.dat.\n\nReplacement metadata must reset DatOffset/DatLength, PlayStart and nested LoopPoint, and restore the original boxed TrackMetadata for empty/disabled pools. Leaving original loop bounds on an untagged replacement is unsafe. Native transitions, saved bookmarks and in-game soundtrack atlas need testing.\n");
File.WriteAllText(Path.Combine(args[2], "docs", "MUSIC_CUE_AUDIT.md"), report.ToString());
Console.WriteLine($"Audited {catalog.Length} cues ({catalog.Count(c => !c.isAmbience)} music). Catalog and audit written.");
