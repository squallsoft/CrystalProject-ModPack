using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;

class Program
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static int count;
    static void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL " + label); count++; Console.WriteLine("PASS " + label); }
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s,e) => { string path = Path.Combine(args[1], new AssemblyName(e.Name).Name + ".dll"); return File.Exists(path) ? Assembly.LoadFrom(path) : null; };
        var helper = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CrystalProjectRandomMusic.dll")).GetType("CrystalProjectRandomMusic.Runtime");
        var game = Assembly.LoadFrom(args[0]); var audio = game.GetType("Sang.Audio.CAudio"); var type = game.GetType("Sang.Audio.TrackMetadata"); var cueType = game.GetType("Sang.Audio.TrackCue");
        var tracks = Array.CreateInstance(type, 103);
        object[] originals = new object[103];
        foreach (int number in Enum.GetValues(cueType))
        {
            object t = Activator.CreateInstance(type); type.GetField("Path").SetValue(t, "original-" + number + ".dat"); type.GetField("PlayStart").SetValue(t, 1.0); type.GetField("DatOffset").SetValue(t, 123); type.GetField("DatLength").SetValue(t, 456); type.GetField("Volume").SetValue(t, 0.5f);
            object loop = Activator.CreateInstance(type.GetField("LoopPoint").FieldType); loop.GetType().GetField("Start").SetValue(loop, 2.0); loop.GetType().GetField("End").SetValue(loop, 10.0); type.GetField("LoopPoint").SetValue(t, loop);
            tracks.SetValue(t, number); originals[number] = t;
        }
        audio.GetField("TRACKS").SetValue(null, tracks);
        var managerType = game.GetType("Sang.Audio.NAudio.NAudioMusicManager"); var manager = Activator.CreateInstance(managerType, true);
        void Set(string field, object value) { managerType.GetField(field, Fields).SetValue(manager, value); }
        object Cue(int n) { return Enum.ToObject(cueType, n); }
        void Queue(int n) { Set("_trackActive", null); Set("_trackFadingOut", null); Set("_isNextTrackQueued", true); Set("_nextTrackCue", Cue(n)); Set("_nextTrackQueueTime", DateTime.Now.AddSeconds(-2)); Set("_nextTrackStartDelayInSeconds", 0.0); Set("_nextTrackStartPosition", 0L); }
        void Start() { helper.GetMethod("BeforeQueuedStart").Invoke(null, new[] { manager }); }
        string PathFor(int n) { return (string)type.GetField("Path").GetValue(tracks.GetValue(n)); }
        string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods", "CrystalProjectModManager"); Directory.CreateDirectory(Path.Combine(root, "Music"));
        File.Copy(args[2], Path.Combine(root, "Music", "a.ogg"), true); File.Copy(args[3], Path.Combine(root, "Music", "b.ogg"), true);
        var pools = new Dictionary<string, object>();
        void Config() { File.WriteAllText(Path.Combine(root, "config.json"), new JavaScriptSerializer().Serialize(new { schemaVersion = 1, pools })); }
        foreach (int n in Enum.GetValues(cueType)) pools[n.ToString()] = new[] { "Music/a.ogg" };
        Config();
        foreach (int n in Enum.GetValues(cueType)) { Queue(n); Start(); Check(PathFor(n).EndsWith("a.ogg"), "Single replacement covers actual cue " + Enum.GetName(cueType,n)); }
        object track = tracks.GetValue(32); object lp = type.GetField("LoopPoint").GetValue(track);
        Check((int)type.GetField("DatOffset").GetValue(track) == 0 && (int)type.GetField("DatLength").GetValue(track) == 0, "Packed-audio offsets reset for replacement");
        Check((double)lp.GetType().GetField("Start").GetValue(lp) == 0 && (double)lp.GetType().GetField("End").GetValue(lp) > 1.9 && (double)lp.GetType().GetField("End").GetValue(lp) < 2.1, "Untagged file loops its own duration");
        pools[32.ToString()] = new[] { "Music/a.ogg", "Music/b.ogg" }; Config();
        string previous = PathFor(32);
        for (int i = 0; i < 100; i++) { Queue(32); Start(); string next = PathFor(32); Check(next != previous, "No immediate repeat " + i); previous = next; }
        Set("_isNextTrackQueued", false); Start(); Check(PathFor(32) == previous, "No selection outside a queued start");
        Queue(32); Set("_nextTrackQueueTime", DateTime.Now.AddHours(1)); Start(); Check(PathFor(32) == previous, "No selection before start delay");
        Queue(32); managerType.GetMethod("Play").Invoke(manager, new[] { Cue(32), (object)0.0, 0.0, 0.0 }); Check(PathFor(32) == previous, "Repeated queued Play request does not reroll metadata");
        Set("_isNextTrackQueued", false);
        var active = FormatterServices.GetUninitializedObject(game.GetType("Sang.Audio.NAudio.NAudioTrack")); Set("_trackActive", active); Set("_trackActiveCue", Cue(32)); managerType.GetMethod("Play").Invoke(manager, new[] { Cue(32), (object)0.0, 0.0, 0.0 }); Check(!(bool)managerType.GetField("_isNextTrackQueued", Fields).GetValue(manager), "Active duplicate cue does not restart");
        Queue(32); Start(); string bookmarked = PathFor(32); Set("_nextTrackStartPosition", 1024L); managerType.GetMethod("SaveBookmark").Invoke(manager, new[] { Cue(32) });
        Queue(32); Start(); Check(PathFor(32) != bookmarked, "Fresh cue start can choose another file after bookmark");
        Set("_isNextTrackQueued", false); managerType.GetMethod("PlayBookmark").Invoke(manager, new[] { Cue(32), (object)0.0, 0.0, 0.0 }); Set("_nextTrackQueueTime", DateTime.Now.AddSeconds(-2)); Start(); Check(PathFor(32) == bookmarked, "Bookmark resumes exact file despite later random selection");
        pools[32.ToString()] = new string[0]; Config(); Queue(32); Start(); Check(PathFor(32) == "original-32.dat" && (int)type.GetField("DatOffset").GetValue(tracks.GetValue(32)) == 123, "Empty pool restores original packed metadata");
        pools[32.ToString()] = new[] { "../escape.ogg", "Music/missing.ogg" }; Config(); Queue(32); Start(); Check(PathFor(32) == "original-32.dat", "Missing and traversal paths fall back to original");
        pools[32.ToString()] = new[] { "Music/b.ogg" }; Config(); Queue(32); Start(); track = tracks.GetValue(32); lp = type.GetField("LoopPoint").GetValue(track); Check(Math.Abs((double)lp.GetType().GetField("Start").GetValue(lp) - 0.5) < 0.001 && Math.Abs((double)lp.GetType().GetField("End").GetValue(lp) - 2.5) < 0.001, "Tagged nested loop points preserved");
        object[] streamArgs = { PathFor(32), 0, 0, null, null, false }; game.GetType("Sang.Audio.MCache").GetMethod("GetWaveStream").Invoke(null, streamArgs); Check(streamArgs[4] != null, "Installed game MCache decodes replacement Ogg Vorbis"); ((IDisposable)streamArgs[4]).Dispose();
        foreach (string method in new[] { "Update", "SaveBookmark", "PlayBookmark" }) { System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(managerType.GetMethod(method, Fields).MethodHandle); Check(true, "JIT actual patched music " + method); }
        Console.WriteLine(count + " music runtime checks passed. Audible gameplay remains pending.");
    }
}
