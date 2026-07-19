using System.Reflection;
using HarmonyLib;
using MelonLoader;

[assembly: MelonInfo(typeof(Main.Core), "InfoGui", "1.0.0", "TheThinker")]
[assembly: MelonGame("Alta", "A Township Tale")]

namespace Main
{
    public class Core : MelonMod
    {
        private readonly InfoGuiMod _infoGui = new InfoGuiMod();

        public override void OnInitializeMelon()
        {
            _infoGui.Init();
            HarmonyInstance.PatchAll();
            
            // Patch OVRTouchpad to stop Unity active input system log spam
            PatchLogSpam();
        }

        public override void OnUpdate()
        {
            _infoGui.OnUpdate();
        }

        public override void OnGUI()
        {
            _infoGui.OnGUI();
        }

        private static bool PreventUpdate()
        {
            return false; // Skip execution of the legacy mouse input code
        }

        private void PatchLogSpam()
        {
            try
            {
                var helperType = ReflectionUtil.FindType("OVRTouchpadHelper");
                if (helperType != null)
                {
                    var updateMethod = helperType.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (updateMethod != null)
                    {
                        HarmonyInstance.Patch(updateMethod, new HarmonyMethod(typeof(Core).GetMethod(nameof(PreventUpdate), BindingFlags.Static | BindingFlags.NonPublic)));
                        MelonLogger.Msg("[InfoGui] Patched OVRTouchpadHelper.Update to prevent log spam.");
                    }
                }

                var padType = ReflectionUtil.FindType("OVRTouchpad");
                if (padType != null)
                {
                    var updateMethod = padType.GetMethod("Update", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (updateMethod != null)
                    {
                        HarmonyInstance.Patch(updateMethod, new HarmonyMethod(typeof(Core).GetMethod(nameof(PreventUpdate), BindingFlags.Static | BindingFlags.NonPublic)));
                        MelonLogger.Msg("[InfoGui] Patched OVRTouchpad.Update to prevent log spam.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error("[InfoGui] Failed to patch OVRTouchpad: " + ex.Message);
            }
        }
    }
}
