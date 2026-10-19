using System.Collections.Concurrent;
using System.Reflection;
using dc.pr;
using ModCore.Utilities;

namespace DeadCellsMultiplayerMod.UI
{
    internal static class TitleScreenReflection
    {
        // GetMemberValue runs on per-frame paths (gamepad camera binding polling). Type.GetProperty
        // with BindingFlags.IgnoreCase does a full metadata walk + case-insensitive compare every
        // call; cache the resolved accessor per (type, name). Not-found results are cached too.
        private static readonly ConcurrentDictionary<(Type, string, bool), MemberAccessor?> s_memberCache = new();

        private sealed class MemberAccessor
        {
            public readonly PropertyInfo? Property;
            public readonly FieldInfo? Field;

            public MemberAccessor(PropertyInfo? property, FieldInfo? field)
            {
                Property = property;
                Field = field;
            }

            public object? GetValue(object instance)
            {
                if (Property != null) return Property.GetValue(instance);
                return Field?.GetValue(instance);
            }

            public bool TrySetValue(object instance, object? value)
            {
                if (Property != null)
                {
                    if (!Property.CanWrite) return false;
                    Property.SetValue(instance, value);
                    return true;
                }
                if (Field != null)
                {
                    Field.SetValue(instance, value);
                    return true;
                }
                return false;
            }
        }

        private static MemberAccessor? ResolveMember(Type type, string name, bool ignoreCase)
        {
            return s_memberCache.GetOrAdd((type, name, ignoreCase), static key =>
            {
                const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var flags = key.Item3 ? Flags | BindingFlags.IgnoreCase : Flags;
                try
                {
                    var prop = key.Item1.GetProperty(key.Item2, flags);
                    if (prop != null) return new MemberAccessor(prop, null);

                    var field = key.Item1.GetField(key.Item2, flags);
                    if (field != null) return new MemberAccessor(null, field);
                }
                catch { }

                return null;
            });
        }

        public static object? GetMemberValue(object? obj, string name, bool ignoreCase)
        {
            if (obj == null || string.IsNullOrWhiteSpace(name)) return null;

            try
            {
                var accessor = ResolveMember(obj.GetType(), name, ignoreCase);
                return accessor?.GetValue(obj);
            }
            catch { }

            return null;
        }

        public static bool TrySetMember(object? obj, string name, object? value)
        {
            if (obj == null || string.IsNullOrWhiteSpace(name)) return false;

            try
            {
                var accessor = ResolveMember(obj.GetType(), name, ignoreCase: true);
                return accessor != null && accessor.TrySetValue(obj, value);
            }
            catch { }

            return false;
        }

        public static int GetArrayLength(object arrObj)
        {
            try
            {
                var lenObj = GetMemberValue(arrObj, "length", true);
                if (lenObj is IConvertible conv)
                    return conv.ToInt32(null);
            }
            catch { }
            return 0;
        }

        public static string GetMenuLabel(object? menuItem)
        {
            if (menuItem == null) return string.Empty;

            try
            {
                var t = GetMemberValue(menuItem, "t", true);
                if (t is dc.String ds)
                    return ds.ToString() ?? string.Empty;

                var textValue = GetMemberValue(t ?? menuItem, "text", true)
                             ?? GetMemberValue(t ?? menuItem, "str", true);
                if (textValue != null)
                    return textValue.ToString() ?? string.Empty;

                return t?.ToString() ?? menuItem.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static object? GetMenuItemAt(object? arrObj, int index)
        {
            if (arrObj == null || index < 0) return null;
            try
            {
                var getDyn = arrObj.GetType().GetMethod("getDyn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (getDyn == null || index >= GetArrayLength(arrObj)) return null;
                return getDyn.Invoke(arrObj, new object[] { index });
            }
            catch { }
            return null;
        }

        public static int FindMenuIndexByLabel(object? arrObj, string label)
        {
            if (arrObj == null) return -1;
            try
            {
                var type = arrObj.GetType();
                var getDyn = type.GetMethod("getDyn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (getDyn == null) return -1;

                int len = GetArrayLength(arrObj);
                for (int i = 0; i < len; i++)
                {
                    var item = getDyn.Invoke(arrObj, new object[] { i });
                    var text = GetMenuLabel(item);
                    if (text.Equals(label, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            catch { }
            return -1;
        }

        public static bool GetIsMainMenu(TitleScreen screen)
        {
            try
            {
                var val = GetMemberValue(screen, "isMainMenu", true);
                if (val is bool b) return b;
            }
            catch { }
            return false;
        }

        public static void SetIsMainMenu(TitleScreen screen, bool value)
        {
            try
            {
                TrySetMember(screen, "isMainMenu", value);
            }
            catch { }
        }

        public static dc.String MakeHLString(string value)
        {
            return value.AsHaxeString();
        }
    }
}
