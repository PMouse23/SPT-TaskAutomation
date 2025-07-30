using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace TaskAutomation.Patches.Raid
{
    internal class LocalGame_Start : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.FirstMethod(typeof(GameWorld), m => m.Name == nameof(GameWorld.OnGameStarted));
        }

        [PatchPostfix]
        private static void PatchPostFix()
        {
            Globals.InRaid = true;
        }
    }
}