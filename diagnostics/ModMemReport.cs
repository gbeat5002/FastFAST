// Temporary diagnostic: estimate how much memory each mod assembly holds.
// Roots: static fields of every type in a mod assembly + every live MonoBehaviour/ScriptableObject whose type
// comes from that assembly. Walks the object graph, but never into game/engine types (so the game's own data
// isn't charged to a mod). Unity textures/render textures/meshes reached are charged at their estimated size.
// Shared objects are charged to the first mod that reaches them. Sizes are estimates.
// Writes modmem.txt to the game's data folder ~60 s after a city loads.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OnDemandAssets
{
    public class ModMemReport : MonoBehaviour
    {
        class RefEq : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object o) { return RuntimeHelpers.GetHashCode(o); }
        }

        class Tally { public string mod; public double managed, native; public long objects; public bool capped; public List<string> big = new List<string>(); }

        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        const long MaxObjectsPerMod = 8000000;   // ponytail: hard cap keeps the walk bounded; flagged in the report when hit

        static readonly string[] GameAssemblies = { "Assembly-CSharp", "Assembly-CSharp-firstpass", "ColossalManaged", "ICities", "UnityEngine",
            "UnityEngine.UI", "UnityEngine.Networking", "mscorlib", "System", "System.Core", "System.Xml", "System.Configuration", "System.Security",
            "Mono.Security", "Mono.Posix", "ICSharpCode.SharpZipLib", "PopsApiWrapper", "EOSSDK", "OnDemandAssets" };

        readonly HashSet<object> seen = new HashSet<object>(new RefEq());
        readonly Dictionary<Type, FieldInfo[]> fieldCache = new Dictionary<Type, FieldInfo[]>();
        readonly Dictionary<Type, int> sizeCache = new Dictionary<Type, int>();

        void Start() { StartCoroutine(Run()); }

        IEnumerator Run()
        {
            yield return new WaitForSeconds(60f);
            var started = DateTime.Now;
            var mods = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !GameAssemblies.Contains(a.GetName().Name) && !a.GetName().Name.StartsWith("System") && !a.GetName().Name.StartsWith("Mono"))
                .ToList();
            var behaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>().Cast<Object>()
                .Concat(Resources.FindObjectsOfTypeAll<ScriptableObject>().Cast<Object>()).ToList();

            var tallies = new List<Tally>();
            foreach (Assembly asm in mods)
            {
                var t = new Tally { mod = asm.GetName().Name };
                var roots = new List<object>();
                Type[] types;
                try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(x => x != null).ToArray(); }
                foreach (Type type in types)
                {
                    if (type.ContainsGenericParameters) continue;
                    FieldInfo[] statics;
                    try { statics = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly); } catch { continue; }
                    foreach (FieldInfo f in statics)
                    {
                        if (f.IsLiteral) continue;
                        object v; try { v = f.GetValue(null); } catch { continue; }
                        if (v != null) roots.Add(v);
                    }
                }
                foreach (Object b in behaviours) if (b != null && b.GetType().Assembly == asm) roots.Add(b);

                Walk(roots, t, true);
                tallies.Add(t);
                yield return null;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Per-mod memory estimate " + DateTime.Now + " (walk took " + (DateTime.Now - started).TotalSeconds.ToString("N0") + " s)");
            sb.AppendLine(string.Format("Managed heap total {0:N0} MB", GC.GetTotalMemory(false) / 1048576.0));
            sb.AppendLine();
            sb.AppendLine("     total    managed     native    objects  mod");
            foreach (var t in tallies.OrderByDescending(x => x.managed + x.native))
                sb.AppendLine(string.Format("{0,8:N0} MB {1,8:N0} MB {2,8:N0} MB {3,10:N0}  {4}{5}", (t.managed + t.native) / 1048576.0,
                    t.managed / 1048576.0, t.native / 1048576.0, t.objects, t.mod, t.capped ? "  (capped, real value is higher)" : ""));
            sb.AppendLine();
            sb.AppendLine("== Largest single items per mod (top mods) ==");
            foreach (var t in tallies.OrderByDescending(x => x.managed + x.native).Take(12))
            {
                sb.AppendLine("-- " + t.mod);
                foreach (string s in t.big.OrderByDescending(s => double.Parse(s.Substring(0, s.IndexOf(' ')))).Take(8)) sb.AppendLine("   " + s);
            }
            string path = Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "modmem.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[OnDemandAssets] Per-mod memory report written: " + path);
        }

        static bool IsGameType(Type type)
        {
            string a = type.Assembly.GetName().Name;
            return a == "Assembly-CSharp" || a == "Assembly-CSharp-firstpass" || a == "ColossalManaged" || a == "ICities";
        }

        void Walk(List<object> roots, Tally t, bool rootsMayBeGame)
        {
            var stack = new Stack<object>();
            foreach (object r in roots) stack.Push(r);
            while (stack.Count > 0)
            {
                if (t.objects >= MaxObjectsPerMod) { t.capped = true; return; }
                object o = stack.Pop();
                if (o == null || !seen.Add(o)) continue;
                t.objects++;
                Type type = o.GetType();

                var uo = o as Object;
                if (uo != null)
                {
                    if (uo == null) continue;   // destroyed Unity object
                    double n = NativeSize(uo);
                    if (n > 0) { t.native += n; if (n > 8 * 1048576) t.big.Add(string.Format("{0:N0} MB {1} '{2}'", n / 1048576.0, type.Name, uo.name)); }
                    // Only walk into Unity objects the mod defines (its MonoBehaviours/ScriptableObjects).
                    if (IsGameType(type) || type.Assembly.GetName().Name.StartsWith("UnityEngine")) continue;
                }
                else if (IsGameType(type)) continue;   // never charge the game's own data to a mod

                if (type == typeof(string)) { t.managed += 20 + 2 * ((string)o).Length; continue; }
                var arr = o as Array;
                if (arr != null)
                {
                    Type et = type.GetElementType();
                    double bytes = 16 + (double)arr.Length * ElemSize(et);
                    t.managed += bytes;
                    if (bytes > 8 * 1048576) t.big.Add(string.Format("{0:N0} MB {1}[{2:N0}]", bytes / 1048576.0, et.Name, arr.Length));
                    if (!et.IsPrimitive && !et.IsEnum && et != typeof(string))
                    {
                        if (et.IsValueType && !HasRefs(et)) continue;
                        foreach (object e in arr) if (e != null) { if (et.IsValueType) PushFields(e, et, stack); else stack.Push(e); }
                    }
                    else if (et == typeof(string)) foreach (object e in arr) if (e != null) stack.Push(e);
                    continue;
                }
                t.managed += ObjSize(type);
                PushFields(o, type, stack);
            }
        }

        void PushFields(object o, Type type, Stack<object> stack)
        {
            for (Type cur = type; cur != null && cur != typeof(object); cur = cur.BaseType)
                foreach (FieldInfo f in Fields(cur))
                {
                    Type ft = f.FieldType;
                    if (ft.IsPrimitive || ft.IsEnum || ft.IsPointer) continue;
                    if (ft.IsValueType && !HasRefs(ft)) continue;
                    object v; try { v = f.GetValue(o); } catch { continue; }
                    if (v == null) continue;
                    if (ft.IsValueType) PushFields(v, ft, stack); else stack.Push(v);
                }
        }

        FieldInfo[] Fields(Type t)
        {
            FieldInfo[] f;
            if (!fieldCache.TryGetValue(t, out f))
            {
                try { f = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly); } catch { f = new FieldInfo[0]; }
                fieldCache[t] = f;
            }
            return f;
        }

        bool HasRefs(Type valueType)
        {
            foreach (FieldInfo f in Fields(valueType))
            {
                Type ft = f.FieldType;
                if (!ft.IsValueType) return true;
                if (!ft.IsPrimitive && !ft.IsEnum && ft != valueType && HasRefs(ft)) return true;
            }
            return false;
        }

        int ObjSize(Type t)
        {
            int s;
            if (sizeCache.TryGetValue(t, out s)) return s;
            s = 16;
            for (Type cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
                foreach (FieldInfo f in Fields(cur)) s += ElemSize(f.FieldType);
            sizeCache[t] = s;
            return s;
        }

        int ElemSize(Type t)
        {
            if (!t.IsValueType) return 8;
            if (t == typeof(byte) || t == typeof(sbyte) || t == typeof(bool)) return 1;
            if (t == typeof(short) || t == typeof(ushort) || t == typeof(char)) return 2;
            if (t == typeof(int) || t == typeof(uint) || t == typeof(float)) return 4;
            if (t.IsPrimitive) return 8;
            if (t.IsEnum) return ElemSize(Enum.GetUnderlyingType(t));
            int s;
            if (sizeCache.TryGetValue(t, out s)) return s;
            s = 0;
            foreach (FieldInfo f in Fields(t)) s += f.FieldType == t ? 8 : ElemSize(f.FieldType);
            s = Math.Max(s, 1);
            sizeCache[t] = s;
            return s;
        }

        static double NativeSize(Object o)
        {
            var rt = o as RenderTexture;
            if (rt != null) return (double)rt.width * rt.height * (rt.volumeDepth > 1 ? rt.volumeDepth : 1) * 8;
            var t2 = o as Texture2D;
            if (t2 != null) return (double)t2.width * t2.height * (t2.format == TextureFormat.DXT1 ? 0.5 : t2.format == TextureFormat.DXT5 ? 1 : 4) * (t2.mipmapCount > 1 ? 1.333 : 1);
            var t3 = o as Texture3D;
            if (t3 != null) return (double)t3.width * t3.height * t3.depth * 4;
            var cm = o as Cubemap;
            if (cm != null) return 6.0 * cm.width * cm.width * 4 * 1.333;
            var m = o as Mesh;
            if (m != null) return m.vertexCount * 48.0;
            return 0;
        }
    }
}
