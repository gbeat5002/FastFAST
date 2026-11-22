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

    // Test automation: "newgame.flag" in the data folder starts a new city on the stock map named in the flag
    // (default "Green Peaks") straight through LoadingManager, the call the New Game panel ends in. No UI involved,
    // so it works when mouse clicks don't reach the game. Only acts on the main menu.
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
            string mapName = "Green Peaks";
            try { string s = System.IO.File.ReadAllText(flag).Trim(); if (s.Length > 1) mapName = s; System.IO.File.Delete(flag); } catch { }
            try { StartCity(mapName); }
            catch (System.Exception e) { ProfilerMod.Notify("newgame.flag: failed: " + e); }
        }

        static void StartCity(string mapName)
        {
            foreach (ColossalFramework.Packaging.Package.Asset asset in ColossalFramework.Packaging.PackageManager.FilterAssets(new[] { UserAssetType.MapMetaData }))
            {
                if (asset == null || !asset.isEnabled) continue;
                MapMetaData meta = asset.Instantiate<MapMetaData>();
                if (meta == null || meta.mapName != mapName || meta.assetRef == null) continue;
                var ngs = new SimulationMetaData
                {
                    m_CityName = "Bench",
                    m_gameInstanceIdentifier = System.Guid.NewGuid().ToString(),
                    m_invertTraffic = SimulationMetaData.MetaBool.False,
                    m_disableAchievements = SimulationMetaData.MetaBool.True,
                    m_startingDateTime = System.DateTime.Now,
                    m_currentDateTime = System.DateTime.Now,
                    m_newGameAppVersion = ColossalFramework.IO.DataLocation.productVersion,
                    m_updateMode = SimulationManager.UpdateMode.NewGameFromMap,
                    m_environment = meta.environment,
                    m_MapName = meta.mapName
                };
                ProfilerMod.Notify("newgame.flag: starting a new city on " + mapName);
                LoadingManager.instance.LoadLevel(meta.assetRef, "Game", "InGame", ngs, false);
                return;
            }
            ProfilerMod.Notify("newgame.flag: map not found: " + mapName);
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
            host.AddComponent<UiGhostTest>();
            fp.StartCoroutine(LoadProfiler.Finish());
        }

        public override void OnLevelUnloading()
        {
            var host = GameObject.Find(HostName);
            if (host != null) Object.Destroy(host);
        }
    }
}
