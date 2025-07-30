using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaskAutomation.Helpers;
using TaskAutomation.MonoBehaviours;
using static RawQuestClass;

namespace TaskAutomation.Patches.Screens
{
    internal class InventoryScreen_Show : ModulePatch
    {
        private static Type conditionChecker;
        private static Type itemsProvider;
        private static MethodInfo itemsProviderMethod;

        protected override MethodBase GetTargetMethod()
        {
            conditionChecker = AccessTools.GetTypesFromAssembly(typeof(AbstractGame).Assembly)
                    .SingleOrDefault(t => t.GetEvent("OnConditionQuestTimeExpired", BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance) != null);
            conditionChecker = conditionChecker.MakeGenericType(typeof(QuestClass));

            itemsProvider = AccessTools.GetTypesFromAssembly(typeof(AbstractGame).Assembly)
                    .SingleOrDefault(t => t.GetMethod("GetItemsForCondition", BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance) != null);
            itemsProvider = itemsProvider.MakeGenericType(typeof(QuestClass));
            LogHelper.LogInfo($"{itemsProvider}");
            itemsProviderMethod = itemsProvider.GetMethod("GetItemsForCondition", BindingFlags.Public | BindingFlags.Static);
            LogHelper.LogInfo($"{itemsProviderMethod}");

            return AccessTools.FirstMethod(typeof(InventoryScreen), this.IsTargetMethod);
        }

        [PatchPostfix]
        private static void PatchPostfix(InventoryScreen __instance, object questController)
        {
            if (Globals.InRaid
                || questController is not AbstractQuestControllerClass abstractQuestController)
                return;
            if (Globals.Debug)
                LogHelper.LogInfo($"Found abstractQuestController.");
            Singleton<UpdateMonoBehaviour>.Instance.SetReflection(conditionChecker, itemsProviderMethod);
            Singleton<UpdateMonoBehaviour>.Instance.SetAbstractQuestController(abstractQuestController);
        }

        private bool IsTargetMethod(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            return method.Name == nameof(InventoryScreen.Show)
                && parameters.Length != 1;
        }
    }
}