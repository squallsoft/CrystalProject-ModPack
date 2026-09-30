using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.CSharp;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CrystalProjectRandomMusicInstaller
{
    public static class Patcher
    {
        public const string RuntimeAssemblyName = "CrystalProjectRandomMusic";
        public const string RuntimeTypeName = "CrystalProjectRandomMusic.Runtime";
        public const string RuntimeDllName = "CrystalProjectRandomMusic.dll";

        private static readonly string[] NormalBattleNames = new[]
        {
            "Battle1", "Battle2", "Battle3", "BattleMountain",
            "BattleDungeon1", "BattleDungeon2", "BattleDungeon3"
        };

        private static readonly string[] BossBattleNames = new[]
        {
            "Boss1", "Boss2", "Boss3", "BossDepths",
            "BattleFinal", "BattleAngelo"
        };

        private static readonly string[] VictoryNames = new[]
        {
            "Fanfare", "FanfareBoss"
        };

        private enum HookShape
        {
            Int32,
            Enum,
            NullableEnum
        }

        public static string Apply(string exePath, string runtimeDllPath)
        {
            if (!File.Exists(exePath))
                throw new FileNotFoundException("Crystal Project.exe was not found.", exePath);

            var reader = new ReaderParameters { InMemory = true, ReadWrite = false };
            using (var asm = AssemblyDefinition.ReadAssembly(exePath, reader))
            {
                if (asm.MainModule.AssemblyReferences.Any(r => r.Name == RuntimeAssemblyName))
                    throw new InvalidOperationException("This executable already references the custom Random Music runtime.");

                var fieldState = FindType(asm.MainModule, "Sang.Field.FieldState");
                if (fieldState == null)
                    throw new InvalidOperationException("Could not find Sang.Field.FieldState.");

                var getBattleCue = fieldState.Methods.FirstOrDefault(m => m.Name == "GetBattleCue" && m.HasBody);
                var getFanfareCue = fieldState.Methods.FirstOrDefault(m => m.Name == "GetFanfareCue" && m.HasBody);
                if (getBattleCue == null || getFanfareCue == null)
                    throw new InvalidOperationException("Could not find GetBattleCue/GetFanfareCue method bodies.");

                var cueEnum = FindCueEnum(asm.MainModule);
                if (cueEnum == null)
                    throw new InvalidOperationException("Could not locate the TrackCue enum by its battle/fanfare constants.");

                // Crystal Project 1.6.9 returns Nullable<TrackCue> from at least
                // GetBattleCue. Preserve the exact method contract instead of
                // coercing the return value to Int32. The hook chosen below has
                // the same stack signature as the game's return type.
                var battleShape = GetHookShape(getBattleCue.ReturnType, cueEnum, "GetBattleCue");
                var victoryShape = GetHookShape(getFanfareCue.ReturnType, cueEnum, "GetFanfareCue");

                var values = ReadEnumValues(cueEnum);
                var normal = ResolveRequired(values, NormalBattleNames, "normal battle");
                var boss = ResolveRequired(values, BossBattleNames, "boss battle");
                var victory = ResolveRequired(values, VictoryNames, "victory");

                string stagingDir = Path.Combine(Path.GetTempPath(), "CrystalProjectRandomMusic-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stagingDir);
                string stagedRuntime = Path.Combine(stagingDir, RuntimeDllName);
                string tempExe = exePath + ".randommusic.tmp";

                try
                {
                    CompileRuntime(stagedRuntime, normal, boss, victory, values);

                    string battleHookName;
                    string victoryHookName;
                    using (var runtimeAsm = AssemblyDefinition.ReadAssembly(stagedRuntime))
                    {
                        var runtimeType = FindType(runtimeAsm.MainModule, RuntimeTypeName);
                        if (runtimeType == null)
                            throw new InvalidOperationException("Compiled runtime type could not be found.");

                        var importedBattle = BuildHookReference(asm.MainModule, runtimeType, getBattleCue.ReturnType, cueEnum, battleShape, true);
                        var importedVictory = BuildHookReference(asm.MainModule, runtimeType, getFanfareCue.ReturnType, cueEnum, victoryShape, false);
                        battleHookName = importedBattle.Name;
                        victoryHookName = importedVictory.Name;

                        WrapReturnValues(getBattleCue, importedBattle);
                        WrapReturnValues(getFanfareCue, importedVictory);
                    }

                    if (File.Exists(tempExe)) File.Delete(tempExe);
                    asm.Write(tempExe);

                    VerifyPatch(tempExe, battleHookName, victoryHookName);

                    // Install the runtime first. If copying it fails, the game EXE
                    // remains untouched. An orphaned runtime DLL is harmless if a
                    // later EXE copy fails, while a patched EXE without its runtime
                    // would prevent the game from starting.
                    File.Copy(stagedRuntime, runtimeDllPath, true);
                    File.Copy(tempExe, exePath, true);

                    return "Patched GetBattleCue/GetFanfareCue for folder-pool custom OGG routing with nested LoopPoint support. Cue enum: " + cueEnum.FullName +
                        "; returns: " + getBattleCue.ReturnType.FullName + " / " + getFanfareCue.ReturnType.FullName +
                        "; hook shapes: " + battleShape + " / " + victoryShape + ".";
                }
                finally
                {
                    try { if (File.Exists(tempExe)) File.Delete(tempExe); } catch { }
                    try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true); } catch { }
                }
            }
        }

        private static void WrapReturnValues(MethodDefinition method, MethodReference hook)
        {
            var il = method.Body.GetILProcessor();
            var rets = method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
            if (rets.Count == 0)
                throw new InvalidOperationException(method.FullName + " contains no return instruction.");

            // The imported hook consumes and returns exactly the same type already
            // on the evaluation stack at each RET. This is essential for
            // Nullable<TrackCue>, whose value cannot safely be converted with conv.i4.
            foreach (var ret in rets)
                il.InsertBefore(ret, il.Create(OpCodes.Call, hook));

            method.Body.MaxStackSize = Math.Max(method.Body.MaxStackSize, 2);
        }

        private static HookShape GetHookShape(TypeReference returnType, TypeDefinition cueEnum, string label)
        {
            if (returnType.FullName == "System.Int32")
                return HookShape.Int32;

            TypeDefinition resolved = SafeResolve(returnType);
            if (resolved != null && resolved.IsEnum && IsInt32Enum(resolved) && resolved.FullName == cueEnum.FullName)
                return HookShape.Enum;

            var generic = returnType as GenericInstanceType;
            if (generic != null && generic.ElementType.FullName == "System.Nullable`1" && generic.GenericArguments.Count == 1)
            {
                var inner = SafeResolve(generic.GenericArguments[0]);
                if (inner != null && inner.IsEnum && IsInt32Enum(inner) && inner.FullName == cueEnum.FullName)
                    return HookShape.NullableEnum;
            }

            throw new InvalidOperationException(
                label + " return type is unsupported: " + returnType.FullName +
                ". Expected TrackCue, Nullable<TrackCue>, or System.Int32. No files were patched.");
        }

        private static MethodReference BuildHookReference(
            ModuleDefinition targetModule,
            TypeDefinition runtimeType,
            TypeReference gameReturnType,
            TypeDefinition cueEnum,
            HookShape shape,
            bool battle)
        {
            string methodName;
            switch (shape)
            {
                case HookShape.Int32:
                    methodName = battle ? "RandomizeBattleInt" : "RandomizeVictoryInt";
                    break;
                case HookShape.Enum:
                    methodName = battle ? "RandomizeBattleEnum" : "RandomizeVictoryEnum";
                    break;
                case HookShape.NullableEnum:
                    methodName = battle ? "RandomizeBattleNullable" : "RandomizeVictoryNullable";
                    break;
                default:
                    throw new InvalidOperationException("Unknown hook shape.");
            }

            var method = runtimeType.Methods.FirstOrDefault(m => m.Name == methodName && m.Parameters.Count == 1);
            if (method == null)
                throw new InvalidOperationException("Compiled runtime hook missing: " + methodName);

            var imported = targetModule.ImportReference(method);
            if (shape == HookShape.Int32)
                return imported;

            var genericHook = new GenericInstanceMethod(imported);
            TypeReference genericArgument;
            if (shape == HookShape.NullableEnum)
            {
                var nullable = gameReturnType as GenericInstanceType;
                if (nullable == null || nullable.GenericArguments.Count != 1)
                    throw new InvalidOperationException("Nullable hook selected for a non-nullable return type.");
                genericArgument = nullable.GenericArguments[0];
            }
            else
            {
                genericArgument = gameReturnType;
            }

            genericHook.GenericArguments.Add(targetModule.ImportReference(genericArgument));
            return genericHook;
        }

        private static TypeDefinition SafeResolve(TypeReference type)
        {
            try { return type.Resolve(); }
            catch { return null; }
        }

        private static bool IsInt32Enum(TypeDefinition type)
        {
            if (type == null || !type.IsEnum) return false;
            var valueField = type.Fields.FirstOrDefault(f => f.Name == "value__");
            return valueField != null && valueField.FieldType.FullName == "System.Int32";
        }

        private static TypeDefinition FindCueEnum(ModuleDefinition module)
        {
            var required = new[] { "Battle1", "Boss1", "Fanfare", "FanfareBoss" };
            foreach (var type in EnumerateTypes(module))
            {
                if (!type.IsEnum) continue;
                var names = new HashSet<string>(
                    type.Fields.Where(f => f.IsStatic && f.HasConstant).Select(f => f.Name),
                    StringComparer.Ordinal);
                bool all = true;
                foreach (var name in required)
                {
                    if (!names.Contains(name)) { all = false; break; }
                }
                if (all) return type;
            }
            return null;
        }

        private static IEnumerable<TypeDefinition> EnumerateTypes(ModuleDefinition module)
        {
            foreach (var type in module.Types)
            {
                foreach (var item in EnumerateTypeRecursive(type))
                    yield return item;
            }
        }

        private static IEnumerable<TypeDefinition> EnumerateTypeRecursive(TypeDefinition type)
        {
            yield return type;
            if (type.HasNestedTypes)
            {
                foreach (var nested in type.NestedTypes)
                {
                    foreach (var item in EnumerateTypeRecursive(nested))
                        yield return item;
                }
            }
        }

        private static Dictionary<string, int> ReadEnumValues(TypeDefinition enumType)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var field in enumType.Fields)
            {
                if (!field.IsStatic || !field.HasConstant) continue;
                result[field.Name] = Convert.ToInt32(field.Constant, CultureInfo.InvariantCulture);
            }
            return result;
        }

        private static int[] ResolveRequired(Dictionary<string, int> values, string[] names, string label)
        {
            var list = new List<int>();
            foreach (var name in names)
            {
                int value;
                if (!values.TryGetValue(name, out value))
                    throw new InvalidOperationException("Missing " + label + " TrackCue value: " + name);
                list.Add(value);
            }
            return list.ToArray();
        }

        private static void CompileRuntime(string outputPath, int[] normal, int[] boss, int[] victory, Dictionary<string, int> values)
        {
            var source = BuildRuntimeSource(normal, boss, victory, values);
            if (File.Exists(outputPath)) File.Delete(outputPath);

            using (var provider = new CSharpCodeProvider())
            {
                var cp = new CompilerParameters();
                cp.GenerateExecutable = false;
                cp.GenerateInMemory = false;
                cp.IncludeDebugInformation = false;
                cp.OutputAssembly = outputPath;
                cp.CompilerOptions = "/optimize+ /target:library";
                cp.ReferencedAssemblies.Add("System.dll");
                cp.ReferencedAssemblies.Add("System.Core.dll");

                var results = provider.CompileAssemblyFromSource(cp, source);
                if (results.Errors.HasErrors)
                {
                    var msgs = results.Errors.Cast<CompilerError>().Select(e => e.ToString());
                    throw new InvalidOperationException("Runtime compilation failed:\r\n" + string.Join("\r\n", msgs));
                }
            }
        }

        private static string BuildRuntimeSource(int[] normal, int[] boss, int[] victory, Dictionary<string, int> values)
        {
            Func<int[], string> arr = xs => string.Join(",", xs.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray());
            return @"
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
        private static readonly int[] NormalPool = new int[] { " + arr(normal) + @" };
        private static readonly int[] BossPool = new int[] { " + arr(boss) + @" };
        private static readonly int[] VictoryPool = new int[] { " + arr(victory) + @" };
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
                    Log(""ROUTE"", ""DEBOUNCE cue="" + cue + "" within=1500ms"");
                    return cue;
                }
                LastBattleCue = cue;
                LastBattleApplyUtc = now;

                if (Contains(BossPool, cue))
                    ApplyCustomTrack(""BOSS"", cue, ref LastBoss);
                else if (Contains(NormalPool, cue))
                    ApplyCustomTrack(""BATTLE"", cue, ref LastNormal);
                else
                    Log(""ROUTE"", ""UNROUTED battle cue="" + cue + "" (not in normal/boss cue sets)"");
                return cue;
            }
        }

        public static int RandomizeVictoryInt(int cue)
        {
            lock (Gate)
            {
                if (Contains(VictoryPool, cue))
                    ApplyCustomTrack(""VICTORY"", cue, ref LastVictory);
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
                    Log(category, ""SKIP cue="" + cue + "" reason=pool-folder-missing path="" + poolFolder);
                    return;
                }

                string[] pool = LoadPool(poolFolder);
                if (pool.Length == 0)
                {
                    Log(category, ""SKIP cue="" + cue + "" reason=empty-pool path="" + poolFolder);
                    return;
                }

                string selected = Pick(pool, ref last);
                LoopInfo loop = ReadLoopInfo(selected);
                string detail;
                if (!TryAssignTrack(cue, selected, loop, out detail))
                {
                    Log(category, ""ERROR cue="" + cue + "" file="" + Path.GetFileName(selected) + "" reason="" + detail);
                    return;
                }

                string loopText = loop.HasStart ? loop.Start.ToString(""0.000000"", CultureInfo.InvariantCulture) : ""original"";
                string endText = loop.HasEnd ? loop.End.ToString(""0.000000"", CultureInfo.InvariantCulture) : ""original"";
                Log(category, ""cue="" + cue + "" selectedPath="" + selected + "" pool="" + pool.Length + "" rawLoopStart="" + (loop.RawStart ?? ""<none>"") + "" loopStartSec="" + loopText + "" loopStartSamples="" + loop.StartSamples + "" loopStartUnit="" + (loop.StartUnit ?? ""<none>"") + "" loopEndSec="" + endText + "" loopEndSamples="" + loop.EndSamples + "" loopEndUnit="" + (loop.EndUnit ?? ""<none>"") + "" sampleRate="" + loop.SampleRate + "" totalSamples="" + loop.TotalSamples + "" loopSource="" + loop.Source + "" | "" + detail);
            }
            catch (Exception ex)
            {
                Log(category, ""ERROR cue="" + cue + "" exception="" + ex.GetType().FullName + "": "" + ex.Message);
            }
        }

        private static string GetPoolFolder(string gameDir, string category)
        {
            string folder = category == ""BATTLE"" ? ""Battle"" : (category == ""BOSS"" ? ""Boss"" : ""Victory"");
            return Path.Combine(gameDir, ""ogg"", folder);
        }

        private static string[] LoadPool(string poolFolder)
        {
            try
            {
                string[] files = Directory.GetFiles(poolFolder, ""*.ogg"", SearchOption.TopDirectoryOnly);
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
            result.Source = ""none"";
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
                bool hasStart = TryFindTag(text, new string[] { ""LOOPSTART="", ""LOOP_START="" }, out startRaw);
                bool hasEnd = TryFindTag(text, new string[] { ""LOOPEND="", ""LOOP_END="" }, out endRaw);
                bool hasLength = TryFindTag(text, new string[] { ""LOOPLENGTH="", ""LOOP_LENGTH="" }, out lengthRaw);

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
                    result.EndUnit = ""start+"" + lengthUnit;
                }
                else if (result.HasStart && result.SampleRate > 0 && result.TotalSamples > 0)
                {
                    // LOOPSTART-only convention: the physical end of the Vorbis
                    // stream is the loop end. Keep both seconds and PCM samples so
                    // we can populate whichever member shape Crystal Project uses.
                    result.HasEnd = true;
                    result.End = (double)result.TotalSamples / (double)result.SampleRate;
                    result.EndSamples = result.TotalSamples;
                    result.EndUnit = ""physical-eof"";
                }

                if (result.HasStart || result.HasEnd)
                    result.Source = ""vorbis-comments"";
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
            unit = ""unknown"";
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
                unit = ""decimal-seconds"";
                return durationSeconds <= 0.0 || seconds <= durationSeconds + 1.0;
            }

            if (durationSeconds > 0.0 && parsed <= durationSeconds + 1.0)
            {
                seconds = parsed;
                samples = sampleRate > 0 ? (long)Math.Round(seconds * sampleRate) : 0;
                unit = ""integer-seconds"";
                return true;
            }

            if (sampleRate > 0)
            {
                double sampleSeconds = parsed / (double)sampleRate;
                if (durationSeconds <= 0.0 || sampleSeconds <= durationSeconds + 1.0)
                {
                    seconds = sampleSeconds;
                    samples = (long)Math.Round(parsed);
                    unit = ""pcm-samples"";
                    return true;
                }
            }

            // Last-resort compatibility for hand-authored integer-second tags
            // when duration probing failed.
            seconds = parsed;
            samples = sampleRate > 0 ? (long)Math.Round(seconds * sampleRate) : 0;
            unit = ""integer-seconds-fallback"";
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
            detail = """";
            Type caudio = FindType(""Sang.Audio.CAudio"");
            if (caudio == null) { detail = ""Sang.Audio.CAudio not found""; return false; }

            object tracks = GetStaticMember(caudio, ""TRACKS"");
            if (tracks == null) { detail = ""CAudio.TRACKS not found/null""; return false; }

            object key;
            object track = GetTrack(tracks, cue, out key);
            if (track == null) { detail = ""track entry not found for cue "" + cue; return false; }

            string pathMember;
            if (!SetMember(track, new string[] { ""Path"" }, selected, out pathMember))
            {
                detail = ""track Path member not writable; trackType="" + track.GetType().FullName + "" members="" + MemberNames(track.GetType());
                return false;
            }

            string playStartMember;
            SetMember(track, new string[] { ""PlayStart"", ""playStart"" }, 0.0, out playStartMember);

            string loopStartMember;
            string loopEndMember;
            string loopContainerDetail;
            ApplyLoopPoints(track, loop, out loopStartMember, out loopEndMember, out loopContainerDetail);

            if (track.GetType().IsValueType && !SetTrack(tracks, key, track))
            {
                detail = ""track value modified but could not write boxed/value entry back; tracksType="" + tracks.GetType().FullName;
                return false;
            }

            if (!ReflectionDetailsLogged)
            {
                ReflectionDetailsLogged = true;
                Log(""REFLECTION"", ""CAudio="" + caudio.FullName + "" TRACKS="" + tracks.GetType().FullName + "" track="" + track.GetType().FullName + "" members="" + MemberNames(track.GetType()) + "" | "" + loopContainerDetail);
            }
            detail = ""pathMember="" + pathMember + "" playStartMember="" + playStartMember + "" loopStartMember="" + loopStartMember + "" loopEndMember="" + loopEndMember + "" | "" + loopContainerDetail;
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
            startUsed = loop.HasStart ? ""not-found"" : ""unchanged"";
            endUsed = loop.HasEnd ? ""not-found"" : ""unchanged"";
            containerDetail = ""loopContainer=not-found"";

            object loopPoint;
            Type loopPointType;
            string loopContainerMember;
            if (TryGetMember(track, new string[] { ""LoopPoint"", ""loopPoint"" }, out loopPoint, out loopPointType, out loopContainerMember))
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
                        new string[] { ""LoopPointStart"", ""loopPointStart"", ""LoopStart"", ""loopStart"", ""Start"", ""start"" },
                        loop.Start, loop.StartSamples, out startUsed);
                    bool endOk = !loop.HasEnd || SetLoopScalar(
                        loopPoint,
                        new string[] { ""LoopPointEnd"", ""loopPointEnd"", ""LoopEnd"", ""loopEnd"", ""End"", ""end"" },
                        loop.End, loop.EndSamples, out endUsed);

                    string writeBackMember;
                    bool wroteBack = SetMember(track, new string[] { ""LoopPoint"", ""loopPoint"" }, loopPoint, out writeBackMember);

                    containerDetail = ""loopContainer="" + loopContainerMember +
                        "" loopType="" + loopPoint.GetType().FullName +
                        "" loopMembers="" + MemberNames(loopPoint.GetType()) +
                        "" loopWriteBack="" + (wroteBack ? writeBackMember : ""not-written"");

                    if (startOk && endOk)
                    {
                        startUsed = startUsed == ""unchanged"" ? startUsed : ""nested:"" + startUsed;
                        endUsed = endUsed == ""unchanged"" ? endUsed : ""nested:"" + endUsed;
                        return;
                    }
                }
                else
                {
                    containerDetail = ""loopContainer="" + loopContainerMember + "" value=null type="" + (loopPointType == null ? ""<unknown>"" : loopPointType.FullName);
                }
            }

            // Compatibility fallback for builds that expose loop points directly
            // on TrackMetadata instead of through TrackMetadata.LoopPoint.
            if (loop.HasStart)
            {
                string directStart;
                if (SetLoopScalar(track,
                    new string[] { ""LoopPointStart"", ""loopPointStart"", ""LoopStart"", ""loopStart"", ""LoopStartSample"", ""LoopStartSamples"", ""loopStartSample"", ""loopStartSamples"" },
                    loop.Start, loop.StartSamples, out directStart))
                    startUsed = ""direct:"" + directStart;
            }
            if (loop.HasEnd)
            {
                string directEnd;
                if (SetLoopScalar(track,
                    new string[] { ""LoopPointEnd"", ""loopPointEnd"", ""LoopEnd"", ""loopEnd"", ""LoopEndSample"", ""LoopEndSamples"", ""loopEndSample"", ""loopEndSamples"" },
                    loop.End, loop.EndSamples, out directEnd))
                    endUsed = ""direct:"" + directEnd;
            }
        }

        private static bool TryGetMember(object target, string[] names, out object value, out Type memberType, out string used)
        {
            value = null;
            memberType = null;
            used = ""not-found"";
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
                        used = ""field:"" + f.Name;
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
                        used = ""property:"" + p.Name;
                        return true;
                    }
                    catch { }
                }
            }
            return false;
        }

        private static bool SetLoopScalar(object target, string[] names, double seconds, long samples, out string used)
        {
            used = ""not-found"";
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
                        used = ""field:"" + f.Name + "":"" + f.FieldType.Name + ""="" + Convert.ToString(value, CultureInfo.InvariantCulture);
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
                        used = ""property:"" + p.Name + "":"" + p.PropertyType.Name + ""="" + Convert.ToString(value, CultureInfo.InvariantCulture);
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
            used = ""not-found"";
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
                        used = ""field:"" + f.Name;
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
                        used = ""property:"" + p.Name;
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
                foreach (FieldInfo f in type.GetFields(flags)) names.Add(""F:"" + f.Name + "":"" + f.FieldType.Name);
                foreach (PropertyInfo p in type.GetProperties(flags)) names.Add(""P:"" + p.Name + "":"" + p.PropertyType.Name);
                return string.Join("","", names.ToArray());
            }
            catch { return ""<unavailable>""; }
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
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ""CrystalProjectRandomMusic"");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, ""randommusic.log"");
                File.AppendAllText(path, DateTime.Now.ToString(""yyyy-MM-dd HH:mm:ss.fff"") + "" | "" + category + "" | "" + message + Environment.NewLine);
            }
            catch { }
        }
    }
}
";
        }

        private static void VerifyPatch(string patchedExe, string battleHookName, string victoryHookName)
        {
            using (var check = AssemblyDefinition.ReadAssembly(patchedExe))
            {
                if (!check.MainModule.AssemblyReferences.Any(r => r.Name == RuntimeAssemblyName))
                    throw new InvalidOperationException("Post-write verification failed: runtime assembly reference missing.");

                var fieldState = FindType(check.MainModule, "Sang.Field.FieldState");
                if (fieldState == null) throw new InvalidOperationException("Post-write verification failed: FieldState missing.");

                VerifyHook(fieldState, "GetBattleCue", battleHookName);
                VerifyHook(fieldState, "GetFanfareCue", victoryHookName);
            }
        }

        private static void VerifyHook(TypeDefinition type, string methodName, string hookName)
        {
            var method = type.Methods.FirstOrDefault(m => m.Name == methodName && m.HasBody);
            if (method == null) throw new InvalidOperationException("Post-write verification failed: " + methodName + " missing.");
            bool found = method.Body.Instructions.Any(i =>
                i.OpCode == OpCodes.Call &&
                i.Operand is MethodReference &&
                ((MethodReference)i.Operand).Name == hookName &&
                ((MethodReference)i.Operand).DeclaringType.FullName == RuntimeTypeName);
            if (!found) throw new InvalidOperationException("Post-write verification failed: " + hookName + " call missing.");
        }

        private static TypeDefinition FindType(ModuleDefinition module, string fullName)
        {
            foreach (var t in module.Types)
            {
                var found = FindTypeRecursive(t, fullName);
                if (found != null) return found;
            }
            return null;
        }

        private static TypeDefinition FindTypeRecursive(TypeDefinition type, string fullName)
        {
            if (type.FullName == fullName) return type;
            if (type.HasNestedTypes)
            {
                foreach (var n in type.NestedTypes)
                {
                    var found = FindTypeRecursive(n, fullName);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }
}
