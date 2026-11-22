// Camera and GC census. Trigger: "census.flag" in the data folder (city loaded); 20 s capture -> census.txt.
//  - Every enabled camera: what it renders and its main-thread cost (OnPreCull..OnPostRender, i.e. culling and
//    draw submission; image effects in OnRenderImage come after and are not included).
//  - Garbage collections: how many, and how many of the slow frames (> 2x the median) had one.
//  - Managed allocation rate (heap growth between collections).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ModProfiler
{
    public partial class Census : MonoBehaviour
    {
        const float Seconds = 20f;

        void Update()
        {
            if (Time.frameCount % 60 != 0) return;
            string flag = Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "census.flag");
            if (!File.Exists(flag)) return;
            try { File.Delete(flag); } catch { }
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            var timers = new List<CameraTimer>();
            foreach (Camera c in Resources.FindObjectsOfTypeAll<Camera>().Where(c => c.gameObject.scene.IsValid()))   // scene cameras, incl. disabled ones mods render by hand (not prefab assets)
            {
                var t = c.gameObject.GetComponent<CameraTimer>() ?? c.gameObject.AddComponent<CameraTimer>();
                t.ticks = 0; t.renders = 0; t.recording = true;
                timers.Add(t);
            }
            InstallUpdaterTimers();
            var frames = new List<float>();
            var parts = new List<double[]>();   // per frame: update, late update, cameras (ms)
            var gcFrame = new List<bool>();
            int gc0 = GC.CollectionCount(0), gcPrev = gc0;
            long heapPrev = GC.GetTotalMemory(false), allocated = 0;
            float until = Time.realtimeSinceStartup + Seconds;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000f);
                double f = 1000.0 / Stopwatch.Frequency;
                parts.Add(new[] { upd * f, late * f, cam * f });
                upd = late = cam = 0;
                int gc = GC.CollectionCount(0);
                long heap = GC.GetTotalMemory(false);
                if (gc == gcPrev && heap > heapPrev) allocated += heap - heapPrev;   // growth between collections ~ allocations
                gcFrame.Add(gc != gcPrev);
                gcPrev = gc; heapPrev = heap;
            }
            foreach (var t in timers) if (t != null) t.recording = false;
            int n = Math.Max(1, frames.Count);

            var sb = new StringBuilder();
            sb.AppendLine("Census " + DateTime.Now + ": " + frames.Count + " frames in " + Seconds + " s");
            sb.AppendLine();
            sb.AppendLine("== Cameras (all, incl. disabled ones rendered by hand), main-thread ms per frame from pre-cull to post-render ==");
            foreach (var t in timers.Where(t => t != null).OrderByDescending(t => t.ticks))
            {
                Camera c = t.GetComponent<Camera>();
                var effects = c.GetComponents<MonoBehaviour>().Where(m => m != null && !(m is CameraTimer)).Select(m => m.GetType().Assembly.GetName().Name + ":" + m.GetType().Name);
                sb.AppendLine(string.Format("{0,6:N2} ms  {1,5:N2} renders/frame  '{2}' depth {3}, mask 0x{4:X}, target {5}, path {6}, hdr {7}",
                    t.ticks * 1000.0 / Stopwatch.Frequency / n, (double)t.renders / n, c.name, c.depth, c.cullingMask,
                    c.targetTexture != null ? c.targetTexture.width + "x" + c.targetTexture.height : "screen", c.actualRenderingPath, c.allowHDR) + (c.enabled ? "" : " [disabled]"));
                sb.AppendLine("        scripts: " + string.Join(", ", effects.ToArray()));
            }
            sb.AppendLine();

            var sorted = frames.OrderBy(f => f).ToList();
            float median = sorted[sorted.Count / 2];
            int slow = 0, slowWithGc = 0;
            for (int i = 0; i < frames.Count; i++)
                if (frames[i] > 2 * median) { slow++; if (gcFrame[i] || (i + 1 < frames.Count && gcFrame[i + 1])) slowWithGc++; }
            int collections = GC.CollectionCount(0) - gc0;
            var gcFrameTimes = frames.Where((f, i) => gcFrame[i] || (i + 1 < frames.Count && gcFrame[i + 1])).ToList();
            sb.AppendLine("== Garbage collection ==");
            sb.AppendLine(string.Format("Collections: {0} in {1} s; managed heap {2:N0} MB; allocation rate ~{3:N1} MB/s",
                collections, Seconds, GC.GetTotalMemory(false) / 1048576.0, allocated / 1048576.0 / Seconds));
            sb.AppendLine(string.Format("Frame median {0:N1} ms; slow frames (> {1:N1} ms): {2}, of which {3} had a collection",
                median, 2 * median, slow, slowWithGc));
            if (gcFrameTimes.Count > 0)
                sb.AppendLine(string.Format("Frames with a collection: avg {0:N1} ms, worst {1:N1} ms", gcFrameTimes.Average(), gcFrameTimes.Max()));
            sb.AppendLine();
            sb.AppendLine("== Where the time goes: normal frames vs slow frames (ms, averages) ==");
            sb.AppendLine("            frame   update  late-upd  cameras  other (engine, GPU/present wait)");
            foreach (bool wantSlow in new[] { false, true })
            {
                var idx = Enumerable.Range(0, frames.Count).Where(i => (frames[i] > 2 * median) == wantSlow).ToList();
                if (idx.Count == 0) continue;
                double fr = idx.Average(i => frames[i]), u = idx.Average(i => parts[i][0]), l = idx.Average(i => parts[i][1]), c = idx.Average(i => parts[i][2]);
                sb.AppendLine(string.Format("{0,-8} {1,7:N1} {2,8:N1} {3,9:N1} {4,8:N1} {5,8:N1}   ({6} frames)", wantSlow ? "slow" : "normal", fr, u, l, c, fr - u - l - c, idx.Count));
            }
            sb.AppendLine("Slowest 10 frames (ms, GC?): " + string.Join(", ",
                frames.Select((f, i) => new { f, gc = gcFrame[i] || (i + 1 < frames.Count && gcFrame[i + 1]) }).OrderByDescending(x => x.f).Take(10)
                      .Select(x => x.f.ToString("N0") + (x.gc ? " GC" : "")).ToArray()));

            string path = Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "census.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[ModProfiler] Census written: " + path);
        }
    }

    public partial class Census
    {
        // Per-frame totals, reset by the capture loop each frame.
        public static long upd, late, cam;
        static bool installed;

        // FPS Booster's updater runs every game Update/LateUpdate; time it as two blocks. Main-thread only methods.
        static void InstallUpdaterTimers()
        {
            if (installed) return;
            installed = true;
            Type t = HarmonyLib.AccessTools.TypeByName("BehaviourUpdater.Updater");
            if (t == null) { Debug.Log("[ModProfiler] Census: BehaviourUpdater.Updater not found"); return; }
            var h = new HarmonyLib.Harmony("modprofiler.census");
            var pre = new HarmonyLib.HarmonyMethod(typeof(Census).GetMethod("Pre", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
            h.Patch(HarmonyLib.AccessTools.Method(t, "Update", null, null), pre, new HarmonyLib.HarmonyMethod(typeof(Census).GetMethod("PostUpdate", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)), null, null);
            h.Patch(HarmonyLib.AccessTools.Method(t, "LateUpdate", null, null), pre, new HarmonyLib.HarmonyMethod(typeof(Census).GetMethod("PostLate", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)), null, null);
        }
        static void Pre(out long __state) { __state = Stopwatch.GetTimestamp(); }
        static void PostUpdate(long __state) { upd += Stopwatch.GetTimestamp() - __state; }
        static void PostLate(long __state) { late += Stopwatch.GetTimestamp() - __state; }
    }

    // Times one camera's culling + draw submission on the main thread.
    public class CameraTimer : MonoBehaviour
    {
        public long ticks; public int renders; public bool recording;
        long start;
        void OnPreCull() { start = Stopwatch.GetTimestamp(); }
        void OnPostRender() { if (start != 0) { long dt = Stopwatch.GetTimestamp() - start; Census.cam += dt; if (recording) { ticks += dt; renders++; } } start = 0; }
    }
}
