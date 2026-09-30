using System;
using System.Reflection;

namespace CrystalProjectHomePoints
{
    // No game code or data is embedded. Reflection keeps the helper independent
    // of the game's internal types and of the installer's .NET 8 runtime.
    public static class Runtime
    {
        const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        static FieldInfo Field(object o, string name) { return o.GetType().GetField(name, Fields); }
        static object Party()
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType("Sang.SangServices");
                if (t != null) return t.GetField("Party", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            }
            return null;
        }
        static object Collection() { object p = Party(); return p == null ? null : Field(p, "HomePoints").GetValue(p); }
        public static bool Enabled()
        {
            object p = Party();
            if (p == null) return false;
            object f = Field(p, "GameplayFlags").GetValue(p);
            return f != null && (bool)Field(f, "MultiHomePoint").GetValue(f);
        }
        static Array Data(object collection) { return (Array)Field(collection, "_homePoints").GetValue(collection); }
        static Array Empty(object collection, int size)
        {
            Type element = Field(collection, "_homePoints").FieldType.GetElementType();
            Array result = Array.CreateInstance(element, size);
            for (int i = 0; i < size; i++)
            {
                object value = Activator.CreateInstance(element);
                Field(value, "ID").SetValue(value, -1);
                Field(value, "Name").SetValue(value, "");
                result.SetValue(value, i);
            }
            return result;
        }
        public static void Ensure(object collection)
        {
            Array old = Data(collection);
            if (old != null && old.Length >= 3) return;
            Array next = Empty(collection, 3);
            if (old != null) Array.Copy(old, next, old.Length);
            Field(collection, "_homePoints").SetValue(collection, next);
        }
        public static void EnsureParty(object party)
        {
            FieldInfo f = Field(party, "HomePoints");
            if (f.GetValue(party) == null) f.SetValue(party, Activator.CreateInstance(f.FieldType, true));
            Ensure(f.GetValue(party));
        }
        public static void Reset(object collection) { Field(collection, "_homePoints").SetValue(collection, Empty(collection, 3)); }
        public static void BeforeSet(object collection, int slot)
        {
            Ensure(collection);
            Array old = Data(collection);
            if (slot != old.Length || !Enabled() || slot >= int.MaxValue - 1) return;
            Array next = Empty(collection, checked(old.Length + 1));
            Array.Copy(old, next, old.Length);
            Field(collection, "_homePoints").SetValue(collection, next);
        }
        public static int VisibleLength()
        {
            object c = Collection();
            if (c == null) return 3;
            Ensure(c);
            return Enabled() ? Data(c).Length : 3;
        }
        public static int NewSlotLimit() { return Enabled() ? int.MaxValue - 1 : 3; }
        static bool Valid(object value)
        {
            return (int)Field(value, "ID").GetValue(value) >= 0 && !string.IsNullOrEmpty((string)Field(value, "Name").GetValue(value));
        }
        // Menu rows omit empty slots. Translate their indices without compacting
        // saved arrays or changing duplicate/swap semantics.
        public static int SlotForRow(int row)
        {
            if (row < 0) return -1;
            object c = Collection();
            if (c == null) return -1;
            Ensure(c);
            Array a = Data(c);
            int length = Enabled() ? a.Length : Math.Min(3, a.Length);
            int count = 0, empty = -1;
            for (int i = 0; i < length; i++)
            {
                if (Valid(a.GetValue(i))) { if (count++ == row) return i; }
                else if (empty < 0) empty = i;
            }
            return row == count ? (empty >= 0 ? empty : length) : -1;
        }
    }
}
