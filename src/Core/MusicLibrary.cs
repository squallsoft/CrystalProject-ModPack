using System.Security.Cryptography;
using System.Text.Json;
using NVorbis;

namespace CrystalProjectModManager.Core;

public sealed record MusicCue(string Id, string DisplayName, string GameCue, int GameValue, string Category, string OriginalTitle, string[] Usage, bool IsAmbience, string Notes)
{
    public string Context => string.Join(" · ", Usage.Select(u => u.Contains(" (") ? u[..u.LastIndexOf(" (", StringComparison.Ordinal)] : u).Distinct());
}
public sealed class Pool { public bool Enabled { get; set; } = true; public List<string> Tracks { get; set; } = []; }
public sealed class LibraryTrack
{
    public string Id { get; set; } = "";
    public string Filename { get; set; } = "";
    public string? ImportedFrom { get; set; }
    public double Duration { get; set; }
    public string Format { get; set; } = "Ogg Vorbis";
}
public sealed class Configuration
{
    public int SchemaVersion { get; set; } = 1;
    public bool MusicEnabled { get; set; } = true;
    public bool UnlimitedHomePoints { get; set; }
    public Dictionary<string, Pool> MusicPools { get; set; } = [];
    public Dictionary<string, LibraryTrack> Library { get; set; } = [];
    public string? GamePath { get; set; }
    public double PreviewVolume { get; set; } = 0.65;
    public string? EnemySpritesFolder { get; set; }
}
public sealed class MusicLibrary
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static IReadOnlyList<MusicCue> Cues { get; } = ReadCues();
    public string Store { get; }
    public string ConfigPath => Path.Combine(Store, "configuration.json");
    public MusicLibrary(string? store = null)
    {
        Store = store ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrystalProjectModManager");
        Directory.CreateDirectory(Path.Combine(Store, "Library"));
    }
    static MusicCue[] ReadCues()
    {
        using var stream = typeof(MusicLibrary).Assembly.GetManifestResourceStream("cues.json")!;
        return JsonSerializer.Deserialize<MusicCue[]>(stream, Json)!;
    }
    public string TrackPath(string id)
    {
        if (id.Length != 64 || id.Any(c => !char.IsAsciiHexDigit(c)) || id != id.ToLowerInvariant()) throw new InvalidDataException("Invalid music library identifier.");
        return Path.Combine(Store, "Library", id + ".ogg");
    }
    public static string Hash(string path) { using var f = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant(); }
    public static double ValidateAudio(string path) => ValidateAudio(path, null);
    static double ValidateAudio(string path, Action<double>? progress)
    {
        if (!Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose Ogg Vorbis (.ogg) music. Other formats need conversion before import.");
        using var reader = new VorbisReader(path);
        if (reader.Channels is < 1 or > 2 || reader.SampleRate < 8000 || reader.TotalTime <= TimeSpan.Zero) throw new InvalidDataException("Choose a valid mono or stereo Ogg Vorbis track.");
        var samples = new float[8192]; long total = 0; int read;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while ((read = reader.ReadSamples(samples, 0, samples.Length)) > 0)
        {
            total += read;
            if (progress != null && clock.ElapsedMilliseconds >= 200)
            {
                progress(Math.Clamp(reader.TimePosition.TotalSeconds / reader.TotalTime.TotalSeconds, 0, 1));
                clock.Restart();
            }
        }
        if (total == 0) throw new InvalidDataException("The music file contains no playable audio.");
        return reader.TotalTime.TotalSeconds;
    }
    public LibraryTrack Import(string path) => Import(path, null);
    public LibraryTrack Import(string path, Action<double>? progress)
    {
        var temp = Path.Combine(Store, "Library", Guid.NewGuid().ToString("N") + ".ogg");
        try
        {
            progress?.Invoke(0);
            File.Copy(path, temp); progress?.Invoke(0.05);
            double duration = ValidateAudio(temp, p => progress?.Invoke(0.05 + 0.9 * p));
            progress?.Invoke(0.95);
            string id = Hash(temp); string destination = TrackPath(id);
            if (File.Exists(destination)) { if (Hash(destination) != id) throw new IOException("A managed music copy is damaged. Locate the original file to repair it."); }
            else File.Move(temp, destination);
            progress?.Invoke(1);
            return new() { Id = id, Filename = Path.GetFileName(path), ImportedFrom = Path.GetFullPath(path), Duration = duration };
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Locate(LibraryTrack track, string path)
    {
        if (Hash(path) != track.Id) throw new IOException("Choose the same music file that was originally assigned to this pool.");
        ValidateAudio(path);
        var destination = TrackPath(track.Id); var temp = destination + ".tmp";
        try { File.Copy(path, temp, true); if (File.Exists(destination)) File.Replace(temp, destination, null); else File.Move(temp, destination); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public Configuration Load() => File.Exists(ConfigPath) ? Parse(File.ReadAllText(ConfigPath)) : new();
    public static Configuration Parse(string json)
    {
        var config = JsonSerializer.Deserialize<Configuration>(json, Json) ?? throw new InvalidDataException("The configuration is empty.");
        Validate(config); return config;
    }
    public static void Validate(Configuration c)
    {
        if (c.SchemaVersion != 1) throw new InvalidDataException("This configuration needs a different Mod Manager version.");
        if (c.Library == null || c.MusicPools == null || !double.IsFinite(c.PreviewVolume) || c.PreviewVolume < 0 || c.PreviewVolume > 1) throw new InvalidDataException("Invalid configuration.");
        foreach (var (id, track) in c.Library)
            if (track == null || id.Length != 64 || id.Any(x => !char.IsAsciiHexDigit(x)) || id != id.ToLowerInvariant() || track.Id != id || string.IsNullOrWhiteSpace(track.Filename) || !double.IsFinite(track.Duration) || track.Duration <= 0) throw new InvalidDataException("Invalid music library entry.");
        foreach (var (id, pool) in c.MusicPools)
            if (!Cues.Any(q => q.Id == id) || pool == null || pool.Tracks == null || pool.Tracks.Any(t => t == null || !c.Library.ContainsKey(t)) || pool.Tracks.Distinct().Count() != pool.Tracks.Count) throw new InvalidDataException("A music pool contains an unknown or duplicate track/cue.");
    }
    public void Save(Configuration c)
    {
        Validate(c); string temp = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(c, Json)); if (File.Exists(ConfigPath)) File.Replace(temp, ConfigPath, null); else File.Move(temp, ConfigPath); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void Export(Configuration c, string path)
    {
        Validate(c); var portable = Parse(JsonSerializer.Serialize(c, Json));
        portable.GamePath = null; portable.EnemySpritesFolder = null; foreach (var track in portable.Library.Values) track.ImportedFrom = null;
        File.WriteAllText(path, JsonSerializer.Serialize(portable, Json));
    }
    public Dictionary<string,string> PrepareRuntime(Configuration c, string work)
    {
        Validate(c); Directory.CreateDirectory(work);
        var assets = new Dictionary<string,string>(); var pools = new Dictionary<string, object>();
        foreach (var cue in Cues)
        {
            var tracks = new List<string>();
            if (c.MusicEnabled && c.MusicPools.TryGetValue(cue.Id, out var pool) && pool.Enabled)
                foreach (var id in pool.Tracks)
                {
                    string source = TrackPath(id);
                    if (!File.Exists(source)) throw new IOException("One of your music files could not be found: " + c.Library[id].Filename + ". Use Locate File or remove it from the pool.");
                    if (Hash(source) != id) throw new IOException("A managed music file changed: " + c.Library[id].Filename + ". Locate the original before applying.");
                    string relative = "Mods/CrystalProjectModManager/Music/" + id + ".ogg";
                    if (!assets.ContainsKey(relative))
                    {
                        string copy = Path.Combine(work, id + ".ogg"); File.Copy(source, copy, true);
                        if (Hash(copy) != id) throw new IOException("Music changed during preparation.");
                        assets[relative] = copy;
                    }
                    tracks.Add("Music/" + id + ".ogg");
                }
            pools[cue.GameValue.ToString(System.Globalization.CultureInfo.InvariantCulture)] = tracks;
        }
        string config = Path.Combine(work, "runtime.json");
        File.WriteAllText(config, JsonSerializer.Serialize(new { schemaVersion = 1, pools }, Json));
        assets["Mods/CrystalProjectModManager/config.json"] = config;
        return assets;
    }
}
