// On-Demand Assets (Cities: Skylines 1)
// Browse enabled custom props/buildings that Loading Screen Mod did NOT load (use LSM "Load used assets" only),
// and load one the moment you pick it. Dependencies (props/trees a building uses) are loaded first.
// Once placed and saved, the asset is "used", so LSM loads it normally next time.
//
// Loading mirrors the game's own Asset Editor sequence (LoadAssetPanel.OnLoad):
//   Instantiate -> name -> SetActive(false) -> InitializePrefabs -> BindPrefabs -> InitRenderData.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using ColossalFramework.Packaging;
using ICities;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OnDemandAssets
{
    // Part of the FastFAST mod: on-demand asset browser (Ctrl+Shift+L) and texture RAM trimming.
    public static class Part
    {
        public static void AddOptions(UIHelperBase helper)
        {
            helper.AddGroup("RAM (On-Demand Assets)").AddCheckbox("Free RAM copies of tooltip and thumbnail images (takes effect next city load)",
                TextureTrim.TrimUiAtlases, on => TextureTrim.TrimUiAtlases = on);
        }

        // Enabled while a city is open (or hot-reloaded): attach without needing a reload.
        public static void OnEnabled()
        {
            if (LoadingManager.exists && LoadingManager.instance.m_loadingComplete) Controller.Create();
        }

        public static void OnDisabled() { Controller.DestroyAll(); }
    }

    public class Loading : LoadingExtensionBase
    {
        public override void OnLevelLoaded(LoadMode mode)
        {
            if (mode == LoadMode.NewGame || mode == LoadMode.LoadGame || mode == LoadMode.NewGameFromScenario) Controller.Create();
        }
        public override void OnLevelUnloading() { Controller.DestroyAll(); }
    }

    class Entry
    {
        public CustomAssetMetaData meta;
        public string prefabName;   // "<package>.<asset>_Data", the name the game and saves use
        public string display;
        public string search;       // lower-case display, for filtering
    }

    public class Controller : MonoBehaviour
    {
        const string HostName = "OnDemandAssets";
        const int MaxThumbs = 240;          // ponytail: plain LRU cap; tune if scrolling feels slow or RAM matters
        const int ThumbLoadsPerFrame = 3;
        const int MaxDepDepth = 3;

        public static void Create()
        {
            DestroyAll();
            var host = new GameObject(HostName);
            host.AddComponent<Controller>();
            host.AddComponent<TextureTrim>();
        }

        public static void DestroyAll()
        {
            GameObject old;
            while ((old = GameObject.Find(HostName)) != null) DestroyImmediate(old);
        }

        static void Log(string msg) { Debug.Log("[OnDemandAssets] " + msg); }

        // ---------------- catalog ----------------
        readonly Dictionary<string, Entry> byName = new Dictionary<string, Entry>();   // every unloaded prop/tree/building
        readonly List<Entry> props = new List<Entry>();
        readonly List<Entry> buildings = new List<Entry>();

        void Start()
        {
            var sw = Stopwatch.StartNew();
            foreach (Package.Asset a in PackageManager.FilterAssets(UserAssetType.CustomAssetMetaData))
            {
                if (a == null || !a.isEnabled) continue;
                CustomAssetMetaData meta;
                try { meta = a.Instantiate<CustomAssetMetaData>(); } catch { continue; }
                if (meta == null || meta.assetRef == null) continue;
                var t = meta.type;
                if (t != CustomAssetMetaData.Type.Prop && t != CustomAssetMetaData.Type.Building && t != CustomAssetMetaData.Type.Tree) continue;
                string name = meta.assetRef.package.packageName + "." + meta.assetRef.name;
                if (IsLoaded(t, name) || byName.ContainsKey(name)) continue;
                string display = string.IsNullOrEmpty(meta.name) ? meta.assetRef.name : meta.name;
                var e = new Entry { meta = meta, prefabName = name, display = display, search = display.ToLowerInvariant() };
                byName[name] = e;
                if (t == CustomAssetMetaData.Type.Prop) props.Add(e);
                else if (t == CustomAssetMetaData.Type.Building) buildings.Add(e);
            }
            Comparison<Entry> byDisplay = (x, y) => string.Compare(x.display, y.display, StringComparison.OrdinalIgnoreCase);
            props.Sort(byDisplay);
            buildings.Sort(byDisplay);
            Log(string.Format("Catalog: {0} buildings, {1} props, {2} total incl. trees not loaded ({3} ms)",
                buildings.Count, props.Count, byName.Count, sw.ElapsedMilliseconds));
        }

        static bool IsLoaded(CustomAssetMetaData.Type t, string name)
        {
            if (t == CustomAssetMetaData.Type.Prop) return PrefabCollection<PropInfo>.LoadedExists(name);
            if (t == CustomAssetMetaData.Type.Tree) return PrefabCollection<TreeInfo>.LoadedExists(name);
            return PrefabCollection<BuildingInfo>.LoadedExists(name);
        }

        // ---------------- loading ----------------
        bool touchedBuildings, touchedProps;
        int loadedCount;
        readonly HashSet<string> inProgress = new HashSet<string>();
        PrefabInfo lodCheck;      // checked a few seconds after a pick: did the deferred LOD rebuild run?
        float lodCheckAt;

        void Pick(Entry e)
        {
            var sw = Stopwatch.StartNew();
            SimulationManager sim = SimulationManager.instance;
            bool wasPaused = sim.SimulationPaused;
            sim.SimulationPaused = true;   // keep the simulation thread off the prefab tables while they change
            touchedBuildings = touchedProps = false;
            loadedCount = 0;
            inProgress.Clear();
            PrefabInfo info = null;
            try
            {
                info = LoadWithDeps(e, 0);
                if (touchedBuildings) BuildingManager.instance.InitRenderData();
                if (touchedProps) PropManager.instance.InitRenderData();
            }
            catch (Exception ex) { Log("Load failed for " + e.prefabName + ": " + ex); }
            finally { sim.SimulationPaused = wasPaused; }

            if (info == null) { Status("Could not load " + e.display + " (see output_log.txt)"); return; }
            Status(string.Format("Loaded {0} ({1} asset{2}) in {3} ms", e.display, loadedCount, loadedCount == 1 ? "" : "s", sw.ElapsedMilliseconds));
            Log(lastStatus);
            lodCheck = info; lodCheckAt = Time.realtimeSinceStartup + 5f;
            var trim = GetComponent<TextureTrim>(); if (trim != null) trim.TrimLater();

            var b = info as BuildingInfo;
            if (b != null) { var bt = ToolsModifierControl.SetTool<BuildingTool>(); bt.m_prefab = b; bt.m_relocate = 0; }
            var p = info as PropInfo;
            if (p != null) { var pt = ToolsModifierControl.SetTool<PropTool>(); pt.m_prefab = p; }
            showPanel = false;
        }

        PrefabInfo LoadWithDeps(Entry e, int depth)
        {
            var t = e.meta.type;
            if (IsLoaded(t, e.prefabName)) return Find(t, e.prefabName);
            if (!inProgress.Add(e.prefabName)) return null;   // reference cycle: already being loaded higher up

            // Load what this asset references first, so the game's deserializer can resolve it by name.
            if (depth < MaxDepDepth)
            {
                List<string> refs = ReferencedNames(e.meta.assetRef);
                int matched = 0;
                var unmatched = new List<string>();
                foreach (string dep in refs)
                {
                    Entry d;
                    if (dep == e.prefabName) continue;
                    if (byName.TryGetValue(dep, out d)) { matched++; LoadWithDeps(d, depth + 1); }
                    else if (unmatched.Count < 5 && WorkshopName.IsMatch(dep)) unmatched.Add(dep);
                }
                Log(e.prefabName + ": " + refs.Count + " referenced names, " + matched + " unloaded dependencies in catalog"
                    + (unmatched.Count > 0 ? "; not in catalog (loaded, missing or other type): " + string.Join(" | ", unmatched.ToArray()) : ""));
            }

            // A dependency (e.g. a sibling asset in the same .crp) may have loaded this one meanwhile.
            if (IsLoaded(t, e.prefabName)) { Forget(e); return Find(t, e.prefabName); }

            PrefabInfo info;
            if (t == CustomAssetMetaData.Type.Prop) { info = Register<PropInfo>(e); touchedProps = true; }
            else if (t == CustomAssetMetaData.Type.Tree) info = Register<TreeInfo>(e);
            else { info = Register<BuildingInfo>(e); touchedBuildings = true; }
            if (info != null) { loadedCount++; Forget(e); }
            return info;
        }

        static PrefabInfo Find(CustomAssetMetaData.Type t, string name)
        {
            if (t == CustomAssetMetaData.Type.Prop) return PrefabCollection<PropInfo>.FindLoaded(name);
            if (t == CustomAssetMetaData.Type.Tree) return PrefabCollection<TreeInfo>.FindLoaded(name);
            return PrefabCollection<BuildingInfo>.FindLoaded(name);
        }

        static T Register<T>(Entry e) where T : PrefabInfo
        {
            Package.Asset main = e.meta.assetRef;
            GameObject go = main.Instantiate<GameObject>();
            go.name = main.package.packageName + "." + go.name;
            go.SetActive(false);
            T info = go.GetComponent<T>();
            if (info == null) { Log("No " + typeof(T).Name + " on " + e.prefabName); Destroy(go); return null; }

            info.m_isCustomContent = true;
            if (info.m_Atlas != null && !string.IsNullOrEmpty(info.m_InfoTooltipThumbnail) && info.m_Atlas[info.m_InfoTooltipThumbnail] != null)
                info.m_InfoTooltipAtlas = info.m_Atlas;
            var b = info as BuildingInfo; if (b != null && b.m_lodObject != null) b.m_lodObject.SetActive(false);
            var p = info as PropInfo; if (p != null && p.m_lodObject != null) p.m_lodObject.SetActive(false);
            PrefabCollection<T>.InitializePrefabs("Custom Assets", info, null);
            PrefabCollection<T>.BindPrefabs();
            if (info.m_prefabDataIndex < 0) { Log("Unbound prefab index for " + e.prefabName); return null; }
            Log("Loaded " + typeof(T).Name + " " + go.name + " -> index " + info.m_prefabDataIndex);
            return info;
        }

        // Names the asset references are stored as length-prefixed strings ("<id>.<name>_Data").
        // ponytail: byte scan for such strings instead of re-implementing the game's deserializer;
        // non-matching noise is harmless because only names found in the catalog are used.
        static readonly byte[] Suffix = Encoding.ASCII.GetBytes("_Data");
        static readonly System.Text.RegularExpressions.Regex WorkshopName =
            new System.Text.RegularExpressions.Regex(@"^\d{6,12}\.[^\x00-\x1F]+_Data$");
        static List<string> ReferencedNames(Package.Asset asset)
        {
            var names = new List<string>();
            byte[] b;
            // Read the whole .crp from disk, which is how the scan was validated offline.
            // (Asset.pathOnDisk is only set for extracted assets; the package knows its .crp path.)
            string path = asset.package.packagePath;
            try { b = System.IO.File.ReadAllBytes(path); }
            catch (Exception ex) { Log("Could not read " + path + ": " + ex.Message); return names; }
            for (int i = 0; i < b.Length - 7; i++)
            {
                int len = b[i], pre = 1;
                if (len >= 0x80) { len = (len & 0x7F) | (b[i + 1] << 7); pre = 2; }
                if (len < 6 || len > 300) continue;
                int end = i + pre + len;
                if (end > b.Length) continue;
                bool match = true;
                for (int k = 0; k < 5; k++) if (b[end - 5 + k] != Suffix[k]) { match = false; break; }
                if (match) names.Add(Encoding.UTF8.GetString(b, i + pre, len));
            }
            return names;
        }

        void Forget(Entry e)
        {
            byName.Remove(e.prefabName);
            props.Remove(e);
            buildings.Remove(e);
        }

        // ---------------- thumbnails (loaded as they scroll into view) ----------------
        readonly Dictionary<string, Texture2D> thumbs = new Dictionary<string, Texture2D>();
        readonly LinkedList<string> thumbOrder = new LinkedList<string>();
        readonly List<Entry> wanted = new List<Entry>();

        Texture2D Thumb(Entry e)
        {
            Texture2D tex;
            if (thumbs.TryGetValue(e.prefabName, out tex)) return tex;
            if (!wanted.Contains(e)) wanted.Add(e);
            return null;
        }

        void LoadSomeThumbs()
        {
            for (int n = 0; n < ThumbLoadsPerFrame && wanted.Count > 0; n++)
            {
                Entry e = wanted[0];
                wanted.RemoveAt(0);
                Texture2D tex = null;
                try { if (e.meta.imageRef != null) tex = e.meta.imageRef.Instantiate<Texture2D>(); } catch { }
                thumbs[e.prefabName] = tex;   // null is cached too, so missing thumbnails are not retried
                thumbOrder.AddLast(e.prefabName);
                while (thumbOrder.Count > MaxThumbs)
                {
                    string old = thumbOrder.First.Value;
                    thumbOrder.RemoveFirst();
                    if (thumbs[old] != null) Destroy(thumbs[old]);
                    thumbs.Remove(old);
                }
            }
        }

        void OnDestroy()
        {
            foreach (var t in thumbs.Values) if (t != null) Destroy(t);
        }

        // ---------------- UI ----------------
        bool showPanel;
        int tab;                 // 0 = buildings, 1 = props
        string query = "";
        Vector2 scroll;
        string lastStatus = "";
        float statusUntil;
        Rect window = new Rect(60, 90, 860, 620);
        Entry clicked;

        void Status(string s) { lastStatus = s; statusUntil = Time.realtimeSinceStartup + 8f; }

        // On-screen notice for other parts of the mod (profilers).
        public static void Notify(string s)
        {
            GameObject host = GameObject.Find(HostName);
            Controller c = host != null ? host.GetComponent<Controller>() : null;
            if (c != null) c.Status(s);
        }

        void Update()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (ctrl && shift && Input.GetKeyDown(KeyCode.L)) Toggle();
            if (clicked != null) { Entry e = clicked; clicked = null; Pick(e); }   // load outside OnGUI
            if (lodCheck != null && Time.realtimeSinceStartup > lodCheckAt)
            {
                var b = lodCheck as BuildingInfo; var p = lodCheck as PropInfo;
                if (b != null) Log("LOD check " + b.name + ": combined material " + (b.m_lodMaterialCombined != null) + ", combined mesh " + (b.m_lodMeshCombined1 != null));
                if (p != null) Log("LOD check " + p.name + ": combined material " + (p.m_lodMaterialCombined != null) + ", combined mesh " + (p.m_lodMeshCombined1 != null));
                lodCheck = null;
            }
            if (showPanel) LoadSomeThumbs();
            else wanted.Clear();
        }

        void Toggle()
        {
            showPanel = !showPanel;
            if (showPanel) ToolsModifierControl.SetTool<DefaultTool>();   // no placement tool under the panel
        }

        int benchCheckedFrame = -1; bool benchRunning;
        void OnGUI()
        {
            // Stay out of benchmark screenshots and timings (the Bench mod creates this object while it runs).
            if (benchCheckedFrame != Time.frameCount) { benchCheckedFrame = Time.frameCount; benchRunning = GameObject.Find("BenchRunning") != null; }
            if (benchRunning) return;
            if (GUI.Button(new Rect(10, 140, 150, 24), showPanel ? "Close assets" : "Unloaded assets")) Toggle();
            if (Time.realtimeSinceStartup < statusUntil) GUI.Label(new Rect(170, 142, 900, 24), lastStatus);
            if (showPanel) window = GUI.Window(0x4F44, window, DrawWindow, "On-Demand Assets (not loaded at startup)");
        }

        void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            tab = GUILayout.Toolbar(tab, new[] { "Buildings (" + buildings.Count + ")", "Props (" + props.Count + ")" }, GUILayout.Width(360));
            GUILayout.Space(12);
            GUILayout.Label("Search", GUILayout.Width(50));
            string q = GUILayout.TextField(query, GUILayout.Width(300));
            if (q != query) { query = q; scroll = Vector2.zero; }
            GUILayout.EndHorizontal();

            List<Entry> source = tab == 0 ? buildings : props;
            string lq = query.Trim().ToLowerInvariant();
            List<Entry> shown = lq.Length == 0 ? source : source.FindAll(x => x.search.Contains(lq));

            const int cols = 6, tileW = 134, tileH = 150;
            int rows = (shown.Count + cols - 1) / cols;
            Rect view = new Rect(8, 56, window.width - 16, window.height - 64);
            scroll = GUI.BeginScrollView(view, scroll, new Rect(0, 0, cols * tileW, rows * tileH));
            int firstRow = Mathf.Max(0, (int)(scroll.y / tileH));
            int lastRow = Mathf.Min(rows - 1, (int)((scroll.y + view.height) / tileH) + 1);
            for (int r = firstRow; r <= lastRow; r++)
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    if (i >= shown.Count) break;
                    Entry e = shown[i];
                    Rect tile = new Rect(c * tileW, r * tileH, tileW - 6, tileH - 6);
                    if (GUI.Button(tile, GUIContent.none)) clicked = e;
                    Texture2D tex = Thumb(e);
                    Rect img = new Rect(tile.x + 4, tile.y + 4, tile.width - 8, 100);
                    if (tex != null) GUI.DrawTexture(img, tex, ScaleMode.ScaleToFit);
                    else GUI.Label(img, "...");
                    GUI.Label(new Rect(tile.x + 4, tile.y + 106, tile.width - 8, 40), e.display);
                }
            GUI.EndScrollView();
            if (shown.Count == 0) GUI.Label(new Rect(16, 64, 600, 24), source.Count == 0
                ? "Nothing unloaded. Untick 'Load enabled assets' in Loading Screen Mod to use this."
                : "No matches.");
            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }
    }
}
