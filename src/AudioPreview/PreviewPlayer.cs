using NAudio.Wave;
using NVorbis;
using System.IO;

namespace CrystalProjectModManager;
internal sealed class PreviewPlayer : IDisposable
{
    sealed class Samples(VorbisReader reader) : ISampleProvider
    {
        public object Gate { get; } = new();
        public bool End { get; set; }
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(reader.SampleRate, reader.Channels);
        public int Read(float[] buffer, int offset, int count) { lock (Gate) return End ? 0 : reader.ReadSamples(buffer, offset, count); }
    }
    VorbisReader? reader;
    Samples? samples;
    WaveOutEvent? output;
    float volume = 0.65f;
    public string? FilePath { get; private set; }
    public PlaybackState State => output?.PlaybackState ?? PlaybackState.Stopped;
    public TimeSpan Duration => reader?.TotalTime ?? TimeSpan.Zero;
    public TimeSpan Position { get { if (reader == null || samples == null) return TimeSpan.Zero; lock (samples.Gate) return samples.End ? reader.TotalTime : reader.TimePosition; } }
    public float Volume { get => volume; set { volume = Math.Clamp(value, 0, 1); if (output != null) output.Volume = volume; } }
    public event Action<Exception>? Error;
    public void Open(string path)
    {
        Dispose();
        try
        {
            reader = new VorbisReader(path); samples = new(reader);
            output = new WaveOutEvent(); output.Init(samples.ToWaveProvider()); output.Volume = volume;
            output.PlaybackStopped += (_, e) => { if (e.Exception != null) Error?.Invoke(e.Exception); };
            FilePath = path;
        }
        catch { Dispose(); throw; }
    }
    public void Play() { if (samples?.End == true || reader != null && reader.TotalSamples > 0 && reader.SamplePosition >= reader.TotalSamples - 1) Seek(TimeSpan.Zero); output?.Play(); }
    public void Pause() => output?.Pause();
    public void Stop() { output?.Stop(); Seek(TimeSpan.Zero); }
    public void Seek(TimeSpan position)
    {
        if (reader == null || samples == null) return;
        lock (samples.Gate)
        {
            // Represent the exclusive end without asking the decoder for a nonexistent packet.
            samples.End = position >= reader.TotalTime;
            if (samples.End) return;
            long target = (long)Math.Clamp(position.TotalSeconds * reader.SampleRate, 0, Math.Max(0, reader.TotalSamples - 1));
            try { reader.SamplePosition = target; }
            catch (InvalidDataException)
            {
                // Some valid final OGG pages cannot be indexed by this decoder.
                // Seek earlier and decode the remaining samples instead.
                long anchor = Math.Max(0, target - reader.SampleRate);
                try { reader.SamplePosition = anchor; } catch (InvalidDataException) { reader.SamplePosition = 0; }
                var discard = new float[8192 * reader.Channels];
                while (reader.SamplePosition < target)
                {
                    int wanted = (int)Math.Min(discard.Length, (target - reader.SamplePosition) * reader.Channels);
                    if (reader.ReadSamples(discard, 0, wanted) == 0) break;
                }
            }
        }
    }
    public void Dispose() { output?.Dispose(); output = null; reader?.Dispose(); reader = null; samples = null; FilePath = null; }
}
