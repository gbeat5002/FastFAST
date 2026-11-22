// Memory snapshot: where the game's RAM goes. Trigger: drop "memreport.flag" in the data folder (city loaded).
// Writes memsnapshot.txt with:
//  1. Process totals vs what the managed heap and Unity account for (the gap is native memory nothing below sees).
//  2. Textures and meshes (estimated size, how many keep a RAM copy because they're readable).
//  3. The game's own data buffers (manager arrays). Mods that raise limits (81 tiles, tree/prop/network limits)
//     enlarge these, so they show up here, not under the mod.
//  4. Objects each mod holds directly (static fields + its MonoBehaviours), never walking into game data.
// Sizes are estimates. Ported from diagnostics/MemReport.cs and ModMemReport.cs.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace ModProfiler
{
    public class MemSnapshot : MonoBehaviour
    {
        void Update()
        {
            if (Time.frameCount % 60 != 0) return;
            string flag = Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "memreport.flag");
            if (!File.Exists(flag)) return;
            try { File.Delete(flag); } catch { }
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            var sb = new StringBuilder();
            var sizes = new Sizes();
            Process p = Process.GetCurrentProcess();
            double priv = p.PrivateMemorySize64 / 1048576.0, ws = p.WorkingSet64 / 1048576.0;
            double managed = GC.GetTotalMemory(false) / 1048576.0;
            double unityAlloc = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576.0;
            double unityRes = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576.0;
            sb.AppendLine("Memory snapshot " + DateTime.Now);
            sb.AppendLine(string.Format("Process: private {0:N0} MB, working set {1:N0} MB", priv, ws));
            sb.AppendLine(string.Format("Managed heap {0:N0} MB; Unity allocated {1:N0} MB (reserved {2:N0} MB)", managed, unityAlloc, unityRes));
            sb.AppendLine(string.Format("Not accounted for by either: {0:N0} MB (native: Mono runtime and JIT code, D3D driver, sound, plugins)", priv - managed - unityRes));
            sb.AppendLine();
            yield return null;

            // 2. Textures and meshes.
            var tex = Resources.FindObjectsOfTypeAll<Texture2D>().Select(t => new { t, mb = Sizes.TexMB(t) }).ToList();
            int sampleN = 0, readableN = 0; double readableMb = 0;
            for (int i = 0; i < tex.Count; i += 20) { sampleN++; if (Sizes.Readable(tex[i].t)) { readableN++; readableMb += tex[i].mb * 20; } }
            var rts = Resources.FindObjectsOfTypeAll<RenderTexture>();
            var meshes = Resources.FindObjectsOfTypeAll<Mesh>();
            sb.AppendLine("== Textures and meshes ==");
            sb.AppendLine(string.Format("Texture2D {0:N0} MB in {1:N0} textures; readable (sampled 1 in 20) {2}/{3}, ~{4:N0} MB also kept in RAM",
                tex.Sum(x => x.mb), tex.Count, readableN, sampleN, readableMb));
            sb.AppendLine(string.Format("RenderTexture {0:N0} MB in {1}", rts.Sum(r => (double)r.width * r.height * 8) / 1048576.0, rts.Length));
            sb.AppendLine(string.Format("Mesh ~{0:N0} MB in {1:N0} meshes", meshes.Sum(m => m.vertexCount * 48.0) / 1048576.0, meshes.Length));
            sb.AppendLine("Largest textures:");
            foreach (var x in tex.OrderByDescending(x => x.mb).Take(12))
                sb.AppendLine(string.Format("  {0,6:N1} MB  {1}x{2} {3} '{4}'", x.mb, x.t.width, x.t.height, x.t.format, x.t.name));
            sb.AppendLine("Texture MB by size bucket:");
            foreach (var g in tex.GroupBy(x => Mathf.Max(x.t.width, x.t.height)).OrderByDescending(g => g.Sum(x => x.mb)).Take(8))
                sb.AppendLine(string.Format("  {0,5} px  {1,7:N0} MB  {2,6:N0} textures", g.Key, g.Sum(x => x.mb), g.Count()));
            sb.AppendLine();
            yield return null;

            // 3. Game data buffers: arrays held by the game's managers, directly or inside Array16/Array32/FastList.
            var arrays = new List<KeyValuePair<string, double>>();
            foreach (MonoBehaviour mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (mb == null) continue;
                Type type = mb.GetType();
                if (type.Assembly.GetName().Name != "Assembly-CSharp" || !type.Name.EndsWith("Manager")) continue;
                for (Type cur = type; cur != null && cur != typeof(MonoBehaviour); cur = cur.BaseType)
                    foreach (FieldInfo f in cur.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.FieldType.IsPointer) continue;   // reading pointer fields crashes Mono natively
                        object v; try { v = f.GetValue(mb); } catch { continue; }
                        if (v == null) continue;
                        var arr = v as Array;
                        if (arr == null && !f.FieldType.IsPrimitive)
                        {
                            FieldInfo inner = f.FieldType.GetField("m_buffer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (inner != null) try { arr = inner.GetValue(v) as Array; } catch { }
                        }
                        if (arr == null) continue;
                        double bytes = (double)arr.Length * sizes.Elem(arr.GetType().GetElementType());
                        if (bytes >= 1048576) arrays.Add(new KeyValuePair<string, double>(type.Name + "." + f.Name + " [" + arr.Length.ToString("N0") + "]", bytes));
                    }
            }
            sb.AppendLine(string.Format("== Game data buffers (arrays >= 1 MB): {0:N0} MB total ==", arrays.Sum(a => a.Value) / 1048576.0));
            foreach (var a in arrays.OrderByDescending(a => a.Value).Take(30))
                sb.AppendLine(string.Format("  {0,7:N0} MB  {1}", a.Value / 1048576.0, a.Key));
            sb.AppendLine();
            yield return null;

            // 4. What each mod holds itself.
            var started = DateTime.Now;
            var walker = new ModWalker(sizes);
            var tallies = new List<ModWalker.Tally>();
            var behaviours = Resources.FindObjectsOfTypeAll<MonoBehaviour>().Cast<Object>().Concat(Resources.FindObjectsOfTypeAll<ScriptableObject>().Cast<Object>()).ToList();
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies().Where(a => Timers.IsMod(a.GetName().Name)))
            {
                // Breadcrumb: if a walk still crashes the game, this file names the mod being walked.
                File.WriteAllText(Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "memsnapshot-walking.txt"), asm.GetName().Name);
                tallies.Add(walker.Measure(asm, behaviours));
                yield return null;
            }
            sb.AppendLine(string.Format("== Held by mods directly (walk {0:N0} s) ==", (DateTime.Now - started).TotalSeconds));
            sb.AppendLine("     total    managed     native  mod");
            foreach (var t in tallies.OrderByDescending(x => x.managed + x.native).Take(25))
                sb.AppendLine(string.Format("{0,8:N0} MB {1,8:N0} MB {2,8:N0} MB  {3}{4}", (t.managed + t.native) / 1048576.0, t.managed / 1048576.0, t.native / 1048576.0, t.mod, t.capped ? "  (capped)" : ""));
            sb.AppendLine(string.Format("All mods together: {0:N0} MB", tallies.Sum(t => t.managed + t.native) / 1048576.0));
            foreach (var t in tallies.OrderByDescending(x => x.managed + x.native).Take(6))
                foreach (string s in t.big.Take(4)) sb.AppendLine("  " + t.mod + ": " + s);

            string path = Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "memsnapshot.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[ModProfiler] Memory snapshot written: " + path);
        }
    }

    // Size estimates shared by the sections above.
    public class Sizes
    {
        readonly Dictionary<Type, int> cache = new Dictionary<Type, int>();
        public readonly Dictionary<Type, FieldInfo[]> fields = new Dictionary<Type, FieldInfo[]>();

        public FieldInfo[] Fields(Type t)
        {
            FieldInfo[] f;
            if (!fields.TryGetValue(t, out f))
            {
                try { f = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly); } catch { f = new FieldInfo[0]; }
                fields[t] = f;
            }
            return f;
        }

        public int Elem(Type t)
        {
            if (!t.IsValueType) return 8;
            if (t == typeof(byte) || t == typeof(sbyte) || t == typeof(bool)) return 1;
            if (t == typeof(short) || t == typeof(ushort) || t == typeof(char)) return 2;
            if (t == typeof(int) || t == typeof(uint) || t == typeof(float)) return 4;
            if (t.IsPrimitive) return 8;
            if (t.IsEnum) return Elem(Enum.GetUnderlyingType(t));
            int s;
            if (cache.TryGetValue(t, out s)) return s;
            s = 0;
            foreach (FieldInfo f in Fields(t)) s += f.FieldType == t ? 8 : Elem(f.FieldType);
            s = Math.Max(s, 1);
            cache[t] = s;
            return s;
        }

        public int Obj(Type t)
        {
            int s = 16;
            for (Type cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
                foreach (FieldInfo f in Fields(cur)) s += Elem(f.FieldType);
            return s;
        }

        public static double TexMB(Texture2D t)
        {
            float bpp;
            switch (t.format)
            {
                case TextureFormat.DXT1: case TextureFormat.BC4: bpp = 0.5f; break;
                case TextureFormat.DXT5: case TextureFormat.BC5: case TextureFormat.BC7: case TextureFormat.BC6H: case TextureFormat.Alpha8: case TextureFormat.R8: bpp = 1f; break;
                case TextureFormat.RGB24: bpp = 3f; break;
                case TextureFormat.RGBAHalf: bpp = 8f; break;
                case TextureFormat.RGBAFloat: bpp = 16f; break;
                case TextureFormat.RHalf: case TextureFormat.R16: case TextureFormat.RGB565: case TextureFormat.ARGB4444: case TextureFormat.RGBA4444: bpp = 2f; break;
                default: bpp = 4f; break;
            }
            return (double)t.width * t.height * bpp * (t.mipmapCount > 1 ? 1.333 : 1.0) / 1048576.0;
        }

        public static bool Readable(Texture2D t) { try { t.GetPixel(0, 0); return true; } catch { return false; } }

        public static double NativeBytes(Object o)
        {
            var rt = o as RenderTexture; if (rt != null) return (double)rt.width * rt.height * 8;
            var t2 = o as Texture2D; if (t2 != null) return TexMB(t2) * 1048576.0;
            var m = o as Mesh; if (m != null) return m.vertexCount * 48.0;
            return 0;
        }
    }

    // Walks the object graph from a mod's static fields and its own Unity objects, never into game/engine types,
    // so game data a mod merely points at isn't charged to it. Shared objects go to the first mod that reaches them.
    public class ModWalker
    {
        public class Tally { public string mod; public double managed, native; public bool capped; public List<string> big = new List<string>(); }
        class RefEq : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object o) { return RuntimeHelpers.GetHashCode(o); }
        }
        const long Cap = 8000000;   // ponytail: bounded walk; flagged in the report when hit
        readonly HashSet<object> seen = new HashSet<object>(new RefEq());
        readonly Sizes sizes;
        public ModWalker(Sizes s) { sizes = s; }

        static bool IsGame(Type t)
        {
            string a = t.Assembly.GetName().Name;
            return a == "Assembly-CSharp" || a == "Assembly-CSharp-firstpass" || a == "ColossalManaged" || a == "ICities" || a.StartsWith("UnityEngine");
        }

        // Reference fields only. Reading a pointer field (int*, void*) by reflection crashes Mono natively, and a
        // try/catch can't stop it (the game crashed here once), so pointers and by-refs are never read.
        static bool Walkable(Type t) { return !t.IsValueType && !t.IsPointer && !t.IsByRef; }

        public Tally Measure(Assembly asm, List<Object> behaviours)
        {
            var t = new Tally { mod = asm.GetName().Name };
            var stack = new Stack<object>();
            foreach (Type type in Timers.Types(asm))
            {
                if (type.ContainsGenericParameters) continue;
                FieldInfo[] statics;
                try { statics = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly); } catch { continue; }
                foreach (FieldInfo f in statics)
                {
                    if (f.IsLiteral || !Walkable(f.FieldType)) continue;
                    object v; try { v = f.GetValue(null); } catch { continue; }
                    if (v != null) stack.Push(v);
                }
            }
            foreach (Object b in behaviours) if (b != null && b.GetType().Assembly == asm) stack.Push(b);

            long n = 0;
            while (stack.Count > 0)
            {
                if (++n > Cap) { t.capped = true; break; }
                object o = stack.Pop();
                if (o == null || !seen.Add(o)) continue;
                Type type = o.GetType();
                var uo = o as Object;
                if (uo != null)
                {
                    if (uo == null) continue;   // destroyed Unity object
                    double nb = Sizes.NativeBytes(uo);
                    if (nb > 0) { t.native += nb; if (nb > 8 * 1048576) t.big.Add(string.Format("{0:N0} MB {1} '{2}'", nb / 1048576.0, type.Name, uo.name)); }
                    if (IsGame(type)) continue;   // only walk into Unity objects the mod defines
                }
                else if (IsGame(type)) continue;   // never charge the game's own data to a mod

                if (type == typeof(string)) { t.managed += 20 + 2 * ((string)o).Length; continue; }
                var arr = o as Array;
                if (arr != null)
                {
                    Type et = type.GetElementType();
                    double bytes = 16 + (double)arr.Length * sizes.Elem(et);
                    t.managed += bytes;
                    if (bytes > 8 * 1048576) t.big.Add(string.Format("{0:N0} MB {1}[{2:N0}]", bytes / 1048576.0, et.Name, arr.Length));
                    if (Walkable(et)) foreach (object e in arr) if (e != null) stack.Push(e);
                    continue;
                }
                t.managed += sizes.Obj(type);
                for (Type cur = type; cur != null && cur != typeof(object); cur = cur.BaseType)
                    foreach (FieldInfo f in sizes.Fields(cur))
                    {
                        if (!Walkable(f.FieldType)) continue;
                        object v; try { v = f.GetValue(o); } catch { continue; }
                        if (v != null) stack.Push(v);
                    }
            }
            return t;
        }
    }
}
