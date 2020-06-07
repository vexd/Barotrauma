﻿using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveGetItem : AIObjective
    {
        public override string DebugTag => "get item";

        private readonly bool equip;
        public HashSet<Item> ignoredItems = new HashSet<Item>();

        public Func<Item, float> GetItemPriority;
        public Func<Item, bool> ItemFilter;
        public float TargetCondition { get; set; } = 1;

        //can be either tags or identifiers
        private string[] itemIdentifiers;
        public IEnumerable<string> Identifiers => itemIdentifiers;

        private Item targetItem;
        private ISpatialEntity moveToTarget;
        private bool isDoneSeeking;
        public Item TargetItem => targetItem;
        private int currSearchIndex;
        public string[] ignoredContainerIdentifiers;
        private AIObjectiveGoTo goToObjective;
        private float currItemPriority;
        private bool checkInventory;

        public static float DefaultReach = 100;

        public bool AllowToFindDivingGear { get; set; } = true;

        public AIObjectiveGetItem(Character character, Item targetItem, AIObjectiveManager objectiveManager, bool equip = true, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier)
        {
            currSearchIndex = -1;
            this.equip = equip;
            this.targetItem = targetItem;
            moveToTarget = targetItem?.GetRootInventoryOwner();
        }

        public AIObjectiveGetItem(Character character, string itemIdentifier, AIObjectiveManager objectiveManager, bool equip = true, bool checkInventory = true, float priorityModifier = 1) 
            : this(character, new string[] { itemIdentifier }, objectiveManager, equip, checkInventory, priorityModifier) { }

        public AIObjectiveGetItem(Character character, string[] itemIdentifiers, AIObjectiveManager objectiveManager, bool equip = true, bool checkInventory = true, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier)
        {
            currSearchIndex = -1;
            this.equip = equip;
            this.itemIdentifiers = itemIdentifiers;
            for (int i = 0; i < itemIdentifiers.Length; i++)
            {
                itemIdentifiers[i] = itemIdentifiers[i].ToLowerInvariant();
            }
            this.checkInventory = checkInventory;
        }

        private bool CheckInventory()
        {
            if (itemIdentifiers == null) { return false; }
            var item = character.Inventory.FindItem(i => CheckItem(i), recursive: true);
            if (item != null)
            {
                targetItem = item;
                moveToTarget = item.GetRootInventoryOwner();
            }
            return item != null;
        }

        protected override void Act(float deltaTime)
        {
            if (character.LockHands)
            {
                Abandon = true;
                return;
            }
            if (itemIdentifiers != null && !isDoneSeeking)
            {
                if (checkInventory)
                {
                    if (CheckInventory())
                    {
                        isDoneSeeking = true;
                    }
                }
                if (!isDoneSeeking)
                {
                    bool dangerousPressure = character.CurrentHull == null || character.CurrentHull.LethalPressure > 0;
                    if (dangerousPressure)
                    {
#if DEBUG
                        DebugConsole.NewMessage($"{character.Name}: Seeking item aborted, because the pressure is dangerous.", Color.Yellow);
#endif
                        Abandon = true;
                        return;
                    }
                    FindTargetItem();
                    objectiveManager.GetObjective<AIObjectiveIdle>().Wander(deltaTime);
                    return;
                }
            }
            if (targetItem == null || targetItem.Removed)
            {
#if DEBUG
                DebugConsole.NewMessage($"{character.Name}: Target null or removed. Aborting.", Color.Red);
#endif
                Abandon = true;
                return;
            }
            if (character.IsItemTakenBySomeoneElse(targetItem))
            {
#if DEBUG
                DebugConsole.NewMessage($"{character.Name}: Found an item, but it's already equipped by someone else.", Color.Yellow);
#endif
                // Try again
                Reset();
                return;
            }
            bool canInteract = false;
            if (moveToTarget is Character c)
            {
                if (character == c)
                {
                    canInteract = true;
                    moveToTarget = null;
                }
                else
                {
                    character.SelectCharacter(c);
                    canInteract = character.CanInteractWith(c, maxDist: DefaultReach);
                    character.DeselectCharacter();
                }
            }
            else if (moveToTarget is Item parentItem)
            {
                canInteract = character.CanInteractWith(parentItem, out _, checkLinked: false);
            }
            if (canInteract)
            {
                var pickable = targetItem.GetComponent<Pickable>();
                if (pickable == null)
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Target not pickable. Aborting.", Color.Yellow);
#endif
                    Abandon = true;
                    return;
                }

                if (equip)
                {
                    int targetSlot = -1;
                    //check if all the slots required by the item are free
                    foreach (InvSlotType slots in pickable.AllowedSlots)
                    {
                        if (slots.HasFlag(InvSlotType.Any)) { continue; }
                        for (int i = 0; i < character.Inventory.Items.Length; i++)
                        {
                            //slot not needed by the item, continue
                            if (!slots.HasFlag(character.Inventory.SlotTypes[i])) { continue; }
                            targetSlot = i;
                            //slot free, continue
                            var otherItem = character.Inventory.Items[i];
                            if (otherItem == null) { continue; }
                            //try to move the existing item to LimbSlot.Any and continue if successful
                            if (otherItem.AllowedSlots.Contains(InvSlotType.Any) && 
                                character.Inventory.TryPutItem(otherItem, character, new List<InvSlotType>() { InvSlotType.Any }))
                            {
                                continue;
                            }
                            //if everything else fails, simply drop the existing item
                            otherItem.Drop(character);
                        }
                    }
                    if (character.Inventory.TryPutItem(targetItem, targetSlot, false, false, character))
                    {
                        targetItem.Equip(character);
                        IsCompleted = true;
                    }
                    else
                    {
#if DEBUG
                        DebugConsole.NewMessage($"{character.Name}: Failed to equip/move the item '{targetItem.Name}' into the character inventory. Aborting.", Color.Red);
#endif
                        Abandon = true;
                    }
                }
                else
                {
                    if (character.Inventory.TryPutItem(targetItem, null, new List<InvSlotType>() { InvSlotType.Any }))
                    {
                        IsCompleted = true;
                    }
                    else
                    {
                        Abandon = true;
#if DEBUG
                        DebugConsole.NewMessage($"{character.Name}: Failed to equip/move the item '{targetItem.Name}' into the character inventory. Aborting.", Color.Red);
#endif
                    }
                }
            }
            else if (moveToTarget != null)
            {
                TryAddSubObjective(ref goToObjective,
                    constructor: () =>
                    {
                        return new AIObjectiveGoTo(moveToTarget, character, objectiveManager, repeat: false, getDivingGearIfNeeded: AllowToFindDivingGear, closeEnough: DefaultReach)
                        {
                            // If the root container changes, the item is no longer where it was (taken by someone -> need to find another item)
                            abortCondition = () => targetItem == null || targetItem.GetRootInventoryOwner() != moveToTarget,
                            DialogueIdentifier = "dialogcannotreachtarget",
                            TargetName = (moveToTarget as MapEntity)?.Name ?? (moveToTarget as Character)?.Name ?? moveToTarget.ToString()
                        };
                    },
                    onAbandon: () =>
                    {
                        ignoredItems.Add(targetItem);
                        Reset();
                    },
                    onCompleted: () => RemoveSubObjective(ref goToObjective));
            }
        }

        private void FindTargetItem()
        {
            if (itemIdentifiers == null)
            {
                if (targetItem == null)
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Cannot find the item, because neither identifiers nor item was defined.", Color.Red);
#endif
                    Abandon = true;
                }
                return;
            }
            for (int i = 0; i < 10 && currSearchIndex < Item.ItemList.Count - 1; i++)
            {
                currSearchIndex++;
                var item = Item.ItemList[currSearchIndex];
                Submarine itemSub = item.Submarine ?? item.ParentInventory?.Owner?.Submarine;
                if (itemSub == null) { continue; }
                if (itemSub.TeamID != character.TeamID) { continue; }
                if (!CheckItem(item)) { continue; }
                if (ignoredContainerIdentifiers != null && item.Container != null)
                {
                    if (ignoredContainerIdentifiers.Contains(item.ContainerIdentifier)) { continue; }
                }
                if (character.Submarine != null)
                {
                    if (itemSub.Info.Type != character.Submarine.Info.Type) { continue; }
                    if (character.Submarine.GetConnectedSubs().None(s => s == itemSub && itemSub.TeamID == character.TeamID && itemSub.Info.Type == character.Submarine.Info.Type)) { continue; }
                }
                if (character.IsItemTakenBySomeoneElse(item)) { continue; }
                float itemPriority = 1;
                if (GetItemPriority != null)
                {
                    itemPriority = GetItemPriority(item);
                }
                Entity rootInventoryOwner = item.GetRootInventoryOwner();
                Vector2 itemPos = (rootInventoryOwner ?? item).WorldPosition;
                float yDist = Math.Abs(character.WorldPosition.Y - itemPos.Y);
                yDist = yDist > 100 ? yDist * 5 : 0;
                float dist = Math.Abs(character.WorldPosition.X - itemPos.X) + yDist;
                float distanceFactor = MathHelper.Lerp(1, 0, MathUtils.InverseLerp(0, 10000, dist));
                itemPriority *= distanceFactor;
                itemPriority *= item.Condition / item.MaxCondition;
                //ignore if the item has a lower priority than the currently selected one
                if (itemPriority < currItemPriority) { continue; }
                currItemPriority = itemPriority;
                targetItem = item;
                moveToTarget = rootInventoryOwner ?? item;
            }
            if (currSearchIndex >= Item.ItemList.Count - 1)
            {
                isDoneSeeking = true;
                if (targetItem == null)
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Cannot find the item with the following identifier(s): {string.Join(", ", itemIdentifiers)}", Color.Yellow);
#endif
                    Abandon = true;
                }
            }
        }

        protected override bool Check()
        {
            if (IsCompleted) { return true; }
            if (targetItem != null)
            {
                return character.HasItem(targetItem, equip);
            }
            else if (itemIdentifiers != null)
            {
                var matchingItem = character.Inventory.FindItem(i => CheckItem(i), recursive: true);
                if (matchingItem != null)
                {
                    return !equip || character.HasEquippedItem(matchingItem);
                }
                return false;
            }
            return false;
        }

        private bool CheckItem(Item item)
        {
            if (item.NonInteractable) { return false; }
            if (ignoredItems.Contains(item)) { return false; };
            if (item.Condition < TargetCondition) { return false; }
            if (ItemFilter != null && !ItemFilter(item)) { return false; }
            return itemIdentifiers.Any(id => id == item.Prefab.Identifier || item.HasTag(id));
        }

        public override void Reset()
        {
            base.Reset();
            RemoveSubObjective(ref goToObjective);
            targetItem = null;
            moveToTarget = null;
            isDoneSeeking = false;
            currSearchIndex = 0;
        }
    }
}
