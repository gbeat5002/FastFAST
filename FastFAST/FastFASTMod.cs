// FastFAST: one mod combining UI Redraw Guard, On-Demand Assets (with texture RAM trimming), and the developer
// tools Mod Profiler and Bench (idle unless their trigger files are used). Each part keeps its own namespace.
using ICities;

namespace FastFAST
{
    public class Mod : IUserMod
    {
        public string Name { get { return "FastFAST"; } }
        public string Description { get { return "Performance for modded Cities: Skylines: fewer UI redraws, less texture RAM, on-demand assets (Ctrl+Shift+L)."; } }

        public void OnEnabled()
        {
            OnDemandAssets.Part.OnEnabled();
            ModProfiler.ProfilerMod.OnEnabled();
        }

        public void OnDisabled() { OnDemandAssets.Part.OnDisabled(); }

        public void OnSettingsUI(UIHelperBase helper)
        {
            UiRedrawGuard.Part.AddOptions(helper);
            OnDemandAssets.Part.AddOptions(helper);
        }
    }
}
