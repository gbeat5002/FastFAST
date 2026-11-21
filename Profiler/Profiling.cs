// Profiling: where frame time and load time go, per mod.
// Frame profiler (Ctrl+Shift+F only, never automatic): times every mod MonoBehaviour per-frame message and every
// Harmony prefix/postfix other mods installed, plus the game's main loops for context; records 20 s into profile.txt.
// Load profiler (opt-in): armed only when the file "ModProfiler.profile-next-load" exists in the game's data
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

namespace ModProfiler
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
            // Breadcrumb: each label is flushed to disk before its patch, so after a crash the last line names the culprit.
            // Some patch methods crash Mono's JIT natively (not catchable); a crashed run's last label is skipped next time.
            string crumbs = Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "profile-patching.txt");
            string skipFile = Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "profile-skip.txt");
            if (File.Exists(crumbs))
            {
                string[] old = File.ReadAllLines(crumbs);
                string last = old.Length > 0 ? old[old.Length - 1] : "";
                if (last.Length > 0 && !last.StartsWith("==") && !last.StartsWith("skip") && !last.StartsWith("  failed"))
                    File.AppendAllText(skipFile, last + Environment.NewLine);
            }
            var skipped = new HashSet<string>(File.Exists(skipFile) ? File.ReadAllLines(skipFile) : new string[0]);
            using (var log = new StreamWriter(crumbs, true) { AutoFlush = true })
            {
                log.WriteLine("== " + DateTime.Now + ": patching " + targets.Count);
                foreach (var kv in targets)
                {
                    if (skipped.Contains(kv.Value)) { log.WriteLine("skip (crashed before) " + kv.Value); continue; }
                    // Never touch a method another mod transpiles: re-patching makes Harmony re-run their transpilers.
                    Patches info = Harmony.GetPatchInfo(kv.Key);
                    if (info != null && info.Transpilers.Count > 0) { log.WriteLine("skip (transpiled) " + kv.Value); continue; }
                    log.WriteLine(kv.Value);
                    try { h.Patch(kv.Key, pre, post, null, null); n++; }
                    catch (Exception e) { log.WriteLine("  failed: " + e.Message); }
                }
                log.WriteLine("== done, " + n + " wrapped");
            }
            return n;
        }

        static readonly string[] skip = { "Assembly-CSharp", "Assembly-CSharp-firstpass", "ColossalManaged", "ICities", "UnityEngine", "UnityEngine.UI",
            "UnityEngine.Networking", "mscorlib", "ICSharpCode.SharpZipLib", "PopsApiWrapper", "EOSSDK", "OnDemandAssets", "ModProfiler",
            "CitiesHarmony.Harmony", "CitiesHarmony.API", "CitiesHarmony", "0Harmony" };

        public static bool IsMod(string n)
        {
            return !(skip.Contains(n) || n.StartsWith("System") || n.StartsWith("Mono") || n.StartsWith("Anonymously") || n.StartsWith("0Harmony"));
        }

        public static IEnumerable<Assembly> ModAssemblies()
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                if (IsMod(a.GetName().Name)) yield return a;
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

        // Timers are installed once per game session and never removed. Pass 1: main-thread per-frame mod hooks and
        // game anchors. Pass 2 (opt-in, after pass 1 survived): other mods' Harmony patch methods, which may run on
        // the simulation thread, so they are installed with the simulation paused.
        static int wrapped, wrapped2;

        void Update()
        {
            if (running) frames.Add(Time.unscaledDeltaTime);
            bool combo = (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            if (combo && Input.GetKeyDown(KeyCode.F) && !running) StartCoroutine(Capture(false));
            // Or drop "profile.flag" / "profile2.flag" in the data folder: starts a capture without keyboard focus.
            if (!running && Time.frameCount % 60 == 0)
            {
                if (TakeFlag("profile.flag")) StartCoroutine(Capture(false));
                else if (TakeFlag("profile2.flag")) StartCoroutine(Capture(true));
            }
        }

        static bool TakeFlag(string name)
        {
            string f = Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, name);
            if (!File.Exists(f)) return false;
            try { File.Delete(f); } catch { }
            return true;
        }

        IEnumerator Capture(bool withPatches)
        {
            running = true;
            Timers.mainThread = Thread.CurrentThread.ManagedThreadId;
            if (wrapped == 0)
            {
                ProfilerMod.Notify("Installing frame timers, pass 1 (one-time hitch)...");
                yield return null;
                wrapped = Timers.Wrap(new Harmony(Id), Targets());
                yield return new WaitForSeconds(2f);   // let the patching hitch pass before measuring
            }
            if (withPatches && wrapped2 == 0)
            {
                ProfilerMod.Notify("Installing timers on other mods' patches, pass 2 (simulation paused)...");
                SimulationManager sim = SimulationManager.instance;
                bool wasPaused = sim.SimulationPaused;
                sim.SimulationPaused = true;
                yield return new WaitForSeconds(1f);   // let the simulation thread leave its current step
                wrapped2 = Timers.Wrap(new Harmony(Id + ".patches"), PatchTargets());
                sim.SimulationPaused = wasPaused;
                yield return new WaitForSeconds(3f);
            }
            ProfilerMod.Notify("Profiling frame time for " + CaptureSeconds + " s...");
            Timers.Reset(); frames.Clear();
            Timers.recording = true;
            float until = Time.realtimeSinceStartup + CaptureSeconds;
            while (Time.realtimeSinceStartup < until) yield return null;
            Timers.recording = false;
            var stats = Timers.stats;
            var frameTimes = new List<float>(frames);
            running = false;
            Write(stats, frameTimes, wrapped + wrapped2, withPatches ? "profile-pass2.txt" : "profile-pass1.txt");
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

            // ICities per-frame hook: called on the main thread by the game's threading wrapper.
            foreach (Assembly a in Timers.ModAssemblies())
                foreach (Type type in Timers.Types(a))
                {
                    if (!typeof(ICities.IThreadingExtension).IsAssignableFrom(type)) continue;
                    MethodInfo m = null;
                    try { m = type.GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly); } catch { }
                    if (Timers.Timeable(m)) t[m] = Timers.Owner(m) + ": " + Timers.Name(m);
                }

            // The game's own per-frame work, split by system. FPS Booster renames each manager's Update/LateUpdate to
            // FpsBooster*; EndRenderingImpl is where each manager submits its draws. All main-thread only.
            // Transpiled ones are skipped by Wrap.
            const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
            foreach (Type type in Timers.Types(typeof(RenderManager).Assembly).Concat(Timers.Types(typeof(ColossalFramework.UI.UIView).Assembly)))
            {
                if (type.IsValueType || type.ContainsGenericParameters) continue;
                MethodInfo[] ms;
                try { ms = type.GetMethods(any); } catch { continue; }
                foreach (MethodInfo m in ms)
                    if ((m.Name.StartsWith("FpsBooster") || m.Name == "EndRenderingImpl" || m.Name == "BeginRenderingImpl")
                        && m.GetParameters().Length <= 1 && Timers.Timeable(m) && !t.ContainsKey(m))
                        t[m] = "[game] " + Timers.Name(m);
            }
            return t;
        }

        // Pass 2: prefixes/postfixes/finalizers other mods installed (not the originals they patch).
        static Dictionary<MethodBase, string> PatchTargets()
        {
            var t = new Dictionary<MethodBase, string>();
            foreach (MethodBase original in Harmony.GetAllPatchedMethods().ToList())
            {
                Patches info = Harmony.GetPatchInfo(original);
                if (info == null) continue;
                // Patches on struct members (TreeInstance, PropInstance, ...) crashed Mono's JIT natively when re-detoured.
                if (original.DeclaringType != null && original.DeclaringType.IsValueType) continue;
                foreach (Patch p in info.Prefixes.Concat(info.Postfixes).Concat(info.Finalizers))
                {
                    MethodInfo pm = p.PatchMethod;
                    if (!Timers.Timeable(pm) || !Timers.IsMod(Timers.Owner(pm))) continue;
                    t[pm] = Timers.Owner(pm) + ": patch " + Timers.Name(pm) + " on " + Timers.Name(original);
                }
            }
            return t;
        }

        static void Write(Dictionary<string, Timers.Stat> stats, List<float> frameTimes, int wrapped, string file)
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
            string path = Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, file);
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[ModProfiler] Frame profile written: " + path);
            ProfilerMod.Notify(string.Format("Frame profile saved: {0:N0} FPS average (see profile.txt)", avgMs > 0 ? 1000 / avgMs : 0));
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

        static string FlagFile { get { return Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "ModProfiler.profile-next-load"); } }

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
            Debug.Log("[ModProfiler] Load profiler armed: timing " + n + " mod load hooks");
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
            Debug.Log("[ModProfiler] Load profile written: " + path);
        }
    }
}

namespace ModProfiler
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using ColossalFramework.UI;
    using HarmonyLib;
    using UnityEngine;
    using Debug = UnityEngine.Debug;

    // Which UI components force a UI rebuild. Any UIComponent.Invalidate() marks the whole UIView dirty, and a dirty
    // view re-renders every visible component. Counts Invalidate calls per component (with the calling method) and
    // how many frames the view actually re-rendered and how long that took. Trigger: "uiprofile.flag", 10 s.
    public class UiProfiler : MonoBehaviour
    {
        const float Seconds = 10f;
        class Hit { public int calls, dirtied; public string caller; public bool otherThread; }
        static readonly object gate = new object();
        static Dictionary<UIComponent, Hit> hits = new Dictionary<UIComponent, Hit>();
        static volatile bool recording;
        static int mainThread, renders;
        static long renderTicks, renderStart;
        static bool installed;

        static readonly System.Reflection.FieldInfo dirtyField = typeof(UIView).GetField("m_IsDirty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        static bool ViewDirty(UIComponent c)
        {
            UIView v = c != null ? c.GetUIView() : null;
            return v != null && dirtyField != null && (bool)dirtyField.GetValue(v);
        }

        // Postfix: this call is the one that made the view re-render if the view was clean before and dirty after.
        static void InvalidatePostfix(UIComponent __instance, bool __state)
        {
            if (!recording || __state || !ViewDirty(__instance)) return;
            lock (gate) { Hit h; if (hits.TryGetValue(__instance, out h)) h.dirtied++; }
        }

        static void InvalidatePrefix(UIComponent __instance, out bool __state)
        {
            __state = true;
            if (!recording) return;
            __state = ViewDirty(__instance);
            lock (gate)
            {
                Hit h;
                if (!hits.TryGetValue(__instance, out h))
                {
                    hits[__instance] = h = new Hit();
                    // Calling method, skipping the Harmony wrapper and Invalidate overloads/property setters on UI types.
                    foreach (StackFrame f in new StackTrace(1, false).GetFrames() ?? new StackFrame[0])
                    {
                        var m = f.GetMethod();
                        if (m == null || m.DeclaringType == null) continue;
                        string ns = m.DeclaringType.Namespace ?? "";
                        if (m.Name.Contains("Invalidate") || m.DeclaringType.Name.StartsWith("DMD") || ns.StartsWith("HarmonyLib")) continue;
                        h.caller = m.DeclaringType.Assembly.GetName().Name + ": " + m.DeclaringType.FullName + "." + m.Name;
                        if (!ns.StartsWith("ColossalFramework.UI")) break;   // first non-UI-framework frame is the culprit
                    }
                }
                h.calls++;
                if (Thread.CurrentThread.ManagedThreadId != mainThread) h.otherThread = true;
            }
        }
        static void RenderPrefix() { renderStart = Stopwatch.GetTimestamp(); }
        static void RenderPostfix() { if (recording) { renders++; renderTicks += Stopwatch.GetTimestamp() - renderStart; } }

        void Update()
        {
            if (Time.frameCount % 60 != 0) return;
            string flag = Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "uiprofile.flag");
            if (!File.Exists(flag)) return;
            try { File.Delete(flag); } catch { }
            StartCoroutine(Capture());
        }

        IEnumerator Capture()
        {
            mainThread = Thread.CurrentThread.ManagedThreadId;
            if (!installed)
            {
                installed = true;
                var h = new Harmony("modprofiler.ui");
                var inv = typeof(UIComponent).GetMethod("Invalidate", Type.EmptyTypes);
                var render = typeof(UIView).GetMethod("Render", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                h.Patch(inv, new HarmonyMethod(typeof(UiProfiler).GetMethod("InvalidatePrefix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)),
                    new HarmonyMethod(typeof(UiProfiler).GetMethod("InvalidatePostfix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)), null, null);
                h.Patch(render, new HarmonyMethod(typeof(UiProfiler).GetMethod("RenderPrefix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)),
                    new HarmonyMethod(typeof(UiProfiler).GetMethod("RenderPostfix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)), null, null);
                yield return new WaitForSeconds(2f);
            }
            lock (gate) hits = new Dictionary<UIComponent, Hit>();
            renders = 0; renderTicks = 0;
            int f0 = Time.frameCount;
            recording = true;
            float until = Time.realtimeSinceStartup + Seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
            recording = false;
            int frames = Math.Max(1, Time.frameCount - f0);

            var sb = new StringBuilder();
            int total = UIView.GetAView() != null ? UIView.GetAView().GetComponentsInChildren<UIComponent>(true).Length : 0;
            sb.AppendLine("UI profile " + DateTime.Now + ": " + frames + " frames, " + total + " UI components");
            sb.AppendLine(string.Format("UIView re-rendered on {0} of {1} frames ({2:P0}); {3:N2} ms per re-render, {4:N2} ms per frame average",
                renders, frames, (double)renders / frames, renders > 0 ? renderTicks * 1000.0 / Stopwatch.Frequency / renders : 0, renderTicks * 1000.0 / Stopwatch.Frequency / frames));
            sb.AppendLine();
            sb.AppendLine("Invalidate() calls per frame by component; 'dirtied' = frames on which this call switched the UI to re-render");
            sb.AppendLine("calls/frame  dirtied  component path | type | visible | first calling method");
            Dictionary<UIComponent, Hit> snap;
            lock (gate) snap = new Dictionary<UIComponent, Hit>(hits);
            foreach (var kv in snap.OrderByDescending(kv => kv.Value.dirtied).ThenByDescending(kv => kv.Value.calls).Take(40))
                sb.AppendLine(string.Format("{0,8:N2}  {6,7}  {1} | {2} | {3} | {4}{5}", (double)kv.Value.calls / frames, PathOf(kv.Key), kv.Key == null ? "?" : kv.Key.GetType().FullName,
                    kv.Key != null && kv.Key.isVisible, kv.Value.caller, kv.Value.otherThread ? "  [OFF MAIN THREAD]" : "", kv.Value.dirtied));
            sb.AppendLine();
            sb.AppendLine(string.Format("Re-renders started by a component Invalidate(): {0} of {1} (the rest come from elsewhere, e.g. UIView.Invalidate or layout)",
                snap.Sum(kv => kv.Value.dirtied), renders));
            sb.AppendLine();
            sb.AppendLine("By calling assembly (calls per frame):");
            foreach (var g in snap.GroupBy(kv => (kv.Value.caller ?? "?").Split(':')[0]).OrderByDescending(g => g.Sum(kv => kv.Value.calls)))
                sb.AppendLine(string.Format("{0,8:N2}  {1}  ({2} components)", (double)g.Sum(kv => kv.Value.calls) / frames, g.Key, g.Count()));
            string path = Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "ui-profile.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[ModProfiler] UI profile written: " + path);
        }

        static string PathOf(UIComponent c)
        {
            if (c == null) return "(destroyed)";
            var parts = new List<string>();
            for (Transform t = c.transform; t != null && parts.Count < 6; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }
    }
}
