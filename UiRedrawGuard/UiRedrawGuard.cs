// UI Redraw Guard: stops UI code that re-applies unchanged state every frame from forcing a full UI redraw.
// Any UIComponent.Invalidate() makes the whole UIView re-render (28k components here), so one needless
// invalidation per frame costs a full redraw per frame. Found by Mod Profiler's UI capture (docs/profile.md):
//  1. Crossroads re-styles its toolbar button 5x per frame with the same values (531 of 535 re-renders).
//  2. The game's hotel-chain panel refreshes its profit label every frame while hidden (566 of 590 once 1 was fixed).
// Each guard has an options checkbox; settings live in UiRedrawGuard.txt ("crossroads=on/off", "hotel=on/off"),
// re-read every 2 s so a benchmark can A/B them in one session.
using System;
using System.Collections.Generic;
using System.IO;
using CitiesHarmony.API;
using ColossalFramework.UI;
using HarmonyLib;
using ICities;
using UnityEngine;

namespace UiRedrawGuard
{
    public class Mod : IUserMod
    {
        public string Name { get { return "UI Redraw Guard"; } }
        public string Description { get { return "Stops needless full-UI redraws every frame (Crossroads button, hidden hotel panel)."; } }

        public void OnSettingsUI(UIHelperBase helper)
        {
            Settings.Reload();
            var g = helper.AddGroup("UI Redraw Guard");
            g.AddCheckbox("Skip unchanged Crossroads button restyles", Settings.Crossroads, on => { Settings.Crossroads = on; Settings.Save(); });
            g.AddCheckbox("Don't refresh the hotel-chain panel while it is hidden", Settings.Hotel, on => { Settings.Hotel = on; Settings.Save(); });
        }
    }

    public static class Settings
    {
        public static bool Crossroads = true, Hotel = true;
        static float nextCheck;
        static string FilePath { get { return Path.Combine(ColossalFramework.IO.DataLocation.localApplicationData, "UiRedrawGuard.txt"); } }

        // Called from the patched methods (main thread); cheap except once every 2 s.
        public static void Poll()
        {
            if (Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + 2f;
            Reload();
        }

        public static void Reload()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                string s = File.ReadAllText(FilePath);
                bool c = !s.Contains("crossroads=off"), h = !s.Contains("hotel=off");
                if (c != Crossroads) Guard.Forget();
                Crossroads = c; Hotel = h;
            }
            catch { }
        }

        public static void Save()
        {
            Guard.Forget();
            try { File.WriteAllText(FilePath, "crossroads=" + (Crossroads ? "on" : "off") + "\nhotel=" + (Hotel ? "on" : "off") + "\n"); } catch { }
        }
    }

    public class Loading : LoadingExtensionBase
    {
        static bool patched;

        // OnCreated runs after every mod assembly is loaded, so Crossroads' type can be found.
        public override void OnCreated(ILoading loading)
        {
            if (patched || !HarmonyHelper.IsHarmonyInstalled) return;
            patched = true;   // patch once per session; never unpatched (unsafe mid-session)
            Settings.Reload();
            var h = new Harmony("uiredrawguard");

            Type style = AccessTools.TypeByName("Crossroads.Tool.ToggleButtonStyle");
            var apply = style == null ? null : AccessTools.Method(style, "Apply", null, null);   // C# 3: no optional arguments
            if (apply != null) { h.Patch(apply, new HarmonyMethod(typeof(Guard).GetMethod("ApplyPrefix")), null, null, null); Debug.Log("[UiRedrawGuard] Guarding " + style.FullName + ".Apply"); }
            else Debug.Log("[UiRedrawGuard] Crossroads not found; its guard is idle");

            // FPS Booster renames MonoBehaviour.Update to FpsBoosterUpdate at runtime; patch whichever exists.
            var hotel = AccessTools.Method(typeof(HotelChainPanel), "FpsBoosterUpdate", null, null) ?? AccessTools.Method(typeof(HotelChainPanel), "Update", null, null);
            if (hotel != null) { h.Patch(hotel, new HarmonyMethod(typeof(Guard).GetMethod("HotelPrefix")), null, null, null); Debug.Log("[UiRedrawGuard] Guarding HotelChainPanel." + hotel.Name); }
        }
    }

    public static class Guard
    {
        // Last arguments applied per button. Main thread only (UI code).
        static readonly Dictionary<UIButton, string> last = new Dictionary<UIButton, string>();

        public static void Forget() { last.Clear(); }

        public static bool ApplyPrefix(UIButton button, bool active, string tooltipWhenActive, string tooltipWhenInactive,
            string captionWhenActive, string captionWhenInactive)
        {
            Settings.Poll();
            if (!Settings.Crossroads || button == null) return true;
            string key = (active ? "1" : "0") + "\u0001" + tooltipWhenActive + "\u0001" + tooltipWhenInactive + "\u0001" + captionWhenActive + "\u0001" + captionWhenInactive;
            string prev;
            if (last.TryGetValue(button, out prev) && prev == key) return false;   // unchanged: skip, nothing to redraw
            if (last.Count > 64) last.Clear();   // buttons recreated on reload: drop stale ones
            last[button] = key;
            return true;
        }

        // Hidden panel: nothing on screen to refresh. It refreshes on becoming visible (OnBecameVisible) and next frame.
        public static bool HotelPrefix(HotelChainPanel __instance)
        {
            Settings.Poll();
            return !Settings.Hotel || __instance.isVisible;
        }
    }
}
