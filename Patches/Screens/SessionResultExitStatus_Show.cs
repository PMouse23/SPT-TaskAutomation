using EFT.UI.SessionEnd;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using TaskAutomation.Helpers;

namespace TaskAutomation.Patches.Screens
{
    internal class SessionResultExitStatus_Show : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.FirstMethod(typeof(SessionResultExitStatus), this.IsTargetMethod);
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
            return method.Name == nameof(SessionResultExitStatus.Show)
                && parameters.Length != 1;
        }
    }
}