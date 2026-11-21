// Feasibility test: load ONE custom asset mid-game (not loaded at startup), place it, save, reload.
// Mirrors the game's own Asset Editor sequence (LoadAssetPanel.OnLoad):
//   Instantiate -> name -> SetActive(false) -> InitializePrefabs -> BindPrefabs -> InitRenderData.
// Hotkeys in game: Ctrl+Shift+P = load next unloaded prop, Ctrl+Shift+B = next unloaded building.
// Everything is logged to output_log.txt with the tag [OnDemandTest].
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using ColossalFramework.Packaging;
using ICities;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OnDemandTest
{
    public class Mod : IUserMod
    {
        public string Name { get { return "On-Demand Loader Test"; } }
        public string Description { get { return "Ctrl+Shift+P / Ctrl+Shift+B: load an unloaded prop/building mid-game. Test only."; } }

        // Hot reload while a city is open: replace the old tester so the new code runs without a restart.
        public void OnEnabled()
        {
            if (!LoadingManager.exists || !LoadingManager.instance.m_loadingComplete) return;
            GameObject old = GameObject.Find("OnDemandTest");
            if (old != null) UnityEngine.Object.Destroy(old);
            new GameObject("OnDemandTest").AddComponent<Tester>();
        }
    }

    public class Loading : LoadingExtensionBase
    {
        GameObject host;

        public override void OnLevelLoaded(LoadMode mode)
        {
            if (mode != LoadMode.NewGame && mode != LoadMode.LoadGame && mode != LoadMode.NewGameFromScenario) return;
            host = new GameObject("OnDemandTest");
            host.AddComponent<Tester>();
        }

        public override void OnLevelUnloading()
        {
            if (host != null) UnityEngine.Object.Destroy(host);
        }
    }

    public class Tester : MonoBehaviour
    {
        readonly List<CustomAssetMetaData> props = new List<CustomAssetMetaData>();
        readonly List<CustomAssetMetaData> buildings = new List<CustomAssetMetaData>();
        int nextProp, nextBuilding;
        string status = "On-Demand Test: scanning...";

        static void Log(string msg) { Debug.Log("[OnDemandTest] " + msg); }

        static long PrivateMB()
        {
            try { return Process.GetCurrentProcess().PrivateMemorySize64 / (1024 * 1024); } catch { return -1; }
        }

        void Start()
        {
            var sw = Stopwatch.StartNew();
            int total = 0, loaded = 0;
            try
            {
                LogDeserializer();
                LogLoadedNameSamples();
                foreach (Package.Asset a in PackageManager.FilterAssets(UserAssetType.CustomAssetMetaData))
                {
                    if (a == null || !a.isEnabled) continue;
                    CustomAssetMetaData meta;
                    try { meta = a.Instantiate<CustomAssetMetaData>(); } catch { continue; }
                    if (meta == null || meta.assetRef == null) continue;
                    if (meta.type != CustomAssetMetaData.Type.Prop && meta.type != CustomAssetMetaData.Type.Building) continue;
                    total++;
                    string name = PrefabName(meta);
                    bool isLoaded = meta.type == CustomAssetMetaData.Type.Prop
                        ? PrefabCollection<PropInfo>.LoadedExists(name)
                        : PrefabCollection<BuildingInfo>.LoadedExists(name);
                    if (isLoaded) { loaded++; continue; }
                    (meta.type == CustomAssetMetaData.Type.Prop ? props : buildings).Add(meta);
                }
                // Fewest triangles first: simplest assets make the cleanest first test.
                Comparison<CustomAssetMetaData> byTris = (x, y) => x.triangles.CompareTo(y.triangles);
                props.Sort(byTris);
                buildings.Sort(byTris);
            }
            catch (Exception e) { Log("SCAN FAILED: " + e); }
            status = string.Format("On-Demand Test: {0} enabled props/buildings, {1} already loaded, {2} props + {3} buildings NOT loaded. Ctrl+Shift+P / Ctrl+Shift+B",
                total, loaded, props.Count, buildings.Count);
            Log(status + " (scan " + sw.ElapsedMilliseconds + " ms)");
            if (props.Count + buildings.Count == 0)
                Log("Nothing to test: every asset is already loaded. Untick LSM 'Load enabled assets' and reload.");
        }

        static string PrefabName(CustomAssetMetaData meta)
        {
            return meta.assetRef.package.packageName + "." + meta.assetRef.name;
        }

        void Update()
        {
            bool combo = (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                      && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            if (combo && Input.GetKeyDown(KeyCode.P) && nextProp < props.Count) LoadOne<PropInfo>(props[nextProp++]);
            if (combo && Input.GetKeyDown(KeyCode.B) && nextBuilding < buildings.Count) LoadOne<BuildingInfo>(buildings[nextBuilding++]);
            if (Time.frameCount % 30 == 0) PollCommands();
        }

        // Unattended driving: write lines to cmd.txt next to the DLL. Commands: prop, building, place, zoomout, zoomin.
        PrefabInfo lastLoaded;
        void PollCommands()
        {
            string dir = System.IO.Path.GetDirectoryName(typeof(Tester).Assembly.Location);
            string file = System.IO.Path.Combine(dir, "cmd.txt");
            if (!System.IO.File.Exists(file)) return;
            string[] lines;
            try { lines = System.IO.File.ReadAllLines(file); System.IO.File.Delete(file); } catch { return; }
            foreach (string raw in lines)
            {
                string cmd = raw.Trim().ToLowerInvariant();
                if (cmd.Length == 0) continue;
                Log("CMD " + cmd);
                if (cmd == "prop" && nextProp < props.Count) lastLoaded = LoadOne<PropInfo>(props[nextProp++]);
                else if (cmd == "building" && nextBuilding < buildings.Count) lastLoaded = LoadOne<BuildingInfo>(buildings[nextBuilding++]);
                else if (cmd == "place") PlaceLast();
                else if (cmd == "zoomout" || cmd == "zoomin")
                {
                    CameraController cam = ToolsModifierControl.cameraController;
                    cam.m_targetSize = cmd == "zoomout" ? cam.m_maxDistance : 40f;
                    Log("Camera target size -> " + cam.m_targetSize);
                }
            }
        }

        // Place the last loaded asset at the camera's focus point, on the simulation thread like the game's tools.
        void PlaceLast()
        {
            if (lastLoaded == null) { Log("PLACE: nothing loaded"); return; }
            Vector3 pos = ToolsModifierControl.cameraController.m_targetPosition;
            pos.y = TerrainManager.instance.SampleRawHeightSmooth(pos);
            PrefabInfo info = lastLoaded;
            SimulationManager.instance.AddAction(delegate
            {
                SimulationManager sm = SimulationManager.instance;
                ushort id; bool ok;
                var b = info as BuildingInfo;
                if (b != null)
                {
                    ok = BuildingManager.instance.CreateBuilding(out id, ref sm.m_randomizer, b, pos, 0f, b.m_cellLength, sm.m_currentBuildIndex);
                    if (ok) sm.m_currentBuildIndex++;
                }
                else ok = PropManager.instance.CreateProp(out id, ref sm.m_randomizer, (PropInfo)info, pos, 0f, true);
                Log("PLACE " + info.name + " at " + pos + " -> ok=" + ok + " id=" + id);
            });
        }

        T LoadOne<T>(CustomAssetMetaData meta) where T : PrefabInfo
        {
            T result = null;
            string expected = PrefabName(meta);
            Log("---- Loading " + typeof(T).Name + " '" + expected + "' tris=" + meta.triangles
                + " tex=" + meta.textureWidth + "x" + meta.textureHeight);
            long mem0 = PrivateMB();
            var sw = Stopwatch.StartNew();
            SimulationManager sim = SimulationManager.instance;
            bool wasPaused = sim.SimulationPaused;
            sim.SimulationPaused = true; // keep the sim thread quiet while prefab tables change
            try
            {
                GameObject go = meta.assetRef.Instantiate<GameObject>();
                long tInst = sw.ElapsedMilliseconds;
                go.name = meta.assetRef.package.packageName + "." + go.name;
                if (go.name != expected) Log("NAME MISMATCH: got '" + go.name + "' expected '" + expected + "'");
                go.SetActive(false);

                T info = go.GetComponent<T>();
                if (info == null) { Log("FAIL: no " + typeof(T).Name + " component"); Destroy(go); return null; }
                if (PrefabCollection<T>.LoadedExists(go.name)) { Log("SKIP: already loaded"); Destroy(go); return null; }

                info.m_isCustomContent = true;
                if (info.m_Atlas != null && !string.IsNullOrEmpty(info.m_InfoTooltipThumbnail) && info.m_Atlas[info.m_InfoTooltipThumbnail] != null)
                    info.m_InfoTooltipAtlas = info.m_Atlas;
                var b = info as BuildingInfo; if (b != null && b.m_lodObject != null) b.m_lodObject.SetActive(false);
                var p = info as PropInfo; if (p != null && p.m_lodObject != null) p.m_lodObject.SetActive(false);

                PrefabCollection<T>.InitializePrefabs("Custom Assets", info, null);
                PrefabCollection<T>.BindPrefabs();
                long tBind = sw.ElapsedMilliseconds;
                Log("Registered: index=" + info.m_prefabDataIndex + " initialized=" + info.m_prefabInitialized
                    + " found=" + PrefabCollection<T>.LoadedExists(go.name));
                if (info.m_prefabDataIndex < 0) { Log("FAIL: prefab index unbound, not activating tool"); return null; }
                result = info;

                if (b != null) { LogMissingRefs(b); BuildingManager.instance.InitRenderData(); }
                if (p != null) PropManager.instance.InitRenderData();
                long tRender = sw.ElapsedMilliseconds;

                if (b != null) { var bt = ToolsModifierControl.SetTool<BuildingTool>(); bt.m_prefab = b; bt.m_relocate = 0; }
                if (p != null) { var pt = ToolsModifierControl.SetTool<PropTool>(); pt.m_prefab = p; }

                long mem1 = PrivateMB();
                status = "Loaded '" + go.name + "' in " + tRender + " ms. Place it, zoom fully out, save as a NEW save.";
                Log(string.Format("OK: instantiate {0} ms, register+bind {1} ms, render-data {2} ms, total {3} ms, RAM {4} -> {5} MB (+{6})",
                    tInst, tBind - tInst, tRender - tBind, tRender, mem0, mem1, mem1 - mem0));
            }
            catch (Exception e)
            {
                status = "FAILED: " + e.GetType().Name + " (see output_log.txt)";
                Log("EXCEPTION: " + e);
            }
            finally { sim.SimulationPaused = wasPaused; }
            return result;
        }

        static void LogMissingRefs(BuildingInfo b)
        {
            int missingProps = 0, totalProps = 0, missingSubs = 0;
            if (b.m_props != null)
                foreach (var pr in b.m_props)
                {
                    totalProps++;
                    if (pr.m_prop == null && pr.m_tree == null && pr.m_finalProp == null && pr.m_finalTree == null) missingProps++;
                }
            if (b.m_subBuildings != null)
                foreach (var sb in b.m_subBuildings) if (sb.m_buildingInfo == null) missingSubs++;
            Log("Dependencies: props " + (totalProps - missingProps) + "/" + totalProps + " resolved, missing sub-buildings " + missingSubs);
        }

        // Which custom deserializer is active right now (LSM swaps its own in during loading).
        static void LogDeserializer()
        {
            try
            {
                foreach (FieldInfo f in typeof(PackageDeserializer).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (!typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                    var d = f.GetValue(null) as Delegate;
                    Log("Deserializer field " + f.Name + " = " + (d == null ? "null" : d.Method.DeclaringType.FullName + "." + d.Method.Name));
                }
            }
            catch (Exception e) { Log("Deserializer check failed: " + e.Message); }
        }

        static void LogLoadedNameSamples()
        {
            int shown = 0;
            for (uint i = 0; i < PrefabCollection<BuildingInfo>.LoadedCount() && shown < 3; i++)
            {
                BuildingInfo info = PrefabCollection<BuildingInfo>.GetLoaded(i);
                if (info != null && info.m_isCustomContent) { Log("Loaded custom name sample: " + info.name); shown++; }
            }
        }

        void OnGUI()
        {
            GUI.Label(new Rect(10, 120, 900, 40), status);
        }
    }
}
