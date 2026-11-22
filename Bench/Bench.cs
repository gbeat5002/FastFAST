// Bench: repeatable in-game benchmark for Cities: Skylines (no Harmony; reads only).
// Start: create the file  <game data>\Bench\run.flag  while a city is loaded (or before loading one).
// It waits until the city has settled, then:
//   1. picks 5 camera positions once (saved to positions.txt, reused by every later run),
//   2. captures 2 reference screenshots per position, paused, UI hidden (noise floor = A vs B),
//   3. per position: 60 s paused + 60 s unpaused at 3x, recording every frame time.
// Output: <game data>\Bench\<run id>\ (frames.csv, segments.csv, meta.txt, shot_*.png).
// A segment is marked invalid if the game lost focus or the camera moved during it.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ColossalFramework.IO;
using ColossalFramework.UI;
using ICities;
using UnityEngine;

namespace Bench
{
    public class Loading : LoadingExtensionBase
    {
        public override void OnLevelLoaded(LoadMode mode)
        {
            if (mode == LoadMode.NewGame || mode == LoadMode.LoadGame || mode == LoadMode.NewGameFromScenario)
                new GameObject("BenchRunner").AddComponent<Runner>();
        }

        public override void OnLevelUnloading()
        {
            GameObject g = GameObject.Find("BenchRunner");
            if (g != null) UnityEngine.Object.Destroy(g);
        }
    }

    struct CamPos
    {
        public string name; public Vector3 pos; public Vector2 angle; public float size;
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0};{1};{2};{3};{4};{5};{6}", name, pos.x, pos.y, pos.z, angle.x, angle.y, size);
        }
        public static CamPos Parse(string line)
        {
            string[] p = line.Split(';');
            Func<int, float> f = i => float.Parse(p[i], CultureInfo.InvariantCulture);
            return new CamPos { name = p[0], pos = new Vector3(f(1), f(2), f(3)), angle = new Vector2(f(4), f(5)), size = f(6) };
        }
    }

    public class Runner : MonoBehaviour
    {
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

        const float SettleSeconds = 60f, SegmentSeconds = 60f;
        static string Root { get { return Path.Combine(DataLocation.localApplicationData, "Bench"); } }

        float loadedAt;
        bool running, focusLost;
        uint simThreadId;
        string dir;
        StreamWriter frames;

        void Start() { loadedAt = Time.realtimeSinceStartup; Directory.CreateDirectory(Root); }

        void OnApplicationFocus(bool focused) { if (!focused) focusLost = true; }

        float nextCheck;
        void Update()
        {
            if (running || Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + 2f;
            if (Time.realtimeSinceStartup - loadedAt < SettleSeconds) return;   // let post-load work finish
            string flag = Path.Combine(Root, "run.flag");
            if (!File.Exists(flag)) return;
            try { File.Delete(flag); } catch { }
            StartCoroutine(Run());
        }

        static void Log(string s) { Debug.Log("[Bench] " + s); }

        IEnumerator Run()
        {
            running = true;
            new GameObject("BenchRunning");   // other mods (On-Demand Assets) hide their overlays while this exists
            string id = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            dir = Path.Combine(Root, id);
            Directory.CreateDirectory(dir);
            SimulationManager sim = SimulationManager.instance;
            sim.SimulationPaused = true;
            sim.AddAction(() => { simThreadId = GetCurrentThreadId(); });
            yield return new WaitForSeconds(1f);

            List<CamPos> positions = LoadOrPickPositions();
            var meta = new StringBuilder();
            meta.AppendLine("run " + id + " started " + DateTime.UtcNow.ToString("o"));
            meta.AppendLine("main thread OS id " + GetCurrentThreadId() + ", simulation thread OS id " + simThreadId);
            meta.AppendLine(string.Format("resolution {0}x{1}, vSyncCount {2}, targetFrameRate {3}, quality '{4}'",
                Screen.width, Screen.height, QualitySettings.vSyncCount, Application.targetFrameRate, QualitySettings.names[QualitySettings.GetQualityLevel()]));
            meta.AppendLine("game time at start " + sim.m_currentGameTime.ToString("o"));
            foreach (CamPos p in positions) meta.AppendLine("position " + p);
            File.WriteAllText(Path.Combine(dir, "meta.txt"), meta.ToString());
            WriteModList(Path.Combine(dir, "mods.csv"));
            Log("run " + id + ": capturing reference screenshots");

            // 1. Reference screenshots, paused, UI hidden: A and B a few seconds apart give the noise floor.
            foreach (CamPos p in positions)
            {
                SetCamera(p);
                SetDayTime(BenchHour);
                yield return new WaitForSeconds(4f);   // LOD and texture streaming settle
                foreach (string take in new[] { "A", "B" })
                {
                    UIView.Show(false);
                    yield return null; yield return null;
                    Application.CaptureScreenshot(Path.Combine(dir, "shot_" + p.name + "_" + take + ".png"));
                    File.AppendAllText(Path.Combine(dir, "meta.txt"), "shot " + p.name + " " + take + ": " + SceneState() + Environment.NewLine);
                    yield return new WaitForEndOfFrame();
                    yield return null;
                    UIView.Show(true);
                    yield return new WaitForSeconds(2f);
                }
            }

            // 2. Frame timing: each position paused, then unpaused at 3x.
            var segs = new StringBuilder("segment,position,mode,start_utc,end_utc,frames,avg_ms,avg_fps,low1_fps,low01_fps,valid,why_invalid\n");
            frames = new StreamWriter(Path.Combine(dir, "frames.csv"));
            frames.WriteLine("segment,frame_ms");
            int seg = 0;
            foreach (CamPos p in positions)
                foreach (bool paused in new[] { true, false })
                {
                    SetCamera(p);
                    SetDayTime(BenchHour);   // every run sees the same lighting, whatever time the city was at
                    sim.SelectedSimulationSpeed = 3;
                    sim.SimulationPaused = paused;
                    yield return new WaitForSeconds(3f);   // let the camera move and the speed change settle
                    focusLost = false;
                    Vector3 camStart = Camera.main.transform.position; Quaternion rotStart = Camera.main.transform.rotation;
                    string startUtc = DateTime.UtcNow.ToString("o");
                    Log(string.Format("segment {0} start: {1} {2}", seg, p.name, paused ? "paused" : "3x"));
                    File.AppendAllText(Path.Combine(dir, "meta.txt"), "segment " + seg + " start: " + SceneState() + Environment.NewLine);
                    var times = new List<float>();
                    float until = Time.realtimeSinceStartup + SegmentSeconds;
                    while (Time.realtimeSinceStartup < until)
                    {
                        yield return null;
                        float ms = Time.unscaledDeltaTime * 1000f;
                        times.Add(ms);
                        frames.WriteLine(seg + "," + ms.ToString("F3", CultureInfo.InvariantCulture));
                    }
                    Log(string.Format("segment {0} end", seg));
                    string endUtc = DateTime.UtcNow.ToString("o");
                    string why = "";
                    if (focusLost) why += "focus lost;";
                    if (Vector3.Distance(camStart, Camera.main.transform.position) > 0.5f || Quaternion.Angle(rotStart, Camera.main.transform.rotation) > 0.2f) why += "camera moved;";
                    segs.AppendLine(Summary(seg, p.name, paused ? "paused" : "3x", startUtc, endUtc, times, why));
                    seg++;
                }
            sim.SimulationPaused = true;
            frames.Close();
            File.WriteAllText(Path.Combine(dir, "segments.csv"), segs.ToString());
            File.WriteAllText(Path.Combine(dir, "done.txt"), DateTime.UtcNow.ToString("o"));
            Destroy(GameObject.Find("BenchRunning"));
            Log("run " + id + " finished: " + dir);
            running = false;
        }

        // The game's own view of every mod: enabled flag, folder, and display name.
        static void WriteModList(string path)
        {
            var sb = new StringBuilder("enabled,name,folder\n");
            foreach (ColossalFramework.Plugins.PluginManager.PluginInfo pi in ColossalFramework.Plugins.PluginManager.instance.GetPluginsInfo())
            {
                string name = pi.name;
                try { var um = pi.userModInstance as IUserMod; if (um != null) name = um.Name; } catch { }
                sb.AppendLine(pi.isEnabled + ",\"" + (name ?? "").Replace("\"", "'") + "\"," + pi.modPath);
            }
            File.WriteAllText(path, sb.ToString());
        }

        static string Summary(int seg, string pos, string mode, string start, string end, List<float> ms, string why)
        {
            var sorted = new List<float>(ms); sorted.Sort(); sorted.Reverse();   // slowest first
            double total = 0; foreach (float m in ms) total += m;
            double avg = ms.Count > 0 ? total / ms.Count : 0;
            Func<double, double> low = frac =>
            {
                int n = Math.Max(1, (int)(sorted.Count * frac)); double s = 0;
                for (int i = 0; i < n && i < sorted.Count; i++) s += sorted[i];
                return s > 0 ? 1000.0 / (s / n) : 0;   // average FPS of the slowest fraction of frames
            };
            return string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5},{6:F2},{7:F1},{8:F1},{9:F1},{10},{11}",
                seg, pos, mode, start, end, ms.Count, avg, avg > 0 ? 1000 / avg : 0, low(0.01), low(0.001), why.Length == 0, why);
        }

        const float BenchHour = 12f;

        // Day/night is driven by frame index + offset; moving the offset jumps the clock without touching the simulation.
        static void SetDayTime(float hour)
        {
            SimulationManager sim = SimulationManager.instance;
            uint target = (uint)(hour * SimulationManager.DAYTIME_HOUR_TO_FRAME);
            sim.m_dayTimeOffsetFrames = (target - sim.m_currentFrameIndex) & (SimulationManager.DAYTIME_FRAMES - 1);
            sim.m_dayTimeFrame = target;
            sim.m_currentDayTimeHour = hour;
            // Weather is random per new game (overcast darkens the frame and turns on headlights): pin it clear.
            WeatherManager w = WeatherManager.instance;
            w.m_currentRain = w.m_targetRain = 0f;
            w.m_currentFog = w.m_targetFog = 0f;
            w.m_currentCloud = w.m_targetCloud = 0f;
            w.m_currentNorthernLights = w.m_targetNorthernLights = 0f;
            w.m_currentRainbow = w.m_targetRainbow = 0f;
        }

        static string SceneState()
        {
            SimulationManager sim = SimulationManager.instance; WeatherManager w = WeatherManager.instance;
            return string.Format(CultureInfo.InvariantCulture, "hour {0:F2}, rain {1:F2}, fog {2:F2}, cloud {3:F2}",
                sim.m_currentDayTimeHour, w.m_currentRain, w.m_currentFog, w.m_currentCloud);
        }

        static void SetCamera(CamPos p)
        {
            CameraController c = ToolsModifierControl.cameraController;
            c.m_targetPosition = c.m_currentPosition = p.pos;
            c.m_targetAngle = c.m_currentAngle = p.angle;
            c.m_targetSize = c.m_currentSize = p.size;
            c.m_targetHeight = c.m_currentHeight = p.pos.y;
        }

        // Positions are chosen once from the city's own data and then frozen in positions.txt.
        List<CamPos> LoadOrPickPositions()
        {
            string file = Path.Combine(Root, "positions.txt");
            var list = new List<CamPos>();
            if (File.Exists(file))
            {
                foreach (string line in File.ReadAllLines(file)) if (line.Trim().Length > 0) list.Add(CamPos.Parse(line));
                return list;
            }
            // Empty city: no densest cell, so spread the views over the start area instead of stacking them on the origin.
            Vector3 downtown = Densest(BuildingCells(), new Vector3(0, 0, 0));
            Vector3 interchange = Densest(NodeCells(), new Vector3(700, 0, 500));
            Vector3 assets = Densest(PropCells(), new Vector3(-600, 0, -400));
            CameraController c = ToolsModifierControl.cameraController;
            list.Add(Make("street", downtown, 30f, 20f, c.m_minDistance + 40f));
            list.Add(Make("downtown_mid", downtown, 30f, 45f, 900f));
            list.Add(Make("zoomed_out", Vector3.zero, 0f, 60f, c.m_maxDistance));
            list.Add(Make("interchange", interchange, 60f, 40f, 450f));
            list.Add(Make("dense_assets", assets, 120f, 35f, 300f));
            var sb = new StringBuilder();
            foreach (CamPos p in list) sb.AppendLine(p.ToString());
            File.WriteAllText(file, sb.ToString());
            return list;
        }

        static CamPos Make(string name, Vector3 at, float heading, float pitch, float size)
        {
            at.y = TerrainManager.instance.SampleRawHeightSmooth(at);
            return new CamPos { name = name, pos = at, angle = new Vector2(heading, pitch), size = size };
        }

        const float Cell = 256f;
        static Vector3 Densest(Dictionary<long, int> cells, Vector3 fallback)
        {
            long best = 0; int max = -1;
            foreach (var kv in cells) if (kv.Value > max) { max = kv.Value; best = kv.Key; }
            if (max < 0) return fallback;
            return new Vector3(((int)(best >> 32) + 0.5f) * Cell, 0, ((int)(best & 0xffffffff) + 0.5f) * Cell);
        }

        static void Count(Dictionary<long, int> cells, Vector3 p)
        {
            long key = ((long)Mathf.FloorToInt(p.x / Cell) << 32) | (uint)Mathf.FloorToInt(p.z / Cell);
            int n; cells.TryGetValue(key, out n); cells[key] = n + 1;
        }

        static Dictionary<long, int> BuildingCells()
        {
            var cells = new Dictionary<long, int>();
            Building[] b = BuildingManager.instance.m_buildings.m_buffer;
            for (int i = 1; i < b.Length; i++) if ((b[i].m_flags & Building.Flags.Created) != 0) Count(cells, b[i].m_position);
            return cells;
        }

        static Dictionary<long, int> NodeCells()
        {
            var cells = new Dictionary<long, int>();
            NetNode[] n = NetManager.instance.m_nodes.m_buffer;
            for (int i = 1; i < n.Length; i++) if ((n[i].m_flags & NetNode.Flags.Created) != 0 && n[i].CountSegments() >= 3) Count(cells, n[i].m_position);
            return cells;
        }

        static Dictionary<long, int> PropCells()
        {
            var cells = new Dictionary<long, int>();
            PropInstance[] p = PropManager.instance.m_props.m_buffer;
            for (int i = 1; i < p.Length; i++) if ((p[i].m_flags & (ushort)PropInstance.Flags.Created) != 0) Count(cells, p[i].Position);
            return cells;
        }
    }
}
