
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace CrystalProjectRandomMusic
{
    public static class Runtime
    {
        private static readonly object Gate = new object();
        private static readonly Random Rng = new Random();
        private static readonly int[] NormalPool = new int[] { 14,16,19,29,22,23,24 };
        private static readonly int[] BossPool = new int[] { 15,17,20,30,21,25 };
        private static readonly int[] VictoryPool = new int[] { 9,13 };
        private static string LastNormal = null;
        private static string LastBoss = null;
        private static string LastVictory = null;
        private static int LastBattleCue = Int32.MinValue;
        private static DateTime LastBattleApplyUtc = DateTime.MinValue;
        private static bool ReflectionDetailsLogged = false;

        public static int RandomizeBattleInt(int cue)
        {
            lock (Gate)
            {
                DateTime now = DateTime.UtcNow;
                if (cue == LastBattleCue && (now - LastBattleApplyUtc).TotalMilliseconds < 1500.0)
                {
                    Log("ROUTE", "DEBOUNCE cue=" + cue + " within=1500ms");
                    return cue;
                }
                LastBattleCue = cue;
                LastBattleApplyUtc = now;

                if (Contains(BossPool, cue))
                    ApplyCustomTrack("BOSS", cue, ref LastBoss);
                else if (Contains(NormalPool, cue))
                    ApplyCustomTrack("BATTLE", cue, ref LastNormal);
                else
                    Log("ROUTE", "UNROUTED battle cue=" + cue + " (not in normal/boss cue sets)");
                return cue;
            }
        }

        public static int RandomizeVictoryInt(int cue)
        {
            lock (Gate)
            {
                if (Contains(VictoryPool, cue))
                    ApplyCustomTrack("VICTORY", cue, ref LastVictory);
                return cue;
            }
        }

        public static T RandomizeBattleEnum<T>(T cue) where T : struct
        {
            int original = Convert.ToInt32((object)cue, CultureInfo.InvariantCulture);
            RandomizeBattleInt(original);
            return cue;
        }

        public static T RandomizeVictoryEnum<T>(T cue) where T : struct
        {
            int original = Convert.ToInt32((object)cue, CultureInfo.InvariantCulture);
            RandomizeVictoryInt(original);
            return cue;
        }

        public static Nullable<T> RandomizeBattleNullable<T>(Nullable<T> cue) where T : struct
        {
            if (!cue.HasValue) return cue;
            T value = cue.Value;
            int original = Convert.ToInt32((object)value, CultureInfo.InvariantCulture);
            RandomizeBattleInt(original);
            return cue;
        }

        public static Nullable<T> RandomizeVictoryNullable<T>(Nullable<T> cue) where T : struct
        {
            if (!cue.HasValue) return cue;
            T value = cue.Value;
            int original = Convert.ToInt32((object)value, CultureInfo.InvariantCulture);
            RandomizeVictoryInt(original);
            return cue;
        }

        private static void ApplyCustomTrack(string category, int cue, ref string last)
        {
            try
            {
                string gameDir = AppDomain.CurrentDomain.BaseDirectory;
                string poolFolder = GetPoolFolder(gameDir, category);
                if (!Directory.Exists(poolFolder))
                {
                    Log(category, "SKIP cue=" + cue + " reason=pool-folder-missing path=" + poolFolder);
                    return;
                }

                string[] pool = LoadPool(poolFolder);
                if (pool.Length == 0)
                {
                    Log(category, "SKIP cue=" + cue + " reason=empty-pool path=" + poolFolder);
                    return;
                }

                string selected = Pick(pool, ref last);
                LoopInfo loop = ReadLoopInfo(selected);
                string detail;
                if (!TryAssignTrack(cue, selected, loop, out detail))
                {
                    Log(category, "ERROR cue=" + cue + " file=" + Path.GetFileName(selected) + " reason=" + detail);
                    return;
                }

                string loopText = loop.HasStart ? loop.Start.ToString("0.000000", CultureInfo.InvariantCulture) : "original";
                string endText = loop.HasEnd ? loop.End.ToString("0.000000", CultureInfo.InvariantCulture) : "original";
                Log(category, "cue=" + cue + " selectedPath=" + selected + " pool=" + pool.Length + " rawLoopStart=" + (loop.RawStart ?? "<none>") + " loopStartSec=" + loopText + " loopStartSamples=" + loop.StartSamples + " loopStartUnit=" + (loop.StartUnit ?? "<none>") + " loopEndSec=" + endText + " loopEndSamples=" + loop.EndSamples + " loopEndUnit=" + (loop.EndUnit ?? "<none>") + " sampleRate=" + loop.SampleRate + " totalSamples=" + loop.TotalSamples + " loopSource=" + loop.Source + " | " + detail);
            }
            catch (Exception ex)
            {
                Log(category, "ERROR cue=" + cue + " exception=" + ex.GetType().FullName + ": " + ex.Message);
            }
        }

        private static string GetPoolFolder(string gameDir, string category)
        {
            string folder = category == "BATTLE" ? "Battle" : (category == "BOSS" ? "Boss" : "Victory");
            return Path.Combine(gameDir, "ogg", folder);
        }

        private static string[] LoadPool(string poolFolder)
        {
            try
            {
                string[] files = Directory.GetFiles(poolFolder, "*.ogg", SearchOption.TopDirectoryOnly);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                return files;
            }
            catch
            {
                return new string[0];
            }
        }

        private static string Pick(string[] pool, ref string last)
        {
            if (pool.Length == 1) { last = pool[0]; return pool[0]; }
            string pick;
            int guard = 0;
            do
            {
                pick = pool[Rng.Next(pool.Length)];
                guard++;
            }
            while (string.Equals(pick, last, StringComparison.OrdinalIgnoreCase) && guard < 32);
            last = pick;
            return pick;
        }

        private sealed class LoopInfo
        {
            public bool HasStart;
            public bool HasEnd;
            public double Start;
            public double End;
            public int SampleRate;
            public long TotalSamples;
            public long StartSamples;
            public long EndSamples;
            public string RawStart;
            public string RawEnd;
            public string StartUnit;
            public string EndUnit;
            public string Source;
        }

        private static LoopInfo ReadLoopInfo(string path)
        {
            var result = new LoopInfo();
            result.Source = "none";
            try
            {
                const int max = 1024 * 1024;
                byte[] data;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int count = (int)Math.Min((long)max, fs.Length);
                    data = new byte[count];
                    int read = 0;
                    while (read < count)
                    {
                        int n = fs.Read(data, read, count - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read != data.Length) Array.Resize(ref data, read);
                }

                result.SampleRate = FindVorbisSampleRate(data);
                result.TotalSamples = FindFinalGranule(path);
                string text = Encoding.ASCII.GetString(data);
                string startRaw;
                string endRaw;
                string lengthRaw;
                bool hasStart = TryFindTag(text, new string[] { "LOOPSTART=", "LOOP_START=" }, out startRaw);
                bool hasEnd = TryFindTag(text, new string[] { "LOOPEND=", "LOOP_END=" }, out endRaw);
                bool hasLength = TryFindTag(text, new string[] { "LOOPLENGTH=", "LOOP_LENGTH=" }, out lengthRaw);

                double startSeconds;
                double endSeconds;
                double lengthSeconds;
                long startSamples;
                long endSamples;
                long lengthSamples;
                string startUnit;
                string endUnit;
                string lengthUnit;
                result.RawStart = hasStart ? startRaw : null;
                result.RawEnd = hasEnd ? endRaw : null;
                if (hasStart && TryLoopValueToSeconds(startRaw, result.SampleRate, result.TotalSamples, out startSeconds, out startSamples, out startUnit))
                {
                    result.HasStart = true;
                    result.Start = startSeconds;
                    result.StartSamples = startSamples;
                    result.StartUnit = startUnit;
                }
                if (hasEnd && TryLoopValueToSeconds(endRaw, result.SampleRate, result.TotalSamples, out endSeconds, out endSamples, out endUnit))
                {
                    result.HasEnd = true;
                    result.End = endSeconds;
                    result.EndSamples = endSamples;
                    result.EndUnit = endUnit;
                }
                else if (result.HasStart && hasLength && TryLoopValueToSeconds(lengthRaw, result.SampleRate, result.TotalSamples, out lengthSeconds, out lengthSamples, out lengthUnit))
                {
                    result.HasEnd = true;
                    result.End = result.Start + lengthSeconds;
                    result.EndSamples = result.StartSamples + lengthSamples;
                    result.EndUnit = "start+" + lengthUnit;
                }
                else if (result.HasStart && result.SampleRate > 0 && result.TotalSamples > 0)
                {
                    // LOOPSTART-only convention: the physical end of the Vorbis
                    // stream is the loop end. Keep both seconds and PCM samples so
                    // we can populate whichever member shape Crystal Project uses.
                    result.HasEnd = true;
                    result.End = (double)result.TotalSamples / (double)result.SampleRate;
                    result.EndSamples = result.TotalSamples;
                    result.EndUnit = "physical-eof";
                }

                if (result.HasStart || result.HasEnd)
                    result.Source = "vorbis-comments";
            }
            catch { }
            return result;
        }

        private static bool TryFindTag(string text, string[] markers, out string value)
        {
            value = null;
            foreach (string marker in markers)
            {
                int start = 0;
                while (true)
                {
                    int i = text.IndexOf(marker, start, StringComparison.OrdinalIgnoreCase);
                    if (i < 0) break;
                    i += marker.Length;
                    while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                    int j = i;
                    if (j < text.Length && (text[j] == '+' || text[j] == '-')) j++;
                    bool dotSeen = false;
                    while (j < text.Length)
                    {
                        char c = text[j];
                        if (char.IsDigit(c)) { j++; continue; }
                        if (c == '.' && !dotSeen) { dotSeen = true; j++; continue; }
                        break;
                    }
                    if (j > i)
                    {
                        value = text.Substring(i, j - i);
                        return true;
                    }
                    start = i;
                }
            }
            return false;
        }

        private static bool TryLoopValueToSeconds(string raw, int sampleRate, long totalSamples, out double seconds, out long samples, out string unit)
        {
            seconds = 0.0;
            samples = 0;
            unit = "unknown";
            if (string.IsNullOrEmpty(raw)) return false;
            double parsed;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return false;
            if (parsed < 0.0) return false;

            double durationSeconds = (sampleRate > 0 && totalSamples > 0) ? (double)totalSamples / (double)sampleRate : 0.0;

            // Decimal values are unambiguously treated as seconds. For integer
            // values, use the actual stream duration as a sanity check: small
            // integers that already fit inside the track are seconds; larger
            // integers that become a plausible in-track position after dividing
            // by sample rate are standard PCM-sample LOOPSTART values.
            if (raw.IndexOf('.') >= 0)
            {
                seconds = parsed;
                samples = sampleRate > 0 ? (long)Math.Round(seconds * sampleRate) : 0;
                unit = "decimal-seconds";
                return durationSeconds <= 0.0 || seconds <= durationSeconds + 1.0;
            }

            if (durationSeconds > 0.0 && parsed <= durationSeconds + 1.0)
            {
                seconds = parsed;
                samples = sampleRate > 0 ? (long)Math.Round(seconds * sampleRate) : 0;
                unit = "integer-seconds";
                return true;
            }

            if (sampleRate > 0)
            {
                double sampleSeconds = parsed / (double)sampleRate;
                if (durationSeconds <= 0.0 || sampleSeconds <= durationSeconds + 1.0)
                {
                    seconds = sampleSeconds;
                    samples = (long)Math.Round(parsed);
                    unit = "pcm-samples";
                    return true;
                }
            }

            // Last-resort compatibility for hand-authored integer-second tags
            // when duration probing failed.
            seconds = parsed;
            samples = sampleRate > 0 ? (long)Math.Round(seconds * sampleRate) : 0;
            unit = "integer-seconds-fallback";
            return true;
        }

        private static int FindVorbisSampleRate(byte[] data)
        {
            if (data == null) return 0;
            for (int i = 0; i + 16 <= data.Length; i++)
            {
                if (data[i] != 1) continue;
                if (data[i + 1] != (byte)'v' || data[i + 2] != (byte)'o' || data[i + 3] != (byte)'r' ||
                    data[i + 4] != (byte)'b' || data[i + 5] != (byte)'i' || data[i + 6] != (byte)'s') continue;
                int rate = data[i + 12] | (data[i + 13] << 8) | (data[i + 14] << 16) | (data[i + 15] << 24);
                if (rate >= 8000 && rate <= 384000) return rate;
            }
            return 0;
        }

        private static long FindFinalGranule(string path)
        {
            try
            {
                const int tailSize = 512 * 1024;
                byte[] tail;
                long baseOffset;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int count = (int)Math.Min((long)tailSize, fs.Length);
                    baseOffset = fs.Length - count;
                    fs.Position = baseOffset;
                    tail = new byte[count];
                    int read = 0;
                    while (read < count)
                    {
                        int n = fs.Read(tail, read, count - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read != tail.Length) Array.Resize(ref tail, read);
                }

                for (int i = tail.Length - 14; i >= 0; i--)
                {
                    if (tail[i] != (byte)'O' || tail[i + 1] != (byte)'g' || tail[i + 2] != (byte)'g' || tail[i + 3] != (byte)'S') continue;
                    if (i + 14 > tail.Length) continue;
                    long granule = ReadInt64LittleEndian(tail, i + 6);
                    if (granule > 0 && granule != -1) return granule;
                }
            }
            catch { }
            return 0;
        }

        private static long ReadInt64LittleEndian(byte[] data, int offset)
        {
            unchecked
            {
                ulong value = 0;
                for (int i = 0; i < 8; i++) value |= ((ulong)data[offset + i]) << (8 * i);
                return (long)value;
            }
        }

        private static bool TryAssignTrack(int cue, string selected, LoopInfo loop, out string detail)
        {
            detail = "";
            Type caudio = FindType("Sang.Audio.CAudio");
            if (caudio == null) { detail = "Sang.Audio.CAudio not found"; return false; }

            object tracks = GetStaticMember(caudio, "TRACKS");
            if (tracks == null) { detail = "CAudio.TRACKS not found/null"; return false; }

            object key;
            object track = GetTrack(tracks, cue, out key);
            if (track == null) { detail = "track entry not found for cue " + cue; return false; }

            string pathMember;
            if (!SetMember(track, new string[] { "Path" }, selected, out pathMember))
            {
                detail = "track Path member not writable; trackType=" + track.GetType().FullName + " members=" + MemberNames(track.GetType());
                return false;
            }

            string playStartMember;
            SetMember(track, new string[] { "PlayStart", "playStart" }, 0.0, out playStartMember);

            string loopStartMember;
            string loopEndMember;
            string loopContainerDetail;
            ApplyLoopPoints(track, loop, out loopStartMember, out loopEndMember, out loopContainerDetail);

            if (track.GetType().IsValueType && !SetTrack(tracks, key, track))
            {
                detail = "track value modified but could not write boxed/value entry back; tracksType=" + tracks.GetType().FullName;
                return false;
            }

            if (!ReflectionDetailsLogged)
            {
                ReflectionDetailsLogged = true;
                Log("REFLECTION", "CAudio=" + caudio.FullName + " TRACKS=" + tracks.GetType().FullName + " track=" + track.GetType().FullName + " members=" + MemberNames(track.GetType()) + " | " + loopContainerDetail);
            }
            detail = "pathMember=" + pathMember + " playStartMember=" + playStartMember + " loopStartMember=" + loopStartMember + " loopEndMember=" + loopEndMember + " | " + loopContainerDetail;
            return true;
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly asm in assemblies)
            {
                try
                {
                    Type t = asm.GetType(fullName, false, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        private static object GetStaticMember(Type type, string name)
        {
            BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy | BindingFlags.IgnoreCase;
            FieldInfo f = type.GetField(name, flags);
            if (f != null) return f.GetValue(null);
            PropertyInfo p = type.GetProperty(name, flags);
            if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(null, null);
            return null;
        }

        private static object GetTrack(object tracks, int cue, out object key)
        {
            key = null;
            Array array = tracks as Array;
            if (array != null)
            {
                if (cue < array.GetLowerBound(0) || cue > array.GetUpperBound(0)) return null;
                key = cue;
                return array.GetValue(cue);
            }

            IList list = tracks as IList;
            if (list != null)
            {
                if (cue < 0 || cue >= list.Count) return null;
                key = cue;
                return list[cue];
            }

            IDictionary dict = tracks as IDictionary;
            if (dict != null)
            {
                foreach (object k in dict.Keys)
                {
                    try
                    {
                        if (Convert.ToInt32(k, CultureInfo.InvariantCulture) == cue)
                        {
                            key = k;
                            return dict[k];
                        }
                    }
                    catch { }
                }
                return null;
            }

            PropertyInfo indexer = FindIndexer(tracks.GetType(), true);
            if (indexer != null)
            {
                ParameterInfo p = indexer.GetIndexParameters()[0];
                object converted = ConvertCue(cue, p.ParameterType);
                key = converted;
                return indexer.GetValue(tracks, new object[] { converted });
            }
            return null;
        }

        private static bool SetTrack(object tracks, object key, object track)
        {
            try
            {
                Array array = tracks as Array;
                if (array != null) { array.SetValue(track, Convert.ToInt32(key, CultureInfo.InvariantCulture)); return true; }
                IList list = tracks as IList;
                if (list != null) { list[Convert.ToInt32(key, CultureInfo.InvariantCulture)] = track; return true; }
                IDictionary dict = tracks as IDictionary;
                if (dict != null) { dict[key] = track; return true; }
                PropertyInfo indexer = FindIndexer(tracks.GetType(), false);
                if (indexer != null && indexer.CanWrite) { indexer.SetValue(tracks, track, new object[] { key }); return true; }
            }
            catch { }
            return false;
        }

        private static PropertyInfo FindIndexer(Type type, bool requireRead)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (PropertyInfo p in type.GetProperties(flags))
            {
                if (p.GetIndexParameters().Length != 1) continue;
                if (requireRead && !p.CanRead) continue;
                return p;
            }
            return null;
        }

        private static object ConvertCue(int cue, Type type)
        {
            Type underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) type = underlying;
            if (type.IsEnum) return Enum.ToObject(type, cue);
            return Convert.ChangeType(cue, type, CultureInfo.InvariantCulture);
        }

        private static void ApplyLoopPoints(object track, LoopInfo loop, out string startUsed, out string endUsed, out string containerDetail)
        {
            startUsed = loop.HasStart ? "not-found" : "unchanged";
            endUsed = loop.HasEnd ? "not-found" : "unchanged";
            containerDetail = "loopContainer=not-found";

            object loopPoint;
            Type loopPointType;
            string loopContainerMember;
            if (TryGetMember(track, new string[] { "LoopPoint", "loopPoint" }, out loopPoint, out loopPointType, out loopContainerMember))
            {
                try
                {
                    if (loopPoint == null && loopPointType != null)
                        loopPoint = Activator.CreateInstance(loopPointType);
                }
                catch { }

                if (loopPoint != null)
                {
                    bool startOk = !loop.HasStart || SetLoopScalar(
                        loopPoint,
                        new string[] { "LoopPointStart", "loopPointStart", "LoopStart", "loopStart", "Start", "start" },
                        loop.Start, loop.StartSamples, out startUsed);
                    bool endOk = !loop.HasEnd || SetLoopScalar(
                        loopPoint,
                        new string[] { "LoopPointEnd", "loopPointEnd", "LoopEnd", "loopEnd", "End", "end" },
                        loop.End, loop.EndSamples, out endUsed);

                    string writeBackMember;
                    bool wroteBack = SetMember(track, new string[] { "LoopPoint", "loopPoint" }, loopPoint, out writeBackMember);

                    containerDetail = "loopContainer=" + loopContainerMember +
                        " loopType=" + loopPoint.GetType().FullName +
                        " loopMembers=" + MemberNames(loopPoint.GetType()) +
                        " loopWriteBack=" + (wroteBack ? writeBackMember : "not-written");

                    if (startOk && endOk)
                    {
                        startUsed = startUsed == "unchanged" ? startUsed : "nested:" + startUsed;
                        endUsed = endUsed == "unchanged" ? endUsed : "nested:" + endUsed;
                        return;
                    }
                }
                else
                {
                    containerDetail = "loopContainer=" + loopContainerMember + " value=null type=" + (loopPointType == null ? "<unknown>" : loopPointType.FullName);
                }
            }

            // Compatibility fallback for builds that expose loop points directly
            // on TrackMetadata instead of through TrackMetadata.LoopPoint.
            if (loop.HasStart)
            {
                string directStart;
                if (SetLoopScalar(track,
                    new string[] { "LoopPointStart", "loopPointStart", "LoopStart", "loopStart", "LoopStartSample", "LoopStartSamples", "loopStartSample", "loopStartSamples" },
                    loop.Start, loop.StartSamples, out directStart))
                    startUsed = "direct:" + directStart;
            }
            if (loop.HasEnd)
            {
                string directEnd;
                if (SetLoopScalar(track,
                    new string[] { "LoopPointEnd", "loopPointEnd", "LoopEnd", "loopEnd", "LoopEndSample", "LoopEndSamples", "loopEndSample", "loopEndSamples" },
                    loop.End, loop.EndSamples, out directEnd))
                    endUsed = "direct:" + directEnd;
            }
        }

        private static bool TryGetMember(object target, string[] names, out object value, out Type memberType, out string used)
        {
            value = null;
            memberType = null;
            used = "not-found";
            if (target == null) return false;

            Type type = target.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            foreach (string name in names)
            {
                FieldInfo f = type.GetField(name, flags);
                if (f != null)
                {
                    try
                    {
                        value = f.GetValue(target);
                        memberType = f.FieldType;
                        used = "field:" + f.Name;
                        return true;
                    }
                    catch { }
                }

                PropertyInfo p = type.GetProperty(name, flags);
                if (p != null && p.GetIndexParameters().Length == 0 && p.CanRead)
                {
                    try
                    {
                        value = p.GetValue(target, null);
                        memberType = p.PropertyType;
                        used = "property:" + p.Name;
                        return true;
                    }
                    catch { }
                }
            }
            return false;
        }

        private static bool SetLoopScalar(object target, string[] names, double seconds, long samples, out string used)
        {
            used = "not-found";
            if (target == null) return false;

            Type type = target.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            foreach (string name in names)
            {
                FieldInfo f = type.GetField(name, flags);
                if (f != null)
                {
                    try
                    {
                        object value = LoopValueForType(seconds, samples, f.FieldType);
                        f.SetValue(target, value);
                        used = "field:" + f.Name + ":" + f.FieldType.Name + "=" + Convert.ToString(value, CultureInfo.InvariantCulture);
                        return true;
                    }
                    catch { }
                }

                PropertyInfo p = type.GetProperty(name, flags);
                if (p != null && p.CanWrite && p.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        object value = LoopValueForType(seconds, samples, p.PropertyType);
                        p.SetValue(target, value, null);
                        used = "property:" + p.Name + ":" + p.PropertyType.Name + "=" + Convert.ToString(value, CultureInfo.InvariantCulture);
                        return true;
                    }
                    catch { }
                }
            }
            return false;
        }

        private static object LoopValueForType(double seconds, long samples, Type destination)
        {
            Type nullable = Nullable.GetUnderlyingType(destination);
            if (nullable != null)
            {
                object inner = LoopValueForType(seconds, samples, nullable);
                return Activator.CreateInstance(destination, new object[] { inner });
            }

            if (destination == typeof(TimeSpan)) return TimeSpan.FromSeconds(seconds);
            if (destination == typeof(float)) return (float)seconds;
            if (destination == typeof(double)) return seconds;
            if (destination == typeof(decimal)) return (decimal)seconds;

            if (destination == typeof(byte)) return Convert.ToByte(samples, CultureInfo.InvariantCulture);
            if (destination == typeof(sbyte)) return Convert.ToSByte(samples, CultureInfo.InvariantCulture);
            if (destination == typeof(short)) return Convert.ToInt16(samples, CultureInfo.InvariantCulture);
            if (destination == typeof(ushort)) return Convert.ToUInt16(samples, CultureInfo.InvariantCulture);
            if (destination == typeof(int)) return Convert.ToInt32(samples, CultureInfo.InvariantCulture);
            if (destination == typeof(uint)) return Convert.ToUInt32(samples, CultureInfo.InvariantCulture);
            if (destination == typeof(long)) return samples;
            if (destination == typeof(ulong)) return Convert.ToUInt64(samples, CultureInfo.InvariantCulture);

            return ConvertValue(seconds, destination);
        }

        private static bool SetMember(object target, string[] names, object value, out string used)
        {
            used = "not-found";
            Type type = target.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;
            foreach (string name in names)
            {
                FieldInfo f = type.GetField(name, flags);
                if (f != null)
                {
                    try
                    {
                        f.SetValue(target, ConvertValue(value, f.FieldType));
                        used = "field:" + f.Name;
                        return true;
                    }
                    catch { }
                }
                PropertyInfo p = type.GetProperty(name, flags);
                if (p != null && p.CanWrite && p.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        p.SetValue(target, ConvertValue(value, p.PropertyType), null);
                        used = "property:" + p.Name;
                        return true;
                    }
                    catch { }
                }
            }
            return false;
        }

        private static object ConvertValue(object value, Type destination)
        {
            if (value == null) return null;
            Type nullable = Nullable.GetUnderlyingType(destination);
            if (nullable != null)
            {
                object inner = ConvertValue(value, nullable);
                return Activator.CreateInstance(destination, new object[] { inner });
            }
            if (destination.IsAssignableFrom(value.GetType())) return value;
            if (destination == typeof(TimeSpan)) return TimeSpan.FromSeconds(Convert.ToDouble(value, CultureInfo.InvariantCulture));
            if (destination.IsEnum) return Enum.ToObject(destination, Convert.ToInt32(value, CultureInfo.InvariantCulture));
            return Convert.ChangeType(value, destination, CultureInfo.InvariantCulture);
        }

        private static string MemberNames(Type type)
        {
            try
            {
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var names = new List<string>();
                foreach (FieldInfo f in type.GetFields(flags)) names.Add("F:" + f.Name + ":" + f.FieldType.Name);
                foreach (PropertyInfo p in type.GetProperties(flags)) names.Add("P:" + p.Name + ":" + p.PropertyType.Name);
                return string.Join(",", names.ToArray());
            }
            catch { return "<unavailable>"; }
        }

        private static bool Contains(int[] pool, int value)
        {
            for (int i = 0; i < pool.Length; i++) if (pool[i] == value) return true;
            return false;
        }

        private static void Log(string category, string message)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrystalProjectRandomMusic");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "randommusic.log");
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " | " + category + " | " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}
