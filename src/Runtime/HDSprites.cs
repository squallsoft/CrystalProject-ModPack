using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace CrystalProjectHDSprites
{
    public static class Runtime
    {
        sealed class Size { public int Width, Height; public Size(int width, int height) { Width = width; Height = height; } }
        static readonly object Gate = new object();
        static readonly Dictionary<string, Dictionary<string, Size>> Catalogs = new Dictionary<string, Dictionary<string, Size>>(StringComparer.OrdinalIgnoreCase);
        static readonly ConditionalWeakTable<object, Size> Sizes = new ConditionalWeakTable<object, Size>();
        static int Actual(object texture, string property) { return (int)texture.GetType().GetProperty(property).GetValue(texture, null); }
        static Dictionary<string, Size> Catalog(string game)
        {
            Dictionary<string, Size> catalog;
            if (Catalogs.TryGetValue(game, out catalog)) return catalog;
            catalog = new Dictionary<string, Size>(StringComparer.Ordinal);
            string path = Path.Combine(game, "Mods", "CrystalProjectModManager", "sprite-sizes.txt");
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("HD sprite catalog is too large.");
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0 || lines[0] != "CrystalProjectHDSprites:1") throw new InvalidDataException("Invalid HD sprite catalog.");
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split('|'); int width, height;
                if (parts.Length != 3 || !parts[0].StartsWith("Monster/", StringComparison.Ordinal) ||
                    !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out width) ||
                    !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out height) || width <= 0 || height <= 0 || width > 8192 || height > 8192 || catalog.ContainsKey(parts[0]))
                    throw new InvalidDataException("Invalid HD sprite catalog entry.");
                catalog.Add(parts[0], new Size(width, height));
            }
            Catalogs.Add(game, catalog); return catalog;
        }
        // Called on each CBattle texture lookup, using the actual selected original/alternate key.
        public static object Register(string key, object texture, object monster)
        {
            if (texture == null || monster == null || key == null || !key.StartsWith("Monster/", StringComparison.Ordinal)) return texture;
            lock (Gate)
            {
                Size size;
                if (Sizes.TryGetValue(texture, out size)) return texture;
                string game = Path.GetDirectoryName(monster.GetType().Assembly.Location);
                if (Catalog(game).TryGetValue(key, out size))
                {
                    int width = Actual(texture, "Width"), height = Actual(texture, "Height");
                    if (width == size.Width && height == size.Height || width == size.Width * 2 && height == size.Height * 2 || width == size.Width * 4 && height == size.Height * 4)
                        Sizes.Add(texture, size);
                }
                return texture;
            }
        }
        public static int LogicalWidth(object texture) { lock (Gate) { Size size; return Sizes.TryGetValue(texture, out size) ? size.Width : Actual(texture, "Width"); } }
        public static int LogicalHeight(object texture) { lock (Gate) { Size size; return Sizes.TryGetValue(texture, out size) ? size.Height : Actual(texture, "Height"); } }
        public static float RenderScale(float scale, object texture) { return scale * LogicalWidth(texture) / Actual(texture, "Width"); }
        // Preserve the game's integer-rounded center for odd-sized original canvases.
        public static int OriginWidth(object texture) { int width = LogicalWidth(texture); return (width / 2) * 2 * (Actual(texture, "Width") / width); }
        public static int OriginHeight(object texture) { int height = LogicalHeight(texture); return (height / 2) * 2 * (Actual(texture, "Height") / height); }
        public static object ScaleRegion(object region, object texture)
        {
            int factor = Actual(texture, "Width") / LogicalWidth(texture);
            if (factor == 1) return region;
            foreach (string name in new[] { "X", "Y", "Width", "Height" })
            {
                var field = region.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
                field.SetValue(region, checked((int)field.GetValue(region) * factor));
            }
            return region;
        }
    }
}
