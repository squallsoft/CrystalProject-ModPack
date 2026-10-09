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
                    double lower = Math.Max(1, Math.Max((width - 0.5) / size.Width, (height - 0.5) / size.Height));
                    double upper = Math.Min(10, Math.Min((width + 0.5) / size.Width, (height + 0.5) / size.Height));
                    if (width >= size.Width && height >= size.Height && width <= size.Width * 10 && height <= size.Height * 10 && lower <= upper)
                        Sizes.Add(texture, size);
                }
                return texture;
            }
        }
        public static int LogicalWidth(object texture) { lock (Gate) { Size size; return Sizes.TryGetValue(texture, out size) ? size.Width : Actual(texture, "Width"); } }
        public static int LogicalHeight(object texture) { lock (Gate) { Size size; return Sizes.TryGetValue(texture, out size) ? size.Height : Actual(texture, "Height"); } }
        public static float RenderScale(float scale, object texture) { return scale * LogicalWidth(texture) / Actual(texture, "Width"); }
        // Preserve the game's integer-rounded center for odd-sized original canvases.
        public static int OriginWidth(object texture) { int width = LogicalWidth(texture); return 2 * Pixel((width / 2) * (Actual(texture, "Width") / (double)width)); }
        public static int OriginHeight(object texture) { int height = LogicalHeight(texture); return 2 * Pixel((height / 2) * (Actual(texture, "Height") / (double)height)); }
        static int Pixel(double value) { return checked((int)Math.Round(value, MidpointRounding.AwayFromZero)); }
        public static object ScaleRegion(object region, object texture)
        {
            double sx = Actual(texture, "Width") / (double)LogicalWidth(texture), sy = Actual(texture, "Height") / (double)LogicalHeight(texture);
            if (sx == 1 && sy == 1) return region;
            var type = region.GetType(); FieldInfo x = type.GetField("X"), y = type.GetField("Y"), w = type.GetField("Width"), h = type.GetField("Height");
            int left = (int)x.GetValue(region), top = (int)y.GetValue(region);
            int right = left + (int)w.GetValue(region), bottom = top + (int)h.GetValue(region);
            int scaledLeft = Pixel(left * sx), scaledTop = Pixel(top * sy);
            x.SetValue(region, scaledLeft); y.SetValue(region, scaledTop);
            w.SetValue(region, Pixel(right * sx) - scaledLeft); h.SetValue(region, Pixel(bottom * sy) - scaledTop);
            return region;
        }
    }
}
