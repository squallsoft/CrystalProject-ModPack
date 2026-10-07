using NAudio.Wave;
using NVorbis;

namespace CrystalProjectModManager;
internal sealed class PreviewPlayer : IDisposable
{
    sealed class Samples(VorbisReader reader) : ISampleProvider
    {
        public object Gate { get; } = new();
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(reader.SampleRate, reader.Channels);
        public int Read(float[] buffer, int offset, int count) { lock (Gate) return reader.ReadSamples(buffer, offset, count); }
    }
    VorbisReader? reader;
    Samples? samples;
    WaveOutEvent? output;
    float volume = 0.65f;
    public string? FilePath { get; private set; }
    public PlaybackState State => output?.PlaybackState ?? PlaybackState.Stopped;
    public TimeSpan Duration => reader?.TotalTime ?? TimeSpan.Zero;
    public TimeSpan Position { get { if (reader == null || samples == null) return TimeSpan.Zero; lock (samples.Gate) return reader.TimePosition; } }
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
    public void Play() => output?.Play();
    public void Pause() => output?.Pause();
    public void Stop() { output?.Stop(); Seek(TimeSpan.Zero); }
    public void Seek(TimeSpan position)
    {
        if (reader == null || samples == null) return;
        lock (samples.Gate) reader.TimePosition = position < TimeSpan.Zero ? TimeSpan.Zero : position > reader.TotalTime ? reader.TotalTime : position;
    }
    public void Dispose() { output?.Dispose(); output = null; reader?.Dispose(); reader = null; samples = null; FilePath = null; }
}
