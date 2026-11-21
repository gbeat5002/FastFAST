// Temporary diagnostic: where does texture memory go, and who owns it?
// Writes memreport.txt to the game's data folder 60 s after a city loads.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace OnDemandAssets
{
    public class MemReport : MonoBehaviour
    {
        float due;
        void Start() { due = Time.realtimeSinceStartup + 60f; }

        void Update()
        {
            if (due > 0 && Time.realtimeSinceStartup > due)
            {
                due = -1;
                try { Write(); } catch (Exception e) { Debug.Log("[OnDemandAssets] MemReport failed: " + e); }
            }
        }

        static float Bpp(TextureFormat f)
        {
            switch (f)
            {
                case TextureFormat.DXT1: case TextureFormat.BC4: return 0.5f;
                case TextureFormat.DXT5: case TextureFormat.BC5: case TextureFormat.BC7: case TextureFormat.BC6H:
                case TextureFormat.Alpha8: case TextureFormat.R8: return 1f;
                case TextureFormat.RGB24: return 3f;
                case TextureFormat.RGBAHalf: return 8f;
                case TextureFormat.RGBAFloat: return 16f;
                case TextureFormat.RHalf: case TextureFormat.R16: case TextureFormat.RGB565: case TextureFormat.ARGB4444: case TextureFormat.RGBA4444: return 2f;
                case TextureFormat.RGHalf: case TextureFormat.RFloat: return 4f;
                default: return 4f;
            }
        }

        static readonly string[] Props = { "_MainTex", "_XYSMap", "_ACIMap", "_APRMap", "_XYCAMap", "_BumpMap", "_NormalMap",
            "_Cube", "_Tex", "_LUT", "_NoiseTex", "_DetailTex", "_SpecTex", "_SkyTex", "_CloudTex", "_EmissionMap" };

        static bool Readable(Texture2D t)
        {
            try { t.GetPixel(0, 0); return true; } catch { return false; }
        }

        void Write()
        {
            // Who uses each texture: material name + shader.
            var owners = new Dictionary<Texture, string>();
            foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
                foreach (string p in Props)
                {
                    if (!m.HasProperty(p)) continue;
                    Texture t = m.GetTexture(p);
                    if (t != null && !owners.ContainsKey(t)) owners[t] = m.name + " [" + (m.shader != null ? m.shader.name : "?") + "] " + p;
                }

            var tex = Resources.FindObjectsOfTypeAll<Texture2D>()
                .Select(t => new { t, mb = (double)t.width * t.height * Bpp(t.format) * (t.mipmapCount > 1 ? 1.333 : 1.0) / 1048576.0 })
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("Texture report " + DateTime.Now + "  total " + tex.Count + " textures, " + tex.Sum(x => x.mb).ToString("N0") + " MB (GPU size estimate)");
            sb.AppendLine(string.Format("Managed heap {0:N0} MB, Unity allocated {1:N0} MB",
                GC.GetTotalMemory(false) / 1048576.0, UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576.0));
            sb.AppendLine();

            sb.AppendLine("== By format ==");
            foreach (var g in tex.GroupBy(x => x.t.format).OrderByDescending(g => g.Sum(x => x.mb)))
                sb.AppendLine(string.Format("{0,-12} {1,8:N0} MB  {2,6} textures", g.Key, g.Sum(x => x.mb), g.Count()));
            sb.AppendLine();

            sb.AppendLine("== By size (largest side) ==");
            foreach (var g in tex.GroupBy(x => Mathf.Max(x.t.width, x.t.height)).OrderByDescending(g => g.Key).Take(10))
                sb.AppendLine(string.Format("{0,6} px  {1,8:N0} MB  {2,6} textures", g.Key, g.Sum(x => x.mb), g.Count()));
            sb.AppendLine();

            // Readable = Unity keeps a CPU (RAM) copy in addition to the GPU copy. Sample to keep this cheap.
            var sample = tex.Where((x, i) => i % 20 == 0).ToList();
            int readable = sample.Count(x => Readable(x.t));
            double readableMb = sample.Where(x => Readable(x.t)).Sum(x => x.mb) * 20;
            sb.AppendLine(string.Format("Readable (sampled 1 in 20): {0}/{1} textures, ~{2:N0} MB extrapolated has a RAM copy", readable, sample.Count, readableMb));
            sb.AppendLine();

            sb.AppendLine("== Top 40 textures with owner ==");
            foreach (var x in tex.OrderByDescending(x => x.mb).Take(40))
            {
                string owner; owners.TryGetValue(x.t, out owner);
                sb.AppendLine(string.Format("{0,7:N1} MB  {1}x{2} {3} mips={4} readable={5}  '{6}'  used by: {7}",
                    x.mb, x.t.width, x.t.height, x.t.format, x.t.mipmapCount, Readable(x.t), x.t.name, owner ?? "(no material found)"));
            }
            sb.AppendLine();

            // Big UI images: which panel shows them (hierarchy path names the owning mod).
            sb.AppendLine("== UI atlases with a 2048+ texture ==");
            foreach (var a in Resources.FindObjectsOfTypeAll<ColossalFramework.UI.UITextureAtlas>())
            {
                Texture t = a.material != null ? a.material.mainTexture : null;
                if (t == null || Mathf.Max(t.width, t.height) < 2048) continue;
                sb.AppendLine(string.Format("{0}x{1}  atlas '{2}'  sprites={3}", t.width, t.height, a.name, a.count));
            }
            sb.AppendLine();

            sb.AppendLine("== UI texture sprites with a 2048+ texture ==");
            foreach (var s in Resources.FindObjectsOfTypeAll<ColossalFramework.UI.UITextureSprite>())
            {
                Texture t = s.texture;
                if (t == null || Mathf.Max(t.width, t.height) < 2048) continue;
                string where = s.name; Transform tr = s.transform.parent;
                for (int d = 0; tr != null && d < 6; d++, tr = tr.parent) where = tr.name + "/" + where;
                sb.AppendLine(string.Format("{0}x{1}  {2}", t.width, t.height, where));
            }
            sb.AppendLine();

            sb.AppendLine("== Texture MB by owning shader ==");
            foreach (var g in tex.GroupBy(x => { string o; return owners.TryGetValue(x.t, out o) ? o.Substring(o.IndexOf('[')) .Split(']')[0] + "]" : "(no material)"; })
                                 .OrderByDescending(g => g.Sum(x => x.mb)).Take(20))
                sb.AppendLine(string.Format("{0,8:N0} MB  {1,6} textures  {2}", g.Sum(x => x.mb), g.Count(), g.Key));

            string path = Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "memreport.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[OnDemandAssets] Texture report written: " + path);
        }
    }
}
