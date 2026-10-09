using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace CrystalProjectModManager.Sprites;

public sealed record SpriteEntry(string Name, byte[] Png, int Width, int Height)
{
    public string Filename => Name + ".png";
}
public sealed record SpriteChange(string Name, string Filename, int Width, int Height, int Scale = 1);
public sealed record SpritePlan(string ArchiveHash, string Workspace, IReadOnlyList<SpriteChange> Changes, byte[] Archive, byte[] SizeCatalog, int HdSprites);
public sealed record SpriteExport(int SchemaVersion, string SourceArchiveHash, SpriteExportEntry[] Sprites);
public sealed record SpriteExportEntry(string Filename, int Width, int Height, string Sha256);
public sealed record SpriteState(string GamePath, string OriginalHash, string AppliedHash, string? PendingHash = null);

// All proprietary images stay in the user's installation/workspace, never embedded in the app.
public sealed class EnemySprites
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    const int MaxArchiveBytes = 256 * 1024 * 1024;
    public string Game { get; }
    public string ArchivePath => Path.Combine(Game, "Content", "Textures", "Monster.dat");
    public string Store { get; }
    string StatePath => Path.Combine(Store, "state.json");
    string BackupPath => Path.Combine(Store, "original.dat");
    readonly string key;
    public EnemySprites(string gameDirectory, string dataDirectory)
    {
        Game = Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar);
        key = Hash(Encoding.UTF8.GetBytes(Game.ToUpperInvariant()))[..24];
        Store = Path.Combine(Path.GetFullPath(dataDirectory), "EnemySprites", key);
    }
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static byte[] Read(string path)
    {
        SafePath(path);
        using var stream = File.OpenRead(path);
        if (stream.Length > MaxArchiveBytes) throw new InvalidDataException("Sprite file exceeds the supported size limit.");
        using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
    }
    public static void SafePath(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Sprite paths must not contain symbolic links or junctions: " + current);
        }
    }
    static void SafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 180 || name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\') ||
            name.EndsWith('.') || name.EndsWith(' ') || name.Contains('.'))
            throw new InvalidDataException("Unsupported sprite archive name.");
        string device = name.ToUpperInvariant();
        if (device is "CON" or "PRN" or "AUX" or "NUL" ||
            device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && device[3] is >= '0' and <= '9')
            throw new InvalidDataException("Unsupported reserved sprite filename.");
    }
    public static (int Width, int Height) ValidatePng(byte[] png)
    {
        if (png.Length < 33 || png.Length > 32 * 1024 * 1024 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 }))
            throw new InvalidDataException("Choose a valid PNG image; renaming another format does not convert it.");
        int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || (long)width * height > 16_777_216)
            throw new InvalidDataException("Unsupported sprite dimensions.");
        int position = 8; bool data = false, end = false;
        while (position < png.Length)
        {
            if (png.Length - position < 12) throw new InvalidDataException("Truncated PNG chunk.");
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position, 4));
            if (length < 0 || length > png.Length - position - 12) throw new InvalidDataException("Invalid PNG chunk bounds.");
            string type = Encoding.ASCII.GetString(png, position + 4, 4);
            if (position == 8 && (type != "IHDR" || length != 13) || position > 8 && type == "IHDR") throw new InvalidDataException("Invalid PNG header.");
            uint crc = uint.MaxValue;
            foreach (byte value in png.AsSpan(position + 4, length + 4))
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
            }
            if (~crc != System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position + 8 + length, 4))) throw new InvalidDataException("PNG checksum failed.");
            if (type == "IDAT") data = true;
            position += length + 12;
            if (type == "IEND") { if (length != 0 || position != png.Length) throw new InvalidDataException("Invalid PNG end chunk."); end = true; break; }
        }
        if (!data || !end) throw new InvalidDataException("Incomplete PNG image.");
        using var stream = new MemoryStream(png, writable: false);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count != 1 || decoder.Frames[0].PixelWidth != width || decoder.Frames[0].PixelHeight != height)
            throw new InvalidDataException("Unsupported PNG image.");
        // Force pixel decoding now so corrupt compressed image data cannot reach the game.
        int stride = checked((width * decoder.Frames[0].Format.BitsPerPixel + 7) / 8);
        decoder.Frames[0].CopyPixels(new byte[checked(stride * height)], stride, 0);
        return (width, height);
    }
    public static IReadOnlyList<SpriteEntry> Parse(byte[] archive)
    {
        if (archive.Length > MaxArchiveBytes) throw new InvalidDataException("Sprite archive is too large.");
        using var stream = new MemoryStream(archive, writable: false);
        using var reader = new BinaryReader(stream, new UTF8Encoding(false, true));
        if (reader.ReadByte() != 0 || reader.ReadByte() != 0) throw new InvalidDataException("Unsupported enemy sprite archive version.");
        int count = reader.ReadInt32();
        if (count <= 0 || count > 10000 || 7L * count > stream.Length - stream.Position) throw new InvalidDataException("Invalid enemy sprite archive index.");
        stream.Position += 7L * count;
        var sprites = new List<SpriteEntry>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            int length = reader.ReadInt32();
            if (length <= 0 || length > 180) throw new InvalidDataException("Invalid sprite name length.");
            string name = new(reader.ReadChars(length)); SafeName(name);
            if (name.Length != length || !names.Add(name)) throw new InvalidDataException("Duplicate or truncated sprite name.");
            int bytes = reader.ReadInt32();
            if (bytes <= 0 || bytes > 32 * 1024 * 1024 || bytes > stream.Length - stream.Position) throw new InvalidDataException("Invalid sprite image bounds.");
            byte[] png = reader.ReadBytes(bytes); var size = ValidatePng(png);
            sprites.Add(new(name, png, size.Width, size.Height));
        }
        if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected trailing sprite archive data.");
        return sprites;
    }
    public static int Scale(int width, int height, int originalWidth, int originalHeight)
    {
        foreach (int factor in new[] { 1, 2, 4, 10 }) if (width == originalWidth * factor && height == originalHeight * factor) return factor;
        throw new InvalidDataException($"Use a {originalWidth} × {originalHeight}, {originalWidth * 2} × {originalHeight * 2}, {originalWidth * 4} × {originalHeight * 4}, or {originalWidth * 10} × {originalHeight * 10} PNG (1×, 2×, 4×, or 10×).");
    }
    public static byte[] SizeCatalog(IReadOnlyList<SpriteEntry> originals) => Encoding.UTF8.GetBytes("CrystalProjectHDSprites:1\n" + string.Join("\n", originals.Select(e => $"Monster/{e.Name}|{e.Width}|{e.Height}")) + "\n");
    public byte[] OriginalSizeCatalog() => Locked(() => SizeCatalog(Parse(Baseline(Read(ArchivePath)))));
    public int InstalledHdCount() => Locked(() =>
    {
        byte[] live = Read(ArchivePath); var original = Parse(Baseline(live)).ToDictionary(e => e.Name);
        return Parse(live).Count(e => Scale(e.Width, e.Height, original[e.Name].Width, original[e.Name].Height) > 1);
    });
    public static byte[] Rebuild(byte[] source, IReadOnlyDictionary<string, byte[]> replacements, IReadOnlyList<SpriteEntry>? originals = null)
    {
        var entries = Parse(source);
        var baseline = (originals ?? entries).ToDictionary(e => e.Name);
        if (replacements.Keys.Any(name => !entries.Any(e => e.Name == name))) throw new InvalidDataException("Unknown enemy sprite replacement.");
        if (replacements.Count == 0) return source.ToArray();
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        // Preserve the game archive's original version, count and timestamp table exactly.
        writer.Write(source.AsSpan(0, 6 + entries.Count * 7));
        foreach (var entry in entries)
        {
            byte[] png = replacements.TryGetValue(entry.Name, out var replacement) ? replacement : entry.Png;
            var size = ValidatePng(png);
            Scale(size.Width, size.Height, baseline[entry.Name].Width, baseline[entry.Name].Height);
            writer.Write(entry.Name.Length); writer.Write(entry.Name.ToCharArray()); writer.Write(png.Length); writer.Write(png);
            if (output.Length > MaxArchiveBytes) throw new InvalidDataException("Updated sprite archive is too large.");
        }
        return output.ToArray();
    }
    T Locked<T>(Func<T> action)
    {
        // Share the patch engine's mutex so renderer installation/removal cannot race
        // an archive replacement. Callback-based engine operations are reentrant.
        using var mutex = new Mutex(false, "Local\\CrystalProjectModInstaller-" + key.ToUpperInvariant());
        bool acquired;
        try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("Another enemy sprite operation is in progress.");
        try { return action(); } finally { mutex.ReleaseMutex(); }
    }
    static void AtomicWrite(string path, byte[] bytes)
    {
        SafePath(path); string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    void WriteState(SpriteState state) => AtomicWrite(StatePath, JsonSerializer.SerializeToUtf8Bytes(state, Json));
    SpriteState? State(byte[] live)
    {
        SafePath(Store); SafePath(StatePath);
        if (!File.Exists(StatePath)) return null;
        var state = JsonSerializer.Deserialize<SpriteState>(Read(StatePath)) ?? throw new InvalidDataException("Invalid sprite backup record.");
        if (!string.Equals(state.GamePath, Game, StringComparison.OrdinalIgnoreCase) || Hash(Read(BackupPath)) != state.OriginalHash)
            throw new IOException("Sprite backup failed verification. No game files were changed.");
        string hash = Hash(live);
        if (state.PendingHash != null)
        {
            if (hash != state.PendingHash && hash != state.AppliedHash) throw new IOException("Sprites changed outside the manager during an interrupted update.");
            state = state with { AppliedHash = hash, PendingHash = null }; WriteState(state);
        }
        if (hash != state.AppliedHash) throw new IOException("Enemy sprites changed outside the manager. No files were overwritten. Verify the installation or preserve the external edits first.");
        return state;
    }
    byte[] Baseline(byte[] live) => State(live) != null ? Read(BackupPath) : live;
    public IReadOnlyList<SpriteEntry> Catalog() => Locked(() => Parse(Baseline(Read(ArchivePath))));
    public IReadOnlyList<SpriteEntry> InstalledSprites() => Locked(() => { var live = Read(ArchivePath); State(live); return Parse(live); });
    public bool HasBackup => File.Exists(StatePath);
    public string ExtractAll(string destination, Action<int, int>? progress = null) => Locked(() =>
    {
        destination = Path.GetFullPath(destination); SafePath(destination);
        if (destination.StartsWith(Game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || destination.Equals(Game, StringComparison.OrdinalIgnoreCase) ||
            destination.StartsWith(Store + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || destination.Equals(Store, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose an editing folder outside the game and sprite backup directories.");
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()) throw new IOException("Choose an empty extraction folder to preserve existing artwork.");
        byte[] original = Baseline(Read(ArchivePath)); var entries = Parse(original);
        Directory.CreateDirectory(destination);
        for (int i = 0; i < entries.Count; i++)
        {
            string path = Path.Combine(destination, entries[i].Filename); SafePath(path);
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); output.Write(entries[i].Png);
            progress?.Invoke(i + 1, entries.Count);
        }
        var manifest = new SpriteExport(1, Hash(original), entries.Select(e => new SpriteExportEntry(e.Filename, e.Width, e.Height, Hash(e.Png))).ToArray());
        using var metadata = new FileStream(Path.Combine(destination, "sprites.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(metadata, manifest, Json);
        return destination;
    });
    public SpritePlan ValidateFolder(string folder, Action<int, int>? progress = null) => Locked(() =>
    {
        folder = Path.GetFullPath(folder); SafePath(folder);
        var manifest = JsonSerializer.Deserialize<SpriteExport>(Read(Path.Combine(folder, "sprites.json"))) ?? throw new InvalidDataException("Choose a folder extracted by Enemy Sprites.");
        byte[] live = Read(ArchivePath), original = Baseline(live);
        if (manifest.SchemaVersion != 1 || manifest.SourceArchiveHash != Hash(original)) throw new InvalidDataException("This sprite folder belongs to a different source archive. Extract sprites from this installation first.");
        var entries = Parse(original); var installed = Parse(live).ToDictionary(e => e.Name);
        var known = entries.ToDictionary(e => e.Filename, StringComparer.OrdinalIgnoreCase);
        var files = Directory.GetFiles(folder).Where(f => Path.GetExtension(f).Equals(".png", StringComparison.OrdinalIgnoreCase)).ToArray();
        var unknown = files.Where(f => !known.ContainsKey(Path.GetFileName(f))).Select(Path.GetFileName).ToArray();
        if (unknown.Length > 0) throw new InvalidDataException("PNG filenames do not match extracted sprites: "
            + string.Join(", ", unknown.Take(5)) + (unknown.Length > 5 ? $" (and {unknown.Length - 5} more)" : "")
            + ". Use the exact extracted filename for a replacement, or move extra PNGs into a subfolder.");
        var replacements = new Dictionary<string, byte[]>(); var changes = new List<SpriteChange>();
        for (int i = 0; i < files.Length; i++)
        {
            var entry = known[Path.GetFileName(files[i])]; byte[] png = Read(files[i]); var size = ValidatePng(png);
            int scale = Scale(size.Width, size.Height, entry.Width, entry.Height);
            if (!png.AsSpan().SequenceEqual(installed[entry.Name].Png)) { replacements[entry.Name] = png; changes.Add(new(entry.Name, entry.Filename, size.Width, size.Height, scale)); }
            progress?.Invoke(i + 1, files.Length);
        }
        byte[] updated = Rebuild(live, replacements, entries); var baseline = entries.ToDictionary(e => e.Name);
        int hd = Parse(updated).Count(e => Scale(e.Width, e.Height, baseline[e.Name].Width, baseline[e.Name].Height) > 1);
        return new SpritePlan(Hash(live), folder, changes, updated, SizeCatalog(entries), hd);
    });
    public void Apply(SpritePlan plan, Action ensureGameClosed, Action<byte[]>? ensureHdRendering = null) => Locked(() =>
    {
        ensureGameClosed(); byte[] live = Read(ArchivePath); var state = State(live);
        if (Hash(live) != plan.ArchiveHash) throw new IOException("Enemy sprites changed after validation. Validate the folder again.");
        var original = Parse(state != null ? Read(BackupPath) : live); var updated = Parse(plan.Archive);
        if (original.Count != updated.Count || original.Zip(updated).Any(p => p.First.Name != p.Second.Name))
            throw new InvalidDataException("Updated archive does not match this installation.");
        var factors = original.Zip(updated).Select(p => Scale(p.Second.Width, p.Second.Height, p.First.Width, p.First.Height)).ToArray();
        bool hd = factors.Any(factor => factor > 1);
        if (hd)
        {
            if (ensureHdRendering == null) throw new IOException("HD sprites require the game's HD rendering patch. Apply them through the manager.");
            ensureHdRendering(SizeCatalog(original));
        }
        if (plan.Changes.Count == 0) return 0;
        SafePath(Store); Directory.CreateDirectory(Store);
        if (state == null)
        {
            // A backup left by a crash before the first state record must match the live file.
            if (File.Exists(BackupPath) && Hash(Read(BackupPath)) != Hash(live)) throw new IOException("Unrecorded sprite backup differs from the game. Preserve it before continuing.");
            AtomicWrite(BackupPath, live); state = new(Game, Hash(live), Hash(live)); WriteState(state);
        }
        Replace(live, plan.Archive, state, ensureGameClosed); return plan.Changes.Count;
    });
    void Replace(byte[] live, byte[] updated, SpriteState state, Action ensureGameClosed)
    {
        ensureGameClosed();
        if (Hash(Read(ArchivePath)) != Hash(live)) throw new IOException("Enemy sprites changed during the operation.");
        string hash = Hash(updated); WriteState(state with { PendingHash = hash });
        AtomicWrite(ArchivePath, updated);
        if (Hash(Read(ArchivePath)) != hash) throw new IOException("Sprite update verification failed.");
        WriteState(state with { AppliedHash = hash });
    }
    public void Restore(Action ensureGameClosed) => Locked(() =>
    {
        ensureGameClosed(); byte[] live = Read(ArchivePath); var state = State(live) ?? throw new IOException("No sprite update has been applied by this manager.");
        byte[] original = Read(BackupPath); Parse(original); Replace(live, original, state, ensureGameClosed); return 0;
    });
}
