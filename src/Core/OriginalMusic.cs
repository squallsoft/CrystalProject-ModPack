using System.Text;

namespace CrystalProjectModManager.Core;

// Reads installed soundtrack bytes in place; never exports them into the music library.
public static class OriginalMusic
{
    public static Stream Open(string gameDirectory, MusicCue cue)
    {
        string audio = Path.Combine(Path.GetFullPath(gameDirectory), "Content", "Audio");
        string? filename = null; bool selected = false;
        foreach (string line in File.ReadLines(Path.Combine(audio, "bgm.config")))
        {
            int equals = line.IndexOf('='); if (equals < 0) continue;
            string key = line[..equals].Trim(), value = line[(equals + 1)..].Trim();
            if (key is "MusicCue" or "AmbienceCue") selected = value == cue.GameCue;
            else if (selected && key == "Path") { filename = value; break; }
        }
        if (string.IsNullOrWhiteSpace(filename) || Path.GetFileName(filename) != filename || filename.Contains('\\') || filename.Contains('/') || !filename.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected cue has no supported original soundtrack entry.");
        string archive = Path.Combine(audio, "bgm.dat");
        if (File.Exists(archive))
        {
            var file = File.Open(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                using var reader = new BinaryReader(file, Encoding.UTF8, leaveOpen: true);
                if (reader.ReadByte() != 0 || reader.ReadBoolean()) throw new InvalidDataException("Unsupported soundtrack archive version.");
                int count = reader.ReadInt32();
                if (count < 0 || count > 10000 || 7L * count > file.Length - file.Position) throw new InvalidDataException("Invalid soundtrack archive index.");
                file.Seek(7L * count, SeekOrigin.Current);
                for (int i = 0; i < count; i++)
                {
                    int length = reader.ReadInt32();
                    if (length <= 0 || length > 4096) throw new InvalidDataException("Invalid soundtrack entry name.");
                    char[] name = reader.ReadChars(length);
                    if (name.Length != length) throw new EndOfStreamException("Incomplete soundtrack entry.");
                    int bytes = reader.ReadInt32(); long start = file.Position;
                    if (bytes <= 0 || bytes > file.Length - start) throw new InvalidDataException("Invalid soundtrack entry bounds.");
                    if (new string(name).Equals(filename, StringComparison.OrdinalIgnoreCase)) return new Segment(file, start, bytes);
                    file.Seek(bytes, SeekOrigin.Current);
                }
            }
            catch { file.Dispose(); throw; }
            file.Dispose();
        }
        string loose = Path.Combine(audio, "BGM", filename);
        if (File.Exists(loose)) return File.OpenRead(loose);
        throw new FileNotFoundException("Original music is unavailable. Select the installed game folder or verify its files in Steam.");
    }

    sealed class Segment(FileStream file, long start, long length) : Stream
    {
        long position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => position; set => Seek(value, SeekOrigin.Begin); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            file.Position = start + position;
            int read = file.Read(buffer[..(int)Math.Min(buffer.Length, length - position)]); position += read; return read;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            long target = checked((origin switch { SeekOrigin.Begin => 0, SeekOrigin.Current => position, SeekOrigin.End => length, _ => throw new ArgumentOutOfRangeException(nameof(origin)) }) + offset);
            if (target < 0 || target > length) throw new IOException("Seek is outside the soundtrack entry.");
            return position = target;
        }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) file.Dispose(); base.Dispose(disposing); }
    }
}
