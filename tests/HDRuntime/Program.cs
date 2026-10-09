using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using CrystalProjectHDSprites;

class Program
{
    const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static int count;
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); count++; Console.WriteLine("PASS " + name); }
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => { string p = Path.Combine(args[1], new AssemblyName(e.Name).Name + ".dll"); return File.Exists(p) ? Assembly.LoadFrom(p) : null; };
        var game = Assembly.LoadFrom(Path.Combine(args[0], "Crystal Project.exe"));
        var services = game.GetType("Sang.SangServices"); var userField = services.GetField("UserData", Fields); object user = FormatterServices.GetUninitializedObject(userField.FieldType); userField.SetValue(null, user);
        var cache = game.GetType("Sang.Gfx.Cache"); var cacheField = cache.GetField("_textures", Fields); var textures = (IDictionary)Activator.CreateInstance(cacheField.FieldType); cacheField.SetValue(null, textures);
        var textureType = cacheField.FieldType.GetGenericArguments()[1];
        object Texture(int width, int height)
        {
            object texture = FormatterServices.GetUninitializedObject(textureType);
            GC.SuppressFinalize(texture);
            textureType.GetProperty("Width", Fields).SetValue(texture, width, null); textureType.GetProperty("Height", Fields).SetValue(texture, height, null); return texture;
        }
        object normal = Texture(10, 20), hd2 = Texture(20, 40), hd4 = Texture(40, 80), hd10 = Texture(100, 200), alt = Texture(48, 24);
        var monsterType = game.GetType("Sang.SangData.SangMonster"); object monster = FormatterServices.GetUninitializedObject(monsterType); monsterType.GetField("TexturePath", Fields).SetValue(monster, "Monster/Test"); monsterType.GetField("TexturePathAlt", Fields).SetValue(monster, "Monster/Alt");
        var get = game.GetType("Sang.Battle.CBattle").GetMethod("GetMonsterTexture", Fields);
        foreach (var item in new[] { normal, hd2, hd4, hd10 })
        {
            textures["Monster/Test"] = item; object selected = get.Invoke(null, new[] { monster });
            Check(ReferenceEquals(selected, item) && Runtime.LogicalWidth(item) == 10 && Runtime.LogicalHeight(item) == 20, "Patched texture lookup registers logical dimensions for actual width " + textureType.GetProperty("Width").GetValue(item, null));
            object battler = FormatterServices.GetUninitializedObject(game.GetType("Sang.Battle.BattlerMonster"));
            battler.GetType().GetMethod("SetMonsterTexture", Fields).Invoke(battler, new[] { monster }); object scale = battler.GetType().GetField("_textureScale", Fields).GetValue(battler);
            Check((float)scale.GetType().GetField("X").GetValue(scale) == 0.25f && (float)scale.GetType().GetField("Y").GetValue(scale) == 0.5f, "Actual patched battle sizing remains constant at all resolutions");
        }
        textures["Monster/Alt"] = alt; user.GetType().GetField("ProfanityFilter", Fields).SetValue(user, true);
        Check(ReferenceEquals(get.Invoke(null, new[] { monster }), alt) && Runtime.LogicalWidth(alt) == 12 && Runtime.LogicalHeight(alt) == 6, "Alternate texture uses its own original dimensions");
        Check(Math.Abs(Runtime.RenderScale(2f, hd4) - 0.5f) < 0.00001f, "HD atlas scale retains original displayed dimensions");
        Check(Math.Abs(Runtime.RenderScale(2f, hd10) - 0.2f) < 0.00001f, "10× atlas scale retains original displayed dimensions");
        var rectangleType = textureType.Assembly.GetType("Microsoft.Xna.Framework.Rectangle"); object region = Activator.CreateInstance(rectangleType, new object[] { 3, 5, 40, 20 }); object scaled = Runtime.ScaleRegion(region, hd4);
        Check((int)rectangleType.GetField("X").GetValue(scaled) == 12 && (int)rectangleType.GetField("Y").GetValue(scaled) == 20 && (int)rectangleType.GetField("Width").GetValue(scaled) == 160 && (int)rectangleType.GetField("Height").GetValue(scaled) == 80, "Portrait crop uses actual HD pixel coordinates");
        object tenRegion = Activator.CreateInstance(rectangleType, new object[] { 3, 5, 4, 2 }); object tenScaled = Runtime.ScaleRegion(tenRegion, hd10);
        Check((int)rectangleType.GetField("X").GetValue(tenScaled) == 30 && (int)rectangleType.GetField("Height").GetValue(tenScaled) == 20, "10× portrait crops use actual image pixels");
        object unrelated = Texture(80, 60); Check(Runtime.LogicalWidth(unrelated) == 80 && Runtime.LogicalHeight(unrelated) == 60, "Unregistered textures retain their actual dimensions");
        Check(Runtime.RenderScale(2f, normal) == 2f, "Original sprites retain normal atlas scale");
        object odd = Texture(44, 28); Runtime.Register("Monster/Odd", odd, monster);
        Check(Runtime.OriginWidth(odd) / 2 == 20 && Runtime.OriginHeight(odd) / 2 == 12, "Odd-size HD atlas centers preserve original integer rounding");
        Console.WriteLine(count + " HD runtime checks passed.");
    }
}
