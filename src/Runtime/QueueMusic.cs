using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

namespace CrystalProjectRandomMusic
{
    public static partial class Runtime
    {
        static readonly Dictionary<int, object> Originals = new Dictionary<int, object>();
        static readonly Dictionary<int, string> Last = new Dictionary<int, string>();
        sealed class Bookmark { public int Cue; public string Path; public bool Resume; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Bookmark> Bookmarks = new System.Runtime.CompilerServices.ConditionalWeakTable<object, Bookmark>();
        static object Read(object o, string field) { return o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(o); }
        public static void RememberBookmark(object manager)
        {
            lock (Gate)
            {
                if (!(bool)Read(manager, "_isTrackBookmarked")) { Bookmarks.Remove(manager); return; }
                int cue = Convert.ToInt32(Read(manager, "_bookmarkCue"), CultureInfo.InvariantCulture);
                string path; Last.TryGetValue(cue, out path);
                var bookmark = Bookmarks.GetOrCreateValue(manager); bookmark.Cue = cue; bookmark.Path = path; bookmark.Resume = false;
            }
        }
        public static void ResumeBookmark(object manager, int cue)
        {
            lock (Gate)
            {
                Bookmark bookmark;
                if (Bookmarks.TryGetValue(manager, out bookmark)) bookmark.Resume = (bool)Read(manager, "_isTrackBookmarked") && bookmark.Cue == cue;
            }
        }
        // Called inside the queue gate, after Play has suppressed duplicate requests.
        // The clock check matches the game's own gate. No song is selected per frame.
        public static void BeforeQueuedStart(object manager)
        {
            lock (Gate)
            {
                if (!(bool)Read(manager, "_isNextTrackQueued") || DateTime.Now < ((DateTime)Read(manager, "_nextTrackQueueTime")).AddSeconds((double)Read(manager, "_nextTrackStartDelayInSeconds"))) return;
                int cue = Convert.ToInt32(Read(manager, "_nextTrackCue"), CultureInfo.InvariantCulture);
                try
                {
                    object tracks = GetStaticMember(FindType("Sang.Audio.CAudio"), "TRACKS"); object key;
                    object track = GetTrack(tracks, cue, out key); if (track == null) return;
                    if (!Originals.ContainsKey(cue)) Originals.Add(cue, track);
                    // GetValue on an array returns a fresh box; do not mutate the backup box.
                    SetTrack(tracks, key, Originals[cue]);
                    string[] pool = ConfiguredPool(cue);
                    if (pool.Length == 0) return;
                    Bookmark bookmark;
                    bool resume = Bookmarks.TryGetValue(manager, out bookmark) && bookmark.Cue == cue && (bookmark.Resume || (bool)Read(manager, "_isPlayOnceToBookmark"));
                    string last; Last.TryGetValue(cue, out last);
                    string selected = resume && bookmark.Path != null && File.Exists(bookmark.Path) ? bookmark.Path : PickWithoutRepeat(pool, last);
                    if (resume) bookmark.Resume = false;
                    LoopInfo loop = ReadLoopInfo(selected);
                    double duration = loop.SampleRate > 0 ? (double)loop.TotalSamples / loop.SampleRate : 0;
                    if (duration <= 0) { Log("POOL", "Invalid Vorbis duration; using original cue=" + cue); return; }
                    if (!loop.HasStart || !loop.HasEnd || loop.Start < 0 || loop.Start >= loop.End || loop.End > duration)
                    { loop.HasStart = loop.HasEnd = true; loop.Start = 0; loop.End = duration; loop.StartSamples = 0; loop.EndSamples = loop.TotalSamples; }
                    string detail;
                    if (!TryAssignTrack(cue, selected, loop, out detail)) { SetTrack(tracks, key, Originals[cue]); Log("POOL", "Assignment refused cue=" + cue + " " + detail); return; }
                    Last[cue] = selected;
                    Log("POOL", "cue=" + cue + " selected=" + selected + " pool=" + pool.Length + " bookmark=" + resume);
                }
                catch (Exception e) { Log("POOL", "ERROR cue=" + cue + " " + e.Message); }
            }
        }
        static string PickWithoutRepeat(string[] pool, string last)
        {
            var eligible = new List<string>();
            foreach (string file in pool) if (!string.Equals(file, last, StringComparison.OrdinalIgnoreCase)) eligible.Add(file);
            return eligible.Count == 0 ? pool[0] : eligible[Rng.Next(eligible.Count)];
        }
        static string[] ConfiguredPool(int cue)
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods", "CrystalProjectModManager");
            string config = Path.Combine(root, "config.json");
            if (!File.Exists(config))
            {
                string category = Contains(BossPool, cue) ? "BOSS" : Contains(NormalPool, cue) ? "BATTLE" : Contains(VictoryPool, cue) ? "VICTORY" : null;
                return category == null ? new string[0] : LoadPool(GetPoolFolder(AppDomain.CurrentDomain.BaseDirectory, category));
            }
            // Deployed configurations are immutable while the game is running.
            var document = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(config));
            if (Convert.ToInt32(document["schemaVersion"]) != 1) return new string[0];
            var pools = document["pools"] as Dictionary<string, object>; object value;
            if (pools == null || !pools.TryGetValue(cue.ToString(CultureInfo.InvariantCulture), out value)) return new string[0];
            var files = new List<string>(); var values = value as IEnumerable; if (values == null) return files.ToArray();
            foreach (object item in values)
            {
                string relative = item as string;
                if (relative == null || !relative.StartsWith("Music/", StringComparison.Ordinal) || relative.IndexOf("..", StringComparison.Ordinal) >= 0 || Path.IsPathRooted(relative)) continue;
                string path = Path.GetFullPath(Path.Combine(root, relative));
                if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) && File.Exists(path) && !files.Contains(path)) files.Add(path);
            }
            return files.ToArray();
        }
    }
}
