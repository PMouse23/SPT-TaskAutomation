using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using HarmonyLib;
using SPT.Common.Utils;
using SPT.SinglePlayer.Utils.InRaid;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using TaskAutomation.Helpers;
using UnityEngine;
using static EFT.Profile;

#nullable enable

namespace TaskAutomation.MonoBehaviours
{
    internal class UpdateMonoBehaviour : MonoBehaviour
    {
        private const string GPCOINTEMPLATEID = "5d235b4d86f7742e017bc88a";
        private AbstractQuestControllerClass? abstractQuestController;
        private CancellationToken? cancellationToken;
        private CancellationTokenSource? cancellationTokenSource;
        private Type? conditionChecker;
        private Type? dailyQuestType;
        private MethodInfo? itemsProviderMethod;

        public void SetAbstractQuestController(AbstractQuestControllerClass abstractQuestController)
        {
            if (this.abstractQuestController == null)
                this.StartCoroutine(this.coroutine());
            this.abstractQuestController = abstractQuestController;
            if (Globals.Debug)
                LogHelper.LogInfo($"SetAbstractQuestController and StartedCoroutine");
        }

        public void SetReflection(Type conditionChecker, MethodInfo itemsProviderMethod, Type dailyQuistType)
        {
            this.conditionChecker = conditionChecker;
            this.itemsProviderMethod = itemsProviderMethod;
            this.dailyQuestType = dailyQuistType;
            if (Globals.Debug)
                LogHelper.LogInfo($"SetReflection");
        }

        public void UnsetAbstractQuestController()
        {
            this.cancellationTokenSource?.Cancel();
            this.abstractQuestController = null;
            this.StopAllCoroutines();
            if (Globals.Debug)
                LogHelper.LogInfo($"StopedAllCoroutines");
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
            this.cancellationTokenSource = new CancellationTokenSource();
            this.cancellationToken = cancellationTokenSource.Token;
            while (true)
            {
                if (Globals.Debug)
                    LogHelper.LogInfo($"Started new run.");
                yield return new WaitForSeconds(0.5f);
                if (this.cancellationToken?.IsCancellationRequested == true)
                    yield break;
                if (this.abstractQuestController == null)
                    yield break;
                if (Globals.Debug)
                    LogHelper.LogInfo($"abstractQuestController not null.");
                if (RaidTimeUtil.HasRaidLoaded())
                {
                    this.UnsetAbstractQuestController();
                    yield break;
                }
                if (Globals.Debug)
                    LogHelper.LogInfo($"Not in a raid.");
                var quests = this.abstractQuestController.Quests;
                if (Globals.Debug)
                    LogHelper.LogInfo($"Handle started quests.");
                foreach (QuestClass quest in quests.Where(this.isStarted))
                {
                    if (this.cancellationToken?.IsCancellationRequested == true)
                        yield break;
                    if (Globals.Debug)
                        LogHelper.LogInfo($"Handle {quest.rawQuestClass.Name}");
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
                    if (Globals.Debug)
                        LogHelper.LogInfo($"Complete quests");
                    List<string> questsReadyToFinish = this.getIdsReadyToComplete();
                    foreach (string id in questsReadyToFinish)
                    {
                        if (this.cancellationToken?.IsCancellationRequested == true)
                            yield break;
                        try
                        {
                            QuestClass? questToComplete = this.getQuestById(id);
                            if (questToComplete == null)
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
                    if (Globals.Debug)
                        LogHelper.LogInfo($"Start quests");
                    List<string> questsReadyToStart = this.getIdsReadyToStart();
                    foreach (string id in questsReadyToStart)
                    {
                        if (this.cancellationToken?.IsCancellationRequested == true)
                            yield break;
                        try
                        {
                            QuestClass? questToStart = this.getQuestById(id);
                            if (questToStart == null)
                                continue;
                            if (Globals.AutoAcceptScavQuests == false
                                && this.abstractQuestController.IsQuestForCurrentProfile(questToStart) == false)
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
                if (this.cancellationToken?.IsCancellationRequested == true)
                    yield break;
                //FailedQuests
                if (Globals.Debug)
                    LogHelper.LogInfo($"Check for failed quest.");
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

        private int getItemCount(string templateId)
        {
            if (this.abstractQuestController == null)
                return 0;
            int count = 0;
            IEnumerable<Item> items = this.abstractQuestController.Profile.Inventory.GetAllItemByTemplate(templateId);
            foreach (Item item in items)
                count += item.StackObjectsCount;
            return count;
        }

        private Item[] getItemsAllowedToHandover(double handoverValue, Item[] result)
        {
            return result.Where(item => this.isAllowToHandover(item, handoverValue)).Take((int)handoverValue).ToArray();
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
                if (this.cancellationToken?.IsCancellationRequested == true)
                    return false;
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
                        abstractQuestController.HandoverItem(quest, conditionHandoverItem, result, runNetworkTransaction: true);
                        LogHelper.LogInfoWithNotification($"HandoverItem(s): {quest.rawQuestClass.Name}");
                        return true;
                    }
                }
                else if (condition is ConditionWeaponAssembly conditionWeaponAssembly)
                {
                    if (Globals.SkipWeaponAssembly)
                        this.completeCondition(abstractQuestController, quest, condition);
                    else if (Globals.BlockTurnInWeapons == false)
                    {
                        IEnumerable<Item> playerItems = abstractQuestController.Profile.Inventory.GetPlayerItems(EPlayerItems.NonQuestItemsExceptHideoutStashes);
                        Item[] weapons = Inventory.GetWeaponAssembly(playerItems, conditionWeaponAssembly).ToArray();
                        if (weapons == null || weapons.Length == 0)
                            continue;
                        abstractQuestController.HandoverItem(quest, conditionWeaponAssembly, weapons, runNetworkTransaction: true);
                        LogHelper.LogInfoWithNotification($"HandoverItem(s): {quest.rawQuestClass.Name}");
                    }
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

        private bool isAllowToHandover(Item item, double handoverValue)
        {
            return this.isInEquipmentSlot(item) == false
                && this.isBlockedWeapon(item) == false
                && this.isPartOfWeaponOrArmor(item) == false
                && this.isBlockedCurrency(item, (int)handoverValue) == false
                && this.isFilledCompoundItem(item) == false
                && this.isFilledWithPlates(item) == false;
        }

        private bool isBlockedCurrency(Item item, int handoverValue)
        {
            if (item is not MoneyItemClass moneyItemClass)
                return false;
            else if (Globals.BlockTurnInCurrency)
                return true;
            int itemCount = this.getItemCount(item.TemplateId);
            if (item.TemplateId == GPCOINTEMPLATEID)
                handoverValue = (int)(handoverValue * Globals.ThresholdGPCoinHandover);
            handoverValue = (int)(handoverValue * Globals.ThresholdCurrencyHandover);
            if (Globals.Debug)
                LogHelper.LogInfo($"count: {itemCount}, expected: {handoverValue}");
            return itemCount < handoverValue;
        }

        private bool isBlockedWeapon(Item item)
        {
            if (Globals.BlockTurnInWeapons == false)
                return false;
            else if (item is Weapon)
                return true;
            return false;
        }

        private bool isEquipmentSlot(ItemAddress itemAddress)
        {
            if (itemAddress == null)
                return false;
            if (Globals.Debug)
                LogHelper.LogInfo($"ItemAddress {itemAddress.Container.ID} {itemAddress.GetType()}");
            string containerId = itemAddress.Container.ID.ToLower();
            if (containerId == null)
                return false;
            else if (containerId == "firstprimaryweapon"
                 || containerId == "secondprimaryweapon"
                 || containerId == "headwear"
                 || containerId == "earpiece"
                 || containerId == "facecover"
                 || containerId == "eyewear"
                 || containerId == "tacticalvest"
                 || containerId == "armorvest"
                 || containerId == "backpack"
                 || containerId == "pocket1"
                 || containerId == "pocket2"
                 || containerId == "pocket3"
                 || containerId == "pocket4"
                 || containerId == "pocket5"
                 || containerId == "pocket6"
                 || containerId == "armband"
                 || containerId == "holster"
                 || containerId == "scabbard"
                 || containerId == "securedcontainer")
                return true;
            return false;
        }

        private bool isFilledCompoundItem(CompoundItem compoundItem)
        {
            return compoundItem.Containers.Any(c => c.Items.Any());
        }

        private bool isFilledCompoundItem(Item item)
        {
            if (Globals.Debug)
                LogHelper.LogInfo($"itemtype: {item.GetType()}");
            if (item.IsContainer
             && item is CompoundItem container
             && this.isFilledCompoundItem(container))
                return true;
            return false;
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

        private bool isInEquipmentSlot(Item item)
        {
            if (this.isEquipmentSlot(item.CurrentAddress))
                return true;
            return false;
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

        private bool isPartOfWeaponOrArmor(Item item)
        {
            if (Globals.Debug)
                LogHelper.LogInfo($"WeaponOrArmor: check {item.LocalizedName()} ");
            if (item.CurrentAddress?.Container is Slot slot
                && (slot.ParentItem is Weapon || slot.ParentItem is ArmoredEquipmentItemClass))
            {
                if (Globals.Debug)
                    LogHelper.LogInfo($"WeaponOrArmor: {item.Id} SlotParentItemType: {slot.ParentItem.GetType()}");
                return true;
            }
            return false;
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
                && this.shouldAcceptDailyQuists(quest)
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

        private bool shouldAcceptDailyQuists(QuestClass quest)
        {
            if (Globals.AutoAcceptDailyQuests)
                return true;
            return quest.rawQuestClass.GetType() != this.dailyQuestType;
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