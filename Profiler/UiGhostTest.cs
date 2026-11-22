// Ghost-panel test for UI Redraw Guard: "uitest.flag" -> screenshots before, while hidden, and after re-showing the
// bottom info bar (a panel that's always visible in a city). If the hidden shot still shows the bar, hiding a panel
// no longer redraws the UI. Writes uitest_A/B/C.png to the data folder; compare the bottom strip.
using System.Collections;
using System.IO;
using ColossalFramework.UI;
using UnityEngine;

namespace ModProfiler
{
    public class UiGhostTest : MonoBehaviour
    {
        void Update()
        {
            if (Time.frameCount % 60 != 0) return;
            string flag = Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "uitest.flag");
            if (!File.Exists(flag)) return;
            try { File.Delete(flag); } catch { }
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            string dir = ColossalFramework.IO.DataLocation.localApplicationData;
            UIComponent bar = UIView.Find("InfoPanel");
            if (bar == null) { ProfilerMod.Notify("uitest: InfoPanel not found"); yield break; }
            Application.CaptureScreenshot(Path.Combine(dir, "uitest_A.png"));
            yield return new WaitForSeconds(1f);
            bar.Hide();
            yield return new WaitForSeconds(1f);
            Application.CaptureScreenshot(Path.Combine(dir, "uitest_B.png"));
            yield return new WaitForSeconds(1f);
            bar.Show();
            yield return new WaitForSeconds(1f);
            Application.CaptureScreenshot(Path.Combine(dir, "uitest_C.png"));
            yield return new WaitForSeconds(1f);
            ProfilerMod.Notify("uitest: done (InfoPanel visible again: " + bar.isVisible + ")");
        }
    }
}
