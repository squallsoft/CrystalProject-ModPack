using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
class Program
{
    const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static int count;
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); count++; Console.WriteLine("PASS " + name); }
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => { string p = Path.Combine(args[1], new AssemblyName(e.Name).Name + ".dll"); return File.Exists(p) ? Assembly.LoadFrom(p) : null; };
        Assembly.LoadFrom(Path.Combine(args[2], "CrystalProjectHomePoints.dll"));
        var a = Assembly.LoadFrom(args[0]);
        var services = a.GetType("Sang.SangServices"); var partyType = a.GetType("Sang.PartyData.Party");
        var user = services.GetField("UserData", Fields); user.SetValue(null, FormatterServices.GetUninitializedObject(user.FieldType));
        object party = FormatterServices.GetUninitializedObject(partyType);
        services.GetField("Party", Fields).SetValue(null, party);
        var gameplayField = partyType.GetField("GameplayFlags", Fields);
        object flags = FormatterServices.GetUninitializedObject(gameplayField.FieldType); gameplayField.SetValue(party, flags);
        var flag = flags.GetType().GetField("MultiHomePoint", Fields);
        var type = a.GetType("Sang.PartyData.HomePointCollection");
        var arrField = type.GetField("_homePoints", Fields);
        object c = Activator.CreateInstance(type, true); partyType.GetField("HomePoints", Fields).SetValue(party, c);
        var coord = Activator.CreateInstance(a.GetType("Sang.Common.Point3"));
        object Call(string name, params object[] values) { return type.GetMethod(name, Array.ConvertAll(values, x => x.GetType())).Invoke(c, values); }
        int Length() { return ((Array)arrField.GetValue(c)).Length; }
        Check(Length() == 3, "Constructor retains three slots"); Call("Clear");
        Check(!(bool)Call("IsSet", 3) && !(bool)Call("IsSet", -1), "Boundary reads are safe");
        Call("Get", 3); Call("Set", 3, 99, "OFF", coord); Check(Length() == 3, "OFF refuses expansion");
        flag.SetValue(flags, true);
        for (int i = 0; i < 40; i++) { Call("Set", i, i + 100, "Point " + i, coord); Check(Length() == Math.Max(3, i + 1) && (bool)Call("IsSet", i), "Incremental point " + (i + 1)); }
        Call("Sanitize"); Check(Length() == 40, "Sanitize preserves expanded storage");
        Call("Set", 1000, 1, "skip", coord); Check(Length() == 40, "No sparse giant allocation");
        var serializer = new JsonSerializer { ObjectCreationHandling = ObjectCreationHandling.Replace };
        byte[] Save(object obj) { using (var ms = new MemoryStream()) { using (var w = new BsonWriter(ms) { CloseOutput = false }) serializer.Serialize(w, obj); return ms.ToArray(); } }
        object Load(byte[] bytes) { using (var ms = new MemoryStream(bytes)) using (var r = new BsonReader(ms)) return serializer.Deserialize(r, type); }
        byte[] saved = Save(c); c = Load(saved); partyType.GetField("HomePoints", Fields).SetValue(party, c); Call("Sanitize");
        Check(Length() == 40 && (bool)Call("IsSet", 0) && (bool)Call("IsSet", 20) && (bool)Call("IsSet", 39), "Real BSON round trip retains first/middle/last");
        Type helper = null; foreach (var x in AppDomain.CurrentDomain.GetAssemblies()) if (x.GetType("CrystalProjectHomePoints.Runtime") != null) helper = x.GetType("CrystalProjectHomePoints.Runtime");
        int Map(int row) { return (int)helper.GetMethod("SlotForRow").Invoke(null, new object[] { row }); }
        Check(Map(39) == 39 && Map(40) == 40, "Last row and new-slot mapping");
        Call("Set", 5, -1, "", coord); Check(Map(5) == 6 && Map(39) == 5, "Removed/gapped slot mapping and reuse");
        Call("Set", 5, 105, "Point 5", coord); Call("Set", 25, 999, "Replacement", coord);
        Check((bool)Call("IsHomePointSet", 999), "Replacement beyond three"); Check((bool)Call("IsHomePointSet", 139), "Duplicate search reaches last entry");
        flag.SetValue(flags, false); Call("Sanitize"); Check(Length() == 40 && (int)helper.GetMethod("VisibleLength").Invoke(null, null) == 3, "OFF preserves data and vanilla visible bound"); flag.SetValue(flags, true);
        Call("Clear"); Check(Length() == 3 && !(bool)Call("IsAnySet"), "Clear resets between saves");
        c = Load(saved); partyType.GetField("HomePoints", Fields).SetValue(party, c); Check(Length() == 40, "Expanded save reload after clear");
        for (int n = 0; n <= 3; n++)
        {
            var old = Array.CreateInstance(arrField.FieldType.GetElementType(), n); Array.Copy((Array)arrField.GetValue(c), old, n); arrField.SetValue(c, old); Call("Sanitize"); Check(Length() == 3, "Sanitize old save length " + n);
        }
        arrField.SetValue(c, null); Call("Sanitize"); Check(Length() == 3 && !(bool)Call("IsAnySet"), "Null array repaired safely");
        partyType.GetField("HomePoints", Fields).SetValue(party, null); helper.GetMethod("EnsureParty").Invoke(null, new[] { party }); Check(partyType.GetField("HomePoints", Fields).GetValue(party) != null, "Null collection repaired");
        var teleport = a.GetType("Sang.SangData.SangDataEnums.TeleportPoint"); Check(Convert.ToInt32(Enum.Parse(teleport, "SalmonSprintReception")) == 3, "Crash-causing unrelated enum preserved");
        c = Load(saved); partyType.GetField("HomePoints", Fields).SetValue(party, c);
        var menuType = a.GetType("Sang.Window.Menu"); object menu = FormatterServices.GetUninitializedObject(menuType);
        foreach (string f in new[] { "Items", "_pool" }) { var field = menuType.GetField(f, Fields); field.SetValue(menu, Activator.CreateInstance(field.FieldType)); }
        menuType.GetField("LinesCount", Fields).SetValue(menu, 3);
        var windowType = a.GetType("Sang.Window.Field.Entity.WindowHomePointSelect"); object window = FormatterServices.GetUninitializedObject(windowType);
        a.GetType("Sang.Window.WindowBase").GetField("Menu", Fields).SetValue(window, menu);
        var vocabField = a.GetType("Sang.SangData.CVocab").GetField("G", Fields); object vocab = FormatterServices.GetUninitializedObject(vocabField.FieldType);
        vocab.GetType().GetField("NewHomePointSlot", Fields).SetValue(vocab, "New Home Point Slot"); vocabField.SetValue(null, vocab);
        var entityType = a.GetType("Sang.Field.Entity.EntityHomePoint"); object entity = FormatterServices.GetUninitializedObject(entityType);
        a.GetType("Sang.Field.Entity.EntityBase").GetField("ID", Fields).SetValue(entity, 9999); windowType.GetField("_homePoint", Fields).SetValue(window, entity);
        windowType.GetMethod("RefreshContent", Fields).Invoke(window, null);
        var items = (System.Collections.IList)menuType.GetField("Items", Fields).GetValue(menu);
        Check(items.Count == 41, "Actual Set menu enumerates 40 points plus New Home Point Slot");
        for (int i = 0; i < 40; i++) menuType.GetMethod("InputDown").Invoke(menu, null);
        Check((int)menuType.GetProperty("SelectedIndex").GetValue(menu, null) == 40 && (int)menuType.GetField("_scrollPos", Fields).GetValue(menu) > 0, "Actual menu scroll reaches final new slot");
        var mode = windowType.GetField("_mode", Fields); mode.SetValue(window, Enum.ToObject(mode.FieldType, 1)); windowType.GetMethod("RefreshContent", Fields).Invoke(window, null);
        Check(items.Count == 40, "Actual Warp menu enumerates all points without new slot");
        foreach (var name in new[] { "UpdateInput", "OnPopupConfirmationOK" }) { System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(windowType.GetMethod(name, Fields).MethodHandle); Check(true, "JIT patched " + name); }
        Console.WriteLine(count + " runtime checks passed; no user saves opened.");
    }
}
