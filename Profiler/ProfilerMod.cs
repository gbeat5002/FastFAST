using CitiesHarmony.API;
using ICities;
using UnityEngine;

namespace ModProfiler
{
    public class ProfilerMod : IUserMod
    {
        public string Name { get { return "Mod Profiler"; } }
        public string Description { get { return "Ctrl+Shift+F (or profile.flag): times every mod's per-frame work for 20 s into profile.txt."; } }

        public void OnEnabled() { HarmonyHelper.DoOnHarmonyReady(LoadProfiler.Install); }

        public static void Notify(string msg) { Debug.Log("[ModProfiler] " + msg); }
    }

    public class Loading : LoadingExtensionBase
    {
        const string HostName = "ModProfiler";

        public override void OnLevelLoaded(LoadMode mode)
        {
            if (!HarmonyHelper.IsHarmonyInstalled || GameObject.Find(HostName) != null) return;
            var host = new GameObject(HostName);
            var fp = host.AddComponent<FrameProfiler>();
            fp.StartCoroutine(LoadProfiler.Finish());
        }

        public override void OnLevelUnloading()
        {
            var host = GameObject.Find(HostName);
            if (host != null) Object.Destroy(host);
        }
    }
}
