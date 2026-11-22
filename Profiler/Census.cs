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
    public class Census : MonoBehaviour
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
            var frames = new List<float>();
            var gcFrame = new List<bool>();
            int gc0 = GC.CollectionCount(0), gcPrev = gc0;
            long heapPrev = GC.GetTotalMemory(false), allocated = 0;
            float until = Time.realtimeSinceStartup + Seconds;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000f);
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
            sb.AppendLine("Slowest 10 frames (ms, GC?): " + string.Join(", ",
                frames.Select((f, i) => new { f, gc = gcFrame[i] || (i + 1 < frames.Count && gcFrame[i + 1]) }).OrderByDescending(x => x.f).Take(10)
                      .Select(x => x.f.ToString("N0") + (x.gc ? " GC" : "")).ToArray()));

            string path = Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "census.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[ModProfiler] Census written: " + path);
        }
    }

    // Times one camera's culling + draw submission on the main thread.
    public class CameraTimer : MonoBehaviour
    {
        public long ticks; public int renders; public bool recording;
        long start;
        void OnPreCull() { start = Stopwatch.GetTimestamp(); }
        void OnPostRender() { if (recording && start != 0) { ticks += Stopwatch.GetTimestamp() - start; renders++; } start = 0; }
    }
}
