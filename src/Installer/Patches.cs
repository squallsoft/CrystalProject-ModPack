using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text;

namespace CrystalProjectModInstaller;

internal static class Patches
{
    public const string Original = "36f7d413160a4deee36b47fc6ac534e87cadb6f23f57337d4630ec99cedb14e6";
    public static readonly string[] Legacy = ["4f9d37b996268fd9de38ec49746474cb9531f2bcc3496ca5d13e43c2475138e7", "1768ff8268fc09f817c8109a43e683411f0f647bb7d74928c00f1a102a5a38f3", "65cfbba9ed7af1f0c7db110f85719fa695a92cc088873a157fa95d92fa720d79"];
    public static string Hash(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant(); }
    public static byte[] Resource(string name) { using var s = typeof(Patches).Assembly.GetManifestResourceStream(name) ?? throw new IOException("Missing resource " + name); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); }
    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[] { t }.Concat(Types(t.NestedTypes)));
    static TypeDefinition Type(ModuleDefinition m, string name) => Types(m.Types).Single(t => t.FullName == name);
    static MethodDefinition Method(TypeDefinition t, string name, int? args = null) => t.Methods.Single(m => m.Name == name && (args == null || m.Parameters.Count == args));
    static void LongBranches(MethodDefinition m)
    {
        var codes = typeof(OpCodes).GetFields().Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Name);
        foreach (var i in m.Body.Instructions)
            if (i.OpCode.OperandType == OperandType.ShortInlineBrTarget) i.OpCode = codes[i.OpCode.Name[..^2]];
    }
    static void Prefix(MethodDefinition m, params Instruction[] prefix)
    {
        var first = m.Body.Instructions[0]; var il = m.Body.GetILProcessor();
        foreach (var i in prefix) il.InsertBefore(first, i);
    }
    public static void Build(string original, string output, bool music, bool home)
    {
        if (Hash(original) != Original) throw new InvalidOperationException("Pristine backup is corrupt or unsupported.");
        if (!music && !home) { File.Copy(original, output, true); return; }
        using var asm = AssemblyDefinition.ReadAssembly(original, new ReaderParameters { InMemory = true });
        var module = asm.MainModule;
        if (music)
        {
            using var runtime = AssemblyDefinition.ReadAssembly(new MemoryStream(Resource("CrystalProjectRandomMusic.dll")));
            var rt = Type(runtime.MainModule, "CrystalProjectRandomMusic.Runtime");
            var field = Type(module, "Sang.Field.FieldState");
            foreach (var pair in new[] { ("GetBattleCue", "RandomizeBattleNullable"), ("GetFanfareCue", "RandomizeVictoryNullable") })
            {
                var m = Method(field, pair.Item1); LongBranches(m);
                if (m.ReturnType is not GenericInstanceType g || g.ElementType.FullName != "System.Nullable`1") throw new InvalidOperationException("Unexpected music return type.");
                var hook = new GenericInstanceMethod(module.ImportReference(Method(rt, pair.Item2)));
                hook.GenericArguments.Add(module.ImportReference(g.GenericArguments[0]));
                // Keep the certified wrapping strategy and runtime behavior.
                foreach (var ret in m.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
                    m.Body.GetILProcessor().InsertBefore(ret, Instruction.Create(OpCodes.Call, hook));
            }
        }
        if (home)
        {
            using var runtime = AssemblyDefinition.ReadAssembly(new MemoryStream(Resource("CrystalProjectHomePoints.dll")));
            var rt = Type(runtime.MainModule, "CrystalProjectHomePoints.Runtime");
            MethodReference Hook(string name) => module.ImportReference(Method(rt, name));
            var collection = Type(module, "Sang.PartyData.HomePointCollection");
            var sanitize = Method(collection, "Sanitize", 0);
            sanitize.Body = new MethodBody(sanitize);
            sanitize.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            sanitize.Body.Instructions.Add(Instruction.Create(OpCodes.Call, Hook("Ensure")));
            sanitize.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            foreach (var name in new[] { "IsSet", "Get", "Set" })
            {
                var m = Method(collection, name); LongBranches(m);
                var bound = m.Body.Instructions.Single(i => i.OpCode == OpCodes.Bgt);
                bound.OpCode = OpCodes.Bge;
                if (name == "Set") Prefix(m, Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Call, Hook("BeforeSet")));
                else Prefix(m, Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Call, Hook("Ensure")));
            }
            Prefix(Method(collection, "Clear"), Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Call, Hook("Reset")));
            Prefix(Method(Type(module, "Sang.PartyData.Party"), "Sanitize"), Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Call, Hook("EnsureParty")));
            var window = Type(module, "Sang.Window.Field.Entity.WindowHomePointSelect");
            var refresh = Method(window, "RefreshContent"); LongBranches(refresh);
            var limits = refresh.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldc_I4_3).ToArray();
            if (limits.Length != 2) throw new InvalidOperationException("Unexpected Home Point menu.");
            limits[0].OpCode = OpCodes.Call; limits[0].Operand = Hook("VisibleLength");
            limits[1].OpCode = OpCodes.Call; limits[1].Operand = Hook("NewSlotLimit");
            var input = Method(window, "UpdateInput");
            var fallback = input.Body.Instructions.Single(i => i.Offset == 0x10c && i.OpCode == OpCodes.Ldc_I4_3);
            fallback.OpCode = OpCodes.Call; fallback.Operand = Hook("VisibleLength");
            foreach (var m in new[] { input, Method(window, "OnPopupConfirmationOK") })
            {
                LongBranches(m); var il = m.Body.GetILProcessor();
                foreach (var i in m.Body.Instructions.Where(i => i.Operand is MethodReference r && r.Name == "get_SelectedIndex").ToArray())
                    il.InsertAfter(i, Instruction.Create(OpCodes.Call, Hook("SlotForRow")));
                if (m.Name == "OnPopupConfirmationOK")
                {
                    var current = m.Body.Instructions.Single(i => i.Offset == 0x5d && i.Operand is FieldReference f && f.Name == "_currentlySetToIndex");
                    il.InsertAfter(current, Instruction.Create(OpCodes.Call, Hook("SlotForRow")));
                }
            }
        }
        asm.Write(output, new WriterParameters { DeterministicMvid = true, Timestamp = 0 });
        Verify(original, output, music, home);
    }
    static string Body(MethodDefinition m)
    {
        if (!m.HasBody) return "";
        string Operand(object? o) => o switch { Instruction i => "@" + m.Body.Instructions.IndexOf(i), Instruction[] a => string.Join(",", a.Select(i => Operand(i))), _ => o?.ToString() ?? "" };
        return string.Join("\n", m.Body.Instructions.Select(i => i.OpCode + " " + Operand(i.Operand)));
    }
    public static void Verify(string original, string output, bool music, bool home)
    {
        using var before = AssemblyDefinition.ReadAssembly(original);
        using var after = AssemblyDefinition.ReadAssembly(output);
        var a = Types(after.MainModule.Types).ToDictionary(t => t.FullName);
        var allowed = new HashSet<string>();
        if (music) foreach (var n in new[] { "GetBattleCue", "GetFanfareCue" }) allowed.Add("Sang.Field.FieldState::" + n);
        if (home)
        {
            foreach (var n in new[] { "Sanitize", "Clear", "IsSet", "Get", "Set" }) allowed.Add("Sang.PartyData.HomePointCollection::" + n);
            allowed.Add("Sang.PartyData.Party::Sanitize");
            foreach (var n in new[] { "RefreshContent", "UpdateInput", "OnPopupConfirmationOK" }) allowed.Add("Sang.Window.Field.Entity.WindowHomePointSelect::" + n);
        }
        foreach (var t in Types(before.MainModule.Types))
        {
            var other = a[t.FullName];
            foreach (var f in t.Fields.Where(f => f.HasConstant))
                if (!Equals(f.Constant, other.Fields.Single(x => x.Name == f.Name).Constant)) throw new InvalidOperationException("Constant changed: " + f.FullName);
            foreach (var m in t.Methods)
            {
                var n = other.Methods.Single(x => x.FullName == m.FullName);
                if (!allowed.Contains(t.FullName + "::" + m.Name) && Body(m) != Body(n)) throw new InvalidOperationException("Unrelated method changed: " + m.FullName);
                if (n.HasBody) foreach (var i in n.Body.Instructions)
                {
                    if (i.Operand is Instruction target && !n.Body.Instructions.Contains(target)) throw new InvalidOperationException("Invalid branch.");
                }
            }
        }
        foreach (var item in new[] { (music, "CrystalProjectRandomMusic"), (home, "CrystalProjectHomePoints") })
            if (after.MainModule.AssemblyReferences.Any(r => r.Name == item.Item2) != item.Item1) throw new InvalidOperationException("Runtime reference mismatch.");
        if (home && !Body(Method(Type(after.MainModule, "Sang.PartyData.HomePointCollection"), "Set")).Contains("BeforeSet")) throw new InvalidOperationException("Expansion hook missing.");
    }
}
