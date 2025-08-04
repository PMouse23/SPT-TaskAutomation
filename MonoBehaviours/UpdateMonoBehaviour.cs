using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using HarmonyLib;
using SPT.Common.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaskAutomation.Helpers;
using UnityEngine;
using static EFT.Profile;

#nullable enable

namespace TaskAutomation.MonoBehaviours
{
    internal class UpdateMonoBehaviour : MonoBehaviour
    {
        private AbstractQuestControllerClass? abstractQuestController;
        private Type? conditionChecker;
        private MethodInfo? itemsProviderMethod;

        public void SetAbstractQuestController(AbstractQuestControllerClass abstractQuestController)
        {
            if (this.abstractQuestController == null)
                this.StartCoroutine(this.coroutine());
            this.abstractQuestController = abstractQuestController;
        }

        public void SetReflection(Type conditionChecker, MethodInfo itemsProviderMethod)
        {
            this.conditionChecker = conditionChecker;
            this.itemsProviderMethod = itemsProviderMethod;
        }

        public void UnsetAbstractQuestController()
        {
            this.abstractQuestController = null;
            this.StopAllCoroutines();
        }

        private void completeCondition(AbstractQuestControllerClass abstractQuestController, QuestClass quest, Condition condition)
        {
            MongoID id = condition.id;
            if (quest.IsConditionDone(condition))
                return;
            quest.ProgressCheckers[condition].SetCurrentValueGetter(_ => condition.value);
            FieldInfo conditionControllerFieldInfo = abstractQuestController.GetType().GetFields().FirstOrDefault(fi => fi.FieldType == this.conditionChecker);
            if (conditionControllerFieldInfo == null)
                return;
            var conditionController = conditionControllerFieldInfo.GetValue(abstractQuestController);
            MethodInfo setConditionCurrentValueMethodInfo = AccessTools.DeclaredMethod(conditionController.GetType().BaseType, "SetConditionCurrentValue");
            if (setConditionCurrentValueMethodInfo == null)
                return;
            setConditionCurrentValueMethodInfo.Invoke(conditionController, new object[] { quest, EQuestStatus.AvailableForFinish, condition, condition.value, true });
        }

        private IEnumerator coroutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(0.5f);
                if (this.abstractQuestController == null)
                    continue;
                if (Globals.InRaid)
                    continue;

                var quests = this.abstractQuestController.Quests;
                foreach (QuestClass quest in quests.Where(this.isStarted))
                {
                    if (Globals.Debug)
                        LogHelper.LogInfo($"Started {quest.rawQuestClass.Name}");
                    try
                    {
                        this.handleQuest(abstractQuestController, quest);
                    }
                    catch (Exception exception)
                    {
                        LogHelper.LogExceptionToConsole(exception);
                    }
                    yield return new WaitForSeconds(0.5f);
                    quests = this.abstractQuestController.Quests;
                }
                //FinishQuests
                if (Globals.AutoCompleteQuests)
                {
                    List<string> questsReadyToFinish = this.getIdsReadyToComplete();
                    foreach (string id in questsReadyToFinish)
                    {
                        try
                        {
                            QuestClass? questToComplete = this.getQuestById(id);
                            if (questToComplete == null)
                                continue;
                            if (this.abstractQuestController.IsQuestForCurrentProfile(questToComplete) == false)
                                continue;
                            if (Globals.Debug)
                                LogHelper.LogInfo($"AvailableForFinish {questToComplete.rawQuestClass.Name}");
                            this.abstractQuestController.FinishQuest(questToComplete, true);
                            LogHelper.LogInfoWithNotification($"Completed: {questToComplete.rawQuestClass.Name}");
                        }
                        catch (Exception exception)
                        {
                            LogHelper.LogExceptionToConsole(exception);
                        }
                        yield return new WaitForSeconds(0.5f);
                    }
                    quests = this.abstractQuestController.Quests;
                }
                //StartQuests
                quests = this.abstractQuestController.Quests;
                if (Globals.AutoAcceptQuests)
                {
                    List<string> questsReadyToStart = this.getIdsReadyToStart();
                    foreach (string id in questsReadyToStart)
                    {
                        try
                        {
                            QuestClass? questToStart = this.getQuestById(id);
                            if (questToStart == null)
                                continue;
                            if (this.abstractQuestController.IsQuestForCurrentProfile(questToStart) == false)
                                continue;
                            if (Globals.Debug)
                                LogHelper.LogInfo($"AvailableForStart {questToStart.rawQuestClass.Name}, json={Json.Serialize<RawQuestClass>(questToStart.rawQuestClass)}");
                            this.abstractQuestController.AcceptQuest(questToStart, true);
                            LogHelper.LogInfoWithNotification($"Accepted: {questToStart.rawQuestClass.Name}");
                        }
                        catch (Exception exception)
                        {
                            LogHelper.LogExceptionToConsole(exception);
                        }
                        yield return new WaitForSeconds(0.5f);
                    }
                    quests = this.abstractQuestController.Quests;
                }
                //FailedQuests
                QuestClass failedQuest = quests.FirstOrDefault(this.isMarkedAsFailed);
                if (failedQuest != null)
                {
                    try
                    {
                        if (this.abstractQuestController.IsQuestForCurrentProfile(failedQuest) == false)
                            continue;
                        if (Globals.Debug)
                            LogHelper.LogInfo($"FailConditional {failedQuest.rawQuestClass.Name}");
                        this.abstractQuestController.FailConditional(failedQuest);
                    }
                    catch (Exception exception)
                    {
                        LogHelper.LogExceptionToConsole(exception);
                    }
                    yield return new WaitForSeconds(0.5f);
                }
            }
        }

        private List<string> getIdsReadyToComplete()
        {
            if (this.abstractQuestController == null)
                return [];
            var quests = this.abstractQuestController.Quests;
            IEnumerable<QuestClass> questsReadyToFinish = quests.Where(this.isReadyToFinish);
            return questsReadyToFinish.Select(quest => quest.Id).ToList();
        }

        private List<string> getIdsReadyToRestart()
        {
            if (this.abstractQuestController == null)
                return [];
            var quests = this.abstractQuestController.Quests;
            IEnumerable<QuestClass> questsReadyToFinish = quests.Where(this.isMarkedAsFailRestartable);
            return questsReadyToFinish.Select(quest => quest.Id).ToList();
        }

        private List<string> getIdsReadyToStart()
        {
            if (this.abstractQuestController == null)
                return [];
            var quests = this.abstractQuestController.Quests;
            IEnumerable<QuestClass> questsReadyToStart = quests.Where(this.isReadyToStart);
            return questsReadyToStart.Select(quest => quest.Id).ToList();
        }

        private Item[] getItemsAllowedToHandover(double handoverValue, Item[] result)
        {
            return result.Where(this.isAllowToHandover).Take((int)handoverValue).ToArray();
        }

        private QuestClass? getQuestById(string id)
        {
            if (this.abstractQuestController == null)
                return null;
            var quests = this.abstractQuestController.Quests;
            return quests.FirstOrDefault(quest => quest.Id == id);
        }

        /// <summary>
        /// Handle quest for automation
        /// </summary>
        /// <returns>True if the sequence should run again.</returns>
        private bool handleQuest(AbstractQuestControllerClass abstractQuestController, QuestClass quest)
        {
            if (quest.QuestStatus != EQuestStatus.Started)
                return false;

            foreach (Condition condition in quest.NecessaryConditions)
            {
                if (Globals.Debug)
                    LogHelper.LogInfo($" - {condition.GetType()} {condition.FormattedDescription} IsNecessary:{condition.IsNecessary} Done:{quest.IsConditionDone(condition)}");
                if (quest.IsConditionDone(condition))
                    continue;

                if (condition is ConditionHandoverItem conditionHandoverItem)
                {
                    if (Globals.SkipFindAndObtain && conditionHandoverItem.onlyFoundInRaid == false)
                    {
                        this.completeCondition(abstractQuestController, quest, condition);
                    }
                    else if (Globals.SkipFindInRaid && conditionHandoverItem.onlyFoundInRaid)
                    {
                        this.completeCondition(abstractQuestController, quest, condition);
                    }
                    else if ((Globals.AutoHandoverFindInRaid && conditionHandoverItem.onlyFoundInRaid == true)
                          || (Globals.AutoHandoverObtain && conditionHandoverItem.onlyFoundInRaid == false))
                    {
                        ConditionProgressChecker conditionProgressChecker = quest.ProgressCheckers[condition];
                        double handoverValue = 0;
                        double currentValue = conditionProgressChecker.CurrentValue;
                        double expectedValue = condition.value;
                        if (currentValue < expectedValue)
                            handoverValue = expectedValue - currentValue;
                        Item[]? result = this.itemsProviderMethod?.Invoke(null, new object[] { abstractQuestController.Profile.Inventory, condition }) as Item[];
                        if (result == null || result.Length == 0)
                            continue;
                        result = this.getItemsAllowedToHandover(handoverValue, result);
                        if (result.Length == 0)
                            continue;
                        if (Globals.Debug)
                            LogHelper.LogInfo($"{quest.rawQuestClass.Name} HandoverItem(s): currentValue={currentValue}, expectedValue={expectedValue}, handoverValue={result.Length} done={quest.IsConditionDone(condition)} test={conditionProgressChecker.Test()}");
                        abstractQuestController.HandoverItem(quest, conditionHandoverItem, result, true);
                        LogHelper.LogInfoWithNotification($"HandoverItem(s): {quest.rawQuestClass.Name}");
                        return true;
                    }
                }
                else if (condition is ConditionWeaponAssembly conditionWeaponAssembly)
                {
                    if (Globals.SkipWeaponAssembly)
                        this.completeCondition(abstractQuestController, quest, condition);
                }
                else if (condition is ConditionFindItem conditionFindItem)
                {
                    if (Globals.SkipFindAndObtain && conditionFindItem.onlyFoundInRaid == false)
                        this.completeCondition(abstractQuestController, quest, condition);
                    if (Globals.SkipFindInRaid && conditionFindItem.onlyFoundInRaid)
                        this.completeCondition(abstractQuestController, quest, condition);
                }
                else if (condition is ConditionCounterCreator conditionCounterCreator)
                {
                    if (Globals.SkipElimination && conditionCounterCreator.type == RawQuestClass.EQuestType.Elimination)
                        this.completeCondition(abstractQuestController, quest, condition);
                    else if (Globals.SkipVisitPlace && conditionCounterCreator.type == RawQuestClass.EQuestType.Exploration)
                        this.completeCondition(abstractQuestController, quest, condition);
                    else if (Globals.SkipVisitPlace && conditionCounterCreator.type == RawQuestClass.EQuestType.Discover)
                        this.completeCondition(abstractQuestController, quest, condition);
                    else if (Globals.SkipSkill && conditionCounterCreator.type == RawQuestClass.EQuestType.Experience)
                        this.completeCondition(abstractQuestController, quest, condition);
                    else if (Globals.SkipSurviveAndExtract && conditionCounterCreator.type == RawQuestClass.EQuestType.Completion)
                        this.completeCondition(abstractQuestController, quest, condition);
                    else if (Globals.Debug)
                        LogHelper.LogInfo($"ConditionCounterCreator: {conditionCounterCreator.type} not handled.");
                }
                else if (condition is ConditionVisitPlace conditionVisitPlace)
                {
                    if (Globals.SkipVisitPlace)
                        this.completeCondition(abstractQuestController, quest, condition);
                }
                else if (condition is ConditionLeaveItemAtLocation conditionLeaveItemAtLocation)
                {
                    if (Globals.SkipLeaveItemAtLocation)
                        this.completeCondition(abstractQuestController, quest, condition);
                }
                else if (condition is ConditionTraderLoyalty conditionTraderLoyalty)
                {
                    if (Globals.SkipTraderLoyalty)
                        this.completeCondition(abstractQuestController, quest, condition);
                }
                else if (condition is ConditionPlaceBeacon conditionPlaceBeacon)
                {
                    if (Globals.SkipPlaceBeacon)
                        this.completeCondition(abstractQuestController, quest, condition);
                }
                else if (condition is ConditionSkill conditionSkill)
                {
                    if (Globals.SkipSkill)
                        this.completeCondition(abstractQuestController, quest, condition);
                }
                else if (condition is ConditionSellItemToTrader conditionSellItemToTrader)
                {
                    if (Globals.SkipSellItemToTrader)
                        this.completeCondition(abstractQuestController, quest, condition);
                }
                else if (Globals.Debug)
                    LogHelper.LogInfo($"ConditionType: {condition.GetType()} not handled.");
            }
            return false;
        }

        private bool isAllowToHandover(Item item)
        {
            return this.isNotFilledCompoundItem(item)
                && this.isFilledWithPlates(item) == false;
        }

        private bool isFilledCompoundItem(CompoundItem compoundItem)
        {
            return compoundItem.Containers.Any(c => c.Items.Any());
        }

        private bool isFilledWithPlates(Item item)
        {
            if (item.Components.Any(this.isFilledWithPlates))
                return true;
            return false;
        }

        private bool isFilledWithPlates(IItemComponent itemComponent)
        {
            if (itemComponent is not ArmorHolderComponent armorHolderComponent)
                return false;
            return armorHolderComponent.MoveAbleArmorSlots.Any(slot => slot.ContainedItem is ArmorPlateItemClass armorPlateItemClass && armorPlateItemClass.Armor.ArmorClass > Globals.BlockTurnInArmorPlateLevelHigherThan);
        }

        private bool isMarkedAsFailed(QuestClass quest)
        {
            return quest.QuestStatus == EQuestStatus.MarkedAsFailed;
        }

        private bool isMarkedAsFailRestartable(QuestClass quest)
        {
            return Globals.AutoRestartFailedQuests
                && quest.QuestStatus == EQuestStatus.FailRestartable;
        }

        private bool isNotFilledCompoundItem(Item item)
        {
            if (Globals.Debug)
                LogHelper.LogInfo($"itemtype: {item.GetType()}");
            if (item.IsContainer
             && item is CompoundItem container
             && this.isFilledCompoundItem(container))
                return false;
            return true;
        }

        private bool isReadyToFinish(QuestClass quest)
        {
            return quest.QuestStatus == EQuestStatus.AvailableForFinish;
        }

        private bool isReadyToStart(QuestClass quest)
        {
            return (quest.QuestStatus == EQuestStatus.AvailableForStart
                || this.isMarkedAsFailRestartable(quest))
                && this.shouldAcceptQuestThatCanFail(quest)
                && this.isUnlockedTrader(quest.rawQuestClass.TraderId);
        }

        private bool isStarted(QuestClass quest)
        {
            return quest.QuestStatus == EQuestStatus.Started;
        }

        private bool isUnlockedTrader(string traderId)
        {
            if (this.abstractQuestController == null)
                return false;
            if (this.abstractQuestController.Profile.TryGetTraderInfo(traderId, out var traderInfo) == false)
                return false;
            bool shouldBlockLightKeeper = Globals.AcceptLightKeeperOutOfRaid == false && traderId == TraderInfo.LIGHT_KEEPER_TRADER_ID;
            if (shouldBlockLightKeeper)
                return false;
            bool shouldBlockBTR = Globals.AcceptBTROutOfRaid == false && traderId == TraderInfo.BTR_TRADER_ID;
            if (shouldBlockBTR)
                return false;
            return traderInfo.Unlocked;
        }

        private bool shouldAcceptQuestThatCanFail(QuestClass quest)
        {
            if (quest.rawQuestClass.Conditions.ContainsKey(EQuestStatus.Fail) == false)
                return true;
            var failconditions = quest.rawQuestClass.Conditions[EQuestStatus.Fail];
            bool canFail = failconditions.IEnumerable_0.Count() > 0;
            if (canFail == false)
                return true;
            else if (Globals.AutoAcceptQuestsThatCanFail)
                return true;
            return false;
        }
    }
}