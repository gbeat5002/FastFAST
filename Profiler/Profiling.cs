// Profiling: where frame time and load time go, per mod.
// Frame profiler (Ctrl+Shift+F only, never automatic): times every mod MonoBehaviour per-frame message and every
// Harmony prefix/postfix other mods installed, plus the game's main loops for context; records 20 s into profile.txt.
// Load profiler (opt-in): armed only when the file "OnDemandAssets.profile-next-load" exists in the game's data
// folder; times mod MonoBehaviour/loading-extension hooks during that one load; writes loadprofile.txt.
// Safety: timers are installed once and never removed during the session (they just stop recording).
// Times are inclusive (nested timed calls are counted in both). Timer overhead inflates very hot methods.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OnDemandAssets
{
    static class Timers
    {
        public class Stat { public long ticks, calls; }
        static readonly object gate = new object();
        public static Dictionary<string, Stat> stats = new Dictionary<string, Stat>();
        public static Dictionary<MethodBase, string> labels = new Dictionary<MethodBase, string>();
        public static int mainThread;

        public static void Pre(out long __state) { __state = Stopwatch.GetTimestamp(); }

        public static void Post(long __state, MethodBase __originalMethod)
        {
            if (!recording) return;
            long dt = Stopwatch.GetTimestamp() - __state;
            string label;
            if (!labels.TryGetValue(__originalMethod, out label)) return;
            if (Thread.CurrentThread.ManagedThreadId != mainThread) label += "  [other thread]";
            lock (gate)
            {
                Stat s;
                if (!stats.TryGetValue(label, out s)) stats[label] = s = new Stat();
                s.ticks += dt; s.calls++;
            }
        }

        public static void Reset() { lock (gate) stats = new Dictionary<string, Stat>(); }

        // When false, Post returns immediately: timers stay installed (never unpatched mid-session, which is unsafe
        // while other threads may be inside a patched method) and only record while a capture is running.
        public static volatile bool recording;

        // Wraps each target with Pre/Post. Labels are published before any patch goes live, because Post can run
        // on the simulation thread. Returns how many were wrapped.
        public static int Wrap(Harmony h, Dictionary<MethodBase, string> targets)
        {
            var merged = new Dictionary<MethodBase, string>(labels);
            foreach (var kv in targets) merged[kv.Key] = kv.Value;
            labels = merged;   // swap in a complete dictionary; readers never see a half-written one
            var pre = new HarmonyMethod(typeof(Timers).GetMethod("Pre"));
            var post = new HarmonyMethod(typeof(Timers).GetMethod("Post"));
            int n = 0;
            foreach (var kv in targets)
            {
                try { h.Patch(kv.Key, pre, post, null, null); n++; }
                catch (Exception e) { Debug.Log("[OnDemandAssets] Could not time " + kv.Value + ": " + e.Message); }
            }
            return n;
        }

        public static IEnumerable<Assembly> ModAssemblies()
        {
            string[] skip = { "Assembly-CSharp", "Assembly-CSharp-firstpass", "ColossalManaged", "ICities", "UnityEngine", "UnityEngine.UI",
                "UnityEngine.Networking", "mscorlib", "ICSharpCode.SharpZipLib", "PopsApiWrapper", "EOSSDK", "OnDemandAssets",
                "CitiesHarmony.Harmony", "CitiesHarmony.API", "0Harmony" };
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                string n = a.GetName().Name;
                if (skip.Contains(n) || n.StartsWith("System") || n.StartsWith("Mono") || n.StartsWith("Anonymously")) continue;
                yield return a;
            }
        }

        public static IEnumerable<Type> Types(Assembly a)
        {
            Type[] t;
            try { t = a.GetTypes(); } catch (ReflectionTypeLoadException e) { t = e.Types.Where(x => x != null).ToArray(); } catch { t = new Type[0]; }
            return t;
        }

        public static bool Timeable(MethodInfo m)
        {
            return m != null && !m.IsAbstract && !m.ContainsGenericParameters && !m.DeclaringType.ContainsGenericParameters && m.GetMethodBody() != null;
        }

        public static string Owner(MethodBase m) { return m.DeclaringType.Assembly.GetName().Name; }
        public static string Name(MethodBase m) { return m.DeclaringType.Name + "." + m.Name; }
    }

    public class FrameProfiler : MonoBehaviour
    {
        const string Id = "ondemandassets.frameprofiler";
        const float CaptureSeconds = 20f;
        static readonly string[] PerFrame = { "Update", "LateUpdate", "FixedUpdate", "OnGUI", "OnRenderObject", "OnPreCull",
            "OnPreRender", "OnPostRender", "OnRenderImage", "OnWillRenderObject" };

        bool running;
        readonly List<float> frames = new List<float>();

        static int wrapped;   // timers are installed once per game session and never removed

        void Update()
        {
            if (running) frames.Add(Time.unscaledDeltaTime);
            bool combo = (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            if (combo && Input.GetKeyDown(KeyCode.F) && !running) StartCoroutine(Capture());
        }

        IEnumerator Capture()
        {
            running = true;
            Timers.mainThread = Thread.CurrentThread.ManagedThreadId;
            if (wrapped == 0)
            {
                Controller.Notify("Installing frame timers (one-time hitch)...");
                yield return null;
                wrapped = Timers.Wrap(new Harmony(Id), Targets());
                yield return new WaitForSeconds(2f);   // let the patching hitch pass before measuring
            }
            Controller.Notify("Profiling frame time for " + CaptureSeconds + " s...");
            Timers.Reset(); frames.Clear();
            Timers.recording = true;
            float until = Time.realtimeSinceStartup + CaptureSeconds;
            while (Time.realtimeSinceStartup < until) yield return null;
            Timers.recording = false;
            var stats = Timers.stats;
            var frameTimes = new List<float>(frames);
            running = false;
            Write(stats, frameTimes, wrapped);
        }

        static Dictionary<MethodBase, string> Targets()
        {
            var t = new Dictionary<MethodBase, string>();
            foreach (Assembly a in Timers.ModAssemblies())
                foreach (Type type in Timers.Types(a))
                {
                    if (!typeof(MonoBehaviour).IsAssignableFrom(type)) continue;
                    foreach (string msg in PerFrame)
                    {
                        MethodInfo m = null;
                        try { m = type.GetMethod(msg, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null); } catch { }
                        if (msg == "OnRenderImage" && m == null)
                            try { m = type.GetMethod(msg, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); } catch { }
                        if (Timers.Timeable(m)) t[m] = Timers.Owner(m) + ": " + Timers.Name(m);
                    }
                }

            foreach (MethodBase original in Harmony.GetAllPatchedMethods().ToList())
            {
                Patches info = Harmony.GetPatchInfo(original);
                if (info == null) continue;
                foreach (Patch p in info.Prefixes.Concat(info.Postfixes).Concat(info.Finalizers))
                {
                    MethodInfo pm = p.PatchMethod;
                    if (!Timers.Timeable(pm) || Timers.Owner(pm) == "OnDemandAssets") continue;
                    t[pm] = Timers.Owner(pm) + ": patch " + Timers.Name(pm) + " on " + Timers.Name(original);
                }
                // Transpiled originals are not timed: re-patching them makes Harmony re-run other mods' transpilers.
            }

            // Game loops for context (inclusive of everything patched inside them).
            foreach (var g in new[] { typeof(RenderManager).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                                      typeof(SimulationManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                                      typeof(ToolController).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) })
                if (g != null && !t.ContainsKey(g)) t[g] = "[game total] " + Timers.Name(g);
            return t;
        }

        static void Write(Dictionary<string, Timers.Stat> stats, List<float> frameTimes, int wrapped)
        {
            int n = Math.Max(frameTimes.Count, 1);
            double freq = Stopwatch.Frequency;
            frameTimes.Sort();
            double avgMs = frameTimes.Count > 0 ? frameTimes.Average() * 1000 : 0;
            double p99 = frameTimes.Count > 0 ? frameTimes[(int)(frameTimes.Count * 0.99)] * 1000 : 0;
            var sb = new StringBuilder();
            sb.AppendLine("Frame profile " + DateTime.Now + "  (" + wrapped + " methods timed, " + frameTimes.Count + " frames)");
            sb.AppendLine(string.Format("Average frame {0:N1} ms = {1:N0} FPS; slowest 1% of frames {2:N1} ms", avgMs, avgMs > 0 ? 1000 / avgMs : 0, p99));
            sb.AppendLine("Budget for 60 FPS: 16.7 ms per frame. Times are per frame, inclusive, and include timer overhead.");
            sb.AppendLine();
            sb.AppendLine("  ms/frame   calls/frame  what");
            foreach (var kv in stats.OrderByDescending(kv => kv.Value.ticks).Take(70))
                sb.AppendLine(string.Format("{0,9:N2}  {1,12:N0}  {2}", kv.Value.ticks / freq * 1000 / n, (double)kv.Value.calls / n, kv.Key));
            sb.AppendLine();
            sb.AppendLine("== Per mod (sum of its timed methods, main thread only) ==");
            foreach (var g in stats.Where(kv => !kv.Key.Contains("[other thread]") && !kv.Key.StartsWith("[game") && !kv.Key.StartsWith("TRANSPILED"))
                                   .GroupBy(kv => kv.Key.Substring(0, kv.Key.IndexOf(':'))).OrderByDescending(g => g.Sum(kv => kv.Value.ticks)).Take(30))
                sb.AppendLine(string.Format("{0,9:N2} ms/frame  {1}", g.Sum(kv => kv.Value.ticks) / freq * 1000 / n, g.Key));
            string path = Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "profile.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[OnDemandAssets] Frame profile written: " + path);
            Controller.Notify(string.Format("Frame profile saved: {0:N0} FPS average (see profile.txt)", avgMs > 0 ? 1000 / avgMs : 0));
        }
    }

    public static class LoadProfiler
    {
        const string Id = "ondemandassets.loadprofiler";
        static Harmony harmony;
        static readonly Stopwatch total = new Stopwatch();
        static bool armed;
        static readonly string[] BehaviourHooks = { "Awake", "Start", "OnEnable" };
        static readonly string[] LoadingHooks = { "OnCreated", "OnLevelLoaded" };
        static readonly string[] DataHooks = { "OnLoadData" };

        static string FlagFile { get { return Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "OnDemandAssets.profile-next-load"); } }

        // Called once Harmony is ready. Opt-in only: does nothing unless the flag file exists.
        public static void Install()
        {
            if (!File.Exists(FlagFile)) return;
            harmony = new Harmony(Id);
            var arm = new HarmonyMethod(typeof(LoadProfiler).GetMethod("Arm", BindingFlags.Static | BindingFlags.NonPublic));
            foreach (MethodInfo m in typeof(LoadingManager).GetMethods(BindingFlags.Instance | BindingFlags.Public))
                if (m.Name == "LoadLevel") try { harmony.Patch(m, arm, null, null, null); } catch { }
        }

        static void Arm()
        {
            if (armed || !File.Exists(FlagFile)) return;
            armed = true;
            try { File.Delete(FlagFile); } catch { }   // one load only
            total.Reset(); total.Start();
            Timers.mainThread = Thread.CurrentThread.ManagedThreadId;
            Timers.Reset();
            var t = new Dictionary<MethodBase, string>();
            foreach (Assembly a in Timers.ModAssemblies())
                foreach (Type type in Timers.Types(a))
                {
                    // Only the hooks the game itself calls during a load, on the types it calls them on.
                    string[] hooks = typeof(MonoBehaviour).IsAssignableFrom(type) ? BehaviourHooks
                        : typeof(ICities.ILoadingExtension).IsAssignableFrom(type) ? LoadingHooks
                        : typeof(ICities.ISerializableDataExtension).IsAssignableFrom(type) ? DataHooks : null;
                    if (hooks == null) continue;
                    foreach (string hook in hooks)
                    {
                        MethodInfo m = null;
                        try { m = type.GetMethod(hook, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); } catch { }
                        if (Timers.Timeable(m)) t[m] = Timers.Owner(m) + ": " + Timers.Name(m);
                    }
                }
            int n = Timers.Wrap(harmony, t);
            Timers.recording = true;
            Debug.Log("[OnDemandAssets] Load profiler armed: timing " + n + " mod load hooks");
        }

        // Called from our own OnLevelLoaded; waits so late Start() calls are included.
        public static IEnumerator Finish()
        {
            if (!armed) yield break;
            yield return new WaitForSeconds(15f);
            total.Stop();
            Timers.recording = false;   // timers stay installed (unpatching mid-session is unsafe); they just stop recording
            var stats = Timers.stats;
            double freq = Stopwatch.Frequency;
            var sb = new StringBuilder();
            sb.AppendLine("Load profile " + DateTime.Now + string.Format("  (load click -> city ready + 15 s: {0:N1} s)", total.Elapsed.TotalSeconds));
            sb.AppendLine("Time spent inside each mod's load hooks (inclusive). Asset loading itself is in LSM's log lines.");
            sb.AppendLine();
            foreach (var kv in stats.OrderByDescending(kv => kv.Value.ticks).Take(40))
                sb.AppendLine(string.Format("{0,8:N0} ms  {1,5} calls  {2}", kv.Value.ticks / freq * 1000, kv.Value.calls, kv.Key));
            sb.AppendLine();
            sb.AppendLine("== Per mod ==");
            foreach (var g in stats.GroupBy(kv => kv.Key.Substring(0, kv.Key.IndexOf(':'))).OrderByDescending(g => g.Sum(kv => kv.Value.ticks)).Take(25))
                sb.AppendLine(string.Format("{0,8:N0} ms  {1}", g.Sum(kv => kv.Value.ticks) / freq * 1000, g.Key));
            string path = Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "loadprofile.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[OnDemandAssets] Load profile written: " + path);
        }
    }
}
