// Stress-test city: "stress.flag" fills the loaded (fresh) city the same way every time, so render costs of a
// built-up city can be measured and A/B tested. Fixed seed, vanilla prefabs only, sorted by name.
//  - Buildings: growable (zoned) buildings on a 48 m grid around the map centre, no roads (simulation is paused so
//    nothing demolishes or abandons them). Their own props and trees come with them, as in a real city.
//  - Trees: random positions over a wider square. Props: random positions inside the building area.
// Skips water. Counts in the flag file override the defaults: "buildings=4000 trees=200000 props=30000".
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ColossalFramework.Math;
using UnityEngine;

namespace ModProfiler
{
    public class StressCity : MonoBehaviour
    {
        void Update()
        {
            if (Time.frameCount % 60 != 0) return;
            string flag = Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "stress.flag");
            if (!File.Exists(flag)) return;
            string text = "";
            try { text = File.ReadAllText(flag); File.Delete(flag); } catch { }
            int buildings = Count(text, "buildings", 4000), trees = Count(text, "trees", 200000), props = Count(text, "props", 30000);
            SimulationManager.instance.SimulationPaused = true;
            SimulationManager.instance.AddAction(() => Generate(buildings, trees, props));   // managers expect the simulation thread
        }

        static int Count(string text, string key, int fallback)
        {
            Match m = Regex.Match(text, key + @"\s*=\s*(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : fallback;
        }

        static void Generate(int buildingCount, int treeCount, int propCount)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = new Randomizer(12345);
            TerrainManager terrain = TerrainManager.instance;

            var growables = new List<BuildingInfo>();
            for (uint i = 0; i < PrefabCollection<BuildingInfo>.LoadedCount(); i++)
            {
                BuildingInfo b = PrefabCollection<BuildingInfo>.GetLoaded(i);
                if (b != null && b.name.IndexOf('.') < 0 && b.m_placementStyle == ItemClass.Placement.Automatic && b.m_cellWidth <= 4 && b.m_cellLength <= 4)
                    growables.Add(b);
            }
            var treeInfos = new List<TreeInfo>();
            for (uint i = 0; i < PrefabCollection<TreeInfo>.LoadedCount(); i++)
            {
                TreeInfo t = PrefabCollection<TreeInfo>.GetLoaded(i);
                if (t != null && t.name.IndexOf('.') < 0) treeInfos.Add(t);
            }
            var propInfos = new List<PropInfo>();
            for (uint i = 0; i < PrefabCollection<PropInfo>.LoadedCount(); i++)
            {
                PropInfo p = PrefabCollection<PropInfo>.GetLoaded(i);
                if (p != null && p.name.IndexOf('.') < 0 && !p.m_isMarker && !p.m_requireHeightMap && !p.m_requireWaterMap) propInfos.Add(p);
            }
            growables.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            treeInfos.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            propInfos.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

            // Buildings: square grid centred on the map, 48 m apart.
            int side = (int)Math.Ceiling(Math.Sqrt(buildingCount));
            float spacing = 48f, half = side * spacing / 2f;
            int madeB = 0;
            for (int i = 0; i < buildingCount && growables.Count > 0; i++)
            {
                var pos = new Vector3(-half + (i % side) * spacing, 0, -half + (i / side) * spacing);
                if (terrain.HasWater(new Vector2(pos.x, pos.z))) continue;
                pos.y = terrain.SampleDetailHeight(pos);
                BuildingInfo info = growables[i % growables.Count];
                ushort id;
                if (BuildingManager.instance.CreateBuilding(out id, ref r, info, pos, 0f, info.m_cellLength, SimulationManager.instance.m_currentBuildIndex))
                { madeB++; SimulationManager.instance.m_currentBuildIndex++; }
            }

            // Trees: random over a square twice the building area's width.
            int madeT = 0;
            float treeHalf = Math.Max(half * 2f, 1500f);
            for (int i = 0; i < treeCount && treeInfos.Count > 0; i++)
            {
                var pos = new Vector3((r.Int32(20000) / 10000f - 1f) * treeHalf, 0, (r.Int32(20000) / 10000f - 1f) * treeHalf);
                if (terrain.HasWater(new Vector2(pos.x, pos.z))) continue;
                pos.y = terrain.SampleDetailHeight(pos);
                uint id;
                if (TreeManager.instance.CreateTree(out id, ref r, treeInfos[i % treeInfos.Count], pos, false)) madeT++;
            }

            // Props: random inside the building area.
            int madeP = 0;
            for (int i = 0; i < propCount && propInfos.Count > 0; i++)
            {
                var pos = new Vector3((r.Int32(20000) / 10000f - 1f) * half, 0, (r.Int32(20000) / 10000f - 1f) * half);
                if (terrain.HasWater(new Vector2(pos.x, pos.z))) continue;
                pos.y = terrain.SampleDetailHeight(pos);
                ushort id;
                if (PropManager.instance.CreateProp(out id, ref r, propInfos[i % propInfos.Count], pos, r.Int32(360) * Mathf.Deg2Rad, false)) madeP++;
            }

            Debug.Log(string.Format("[ModProfiler] stress city: {0} buildings ({1} kinds), {2} trees ({3} kinds), {4} props ({5} kinds), {6:N0} ms",
                madeB, growables.Count, madeT, treeInfos.Count, madeP, propInfos.Count, sw.ElapsedMilliseconds));
        }
    }
}
