using CitiesHarmony.API;
using ICities;
using UnityEngine;

namespace ModProfiler
{
    public class ProfilerMod : IUserMod
    {
        public string Name { get { return "Mod Profiler"; } }
        public string Description { get { return "Ctrl+Shift+F (or profile.flag): times every mod's per-frame work for 20 s into profile.txt."; } }

        public void OnEnabled()
        {
            HarmonyHelper.DoOnHarmonyReady(LoadProfiler.Install);
            if (GameObject.Find("ModProfilerMenu") == null)
            {
                var go = new GameObject("ModProfilerMenu");
                Object.DontDestroyOnLoad(go);
                go.AddComponent<NewGameTrigger>();
            }
        }

        public static void Notify(string msg) { Debug.Log("[ModProfiler] " + msg); }
    }

    // Test automation: "newgame.flag" in the data folder starts a new game on the map the New Game panel selects
    // by default (Green Peaks here), exactly as clicking New Game then Start. Only acts on the main menu.
    public class NewGameTrigger : MonoBehaviour
    {
        void Update()
        {
            if (Time.frameCount % 60 != 0) return;
            string flag = System.IO.Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "newgame.flag");
            if (!System.IO.File.Exists(flag)) return;
            // The main menu is loaded additively (the active scene stays "Startup"), so check that it's loaded, not active.
            if (!UnityEngine.SceneManagement.SceneManager.GetSceneByName("MainMenu").isLoaded
                || LoadingManager.instance.m_currentlyLoading || LoadingManager.instance.m_loadingComplete) return;
            if (!starting) StartCoroutine(Start2(flag));
        }

        bool starting;

        // The flag is removed only once the game is actually started, so a menu that isn't ready yet gets retried.
        System.Collections.IEnumerator Start2(string flag)
        {
            starting = true;
            var panel = ColossalFramework.UI.UIView.library.Get<NewGamePanel>("NewGamePanel");
            if (panel == null || !panel.component.isVisible)   // don't stack a second modal on a retry
                panel = ColossalFramework.UI.UIView.library.ShowModal<NewGamePanel>("NewGamePanel");
            for (int i = 0; i < 120 && (panel == null || !panel.IsValid()); i++) yield return null;   // map list fills in over a few frames
            starting = false;
            if (panel == null || !panel.IsValid()) { ProfilerMod.Notify("newgame.flag: New Game panel not ready, retrying"); yield break; }
            try { System.IO.File.Delete(flag); } catch { }
            ProfilerMod.Notify("newgame.flag: starting a new game");
            typeof(NewGamePanel).GetMethod("OnStartNewGame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .Invoke(panel, null);
        }
    }

    public class Loading : LoadingExtensionBase
    {
        const string HostName = "ModProfiler";

        public override void OnLevelLoaded(LoadMode mode)
        {
            if (!HarmonyHelper.IsHarmonyInstalled || GameObject.Find(HostName) != null) return;
            var host = new GameObject(HostName);
            var fp = host.AddComponent<FrameProfiler>();
            host.AddComponent<UiProfiler>();
            host.AddComponent<MemSnapshot>();
            host.AddComponent<Census>();
            fp.StartCoroutine(LoadProfiler.Finish());
        }

        public override void OnLevelUnloading()
        {
            var host = GameObject.Find(HostName);
            if (host != null) Object.Destroy(host);
        }
    }
}
