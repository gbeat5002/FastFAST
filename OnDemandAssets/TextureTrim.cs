// Frees the RAM copy of custom asset textures after they are on the GPU (Texture2D.Apply(false, true)).
// No visual change: the GPU copy is what gets drawn. Only main materials of custom buildings, props and
// vehicles are touched. LOD materials, trees and networks are left readable because the game reads their
// pixels when it builds its zoomed-out atlases.
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OnDemandAssets
{
    public class TextureTrim : MonoBehaviour
    {
        static readonly string[] Props = { "_MainTex", "_XYSMap", "_ACIMap", "_APRMap", "_XYCAMap" };
        readonly HashSet<int> done = new HashSet<int>();   // texture instance ids already handled this city

        void Start() { StartCoroutine(TrimAll(10f)); }

        public void TrimLater() { StartCoroutine(TrimAll(1f)); }   // after on-demand loads

        IEnumerator TrimAll(float delay)
        {
            yield return new WaitForSeconds(delay);
            var textures = new List<Texture2D>();
            for (uint i = 0; i < PrefabCollection<BuildingInfo>.LoadedCount(); i++)
            {
                BuildingInfo b = PrefabCollection<BuildingInfo>.GetLoaded(i);
                if (b != null) Collect(b.m_material, textures);
            }
            for (uint i = 0; i < PrefabCollection<BuildingInfoSub>.LoadedCount(); i++)
            {
                BuildingInfoSub s = PrefabCollection<BuildingInfoSub>.GetLoaded(i);
                if (s != null) Collect(s.m_material, textures);
            }
            for (uint i = 0; i < PrefabCollection<PropInfo>.LoadedCount(); i++)
            {
                PropInfo p = PrefabCollection<PropInfo>.GetLoaded(i);
                if (p != null) Collect(p.m_material, textures);
            }
            for (uint i = 0; i < PrefabCollection<VehicleInfo>.LoadedCount(); i++)
            {
                VehicleInfo v = PrefabCollection<VehicleInfo>.GetLoaded(i);
                if (v != null) Collect(v.m_material, textures);
            }
            // Never touch textures a LOD material also uses: the game reads those pixels for its atlases.
            var lodTextures = new HashSet<int>();
            for (uint i = 0; i < PrefabCollection<BuildingInfo>.LoadedCount(); i++) { var b = PrefabCollection<BuildingInfo>.GetLoaded(i); if (b != null) Mark(b.m_lodMaterial, lodTextures); }
            for (uint i = 0; i < PrefabCollection<BuildingInfoSub>.LoadedCount(); i++) { var s = PrefabCollection<BuildingInfoSub>.GetLoaded(i); if (s != null) Mark(s.m_lodMaterial, lodTextures); }
            for (uint i = 0; i < PrefabCollection<PropInfo>.LoadedCount(); i++) { var p = PrefabCollection<PropInfo>.GetLoaded(i); if (p != null) Mark(p.m_lodMaterial, lodTextures); }
            for (uint i = 0; i < PrefabCollection<VehicleInfo>.LoadedCount(); i++) { var v = PrefabCollection<VehicleInfo>.GetLoaded(i); if (v != null) Mark(v.m_lodMaterial, lodTextures); }
            textures.RemoveAll(t => t != null && lodTextures.Contains(t.GetInstanceID()));

            var sw = Stopwatch.StartNew();
            int trimmed = 0, skipped = 0;
            double mb = 0;
            foreach (Texture2D t in textures)
            {
                if (t == null) continue;
                try
                {
                    t.GetPixel(0, 0);   // throws if already not readable (e.g. shared and trimmed before)
                }
                catch { skipped++; continue; }
                try
                {
                    t.Apply(false, true);
                    trimmed++;
                    mb += (double)t.width * t.height * Bpp(t.format) * (t.mipmapCount > 1 ? 1.333 : 1.0) / 1048576.0;
                }
                catch { skipped++; }
                if (trimmed % 100 == 0) yield return null;   // spread GPU re-uploads over frames
            }
            Debug.Log(string.Format("[OnDemandAssets] TextureTrim: freed RAM copy of {0} textures (~{1:N0} MB), skipped {2}, {3} ms",
                trimmed, mb, skipped, sw.ElapsedMilliseconds));
        }

        static void Mark(Material m, HashSet<int> ids)
        {
            if (m == null) return;
            foreach (string p in Props)
                if (m.HasProperty(p)) { Texture t = m.GetTexture(p); if (t != null) ids.Add(t.GetInstanceID()); }
        }

        void Collect(Material m, List<Texture2D> into)
        {
            if (m == null) return;
            foreach (string p in Props)
            {
                if (!m.HasProperty(p)) continue;
                var t = m.GetTexture(p) as Texture2D;
                if (t != null && done.Add(t.GetInstanceID())) into.Add(t);
            }
        }

        static float Bpp(TextureFormat f)
        {
            switch (f)
            {
                case TextureFormat.DXT1: return 0.5f;
                case TextureFormat.DXT5: case TextureFormat.Alpha8: return 1f;
                case TextureFormat.RGB24: return 3f;
                default: return 4f;
            }
        }
    }
}
