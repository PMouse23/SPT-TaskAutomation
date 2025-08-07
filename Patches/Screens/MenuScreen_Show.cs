using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using TaskAutomation.Helpers;

namespace TaskAutomation.Patches.Screens
{
    internal class MenuScreen_Show : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.FirstMethod(typeof(MenuScreen), this.IsTargetMethod);
        }

        [PatchPostfix]
        private static void PatchPostfix()
        {
            Globals.InRaid = false;
            if (Globals.Debug)
                LogHelper.LogInfo($"inRaid={Globals.InRaid}");
        }

        private bool IsTargetMethod(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return method.Name == nameof(MenuScreen.Show)
                && parameters.Length != 1;
        }
    }
}