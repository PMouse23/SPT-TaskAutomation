using HarmonyLib;
using SPT.Reflection.Patching;
using SPT.Reflection.Utils;
using System;
using System.Linq;
using System.Reflection;
using TaskAutomation.Helpers;

namespace TaskAutomation.Patches.Raid
{
    internal class LocalGame_Stop : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            Type baseLocalGameType = PatchConstants.EftTypes.Single(x => x.Name == "LocalGame").BaseType;
            return AccessTools.FirstMethod(baseLocalGameType, m => m.Name == "Stop");
        }

        [PatchPostfix]
        private static void PatchPostfix()
        {
            Globals.InRaid = false;
            if (Globals.Debug)
                LogHelper.LogInfo($"inRaid={Globals.InRaid}");
        }
    }
}